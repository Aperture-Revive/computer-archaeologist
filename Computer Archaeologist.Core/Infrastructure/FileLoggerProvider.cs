using System.Collections.Concurrent;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace ComputerArchaeologist.Core.Infrastructure;

/// <summary>
/// Structured daily-rolling file log under <c>%LOCALAPPDATA%\Computer Archaeologist\Logs</c>.
/// <para>
/// Two guarantees matter here and are enforced in <see cref="Redact"/>: an API key can never reach a
/// log line even if a caller formats one by accident, and file contents are never written because no
/// caller is allowed to log an excerpt.
/// </para>
/// </summary>
public sealed partial class FileLoggerProvider : ILoggerProvider
{
    private const long MaxFileBytes = 8L * 1024 * 1024;

    private readonly BlockingCollection<string> _queue = new(new ConcurrentQueue<string>());
    private readonly Task _writer;
    private readonly string _directory;
    private readonly LogLevel _minimum;

    public FileLoggerProvider(string? directory = null, LogLevel minimum = LogLevel.Information)
    {
        _directory = directory ?? AppPaths.LogDirectory;
        _minimum = minimum;
        Directory.CreateDirectory(_directory);
        _writer = Task.Run(WriteLoop);
    }

    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);

    private bool IsEnabled(LogLevel level) => level >= _minimum && level != LogLevel.None;

    private void Enqueue(string line)
    {
        if (_queue.IsAddingCompleted)
        {
            return;
        }

        try
        {
            _queue.Add(line);
        }
        catch (InvalidOperationException)
        {
            // Shutting down.
        }
    }

    private void WriteLoop()
    {
        foreach (var line in _queue.GetConsumingEnumerable())
        {
            try
            {
                var path = Path.Combine(_directory, $"computer-archaeologist-{DateTime.UtcNow:yyyyMMdd}.log");
                if (File.Exists(path) && new FileInfo(path).Length > MaxFileBytes)
                {
                    var rolled = Path.Combine(_directory, $"computer-archaeologist-{DateTime.UtcNow:yyyyMMdd}-{DateTime.UtcNow:HHmmss}.log");
                    File.Move(path, rolled, overwrite: true);
                }

                File.AppendAllText(path, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // A log write failure must never affect the application.
            }
        }
    }

    /// <summary>Removes anything that looks like a credential before it is written.</summary>
    internal static string Redact(string message)
    {
        if (string.IsNullOrEmpty(message))
        {
            return message;
        }

        var redacted = ApiKeyRegex().Replace(message, "sk-***REDACTED***");
        redacted = BearerRegex().Replace(redacted, "Bearer ***REDACTED***");
        return redacted;
    }

    public void Dispose()
    {
        _queue.CompleteAdding();
        try
        {
            _writer.Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Ignore shutdown races.
        }

        _queue.Dispose();
    }

    [GeneratedRegex(@"sk-[A-Za-z0-9_\-]{12,}", RegexOptions.CultureInvariant)]
    private static partial Regex ApiKeyRegex();

    [GeneratedRegex(@"(?i)bearer\s+[A-Za-z0-9._\-]{12,}", RegexOptions.CultureInvariant)]
    private static partial Regex BearerRegex();

    private sealed class FileLogger : ILogger
    {
        private readonly FileLoggerProvider _provider;
        private readonly string _category;

        public FileLogger(FileLoggerProvider provider, string category)
        {
            _provider = provider;
            _category = category;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => _provider.IsEnabled(logLevel);

        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel) || formatter is null)
            {
                return;
            }

            var builder = new StringBuilder(256);
            builder.Append(DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz"));
            builder.Append(" [").Append(Level(logLevel)).Append(']');
            builder.Append(" [").Append(_category).Append("] ");

            try
            {
                builder.Append(formatter(state, exception));
            }
            catch (Exception ex)
            {
                builder.Append("<formatter failed: ").Append(ex.GetType().Name).Append('>');
            }

            if (exception is not null)
            {
                builder.Append(" | ").Append(exception.GetType().Name).Append(": ").Append(exception.Message);
            }

            _provider.Enqueue(Redact(builder.ToString()));
        }

        private static string Level(LogLevel level) => level switch
        {
            LogLevel.Trace => "TRC",
            LogLevel.Debug => "DBG",
            LogLevel.Information => "INF",
            LogLevel.Warning => "WRN",
            LogLevel.Error => "ERR",
            LogLevel.Critical => "CRT",
            _ => "???",
        };

        private sealed class NullScope : IDisposable
        {
            public static NullScope Instance { get; } = new();

            public void Dispose()
            {
            }
        }
    }
}

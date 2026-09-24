using System.Text.Json;
using System.Text.Json.Serialization;
using ComputerArchaeologist.Core.Infrastructure;
using ComputerArchaeologist.Core.Models;
using Microsoft.Extensions.Logging;

namespace ComputerArchaeologist.Core.Storage;

/// <summary>
/// Persistence for archaeology runs. The MVP writes JSON, but the interface is storage-agnostic so
/// SQLite can replace it later without touching the view models.
/// </summary>
public interface IScanRepository
{
    string Directory { get; }

    Task SaveAsync(ArchaeologySession session, CancellationToken cancellationToken = default);

    Task<ArchaeologySession?> GetAsync(string sessionId, CancellationToken cancellationToken = default);

    Task<ArchaeologySession?> GetLatestAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<SessionSummary>> ListAsync(int max = 50, CancellationToken cancellationToken = default);
}

public sealed class JsonScanRepository : IScanRepository
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        MaxDepth = 64,
    };

    private readonly ILogger<JsonScanRepository>? _logger;
    private readonly string? _directory;

    /// <param name="logger">Optional logger.</param>
    /// <param name="directory">
    /// Storage location. Defaults to <c>%LOCALAPPDATA%\Computer Archaeologist\sessions</c>; tests point
    /// it at a throw-away folder so they never touch the real archive.
    /// </param>
    public JsonScanRepository(ILogger<JsonScanRepository>? logger = null, string? directory = null)
    {
        _logger = logger;
        _directory = directory;
    }

    public string Directory => _directory ?? AppPaths.SessionDirectory;

    public async Task SaveAsync(ArchaeologySession session, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(session);

        try
        {
            System.IO.Directory.CreateDirectory(Directory);

            var path = PathFor(session.SessionId);
            var json = JsonSerializer.Serialize(session, SerializerOptions);
            var temporary = path + ".tmp";
            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, path, overwrite: true);

            // A tiny companion file keeps the history list cheap to render.
            var summaryPath = SummaryPathFor(session.SessionId);
            var summaryJson = JsonSerializer.Serialize(session.ToSummary(), SerializerOptions);
            await File.WriteAllTextAsync(summaryPath, summaryJson, cancellationToken).ConfigureAwait(false);

            _logger?.LogInformation("Archived session {SessionId} to {Path}", session.SessionId, path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Session {SessionId} could not be saved", session.SessionId);
            throw;
        }
    }

    public async Task<ArchaeologySession?> GetAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return null;
        }

        var path = PathFor(sessionId);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<ArchaeologySession>(stream, SerializerOptions, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Session {SessionId} could not be read", sessionId);
            return null;
        }
    }

    public async Task<ArchaeologySession?> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        var summaries = await ListAsync(1, cancellationToken).ConfigureAwait(false);
        var latest = summaries.FirstOrDefault();
        return latest is null ? null : await GetAsync(latest.SessionId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<SessionSummary>> ListAsync(int max = 50, CancellationToken cancellationToken = default)
    {
        try
        {
            if (!System.IO.Directory.Exists(Directory))
            {
                return Array.Empty<SessionSummary>();
            }

            var files = System.IO.Directory
                .EnumerateFiles(Directory, "*.summary.json")
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .Take(Math.Max(1, max))
                .ToArray();

            var summaries = new List<SessionSummary>(files.Length);
            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    await using var stream = File.OpenRead(file);
                    var summary = await JsonSerializer.DeserializeAsync<SessionSummary>(stream, SerializerOptions, cancellationToken)
                        .ConfigureAwait(false);
                    if (summary is not null)
                    {
                        summaries.Add(summary);
                    }
                }
                catch (Exception ex) when (ex is JsonException or IOException)
                {
                    _logger?.LogDebug(ex, "Skipping unreadable session summary {File}", file);
                }
            }

            return summaries
                .OrderByDescending(s => s.StartTime)
                .ToArray();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "The session history could not be listed");
            return Array.Empty<SessionSummary>();
        }
    }

    private string PathFor(string sessionId) => Path.Combine(Directory, $"{Sanitize(sessionId)}.json");

    private string SummaryPathFor(string sessionId) => Path.Combine(Directory, $"{Sanitize(sessionId)}.summary.json");

    private static string Sanitize(string sessionId)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(sessionId.Where(c => !invalid.Contains(c)).ToArray());
        return string.IsNullOrEmpty(cleaned) ? Guid.NewGuid().ToString("N") : cleaned;
    }
}

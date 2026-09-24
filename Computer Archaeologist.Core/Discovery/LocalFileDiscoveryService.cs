using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading.Channels;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Options;
using Microsoft.Extensions.Logging;

namespace ComputerArchaeologist.Core.Discovery;

/// <summary>
/// The file discovery engine: a bounded, parallel walk of the local file system.
/// <para>
/// Design constraints, all of which matter on a multi-million file machine:
/// </para>
/// <list type="bullet">
/// <item>The walk is <b>breadth first with a shared work queue</b>, so a handful of workers keep every
/// drive busy instead of one thread crawling a deep tree.</item>
/// <item>Excluded directories are pruned <b>before</b> they are entered, so caches and build output
/// cost nothing rather than costing a filter pass over millions of entries.</item>
/// <item>Results flow through a <b>bounded channel</b>, so discovery can never outrun the scoring
/// stage and memory stays flat no matter how large the scope is.</item>
/// <item>Both a file budget and a wall-clock budget apply, and cancellation is cooperative.</item>
/// <item>Nothing is ever written, moved or opened for reading: only directory listings and file
/// metadata are touched.</item>
/// </list>
/// </summary>
public sealed class LocalFileDiscoveryService : IFileDiscoveryService
{
    /// <summary>
    /// Entries are read straight from the directory listing, which on Windows already carries size,
    /// timestamps and attributes, so building <see cref="FileMetadata"/> costs no extra file system
    /// round trip.
    /// </summary>
    private static readonly EnumerationOptions EnumerationOptions = new()
    {
        IgnoreInaccessible = true,
        RecurseSubdirectories = false,
        ReturnSpecialDirectories = false,
        AttributesToSkip = 0,
        MatchType = MatchType.Simple,
    };

    private const int ResultBufferSize = 4_096;

    private readonly ExclusionPolicyProvider _policyProvider;
    private readonly DiscoveryOptions _options;
    private readonly ILogger<LocalFileDiscoveryService>? _logger;

    public LocalFileDiscoveryService(
        ExclusionPolicyProvider policyProvider,
        DiscoveryOptions options,
        ILogger<LocalFileDiscoveryService>? logger = null)
    {
        _policyProvider = policyProvider;
        _options = options;
        _logger = logger;
    }

    public string SourceLabelKey => "Discovery_Source_Local";

    public async IAsyncEnumerable<FileMetadata> DiscoverAsync(
        DiscoveryRequest request,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var policy = _policyProvider.ForRoots(request.Roots);
        var roots = ResolveRoots(request);
        if (roots.Count == 0)
        {
            _logger?.LogWarning("No readable root is available to scan");
            yield break;
        }

        var maxFiles = request.SafeMaxFiles;
        var budget = request.Budget;
        var concurrency = request.SafeConcurrency;

        var results = Channel.CreateBounded<FileMetadata>(new BoundedChannelOptions(ResultBufferSize)
        {
            SingleReader = true,
            SingleWriter = false,
            FullMode = BoundedChannelFullMode.Wait,
        });

        // The directory queue must be unbounded: the same workers both produce and consume directories,
        // so applying backpressure here would deadlock.
        var directories = Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = false,
            SingleWriter = false,
        });

        var state = new WalkState(maxFiles);
        foreach (var root in roots)
        {
            if (directories.Writer.TryWrite(root))
            {
                state.DirectoryQueued();
            }
        }

        _logger?.LogInformation(
            "Scanning {Roots} root(s) with {Workers} parallel walkers (max {MaxFiles} files)",
            roots.Count,
            concurrency,
            maxFiles == int.MaxValue ? "unlimited" : maxFiles.ToString("N0"));

        var stopwatch = Stopwatch.StartNew();

        var workers = new Task[concurrency];
        for (var i = 0; i < concurrency; i++)
        {
            workers[i] = Task.Run(
                () => WalkAsync(directories, results.Writer, state, policy, maxFiles, budget, stopwatch, cancellationToken),
                CancellationToken.None);
        }

        var completion = CompleteWhenDoneAsync(directories, results.Writer, state, workers);

        try
        {
            await foreach (var file in results.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            {
                yield return file;
            }
        }
        finally
        {
            // Make sure no worker is left waiting when the consumer stops early (cancellation or the
            // pipeline having seen enough).
            directories.Writer.TryComplete();
            await completion.ConfigureAwait(false);
        }
    }

    /// <summary>Closes the result channel once every worker has finished.</summary>
    private static async Task CompleteWhenDoneAsync(
        Channel<string> directories,
        ChannelWriter<FileMetadata> results,
        WalkState state,
        Task[] workers)
    {
        try
        {
            await Task.WhenAll(workers).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Individual failures are already handled inside the workers.
        }
        finally
        {
            directories.Writer.TryComplete();
            results.TryComplete();
            state.ReportIfStopped();
        }
    }

    private async Task WalkAsync(
        Channel<string> directories,
        ChannelWriter<FileMetadata> results,
        WalkState state,
        PathExclusionPolicy policy,
        int maxFiles,
        TimeSpan budget,
        Stopwatch stopwatch,
        CancellationToken cancellationToken)
    {
        try
        {
            while (await directories.Reader.WaitToReadAsync(cancellationToken).ConfigureAwait(false))
            {
                while (directories.Reader.TryRead(out var directory))
                {
                    if (cancellationToken.IsCancellationRequested || state.ShouldStop)
                    {
                        state.MarkStopped();
                        directories.Writer.TryComplete();
                        return;
                    }

                    if (budget > TimeSpan.Zero && stopwatch.Elapsed > budget)
                    {
                        state.MarkStopped();
                        directories.Writer.TryComplete();
                        return;
                    }

                    await ScanDirectoryAsync(directory, directories, results, state, policy, maxFiles, cancellationToken)
                        .ConfigureAwait(false);

                    // The last directory finishing is what closes the queue. The counter is incremented
                    // before a subdirectory is queued and decremented only here, so a zero value means
                    // nothing is pending and nothing is in flight.
                    if (state.DirectoryFinished())
                    {
                        directories.Writer.TryComplete();
                    }
                }
            }
        }
        catch (OperationCanceledException)
        {
            directories.Writer.TryComplete();
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "A discovery worker failed");
            directories.Writer.TryComplete();
        }
    }

    private async Task ScanDirectoryAsync(
        string directory,
        Channel<string> directories,
        ChannelWriter<FileMetadata> results,
        WalkState state,
        PathExclusionPolicy policy,
        int maxFiles,
        CancellationToken cancellationToken)
    {
        IReadOnlyList<FileSystemInfo> entries;
        try
        {
            // Materialising one directory at a time keeps the file handles short lived while still
            // letting the workers run in parallel on different directories.
            entries = new DirectoryInfo(directory).GetFileSystemInfos("*", EnumerationOptions);
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PathTooLongException or System.Security.SecurityException or ArgumentException)
        {
            return;
        }

        foreach (var entry in entries)
        {
            if (cancellationToken.IsCancellationRequested || state.ShouldStop)
            {
                return;
            }

            try
            {
                if ((entry.Attributes & FileAttributes.Directory) != 0)
                {
                    // Pruned before it is ever entered: an excluded folder costs one attribute check.
                    if (!policy.IsExcluded(entry.FullName + Path.DirectorySeparatorChar))
                    {
                        if (directories.Writer.TryWrite(entry.FullName))
                        {
                            state.DirectoryQueued();
                        }
                    }

                    continue;
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                continue;
            }

            var extension = Path.GetExtension(entry.Name);
            if (policy.IsExcluded(entry.FullName, extension))
            {
                continue;
            }

            if (!state.TryTakeFile(maxFiles))
            {
                return;
            }

            FileMetadata metadata;
            try
            {
                metadata = FileMetadata.FromFileSystemInfo(entry, "Local scan");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                continue;
            }

            try
            {
                await results.WriteAsync(metadata, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (ChannelClosedException)
            {
                return;
            }
        }
    }

    /// <summary>The run scope when the user picked one, otherwise every ready fixed drive.</summary>
    private List<string> ResolveRoots(DiscoveryRequest request)
    {
        var roots = new List<string>();

        if (request.Roots.Count > 0)
        {
            foreach (var root in request.Roots)
            {
                if (string.IsNullOrWhiteSpace(root))
                {
                    continue;
                }

                if (!Directory.Exists(root))
                {
                    _logger?.LogWarning("Skipping run root {Root}: it no longer exists", root);
                    continue;
                }

                if (!roots.Contains(root, StringComparer.OrdinalIgnoreCase))
                {
                    roots.Add(root);
                }
            }

            return roots;
        }

        DriveInfo[] drives;
        try
        {
            drives = DriveInfo.GetDrives();
        }
        catch (IOException)
        {
            return roots;
        }

        foreach (var drive in drives)
        {
            bool usable;
            try
            {
                usable = drive.IsReady && drive.DriveType == DriveType.Fixed;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                usable = false;
            }

            if (usable)
            {
                roots.Add(drive.RootDirectory.FullName);
            }
        }

        return roots;
    }

    /// <summary>
    /// Shared walk counters. Kept in one object so the workers do not need a lock: every field is
    /// updated with interlocked operations.
    /// </summary>
    private sealed class WalkState
    {
        private readonly int _maxFiles;
        private int _outstandingDirectories;
        private int _filesSeen;
        private int _stopped;

        public WalkState(int maxFiles) => _maxFiles = maxFiles;

        public bool ShouldStop => Volatile.Read(ref _stopped) != 0 || Volatile.Read(ref _filesSeen) >= _maxFiles;

        public int FilesSeen => Volatile.Read(ref _filesSeen);

        public void DirectoryQueued() => Interlocked.Increment(ref _outstandingDirectories);

        /// <summary>Returns true when this was the last directory in flight.</summary>
        public bool DirectoryFinished() => Interlocked.Decrement(ref _outstandingDirectories) == 0;

        /// <summary>Reserves one file slot, or returns false when the budget is exhausted.</summary>
        public bool TryTakeFile(int maxFiles)
        {
            if (Interlocked.Increment(ref _filesSeen) > maxFiles)
            {
                MarkStopped();
                return false;
            }

            return true;
        }

        public void MarkStopped() => Interlocked.Exchange(ref _stopped, 1);

        public void ReportIfStopped()
        {
            if (Volatile.Read(ref _stopped) != 0)
            {
                Volatile.Write(ref _stopped, _stopped);
            }
        }
    }
}

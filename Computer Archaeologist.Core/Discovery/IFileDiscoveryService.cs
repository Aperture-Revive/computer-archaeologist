using ComputerArchaeologist.Core.Models;

namespace ComputerArchaeologist.Core.Discovery;

/// <summary>What the discovery stage has been asked to walk, decided before a single file is touched.</summary>
public sealed record DiscoveryRequest
{
    /// <summary>
    /// The scope chosen by the user. An empty list means every ready fixed drive.
    /// </summary>
    public IReadOnlyList<string> Roots { get; init; } = Array.Empty<string>();

    /// <summary>Hard cap on how many files may be inspected. Zero means no cap.</summary>
    public int MaxFiles { get; init; } = 400_000;

    /// <summary>
    /// Wall-clock budget for the walk. Zero disables the budget.
    /// </summary>
    public TimeSpan Budget { get; init; } = TimeSpan.FromMinutes(10);

    /// <summary>How many directories may be walked in parallel.</summary>
    public int Concurrency { get; init; } = 6;

    public int SafeMaxFiles => MaxFiles <= 0 ? int.MaxValue : Math.Clamp(MaxFiles, 1_000, 20_000_000);

    public int SafeConcurrency => Math.Clamp(Concurrency, 1, 32);
}

/// <summary>
/// Discovery of candidate files on the local machine. The interface exists so the implementation can
/// be swapped (for example for an indexed backend) without touching the pipeline.
/// </summary>
public interface IFileDiscoveryService
{
    /// <summary>
    /// Streams candidate files, already filtered by the exclusion policy and the run scope. The stream
    /// is produced in parallel internally but is consumed sequentially, and memory stays flat because
    /// the caller only ever holds one item at a time.
    /// </summary>
    IAsyncEnumerable<FileMetadata> DiscoverAsync(
        DiscoveryRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Describes the engine for the report, for example <c>Discovery_Source_Local</c>.</summary>
    string SourceLabelKey { get; }
}

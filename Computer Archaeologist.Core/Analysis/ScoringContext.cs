using ComputerArchaeologist.Core.Models;

namespace ComputerArchaeologist.Core.Analysis;

/// <summary>What the calculator knows about the machine beyond a single file.</summary>
public sealed class ScoringContext
{
    public static ScoringContext Empty { get; } = new();

    /// <summary>How many files the discovery stage inspected. Used to normalise rarity.</summary>
    public long TotalFilesScanned { get; init; } = 1;

    /// <summary>Extension -> number of occurrences in the scanned set.</summary>
    public IReadOnlyDictionary<string, int> ExtensionCounts { get; init; } =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Normalised directory path -> structural summary of that directory.</summary>
    public IReadOnlyDictionary<string, DirectoryClusterInfo> DirectoryClusters { get; init; } =
        new Dictionary<string, DirectoryClusterInfo>(StringComparer.OrdinalIgnoreCase);

    public DateTimeOffset Now { get; init; } = DateTimeOffset.UtcNow;

    public int CountOf(string extension) =>
        ExtensionCounts.TryGetValue(FileMetadata.NormalizeExtension(extension), out var count) ? count : 0;

    public DirectoryClusterInfo ClusterOf(string directory)
    {
        if (string.IsNullOrEmpty(directory))
        {
            return DirectoryClusterInfo.Empty;
        }

        var key = directory.Replace('/', '\\').TrimEnd('\\');
        return DirectoryClusters.TryGetValue(key, out var info) ? info : DirectoryClusterInfo.Empty;
    }
}

/// <summary>Structural facts about one folder, produced during the metadata stage.</summary>
public sealed class DirectoryClusterInfo
{
    public static DirectoryClusterInfo Empty { get; } = new();

    public int SiblingCount { get; init; }

    public bool LooksLikeProject { get; init; }

    public bool HasReadme { get; init; }

    public bool HasManifest { get; init; }

    public int SourceFileCount { get; init; }

    public int AssetFileCount { get; init; }

    public IReadOnlyList<string> FileNames { get; init; } = Array.Empty<string>();
}

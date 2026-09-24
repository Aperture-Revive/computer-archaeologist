using System.Text.Json.Serialization;

namespace ComputerArchaeologist.Core.Models;

/// <summary>
/// The fusion of local evidence and AI judgement. The AI never decides on its own: its weight is
/// scaled by the confidence it reported (see <c>InterestingnessCalculator</c>).
/// </summary>
public sealed class InterestingnessResult
{
    public double LocalScore { get; init; }

    /// <summary>AI score as reported, before confidence scaling.</summary>
    public double AiScore { get; init; }

    /// <summary>Configured AI weight actually used, after confidence scaling. 0 when AI was unavailable.</summary>
    public double EffectiveAiWeight { get; init; }

    /// <summary>Configured local weight actually used (1 - EffectiveAiWeight).</summary>
    public double EffectiveLocalWeight { get; init; }

    /// <summary>Final fused score, guaranteed to be inside 0..100.</summary>
    public double FinalScore { get; init; }

    public double Confidence { get; init; }

    public bool AiApplied { get; init; }

    public LocalScoreBreakdown Local { get; init; } = new();

    public AiAnalysisResult? Ai { get; init; }

    [JsonIgnore]
    public string FinalScoreText => $"{FinalScore:0} / 100";

    public static double Clamp(double value) =>
        double.IsNaN(value) ? 0 : Math.Clamp(value, 0, 100);
}

/// <summary>A scored candidate: the unit that the Discoveries page and the report both render.</summary>
public sealed class FileArtifact
{
    public required FileMetadata File { get; init; }

    public InterestingnessResult Score { get; init; } = new();

    public FileContext Context { get; init; } = new();

    public string Category { get; init; } = ArtifactCategories.Unknown;

    /// <summary>Short "why is this interesting" line, from the AI when available, otherwise local.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>
    /// Report-only note produced when the narrative was written. Kept separate from
    /// <see cref="Summary"/> so a measured fact and an interpretation never get mixed up.
    /// </summary>
    public string? NarrativeNote { get; set; }

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public DateTimeOffset DiscoveredUtc { get; init; } = DateTimeOffset.UtcNow;

    [JsonIgnore]
    public string FullPath => File.FullPath;

    [JsonIgnore]
    public string FileName => File.FileName;

    [JsonIgnore]
    public double FinalScore => Score.FinalScore;

    /// <summary>Rank inside the session after fusion; 1 is the most interesting artifact.</summary>
    public int Rank { get; set; }
}

/// <summary>Everything the pipeline learned about a file's surroundings; also the payload sent to the AI.</summary>
public sealed class FileContext
{
    public string DirectoryPath { get; init; } = string.Empty;

    /// <summary>Names of sibling files (name only, no full paths) used as structural evidence.</summary>
    public IReadOnlyList<string> NearbyFiles { get; init; } = Array.Empty<string>();

    /// <summary>Number of sibling files discovered in the same folder.</summary>
    public int SiblingCount { get; init; }

    /// <summary>True when the folder looks like a self-contained project (readme, sources, assets, manifest...).</summary>
    public bool LooksLikeProject { get; init; }

    /// <summary>Bounded text excerpt (never the whole file) when the privacy mode allows it.</summary>
    public string? ContentExcerpt { get; init; }

    /// <summary>True when <see cref="ContentExcerpt"/> was truncated because the file is larger.</summary>
    public bool ContentTruncated { get; init; }

    /// <summary>How many bytes of the original file the excerpt represents.</summary>
    public int ContentBytesRead { get; init; }

    public string? DetectedLanguage { get; init; }

    /// <summary>Image dimensions, EXIF summary or archive listing, whichever applies.</summary>
    public string? StructuralSummary { get; init; }
}

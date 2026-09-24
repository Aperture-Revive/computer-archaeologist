using System.Text.Json.Serialization;

namespace ComputerArchaeologist.Core.Models;

/// <summary>One archaeology run. Persisted as JSON so the previous report can always be reopened.</summary>
public sealed class ArchaeologySession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString("N");

    public DateTimeOffset StartTime { get; set; } = DateTimeOffset.UtcNow;

    public DateTimeOffset? EndTime { get; set; }

    public string Language { get; set; } = "en-US";

    public string DiscoverySource { get; set; } = string.Empty;

    /// <summary>The scope this run was restricted to. Empty means the whole machine.</summary>
    public IReadOnlyList<string> Roots { get; set; } = Array.Empty<string>();

    public long IndexedFileCount { get; set; }

    public int FilesDiscovered { get; set; }

    public int Candidates { get; set; }

    public int FilesAnalyzed { get; set; }

    /// <summary>How many candidates received AI analysis (successful requests only).</summary>
    public int FilesAnalyzedByAi { get; set; }

    public IReadOnlyList<FileArtifact> FinalDiscoveries { get; set; } = Array.Empty<FileArtifact>();

    public ArchaeologyReport? Report { get; set; }

    public bool Cancelled { get; set; }

    public bool AiAvailable { get; set; }

    public string? AiError { get; set; }

    public IReadOnlyList<string> Warnings { get; set; } = Array.Empty<string>();

    [JsonIgnore]
    public TimeSpan Duration => (EndTime ?? DateTimeOffset.UtcNow) - StartTime;

    /// <summary>Lightweight header used by the Home page without deserialising every artifact.</summary>
    public SessionSummary ToSummary() => new()
    {
        SessionId = SessionId,
        StartTime = StartTime,
        EndTime = EndTime,
        FilesDiscovered = FilesDiscovered,
        Candidates = Candidates,
        FilesAnalyzedByAi = FilesAnalyzedByAi,
        Discoveries = FinalDiscoveries.Count,
        Cancelled = Cancelled,
        Language = Language,
        AiAvailable = AiAvailable,
    };
}

public sealed class SessionSummary
{
    public string SessionId { get; set; } = string.Empty;

    public DateTimeOffset StartTime { get; set; }

    public DateTimeOffset? EndTime { get; init; }

    public int FilesDiscovered { get; init; }

    public int Candidates { get; init; }

    public int FilesAnalyzedByAi { get; init; }

    public int Discoveries { get; init; }

    public bool Cancelled { get; init; }

    public string Language { get; set; } = "en-US";

    public bool AiAvailable { get; init; }
}

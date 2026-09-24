using System.Text.Json.Serialization;

namespace ComputerArchaeologist.Core.Models;

/// <summary>The complete archaeology report. Reports are rendered inside the application (never a browser).</summary>
public sealed class ArchaeologyReport
{
    public string Title { get; init; } = string.Empty;

    /// <summary>Language the narrative was generated in, for example <c>en-US</c> or <c>zh-CN</c>.</summary>
    public string Language { get; init; } = "en-US";

    public DateTimeOffset GeneratedUtc { get; init; } = DateTimeOffset.UtcNow;

    // ----- Facts (measured, never invented) -----

    public long IndexedFileCount { get; init; }

    public int FilesDiscovered { get; init; }

    public int CandidatesExamined { get; init; }

    public int FilesAnalyzedByAi { get; init; }

    public int DiscoveriesCount { get; init; }

    public string DiscoverySource { get; init; } = string.Empty;

    /// <summary>Human readable description of the run scope, or null for the whole machine.</summary>
    public string? Scope { get; init; }

    public TimeSpan Duration { get; init; }

    public bool AiAvailable { get; init; }

    /// <summary>Set when part of the run had to fall back to local-only scoring.</summary>
    public bool PartialAiFailure { get; init; }

    public string? AiError { get; init; }

    // ----- Narrative -----

    public string Overview { get; init; } = string.Empty;

    public IReadOnlyList<ReportSection> Sections { get; init; } = Array.Empty<ReportSection>();

    public IReadOnlyList<ReportTimelineEntry> Timeline { get; init; } = Array.Empty<ReportTimelineEntry>();

    public string AiObservations { get; init; } = string.Empty;

    public string FinalSummary { get; init; } = string.Empty;

    /// <summary>True when <see cref="Overview"/>, <see cref="AiObservations"/> and <see cref="FinalSummary"/> came from the model.</summary>
    public bool NarrativeFromAi { get; init; }

    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

/// <summary>A named group of findings, for example "Forgotten Projects".</summary>
public sealed class ReportSection
{
    public required string Id { get; init; }

    public required string TitleKey { get; init; }

    public string? IntroKey { get; init; }

    public IReadOnlyList<FileArtifact> Artifacts { get; init; } = Array.Empty<FileArtifact>();

    [JsonIgnore]
    public bool IsEmpty => Artifacts.Count == 0;
}

/// <summary>One entry on the machine-history timeline.</summary>
public sealed class ReportTimelineEntry
{
    public required int Year { get; init; }

    public int ArtifactCount { get; init; }

    public string? HighlightName { get; init; }

    public string? HighlightPath { get; init; }

    public double HighlightScore { get; init; }
}

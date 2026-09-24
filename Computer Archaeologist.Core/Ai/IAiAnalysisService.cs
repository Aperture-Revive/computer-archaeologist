using ComputerArchaeologist.Core.Analysis;
using ComputerArchaeologist.Core.Models;

namespace ComputerArchaeologist.Core.Ai;

/// <summary>One file handed to the semantic engine, together with everything already known locally.</summary>
public sealed record AiFileRequest
{
    public required FileMetadata File { get; init; }

    public required FileContext Context { get; init; }

    public required LocalScoreBreakdown Local { get; init; }

    /// <summary>BCP-47 tag the model must answer in.</summary>
    public string Language { get; init; } = "en-US";

    public PrivacyMode PrivacyMode { get; init; } = PrivacyMode.MetadataOnly;
}

/// <summary>Measured facts about a run, used as the spine of the report.</summary>
public sealed record ReportFacts
{
    public long IndexedFiles { get; init; }

    public int FilesDiscovered { get; init; }

    public int Candidates { get; init; }

    public int AnalyzedByAi { get; init; }

    public TimeSpan Duration { get; init; }

    public string SourceLabel { get; init; } = string.Empty;

    public bool AiAvailable { get; init; }
}

public sealed record AiReportRequest
{
    public required IReadOnlyList<FileArtifact> Discoveries { get; init; }

    public required ReportFacts Facts { get; init; }

    public string Language { get; init; } = "en-US";
}

/// <summary>The narrative half of a report. Every field here is explicitly an AI interpretation.</summary>
public sealed record AiReportNarrative
{
    public string Overview { get; init; } = string.Empty;

    public string Observations { get; init; } = string.Empty;

    public string FinalSummary { get; init; } = string.Empty;

    /// <summary>Per-artifact one-line notes keyed by full path.</summary>
    public IReadOnlyDictionary<string, string> Highlights { get; init; } =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
}

public sealed record AiConnectionTestResult
{
    public bool Success { get; init; }

    /// <summary>Localization key describing the outcome.</summary>
    public string MessageKey { get; init; } = "Ai_TestFailed";

    public string? Detail { get; init; }

    public string? Model { get; init; }

    public TimeSpan Latency { get; init; }

    public int? StatusCode { get; init; }
}

/// <summary>
/// The semantic half of the application. The view models never touch the OpenAI client directly;
/// they only ever see this interface, which keeps the provider replaceable.
/// </summary>
public interface IAiAnalysisService
{
    /// <summary>False when no API key is stored or the privacy mode disables AI entirely.</summary>
    bool IsConfigured { get; }

    string Model { get; }

    /// <summary>Issues the smallest possible request to prove key, endpoint and model all work.</summary>
    Task<AiConnectionTestResult> TestConnectionAsync(CancellationToken cancellationToken = default);

    Task<AiAnalysisResult> AnalyzeFileAsync(AiFileRequest request, CancellationToken cancellationToken = default);

    /// <summary>Returns null when the narrative could not be produced; the report still renders locally.</summary>
    Task<AiReportNarrative?> GenerateReportNarrativeAsync(AiReportRequest request, CancellationToken cancellationToken = default);
}

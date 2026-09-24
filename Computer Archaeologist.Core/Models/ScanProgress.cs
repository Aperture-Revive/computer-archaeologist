namespace ComputerArchaeologist.Core.Models;

/// <summary>How much of a file may ever leave the machine (specification sections 14 and 34).</summary>
public enum PrivacyMode
{
    /// <summary>Only file name, path, timestamps, size, extension and folder context are sent. Default.</summary>
    MetadataOnly = 0,

    /// <summary>A small, bounded excerpt of selected text-like files may be sent as well.</summary>
    SmartContent = 1,

    /// <summary>No AI request is ever issued. The application still produces a local report.</summary>
    Disabled = 2,
}

/// <summary>Pipeline stages, in the order the Archaeology page displays them.</summary>
public enum ScanStage
{
    Idle = 0,
    Preparing = 1,
    DiscoveringFiles = 2,
    CollectingMetadata = 3,
    LocalScoring = 4,
    AiAnalysis = 5,
    FinalScoring = 6,
    GeneratingReport = 7,
    Completed = 8,
    Cancelled = 9,
    Failed = 10,
}

/// <summary>A progress snapshot pushed to the UI. Everything on this object is measured, never faked.</summary>
public sealed record ScanProgress
{
    public ScanStage Stage { get; init; } = ScanStage.Idle;

    public double StageProgress { get; init; }

    public double OverallProgress { get; init; }

    public long FilesScanned { get; init; }

    public long? FilesTotal { get; init; }

    public int Candidates { get; init; }

    public int AiAnalyzed { get; init; }

    public int AiPlanned { get; init; }

    public string? CurrentItem { get; init; }

    public string? MessageKey { get; init; }

    public string? Detail { get; init; }

    public bool IsAiDegraded { get; init; }

    public string? AiError { get; init; }
}

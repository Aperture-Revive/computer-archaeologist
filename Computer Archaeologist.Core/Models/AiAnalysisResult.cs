using System.Text.Json.Serialization;

namespace ComputerArchaeologist.Core.Models;

/// <summary>The structured semantic analysis returned by the AI engine (section 12 of the specification).</summary>
public sealed record AiAnalysisResult
{
    public double Interestingness { get; init; }

    public double Confidence { get; init; }

    public string Category { get; init; } = ArtifactCategories.Unknown;

    public string Summary { get; init; } = string.Empty;

    public IReadOnlyList<string> Reasons { get; init; } = Array.Empty<string>();

    public double HistoricalSignificance { get; init; }

    public double PersonalSignificance { get; init; }

    public double TechnicalSignificance { get; init; }

    public double Rarity { get; init; }

    public double Unusualness { get; init; }

    public double StoryPotential { get; init; }

    public string RecommendedAction { get; init; } = RecommendedActions.Maybe;

    /// <summary>Model that produced this analysis.</summary>
    public string Model { get; init; } = string.Empty;

    /// <summary>False when the request failed and the pipeline fell back to local scoring only.</summary>
    public bool Succeeded { get; init; }

    /// <summary>Human readable failure description when <see cref="Succeeded"/> is false.</summary>
    public string? Error { get; init; }

    /// <summary>True when the first response was not valid JSON and had to be repaired.</summary>
    public bool RequiredRepair { get; init; }

    [JsonIgnore]
    public bool Include => string.Equals(RecommendedAction, RecommendedActions.Include, StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool Exclude => string.Equals(RecommendedAction, RecommendedActions.Exclude, StringComparison.OrdinalIgnoreCase);

    public static AiAnalysisResult Unavailable(string error, string model = "") => new()
    {
        Succeeded = false,
        Error = error,
        Model = model,
        Confidence = 0,
        Interestingness = 0,
        RecommendedAction = RecommendedActions.Maybe,
    };
}

public static class RecommendedActions
{
    public const string Include = "include";
    public const string Maybe = "maybe";
    public const string Exclude = "exclude";

    public static bool IsValid(string? value) =>
        string.Equals(value, Include, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Maybe, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, Exclude, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Categories the AI may assign. The key doubles as a localisation resource key suffix.</summary>
public static class ArtifactCategories
{
    public const string ForgottenProject = "forgotten_project";
    public const string OldDocument = "old_document";
    public const string OldPhoto = "old_photo";
    public const string GameSave = "game_save";
    public const string SourceCode = "source_code";
    public const string StrangeFile = "strange_file";
    public const string Configuration = "configuration";
    public const string LogFile = "log_file";
    public const string Archive = "archive";
    public const string PersonalNote = "personal_note";
    public const string SchoolWork = "school_work";
    public const string Media = "media";
    public const string Unknown = "unknown";

    public static readonly IReadOnlyList<string> All = new[]
    {
        ForgottenProject, OldDocument, OldPhoto, GameSave, SourceCode, StrangeFile,
        Configuration, LogFile, Archive, PersonalNote, SchoolWork, Media, Unknown,
    };

    public static string Normalize(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return Unknown;
        }

        var value = raw.Trim().ToLowerInvariant().Replace(' ', '_').Replace('-', '_');
        return All.Contains(value) ? value : Unknown;
    }
}

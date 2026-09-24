using System.Text.Json.Serialization;

namespace ComputerArchaeologist.Core.Models;

/// <summary>
/// Every local, explainable feature that contributes to the local interestingness score.
/// Each individual feature is normalised to 0..100 and the weighted sum produces
/// <see cref="LocalScore"/>. The weights live in <c>InterestingnessWeightsOptions</c>, never here.
/// </summary>
public sealed class LocalScoreBreakdown
{
    /// <summary>How old the artifact is, using a non-linear curve (old is not automatically interesting).</summary>
    public double Age { get; init; }

    /// <summary>How rare the file extension is across the whole indexed machine.</summary>
    public double Rarity { get; init; }

    /// <summary>Semantic value of the path (Documents/School/... versus AppData/Local/Temp/...).</summary>
    public double PathContext { get; init; }

    /// <summary>Signals found in the file name itself (old, backup, final2, diary, ...).</summary>
    public double Filename { get; init; }

    /// <summary>Does the surrounding folder look like a project / cluster of related files?</summary>
    public double Cluster { get; init; }

    /// <summary>School work, personal projects, game saves, notes: things a human made.</summary>
    public double PersonalArtifact { get; init; }

    /// <summary>Created long ago and never touched again = abandoned, which is archaeologically interesting.</summary>
    public double ModificationPattern { get; init; }

    /// <summary>Structural oddities: no extension, very long name, emoji, doubled extensions, ...</summary>
    public double Unusualness { get; init; }

    /// <summary>Weighted local score in the 0..100 range.</summary>
    public double LocalScore { get; init; }

    /// <summary>Human readable, evidence-backed explanation for every contribution above.</summary>
    public IReadOnlyList<ScoreReason> Reasons { get; init; } = Array.Empty<ScoreReason>();

    /// <summary>Number of sibling files found in the same directory (used by the cluster feature).</summary>
    public int DirectorySiblingCount { get; init; }

    /// <summary>True when the file was already fingerprinted with SHA-256.</summary>
    public bool HasHash { get; init; }
}

/// <summary>A single auditable reason behind a score, so the UI never shows a naked number.</summary>
/// <param name="Feature">Feature key, for example <c>Age</c> or <c>PathContext</c>.</param>
/// <param name="Weight">Weight applied to this feature, 0..1.</param>
/// <param name="Value">Normalised feature value, 0..100.</param>
/// <param name="Weighted">Contribution to the local score, 0..100.</param>
/// <param name="ExplanationKey">
/// Localisation resource key. The Core never hard-codes prose: the active
/// <c>ILocalizationService</c> resolves the key, which keeps English and Simplified Chinese
/// in a single translatable catalogue.
/// </param>
/// <param name="Arguments">Arguments substituted into the localized template.</param>
public sealed record ScoreReason(
    string Feature,
    double Weight,
    double Value,
    double Weighted,
    string ExplanationKey,
    IReadOnlyList<string>? Arguments = null)
{
    /// <summary>Invariant, culture-free rendering used for logs and diagnostics.</summary>
    public override string ToString() =>
        Arguments is { Count: > 0 }
            ? $"{Feature}={Value:0.#} ({Weighted:0.##}) {ExplanationKey}[{string.Join(", ", Arguments)}]"
            : $"{Feature}={Value:0.#} ({Weighted:0.##}) {ExplanationKey}";
}

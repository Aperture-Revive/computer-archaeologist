namespace ComputerArchaeologist.Core.Models;

/// <summary>
/// All tunable weights for the interestingness algorithm live here and nowhere else
/// (specification section 23: weights must be centrally configured, not scattered).
/// </summary>
public sealed class InterestingnessWeightsOptions
{
    public const string SectionName = "Interestingness";

    // ---- Local feature weights. They are normalised at runtime, so they do not have to sum to 1. ----
    public double Age { get; set; } = 0.15;
    public double Rarity { get; set; } = 0.15;
    public double PathContext { get; set; } = 0.15;
    public double Filename { get; set; } = 0.10;
    public double Cluster { get; set; } = 0.15;
    public double PersonalArtifact { get; set; } = 0.15;
    public double ModificationPattern { get; set; } = 0.10;
    public double Unusualness { get; set; } = 0.05;

    // ---- Fusion between local evidence and AI judgement ----
    /// <summary>Configured weight of the AI score. The local weight is always 1 - this value.</summary>
    public double AiWeight { get; set; } = 0.55;

    /// <summary>AI weight is multiplied by (Confidence/100) clamped into [MinConfidenceScale, 1].</summary>
    public double MinConfidenceScale { get; set; } = 0.10;

    /// <summary>Below this confidence the AI judgement contributes almost nothing.</summary>
    public double LowConfidenceThreshold { get; set; } = 30;

    // ---- Age curve, in years ----
    public double AgeLowYears { get; set; } = 1;
    public double AgeMidYears { get; set; } = 3;
    public double AgeHighYears { get; set; } = 5;
    public double AgeVeryHighYears { get; set; } = 10;

    /// <summary>Age is capped so that "old" alone can never produce a high score.</summary>
    public double AgeCeiling { get; set; } = 92;

    /// <summary>Score added when the file was created long ago and never modified again.</summary>
    public double AbandonedBonus { get; set; } = 22;

    /// <summary>Score removed when the file was modified in the last few months (it is still alive).</summary>
    public double RecentlyTouchedPenalty { get; set; } = 25;

    [System.Text.Json.Serialization.JsonIgnore]
    public double PendingAiWeight => Math.Clamp(AiWeight, 0, 1);

    public IReadOnlyDictionary<string, double> AsLocalWeightMap() => new Dictionary<string, double>(StringComparer.Ordinal)
    {
        ["Age"] = Math.Max(0, Age),
        ["Rarity"] = Math.Max(0, Rarity),
        ["PathContext"] = Math.Max(0, PathContext),
        ["Filename"] = Math.Max(0, Filename),
        ["Cluster"] = Math.Max(0, Cluster),
        ["PersonalArtifact"] = Math.Max(0, PersonalArtifact),
        ["ModificationPattern"] = Math.Max(0, ModificationPattern),
        ["Unusualness"] = Math.Max(0, Unusualness),
    };
}

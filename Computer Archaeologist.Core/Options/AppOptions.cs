using System.Text.Json.Serialization;
using ComputerArchaeologist.Core.Models;

namespace ComputerArchaeologist.Core.Options;

/// <summary>
/// Non-sensitive settings that live in <c>settings.json</c>. The API key is deliberately absent:
/// it is stored in the Windows Credential Manager instead (specification section 15).
/// </summary>
public sealed class AppOptions
{
    /// <summary>Light, Dark or System.</summary>
    public string Theme { get; set; } = "System";

    /// <summary>zh-CN, en-US or System.</summary>
    public string Language { get; set; } = "System";

    public string? LastSessionId { get; set; }
}

public sealed class OpenAiOptions
{
    public string BaseUrl { get; set; } = "https://api.openai.com/v1";

    public string Model { get; set; } = "gpt-4o-mini";

    public int TimeoutSeconds { get; set; } = 60;

    /// <summary>Maximum retries for 429/5xx/timeouts. Capped, never infinite.</summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>Base delay for exponential backoff: 1s, 2s, 4s.</summary>
    public double BaseRetryDelaySeconds { get; set; } = 1.0;

    /// <summary>Concurrent AI requests. Kept deliberately small (specification section 25).</summary>
    public int MaxConcurrency { get; set; } = 3;

    /// <summary>Ask the endpoint for <c>response_format: json_object</c>. Disabled automatically if unsupported.</summary>
    public bool UseJsonResponseFormat { get; set; } = true;

    /// <summary>Maximum tokens requested per analysis.</summary>
    public int MaxOutputTokens { get; set; } = 900;

    [JsonIgnore]
    public bool HasUsableEndpoint =>
        Uri.TryCreate(BaseUrl, UriKind.Absolute, out var uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    [JsonIgnore]
    public TimeSpan Timeout => TimeSpan.FromSeconds(Math.Clamp(TimeoutSeconds, 5, 600));

    [JsonIgnore]
    public int SafeMaxRetries => Math.Clamp(MaxRetries, 0, 6);

    [JsonIgnore]
    public int SafeMaxConcurrency => Math.Clamp(MaxConcurrency, 1, 8);
}

/// <summary>
/// How the local file system is walked. The application indexes nothing and depends on no external
/// service: it reads directory listings directly, in parallel, with hard budgets.
/// </summary>
public sealed class DiscoveryOptions
{
    /// <summary>Hard cap on how many files a single run may inspect.</summary>
    public int MaxFiles { get; set; } = 400_000;

    /// <summary>Wall-clock budget for the walk, in minutes. Zero disables the budget.</summary>
    public int BudgetMinutes { get; set; } = 10;

    /// <summary>How many directories are walked in parallel. Higher is faster on SSD, noisier on HDD.</summary>
    public int Concurrency { get; set; } = 6;

    [JsonIgnore]
    public int SafeMaxFiles => MaxFiles <= 0 ? int.MaxValue : Math.Clamp(MaxFiles, 1_000, 20_000_000);

    [JsonIgnore]
    public int SafeConcurrency => Math.Clamp(Concurrency, 1, 32);

    [JsonIgnore]
    public TimeSpan Budget => BudgetMinutes <= 0
        ? Timeout.InfiniteTimeSpan
        : TimeSpan.FromMinutes(Math.Clamp(BudgetMinutes, 1, 240));
}

public sealed class ArchaeologyOptions
{
    /// <summary>How many locally scored candidates are kept for the deep local stage (a bounded heap).</summary>
    public int MaxCandidates { get; set; } = 5_000;

    /// <summary>How many candidates are sent to the AI stage.</summary>
    public int MaxAiAnalysis { get; set; } = 300;

    /// <summary>How many artifacts end up in the report and the Discoveries page.</summary>
    public int MaxDiscoveries { get; set; } = 40;

    /// <summary>Artifacts below this final score are never shown.</summary>
    public double MinInterestingness { get; set; } = 55;

    public PrivacyMode PrivacyMode { get; set; } = PrivacyMode.MetadataOnly;

    public bool AnalyzeCode { get; set; } = true;

    public bool AnalyzeImages { get; set; }

    public bool AnalyzeArchives { get; set; }

    /// <summary>Maximum bytes read from a single file when SmartContent is enabled.</summary>
    public int MaxContentBytes { get; set; } = 32 * 1024;

    /// <summary>Compute SHA-256 only for the top candidates, never for the whole disk.</summary>
    public bool ComputeHashesForTopCandidates { get; set; } = true;

    /// <summary>Maximum concurrent file reads during the metadata stage.</summary>
    public int MaxFileReadConcurrency { get; set; } = 8;

    public List<string> ExcludedPaths { get; set; } = new();

    public List<string> ExcludedExtensions { get; set; } = new();

    /// <summary>Restrict discovery to these roots (empty = the whole machine).</summary>
    public List<string> IncludedRoots { get; set; } = new();

    [JsonIgnore]
    public int SafeMaxCandidates => Math.Clamp(MaxCandidates, 50, 200_000);

    [JsonIgnore]
    public int SafeMaxAiAnalysis => Math.Clamp(MaxAiAnalysis, 0, 5_000);

    [JsonIgnore]
    public int SafeMaxDiscoveries => Math.Clamp(MaxDiscoveries, 5, 500);

    [JsonIgnore]
    public double SafeMinInterestingness => Math.Clamp(MinInterestingness, 0, 100);

    [JsonIgnore]
    public int SafeMaxContentBytes => Math.Clamp(MaxContentBytes, 512, 256 * 1024);

    [JsonIgnore]
    public int SafeFileReadConcurrency => Math.Clamp(MaxFileReadConcurrency, 1, 32);
}

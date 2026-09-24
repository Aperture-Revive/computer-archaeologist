using System.Text.Json;
using System.Text.Json.Serialization;
using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Infrastructure;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Options;
using Microsoft.Extensions.Logging;

namespace ComputerArchaeologist.Core.Settings;

/// <summary>Persisted settings. Note the absence of any credential field.</summary>
public sealed class SettingsDocument
{
    public int Version { get; set; } = 1;

    public AppOptions App { get; set; } = new();

    public OpenAiOptions OpenAi { get; set; } = new();

    public DiscoveryOptions Discovery { get; set; } = new();

    public ArchaeologyOptions Archaeology { get; set; } = new();

    public InterestingnessWeightsOptions Interestingness { get; set; } = new();
}

/// <summary>
/// Loads and persists <c>settings.json</c>. All option objects are process-wide singletons whose
/// properties are mutated in place, so a change made on the Settings page is immediately visible to
/// the services that already hold a reference.
/// </summary>
public interface ISettingsService
{
    AppOptions App { get; }

    OpenAiOptions OpenAi { get; }

    DiscoveryOptions Discovery { get; }

    ArchaeologyOptions Archaeology { get; }

    InterestingnessWeightsOptions Weights { get; }

    string SettingsFilePath { get; }

    bool LastLoadFailed { get; }

    event EventHandler? SettingsChanged;

    Task LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Records which run the user last looked at without touching any other setting.
    /// <para>
    /// This is deliberately separate from <see cref="SaveAsync"/>: a background session bookmark must
    /// never be able to rewrite the user's options from possibly-stale in-memory state.
    /// </para>
    /// </summary>
    Task RememberLastSessionAsync(string sessionId, CancellationToken cancellationToken = default);

    void ResetToDefaults();
}

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions SerializerOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly ExclusionPolicyProvider _policyProvider;
    private readonly ILogger<SettingsService>? _logger;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SettingsService(
        AppOptions app,
        OpenAiOptions openAi,
        DiscoveryOptions discovery,
        ArchaeologyOptions archaeology,
        InterestingnessWeightsOptions weights,
        ExclusionPolicyProvider policyProvider,
        ILogger<SettingsService>? logger = null)
    {
        App = app;
        OpenAi = openAi;
        Discovery = discovery;
        Archaeology = archaeology;
        Weights = weights;
        _policyProvider = policyProvider;
        _logger = logger;
    }

    public AppOptions App { get; }

    public OpenAiOptions OpenAi { get; }

    public DiscoveryOptions Discovery { get; }

    public ArchaeologyOptions Archaeology { get; }

    public InterestingnessWeightsOptions Weights { get; }

    public string SettingsFilePath => AppPaths.SettingsFile;

    public bool LastLoadFailed { get; private set; }

    public event EventHandler? SettingsChanged;

    public async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppPaths.EnsureCreated();

            if (!File.Exists(SettingsFilePath))
            {
                LastLoadFailed = false;
                _logger?.LogInformation("No settings file yet; using defaults at {Path}", SettingsFilePath);
                _policyProvider.Refresh();
                return;
            }

            try
            {
                await using var stream = File.OpenRead(SettingsFilePath);
                var document = await JsonSerializer.DeserializeAsync<SettingsDocument>(stream, SerializerOptions, cancellationToken)
                    .ConfigureAwait(false);

                if (document is null)
                {
                    LastLoadFailed = true;
                    _logger?.LogWarning("settings.json was empty; defaults are in use");
                    return;
                }

                Apply(document);
                LastLoadFailed = false;
                _logger?.LogInformation("Settings loaded from {Path}", SettingsFilePath);
            }
            catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
            {
                // A corrupt settings file must never stop the application from starting.
                LastLoadFailed = true;
                _logger?.LogError(ex, "settings.json could not be read; defaults are in use");
                TryQuarantine();
            }

            _policyProvider.Refresh();
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// Patches only <c>app.lastSessionId</c> in the settings file. Everything else is read back from
    /// disk first, so the file stays exactly as the user last saved it.
    /// </summary>
    public async Task RememberLastSessionAsync(string sessionId, CancellationToken cancellationToken = default)
    {
        App.LastSessionId = sessionId;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppPaths.EnsureCreated();

            SettingsDocument document;
            if (File.Exists(SettingsFilePath))
            {
                try
                {
                    await using var read = File.OpenRead(SettingsFilePath);
                    document = await JsonSerializer.DeserializeAsync<SettingsDocument>(read, SerializerOptions, cancellationToken)
                        .ConfigureAwait(false) ?? new SettingsDocument();
                }
                catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
                {
                    _logger?.LogWarning(ex, "settings.json could not be read while bookmarking a run; writing a fresh document");
                    document = new SettingsDocument();
                }
            }
            else
            {
                document = new SettingsDocument();
            }

            document.App ??= new AppOptions();
            document.App.LastSessionId = sessionId;

            var json = JsonSerializer.Serialize(document, SerializerOptions);
            var temporary = SettingsFilePath + ".tmp";
            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, SettingsFilePath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "The last session pointer could not be recorded");
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            AppPaths.EnsureCreated();

            var document = new SettingsDocument
            {
                App = App,
                OpenAi = OpenAi,
                Discovery = Discovery,
                Archaeology = Archaeology,
                Interestingness = Weights,
            };

            var json = JsonSerializer.Serialize(document, SerializerOptions);
            var temporary = SettingsFilePath + ".tmp";
            await File.WriteAllTextAsync(temporary, json, cancellationToken).ConfigureAwait(false);
            File.Move(temporary, SettingsFilePath, overwrite: true);

            _policyProvider.Refresh();

            // The caller is recorded because a full settings write is only ever legitimate from the
            // Settings page; anything else rewriting the document is a bug worth seeing in the log.
            var caller = new System.Diagnostics.StackTrace(1, false).GetFrame(0)?.GetMethod();
            _logger?.LogInformation(
                "Settings saved to {Path} (theme={Theme} language={Language} roots={Roots} maxFiles={MaxFiles}) by {Caller}",
                SettingsFilePath,
                App.Theme,
                App.Language,
                Archaeology.IncludedRoots.Count,
                Discovery.MaxFiles,
                caller is null ? "unknown" : $"{caller.DeclaringType?.Name}.{caller.Name}");
            SettingsChanged?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogError(ex, "Settings could not be saved");
            throw;
        }
        finally
        {
            _gate.Release();
        }
    }

    public void ResetToDefaults()
    {
        Apply(new SettingsDocument());
        _policyProvider.Refresh();
        SettingsChanged?.Invoke(this, EventArgs.Empty);
    }

    private void Apply(SettingsDocument document)
    {
        Copy(document.App ?? new AppOptions(), App);
        Copy(document.OpenAi ?? new OpenAiOptions(), OpenAi);
        Copy(document.Discovery ?? new DiscoveryOptions(), Discovery);
        Copy(document.Archaeology ?? new ArchaeologyOptions(), Archaeology);
        Copy(document.Interestingness ?? new InterestingnessWeightsOptions(), Weights);

        // Guard rails against a hand-edited file.
        OpenAi.BaseUrl = OpenAiOptionsNormalizer.Normalize(OpenAi.BaseUrl);
        OpenAi.Model = string.IsNullOrWhiteSpace(OpenAi.Model) ? "gpt-4o-mini" : OpenAi.Model.Trim();
        Discovery.MaxFiles = Discovery.SafeMaxFiles;
        Discovery.Concurrency = Discovery.SafeConcurrency;
        Discovery.BudgetMinutes = Math.Clamp(Discovery.BudgetMinutes, 0, 240);

        OpenAi.MaxConcurrency = Math.Clamp(OpenAi.MaxConcurrency, 1, 8);
        OpenAi.MaxRetries = Math.Clamp(OpenAi.MaxRetries, 0, 6);
        OpenAi.TimeoutSeconds = Math.Clamp(OpenAi.TimeoutSeconds, 5, 600);

        Archaeology.MaxCandidates = Archaeology.SafeMaxCandidates;
        Archaeology.MaxAiAnalysis = Archaeology.SafeMaxAiAnalysis;
        Archaeology.MaxDiscoveries = Archaeology.SafeMaxDiscoveries;
        Archaeology.MinInterestingness = Archaeology.SafeMinInterestingness;

        if (!Enum.IsDefined(Archaeology.PrivacyMode))
        {
            Archaeology.PrivacyMode = PrivacyMode.MetadataOnly;
        }

        App.Theme = App.Theme is "Light" or "Dark" or "System" ? App.Theme : "System";
    }

    private void TryQuarantine()
    {
        try
        {
            var target = SettingsFilePath + $".corrupt-{DateTimeOffset.UtcNow:yyyyMMddHHmmss}";
            File.Move(SettingsFilePath, target, overwrite: true);
            _logger?.LogWarning("Corrupt settings file moved to {Path}", target);
        }
        catch (IOException)
        {
            // Nothing else to try.
        }
    }

    /// <summary>Member-wise copy so the singleton instances keep their identity.</summary>
    private static void Copy<T>(T source, T target)
    {
        foreach (var property in typeof(T).GetProperties())
        {
            if (!property.CanRead || !property.CanWrite)
            {
                continue;
            }

            property.SetValue(target, property.GetValue(source));
        }
    }
}

internal static class OpenAiOptionsNormalizer
{
    public static string Normalize(string? baseUrl)
    {
        var value = (baseUrl ?? string.Empty).Trim();
        if (value.Length == 0)
        {
            return "https://api.openai.com/v1";
        }

        return value.TrimEnd('/');
    }
}

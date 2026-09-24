using System.Collections.ObjectModel;
using System.Globalization;
using ComputerArchaeologist.Core.Ai;
using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Infrastructure;
using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Security;
using ComputerArchaeologist.Core.Settings;
using ComputerArchaeologist.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ComputerArchaeologist.ViewModels;

/// <summary>
/// One editable string in a list (excluded path, excluded extension, included root).
/// The remove command lives on the item so XAML templates need no cross-template lookup.
/// </summary>
public sealed class ListEntryViewModel
{
    public ListEntryViewModel(string value, Action<string> remove)
    {
        Value = value;
        RemoveCommand = new RelayCommand(() => remove(value));
    }

    public string Value { get; }

    public RelayCommand RemoveCommand { get; }
}

/// <summary>
/// The Settings page. It reads and writes the same option singletons the services use, and it never
/// stores the API key anywhere except secure storage.
/// </summary>
public sealed partial class SettingsViewModel : ObservableObject
{
    private readonly ISettingsService _settings;
    private readonly ISecureStorage _secureStorage;
    private readonly IAiAnalysisService _ai;
    private readonly ExclusionPolicyProvider _exclusions;
    private readonly IThemeService _theme;
    private readonly ILocalizationService _localization;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<SettingsViewModel>? _logger;

    /// <summary>True once the form has been populated from the persisted options.</summary>
    private bool _loaded;

    [ObservableProperty]
    private string _apiKeyInput = string.Empty;

    [ObservableProperty]
    private bool _hasStoredKey;

    [ObservableProperty]
    private string _keyStorageText = string.Empty;

    [ObservableProperty]
    private string _baseUrl = string.Empty;

    [ObservableProperty]
    private string _model = string.Empty;

    [ObservableProperty]
    private string _connectionStatus = string.Empty;

    [ObservableProperty]
    private bool _connectionStatusVisible;

    [ObservableProperty]
    private bool _connectionSucceeded;

    [ObservableProperty]
    private bool _isTesting;

    [ObservableProperty]
    private double _maxFiles;

    [ObservableProperty]
    private double _budgetMinutes;

    [ObservableProperty]
    private double _discoveryConcurrency;

    [ObservableProperty]
    private double _maxCandidates;

    [ObservableProperty]
    private double _maxAiAnalysis;

    [ObservableProperty]
    private double _maxDiscoveries;

    [ObservableProperty]
    private double _minInterestingness;

    [ObservableProperty]
    private double _maxContentBytes;

    [ObservableProperty]
    private bool _analyzeImages;

    [ObservableProperty]
    private bool _analyzeCode;

    [ObservableProperty]
    private bool _analyzeArchives;

    [ObservableProperty]
    private bool _computeHashes;

    [ObservableProperty]
    private bool _useJsonResponseFormat;

    [ObservableProperty]
    private int _privacyIndex;

    [ObservableProperty]
    private int _themeIndex;

    [ObservableProperty]
    private int _languageIndex;

    [ObservableProperty]
    private double _maxConcurrency;

    [ObservableProperty]
    private double _timeoutSeconds;

    [ObservableProperty]
    private double _maxRetries;

    [ObservableProperty]
    private double _aiWeight;

    [ObservableProperty]
    private double _minConfidenceScale;

    [ObservableProperty]
    private string _newExcludedPath = string.Empty;

    [ObservableProperty]
    private string _newExcludedExtension = string.Empty;

    [ObservableProperty]
    private string _newIncludedRoot = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _statusVisible;

    [ObservableProperty]
    private string _dataFolder = string.Empty;

    public SettingsViewModel(
        ISettingsService settings,
        ISecureStorage secureStorage,
        IAiAnalysisService ai,
        ExclusionPolicyProvider exclusions,
        IThemeService theme,
        ILocalizationService localization,
        IUiDispatcher ui,
        ILogger<SettingsViewModel>? logger = null)
    {
        _settings = settings;
        _secureStorage = secureStorage;
        _ai = ai;
        _exclusions = exclusions;
        _theme = theme;
        _localization = localization;
        _ui = ui;
        _logger = logger;

        _localization.LanguageChanged += (_, _) => _ui.Post(RefreshLocalizedText);

        DataFolder = AppPaths.Root;
        LoadFromSettings();
        RefreshLocalizedText();
    }

    /// <summary>Set by the page, which owns the folder picker and the window handle.</summary>
    public Func<Task<string?>>? PickFolderAsync { get; set; }

    public ObservableCollection<ListEntryViewModel> ExcludedPaths { get; } = new();

    public ObservableCollection<ListEntryViewModel> ExcludedExtensions { get; } = new();

    public ObservableCollection<ListEntryViewModel> IncludedRoots { get; } = new();

    /// <summary>Raised when the user asks to reset; the page shows the confirmation dialog.</summary>
    public event EventHandler? ResetToDefaultsRequested;

    /// <summary>
    /// Re-reads the persisted options into the form. Called every time the page is shown so the
    /// controls always reflect what is actually on disk.
    /// </summary>
    public void ReloadFromSettings()
    {
        LoadFromSettings();
        RefreshLocalizedText();
    }

    // ------------------------------------------------------------------ loading

    private void LoadFromSettings()
    {
        try
        {
            var archaeology = _settings.Archaeology;
            var discovery = _settings.Discovery;
            var openAi = _settings.OpenAi;

            BaseUrl = openAi.BaseUrl;
            Model = openAi.Model;
            MaxConcurrency = openAi.MaxConcurrency;
            TimeoutSeconds = openAi.TimeoutSeconds;
            MaxRetries = openAi.MaxRetries;
            UseJsonResponseFormat = openAi.UseJsonResponseFormat;

            MaxFiles = discovery.MaxFiles;
            BudgetMinutes = discovery.BudgetMinutes;
            DiscoveryConcurrency = discovery.Concurrency;

            MaxCandidates = archaeology.MaxCandidates;
            MaxAiAnalysis = archaeology.MaxAiAnalysis;
            MaxDiscoveries = archaeology.MaxDiscoveries;
            MinInterestingness = archaeology.MinInterestingness;
            MaxContentBytes = archaeology.MaxContentBytes;
            AnalyzeImages = archaeology.AnalyzeImages;
            AnalyzeCode = archaeology.AnalyzeCode;
            AnalyzeArchives = archaeology.AnalyzeArchives;
            ComputeHashes = archaeology.ComputeHashesForTopCandidates;
            PrivacyIndex = (int)archaeology.PrivacyMode;

            AiWeight = Math.Round(_settings.Weights.AiWeight * 100, 0);
            MinConfidenceScale = Math.Round(_settings.Weights.MinConfidenceScale * 100, 0);

            ThemeIndex = _settings.App.Theme switch { "Light" => 1, "Dark" => 2, _ => 0 };
            LanguageIndex = _settings.App.Language switch { "en-US" => 1, "zh-CN" => 2, _ => 0 };

            ExcludedPaths.Clear();
            foreach (var path in archaeology.ExcludedPaths)
            {
                ExcludedPaths.Add(new ListEntryViewModel(path, RemoveExcludedPath));
            }

            ExcludedExtensions.Clear();
            foreach (var extension in archaeology.ExcludedExtensions)
            {
                ExcludedExtensions.Add(new ListEntryViewModel(extension, RemoveExcludedExtension));
            }

            IncludedRoots.Clear();
            foreach (var root in archaeology.IncludedRoots)
            {
                IncludedRoots.Add(new ListEntryViewModel(root, RemoveIncludedRoot));
            }

            HasStoredKey = _secureStorage.HasApiKey;
            ApiKeyInput = string.Empty;
            _loaded = true;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "Loading settings into the form failed");
        }
    }

    private void RefreshLocalizedText() =>
        KeyStorageText = _localization.Format(
            "Settings_KeyStorageFormat",
            _localization.Get(_secureStorage.StorageDescriptionKey));

    // ------------------------------------------------------------------ commands

    [RelayCommand]
    private async Task SaveAsync()
    {
        if (!_loaded)
        {
            // Saving an unpopulated form would write defaults over the user's real options.
            _logger?.LogError("Refusing to save settings: the form was never loaded from disk");
            return;
        }

        _logger?.LogInformation(
            "Settings page is saving (themeIndex={Theme} languageIndex={Language} privacyIndex={Privacy} roots={Roots})",
            ThemeIndex,
            LanguageIndex,
            PrivacyIndex,
            IncludedRoots.Count);

        try
        {
            if (!string.IsNullOrWhiteSpace(ApiKeyInput))
            {
                _secureStorage.SetApiKey(ApiKeyInput);
                ApiKeyInput = string.Empty;
                HasStoredKey = _secureStorage.HasApiKey;
            }

            var openAi = _settings.OpenAi;
            openAi.BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? "https://api.openai.com/v1" : BaseUrl.Trim();
            openAi.Model = string.IsNullOrWhiteSpace(Model) ? "gpt-4o-mini" : Model.Trim();
            openAi.MaxConcurrency = (int)Math.Clamp(MaxConcurrency, 1, 8);
            openAi.TimeoutSeconds = (int)Math.Clamp(TimeoutSeconds, 5, 600);
            openAi.MaxRetries = (int)Math.Clamp(MaxRetries, 0, 6);
            openAi.UseJsonResponseFormat = UseJsonResponseFormat;

            var discovery = _settings.Discovery;
            discovery.MaxFiles = (int)Math.Clamp(MaxFiles, 1_000, 20_000_000);
            discovery.BudgetMinutes = (int)Math.Clamp(BudgetMinutes, 0, 240);
            discovery.Concurrency = (int)Math.Clamp(DiscoveryConcurrency, 1, 32);

            var archaeology = _settings.Archaeology;
            archaeology.MaxCandidates = (int)Math.Clamp(MaxCandidates, 50, 200_000);
            archaeology.MaxAiAnalysis = (int)Math.Clamp(MaxAiAnalysis, 0, 5_000);
            archaeology.MaxDiscoveries = (int)Math.Clamp(MaxDiscoveries, 5, 500);
            archaeology.MinInterestingness = Math.Clamp(MinInterestingness, 0, 100);
            archaeology.MaxContentBytes = (int)Math.Clamp(MaxContentBytes, 512, 262_144);
            archaeology.AnalyzeImages = AnalyzeImages;
            archaeology.AnalyzeCode = AnalyzeCode;
            archaeology.AnalyzeArchives = AnalyzeArchives;
            archaeology.ComputeHashesForTopCandidates = ComputeHashes;
            archaeology.ExcludedPaths = ExcludedPaths.Select(e => e.Value).ToList();
            archaeology.ExcludedExtensions = ExcludedExtensions.Select(e => e.Value).ToList();
            archaeology.IncludedRoots = IncludedRoots.Select(e => e.Value).ToList();

            _settings.Weights.AiWeight = Math.Clamp(AiWeight / 100.0, 0, 1);
            _settings.Weights.MinConfidenceScale = Math.Clamp(MinConfidenceScale / 100.0, 0, 1);

            var previousLanguage = _settings.App.Language;

            // Guard against a two-way bound control reporting "no selection" before its items exist:
            // an out-of-range index keeps the previous value instead of silently resetting it.
            if (ThemeIndex is >= 0 and <= 2)
            {
                _settings.App.Theme = ThemeIndex switch { 1 => "Light", 2 => "Dark", _ => "System" };
            }

            if (LanguageIndex is >= 0 and <= 2)
            {
                _settings.App.Language = LanguageIndex switch { 1 => "en-US", 2 => "zh-CN", _ => "System" };
            }

            if (PrivacyIndex is >= 0 and <= 2)
            {
                archaeology.PrivacyMode = (PrivacyMode)PrivacyIndex;
            }

            _exclusions.Refresh();
            await _settings.SaveAsync().ConfigureAwait(true);

            _theme.Apply(_settings.App.Theme);

            if (!string.Equals(previousLanguage, _settings.App.Language, StringComparison.OrdinalIgnoreCase))
            {
                _localization.SetLanguage(_settings.App.Language);
            }

            LoadFromSettings();
            ShowStatus(_localization.Get("Common_Saved"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger?.LogWarning(ex, "Saving settings failed");
            ShowStatus(_localization.Get("Error_Settings"));
        }
    }

    [RelayCommand]
    private async Task TestConnectionAsync()
    {
        IsTesting = true;
        ConnectionStatusVisible = true;
        ConnectionStatus = _localization.Get("Settings_Testing");

        try
        {
            if (!string.IsNullOrWhiteSpace(ApiKeyInput))
            {
                _secureStorage.SetApiKey(ApiKeyInput);
                ApiKeyInput = string.Empty;
                HasStoredKey = _secureStorage.HasApiKey;
            }

            _settings.OpenAi.BaseUrl = string.IsNullOrWhiteSpace(BaseUrl) ? "https://api.openai.com/v1" : BaseUrl.Trim();
            _settings.OpenAi.Model = string.IsNullOrWhiteSpace(Model) ? "gpt-4o-mini" : Model.Trim();

            var result = await _ai.TestConnectionAsync().ConfigureAwait(true);
            ConnectionSucceeded = result.Success;
            ConnectionStatus = result.Success
                ? $"{_localization.Get("Settings_Connected")} · {result.Model} · {result.Latency.TotalMilliseconds:0} ms"
                : $"{_localization.Get(result.MessageKey)}{(string.IsNullOrWhiteSpace(result.Detail) ? string.Empty : $" · {result.Detail}")}";
        }
        finally
        {
            IsTesting = false;
        }
    }

    [RelayCommand]
    private void ClearKey()
    {
        _secureStorage.ClearApiKey();
        ApiKeyInput = string.Empty;
        HasStoredKey = false;
        ShowStatus(_localization.Get("Settings_KeyCleared"));
    }

    [RelayCommand]
    private async Task AddIncludedRootAsync()
    {
        var picked = PickFolderAsync is null ? null : await PickFolderAsync();
        var value = picked ?? NewIncludedRoot;

        if (!string.IsNullOrWhiteSpace(value) &&
            !IncludedRoots.Any(e => string.Equals(e.Value, value, StringComparison.OrdinalIgnoreCase)))
        {
            IncludedRoots.Add(new ListEntryViewModel(value, RemoveIncludedRoot));
            NewIncludedRoot = string.Empty;
        }
    }

    [RelayCommand]
    private void AddExcludedPath()
    {
        var value = NewExcludedPath.Trim();
        if (value.Length > 0 && !ExcludedPaths.Any(e => string.Equals(e.Value, value, StringComparison.OrdinalIgnoreCase)))
        {
            ExcludedPaths.Add(new ListEntryViewModel(value, RemoveExcludedPath));
            NewExcludedPath = string.Empty;
        }
    }

    [RelayCommand]
    private void AddExcludedExtension()
    {
        var value = NewExcludedExtension.Trim();
        if (value.Length == 0)
        {
            return;
        }

        if (!value.StartsWith('.'))
        {
            value = "." + value;
        }

        if (!ExcludedExtensions.Any(e => string.Equals(e.Value, value, StringComparison.OrdinalIgnoreCase)))
        {
            ExcludedExtensions.Add(new ListEntryViewModel(value, RemoveExcludedExtension));
            NewExcludedExtension = string.Empty;
        }
    }

    private void RemoveExcludedPath(string value) => RemoveByValue(ExcludedPaths, value);

    private void RemoveExcludedExtension(string value) => RemoveByValue(ExcludedExtensions, value);

    private void RemoveIncludedRoot(string value) => RemoveByValue(IncludedRoots, value);

    private static void RemoveByValue(ObservableCollection<ListEntryViewModel> entries, string value)
    {
        var match = entries.FirstOrDefault(e => string.Equals(e.Value, value, StringComparison.OrdinalIgnoreCase));
        if (match is not null)
        {
            entries.Remove(match);
        }
    }

    [RelayCommand]
    private void RequestResetToDefaults() => ResetToDefaultsRequested?.Invoke(this, EventArgs.Empty);

    [RelayCommand]
    private void OpenDataFolder() => ExternalLinks.OpenUrl(AppPaths.Root);

    /// <summary>Shows the confirmation dialog before wiping settings; called from the page.</summary>
    public void ResetToDefaults()
    {
        _logger?.LogWarning("Settings were reset to defaults from the Settings page");
        _settings.ResetToDefaults();
        LoadFromSettings();
        _theme.Apply(_settings.App.Theme);
        _localization.SetLanguage(_settings.App.Language);
        ShowStatus(_localization.Get("Common_Saved"));
    }

    private void ShowStatus(string message)
    {
        StatusMessage = message;
        StatusVisible = !string.IsNullOrWhiteSpace(message);
    }
}

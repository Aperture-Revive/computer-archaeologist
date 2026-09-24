using System.Collections.ObjectModel;
using ComputerArchaeologist.Core.Ai;
using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Settings;
using ComputerArchaeologist.Core.Utilities;
using ComputerArchaeologist.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComputerArchaeologist.Core.Storage;

namespace ComputerArchaeologist.ViewModels;

/// <summary>One row of the "previous runs" list on the Home page.</summary>
public sealed partial class SessionRowViewModel : ObservableObject
{
    [ObservableProperty]
    private string _when = string.Empty;

    [ObservableProperty]
    private string _summary = string.Empty;

    public SessionRowViewModel(SessionSummary summary, ILocalizationService localization, Action<string>? open = null)
    {
        SessionId = summary.SessionId;
        OpenCommand = new RelayCommand(() => open?.Invoke(SessionId));
        Update(summary, localization);
    }

    public string SessionId { get; }

    public RelayCommand OpenCommand { get; }

    public void Update(SessionSummary summary, ILocalizationService localization)
    {
        When = summary.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture);
        Summary = localization.Format(
            "Home_HistoryRow",
            summary.FilesDiscovered.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
            summary.Discoveries.ToString("N0", System.Globalization.CultureInfo.CurrentCulture));
    }
}

/// <summary>One metric tile on the Home page. Reflowed by a UniformGridLayout, so it adapts to any window width.</summary>
public sealed record StatCard(string Glyph, string Value, string Label);

/// <summary>Home page: what happened last time, and the entry point into a new run.</summary>
public sealed partial class HomeViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly INavigationService _navigation;
    private readonly ISettingsService _settings;
    private readonly IAiAnalysisService _ai;
    private readonly ILocalizationService _localization;
    private readonly IUiDispatcher _ui;

    [ObservableProperty]
    private string _lastRunText = string.Empty;

    [ObservableProperty]
    private bool _hasLastRun;

    [ObservableProperty]
    private string _statScanned = "—";

    [ObservableProperty]
    private string _statCandidates = "—";

    [ObservableProperty]
    private string _statDiscoveries = "—";

    [ObservableProperty]
    private string _statAi = "—";

    [ObservableProperty]
    private string _statDuration = "—";

    [ObservableProperty]
    private bool _apiNoticeVisible;

    [ObservableProperty]
    private string _apiNotice = string.Empty;

    [ObservableProperty]
    private bool _hasReport;

    [ObservableProperty]
    private string _heroYear = "—";

    [ObservableProperty]
    private string _heroKind = string.Empty;

    [ObservableProperty]
    private string _heroScore = "—";

    [ObservableProperty]
    private string _heroName = string.Empty;

    [ObservableProperty]
    private bool _isHistoryEmpty = true;

    public HomeViewModel(
        AppState state,
        INavigationService navigation,
        ISettingsService settings,
        IAiAnalysisService ai,
        ILocalizationService localization,
        IUiDispatcher ui)
    {
        _state = state;
        _navigation = navigation;
        _settings = settings;
        _ai = ai;
        _localization = localization;
        _ui = ui;

        _state.SessionChanged += OnSessionChanged;
        _state.HistoryChanged += OnSessionChanged;
        _localization.LanguageChanged += (_, _) => _ui.Post(Refresh);

        Refresh();
    }

    public ObservableCollection<SessionRowViewModel> RecentSessions { get; } = new();

    /// <summary>Reflowed automatically, so the tile row works on a narrow window and when maximized.</summary>
    public ObservableCollection<StatCard> Stats { get; } = new();

    private void OnSessionChanged(object? sender, EventArgs e) => _ui.Post(Refresh);

    /// <summary>Recomputes every derived value. Never invents data: absent means an em dash.</summary>
    public void Refresh()
    {
        var session = _state.CurrentSession;
        HasLastRun = session is not null;

        if (session is null)
        {
            LastRunText = _localization.Get("Home_Never");
            StatScanned = "—";
            StatCandidates = "—";
            StatDiscoveries = "—";
            StatAi = "—";
            StatDuration = "—";
        }
        else
        {
            LastRunText = session.StartTime.ToLocalTime().ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.CurrentCulture);
            StatScanned = session.FilesDiscovered.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
            StatCandidates = session.Candidates.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
            StatDiscoveries = session.FinalDiscoveries.Count.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
            StatAi = session.FilesAnalyzedByAi.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
            StatDuration = FormatHelpers.Duration(session.Duration);
        }

        HasReport = _state.HasReport;
        RefreshHeroCard();
        RefreshStats();
        RefreshHistory();
        _ = RefreshNoticesAsync();
    }

    private void RefreshStats()
    {
        Stats.Clear();
        Stats.Add(new StatCard("\uE8A5", StatScanned, _localization.Get("Home_Stat_Scanned")));
        Stats.Add(new StatCard("\uE721", StatCandidates, _localization.Get("Home_Stat_Candidates")));
        Stats.Add(new StatCard("\uE7C1", StatDiscoveries, _localization.Get("Home_Stat_Discoveries")));
        Stats.Add(new StatCard("\uE945", StatAi, _localization.Get("Home_Stat_Ai")));
        Stats.Add(new StatCard("\uE823", StatDuration, _localization.Get("Home_Stat_Duration")));
    }

    private void RefreshHeroCard()
    {
        var best = _state.Discoveries.FirstOrDefault();
        if (best is null)
        {
            HeroName = "—";
            HeroYear = "—";
            HeroKind = _localization.Get("Category_unknown");
            HeroScore = "—";
            return;
        }

        HeroName = best.FileName;
        HeroYear = FormatHelpers.Year(best.File.CreatedUtc ?? best.File.ModifiedUtc);
        HeroKind = _localization.Get($"Category_{best.Category}");
        HeroScore = $"{best.Score.FinalScore:0} / 100";
    }

    private void RefreshHistory()
    {
        RecentSessions.Clear();
        foreach (var summary in _state.RecentSessions.Take(6))
        {
            RecentSessions.Add(new SessionRowViewModel(summary, _localization, id => _ = OpenSessionAsync(id)));
        }

        IsHistoryEmpty = RecentSessions.Count == 0;
    }

    private Task RefreshNoticesAsync()
    {
        ApiNoticeVisible = !_ai.IsConfigured;
        ApiNotice = _localization.Get("Home_ApiMissing");
        return Task.CompletedTask;
    }

    [RelayCommand]
    private void Start() => _navigation.Navigate(INavigationService.Archaeology, "start");

    [RelayCommand]
    private void OpenReport() => _navigation.Navigate(INavigationService.Reports, null);

    [RelayCommand]
    private void OpenDiscoveries() => _navigation.Navigate(INavigationService.Discoveries, null);

    [RelayCommand]
    private void OpenSettings() => _navigation.Navigate(INavigationService.Settings, null);

    [RelayCommand]
    private async Task OpenSessionAsync(string? sessionId)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            return;
        }

        await _state.OpenSessionAsync(sessionId!).ConfigureAwait(true);
        _navigation.Navigate(INavigationService.Reports, null);
    }
}

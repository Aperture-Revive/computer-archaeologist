using System.Collections.ObjectModel;
using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Orchestration;
using ComputerArchaeologist.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ComputerArchaeologist.Core.Storage;

namespace ComputerArchaeologist.ViewModels;

/// <summary>One row of the seven stage checklist.</summary>
public sealed partial class StageItemViewModel : ObservableObject
{
    [ObservableProperty]
    private string _label = string.Empty;

    [ObservableProperty]
    private bool _isActive;

    [ObservableProperty]
    private bool _isComplete;

    [ObservableProperty]
    private bool _isPending = true;

    partial void OnIsActiveChanged(bool value) => IsPending = !value && !IsComplete;

    partial void OnIsCompleteChanged(bool value) => IsPending = !value && !IsActive;

    public StageItemViewModel(ScanStage stage, string labelKey, ILocalizationService localization)
    {
        Stage = stage;
        LabelKey = labelKey;
        Update(localization);
    }

    public ScanStage Stage { get; }

    public string LabelKey { get; }

    public void Update(ILocalizationService localization) => Label = localization.Get(LabelKey);
}

/// <summary>
/// Drives an archaeology run and exposes the live progress the page displays.
/// All work happens in <c>IArchaeologyPipeline</c>; this type only translates progress into
/// observable state and forwards cancellation.
/// </summary>
public sealed partial class ArchaeologyViewModel : ObservableObject
{
    private static readonly (ScanStage Stage, string Key)[] StageOrder =
    {
        (ScanStage.Preparing, "Arch_Stage_Preparing"),
        (ScanStage.DiscoveringFiles, "Arch_Stage_Discovering"),
        (ScanStage.CollectingMetadata, "Arch_Stage_Metadata"),
        (ScanStage.LocalScoring, "Arch_Stage_LocalScore"),
        (ScanStage.AiAnalysis, "Arch_Stage_Ai"),
        (ScanStage.FinalScoring, "Arch_Stage_FinalScore"),
        (ScanStage.GeneratingReport, "Arch_Stage_Report"),
    };

    private readonly IArchaeologyPipeline _pipeline;
    private readonly AppState _state;
    private readonly INavigationService _navigation;
    private readonly ILocalizationService _localization;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<ArchaeologyViewModel>? _logger;

    private CancellationTokenSource? _cancellation;
    private ScanProgress _last = new();

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanStart))]
    private bool _isRunning;

    [ObservableProperty]
    private bool _isCancelling;

    [ObservableProperty]
    private string _statusText = string.Empty;

    [ObservableProperty]
    private string _stageHeading = string.Empty;

    [ObservableProperty]
    private double _overallProgress;

    [ObservableProperty]
    private bool _isStageIndeterminate;

    [ObservableProperty]
    private double _stageProgress;

    [ObservableProperty]
    private string _filesScannedText = "—";

    [ObservableProperty]
    private string _candidatesText = "—";

    [ObservableProperty]
    private string _aiText = "—";

    [ObservableProperty]
    private string _currentItem = string.Empty;

    [ObservableProperty]
    private string _lastOutcome = string.Empty;

    [ObservableProperty]
    private bool _lastOutcomeVisible;

    public ArchaeologyViewModel(
        IArchaeologyPipeline pipeline,
        AppState state,
        INavigationService navigation,
        ILocalizationService localization,
        IUiDispatcher ui,
        ILogger<ArchaeologyViewModel>? logger = null)
    {
        _pipeline = pipeline;
        _state = state;
        _navigation = navigation;
        _localization = localization;
        _ui = ui;
        _logger = logger;

        foreach (var (stage, key) in StageOrder)
        {
            Stages.Add(new StageItemViewModel(stage, key, localization));
        }

        _localization.LanguageChanged += (_, _) => _ui.Post(RefreshLocalizedText);
        RefreshLocalizedText();
    }

    public ObservableCollection<StageItemViewModel> Stages { get; } = new();

    /// <summary>Bounded log summary shown next to the progress area.</summary>
    public ObservableCollection<string> LogLines { get; } = new();

    /// <summary>True when a new run may be started.</summary>
    public bool CanStart => !IsRunning;

    /// <summary>Raised when the user asks to start a run; the page answers with a scope dialog.</summary>
    public event EventHandler? StartRequested;

    private void RefreshLocalizedText()
    {
        foreach (var stage in Stages)
        {
            stage.Update(_localization);
        }

        StatusText = IsRunning
            ? _localization.Get(_last.MessageKey ?? "Arch_Stage_Discovering")
            : _localization.Get("Arch_Idle");
    }

    [RelayCommand]
    private void RequestStart() => StartRequested?.Invoke(this, EventArgs.Empty);

    /// <summary>Runs the pipeline. Called by the page after the scope dialog has been answered.</summary>
    public async Task<ArchaeologySession?> RunAsync(IReadOnlyList<string> roots)
    {
        if (IsRunning)
        {
            _logger?.LogWarning("A run was requested while another run is still in progress; ignoring it");
            return null;
        }

        IsRunning = true;
        IsCancelling = false;
        LastOutcomeVisible = false;
        LogLines.Clear();
        OverallProgress = 0;
        StageProgress = 0;
        IsStageIndeterminate = false;
        FilesScannedText = "—";
        CandidatesText = "—";
        AiText = "—";
        CurrentItem = string.Empty;

        _cancellation = new CancellationTokenSource();
        var progress = new UiProgress<ScanProgress>(_ui, OnProgress);

        var request = new ArchaeologyRequest
        {
            Roots = roots ?? Array.Empty<string>(),
            Language = _localization.CurrentLanguage,
        };

        _logger?.LogInformation(
            "Starting an archaeology run over {Scope}",
            request.Roots.Count == 0 ? "the whole machine" : string.Join(", ", request.Roots));

        try
        {
            // The pipeline is fully asynchronous, but its synchronous hot loops (cheap scoring over
            // hundreds of thousands of rows, regex based feature extraction) must never run on the UI
            // thread, so the whole run is started on the thread pool.
            var session = await Task.Run(
                () => _pipeline.RunAsync(request, progress, _cancellation.Token),
                _cancellation.Token).ConfigureAwait(true);

            _state.SetSession(session);

            if (session.Cancelled)
            {
                LastOutcome = _localization.Get("Arch_Cancelled");
            }
            else if (session.FinalDiscoveries.Count == 0)
            {
                LastOutcome = _localization.Get("Disc_Empty");
            }
            else
            {
                LastOutcome = _localization.Format("Disc_Subtitle", session.FinalDiscoveries.Count);
            }

            LastOutcomeVisible = true;
            AppendLog(LastOutcome);

            _logger?.LogInformation(
                "Run finished: cancelled={Cancelled} discovered={Discovered} candidates={Candidates} discoveries={Discoveries}",
                session.Cancelled,
                session.FilesDiscovered,
                session.Candidates,
                session.FinalDiscoveries.Count);

            if (!session.Cancelled && session.FinalDiscoveries.Count > 0)
            {
                _navigation.Navigate(INavigationService.Discoveries, null);
            }

            return session;
        }
        catch (OperationCanceledException)
        {
            _logger?.LogInformation("The archaeology run was cancelled by the user");
            LastOutcome = _localization.Get("Arch_Cancelled");
            LastOutcomeVisible = true;
            return null;
        }
        catch (Exception ex)
        {
            // A failed run must always leave a trace in the log, never just a message on screen.
            _logger?.LogError(ex, "The archaeology run failed");
            LastOutcome = $"{_localization.Get("Error_ScanFailed")} {ex.Message}";
            LastOutcomeVisible = true;
            AppendLog(LastOutcome);
            return null;
        }
        finally
        {
            IsRunning = false;
            IsCancelling = false;
            _cancellation?.Dispose();
            _cancellation = null;
            RefreshLocalizedText();
        }
    }

    [RelayCommand]
    private void Cancel()
    {
        if (!IsRunning || _cancellation is null)
        {
            return;
        }

        IsCancelling = true;
        StatusText = _localization.Get("Arch_Cancelling");
        AppendLog(_localization.Get("Arch_Cancelling"));

        try
        {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // The run already finished.
        }
    }

    private void OnProgress(ScanProgress progress)
    {
        _last = progress;

        if (!string.IsNullOrWhiteSpace(progress.MessageKey))
        {
            StageHeading = _localization.Get(progress.MessageKey!);
            if (!IsRunning)
            {
                StatusText = StageHeading;
            }
        }

        OverallProgress = progress.OverallProgress * 100;
        IsStageIndeterminate = progress.StageProgress < 0;
        StageProgress = Math.Clamp(progress.StageProgress, 0, 1) * 100;

        FilesScannedText = progress.FilesScanned.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);

        if (progress.Candidates > 0)
        {
            CandidatesText = progress.Candidates.ToString("N0", System.Globalization.CultureInfo.CurrentCulture);
        }

        if (progress.AiPlanned > 0)
        {
            AiText = _localization.Format(
                "Arch_OfTotal",
                progress.AiAnalyzed.ToString("N0", System.Globalization.CultureInfo.CurrentCulture),
                progress.AiPlanned.ToString("N0", System.Globalization.CultureInfo.CurrentCulture));
        }

        if (!string.IsNullOrWhiteSpace(progress.CurrentItem))
        {
            CurrentItem = progress.CurrentItem!;
        }

        if (!string.IsNullOrWhiteSpace(progress.Detail))
        {
            AppendLog(progress.Detail!);
        }

        UpdateStageChecklist(progress.Stage);
    }

    private void UpdateStageChecklist(ScanStage stage)
    {
        var current = StageIndex(stage);
        var terminal = stage == ScanStage.Completed;

        for (var i = 0; i < Stages.Count; i++)
        {
            Stages[i].IsComplete = terminal || (current >= 0 && i < current);
            Stages[i].IsActive = !terminal && i == current;
        }
    }

    private static int StageIndex(ScanStage stage)
    {
        for (var i = 0; i < StageOrder.Length; i++)
        {
            if (StageOrder[i].Stage == stage)
            {
                return i;
            }
        }

        return -1;
    }

    private void AppendLog(string line)
    {
        const int maxLines = 200;
        LogLines.Add($"{DateTime.Now:HH:mm:ss}  {line}");
        while (LogLines.Count > maxLines)
        {
            LogLines.RemoveAt(0);
        }
    }
}

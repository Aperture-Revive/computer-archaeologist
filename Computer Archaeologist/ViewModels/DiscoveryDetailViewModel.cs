using System.Collections.ObjectModel;
using System.Globalization;
using ComputerArchaeologist.Core.Analysis;
using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Utilities;
using ComputerArchaeologist.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;

namespace ComputerArchaeologist.ViewModels;

/// <summary>One row of the score breakdown table.</summary>
public sealed record FeatureRow(string Name, double Value, double Weight, double Contribution);

/// <summary>One key/value fact row.</summary>
public sealed record FactRow(string Label, string Value);

/// <summary>Full detail for a single discovery, including preview and open actions.</summary>
public sealed partial class DiscoveryDetailViewModel : ObservableObject
{
    private readonly IFilePreviewService _preview;
    private readonly IFileLauncherService _launcher;
    private readonly IClipboardService _clipboard;
    private readonly ILocalizationService _localization;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<DiscoveryDetailViewModel>? _logger;

    [ObservableProperty]
    private FileArtifact? _artifact;

    [ObservableProperty]
    private string _fileName = string.Empty;

    [ObservableProperty]
    private string _fullPath = string.Empty;

    [ObservableProperty]
    private string _category = string.Empty;

    [ObservableProperty]
    private string _scoreText = "—";

    [ObservableProperty]
    private double _scoreValue;

    [ObservableProperty]
    private string _localScoreText = "—";

    [ObservableProperty]
    private string _aiScoreText = "—";

    [ObservableProperty]
    private string _confidenceText = string.Empty;

    [ObservableProperty]
    private bool _hasAi;

    [ObservableProperty]
    private string _aiSummary = string.Empty;

    [ObservableProperty]
    private string _whyText = string.Empty;

    [ObservableProperty]
    private string _narrativeNote = string.Empty;

    [ObservableProperty]
    private bool _hasNarrativeNote;

    [ObservableProperty]
    private string _previewText = string.Empty;

    [ObservableProperty]
    private string _previewNote = string.Empty;

    [ObservableProperty]
    private string? _previewImagePath;

    [ObservableProperty]
    private bool _hasPreviewImage;

    [ObservableProperty]
    private bool _hasPreviewText;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _statusVisible;

    [ObservableProperty]
    private string _openButtonText = string.Empty;

    [ObservableProperty]
    private string _openFolderText = string.Empty;

    [ObservableProperty]
    private string _copyPathText = string.Empty;

    [ObservableProperty]
    private string _backText = string.Empty;

    [ObservableProperty]
    private string _evidenceTitle = string.Empty;

    [ObservableProperty]
    private string _breakdownTitle = string.Empty;

    [ObservableProperty]
    private string _factsTitle = string.Empty;

    [ObservableProperty]
    private string _relatedTitle = string.Empty;

    [ObservableProperty]
    private string _previewTitle = string.Empty;

    [ObservableProperty]
    private string _timelineTitle = string.Empty;

    public DiscoveryDetailViewModel(
        IFilePreviewService preview,
        IFileLauncherService launcher,
        IClipboardService clipboard,
        ILocalizationService localization,
        IUiDispatcher ui,
        ILogger<DiscoveryDetailViewModel>? logger = null)
    {
        _preview = preview;
        _launcher = launcher;
        _clipboard = clipboard;
        _localization = localization;
        _ui = ui;
        _logger = logger;

        _localization.LanguageChanged += (_, _) => _ui.Post(() => Load(Artifact));
        RefreshChrome();
    }

    /// <summary>Set by the page so executable files can require an explicit confirmation.</summary>
    public Func<string, Task<bool>>? ConfirmExecutableAsync { get; set; }

    public ObservableCollection<FeatureRow> Features { get; } = new();

    public ObservableCollection<string> LocalReasons { get; } = new();

    public ObservableCollection<string> AiReasons { get; } = new();

    public ObservableCollection<FactRow> Facts { get; } = new();

    public ObservableCollection<string> RelatedFiles { get; } = new();

    public ObservableCollection<TimelinePoint> Timeline { get; } = new();

    public bool HasRelatedFiles => RelatedFiles.Count > 0;

    public bool HasTimeline => Timeline.Count > 0;

    /// <summary>Populates the page. Safe to call repeatedly (for example after a language change).</summary>
    public void Load(FileArtifact? artifact)
    {
        Artifact = artifact;
        RefreshChrome();
        Features.Clear();
        LocalReasons.Clear();
        AiReasons.Clear();
        Facts.Clear();
        RelatedFiles.Clear();
        Timeline.Clear();
        HasPreviewImage = false;
        HasPreviewText = false;
        PreviewImagePath = null;
        PreviewText = string.Empty;
        PreviewNote = string.Empty;

        if (artifact is null)
        {
            return;
        }

        var file = artifact.File;
        FileName = file.FileName;
        FullPath = file.FullPath;
        Category = _localization.Get($"Category_{artifact.Category}");
        ScoreValue = artifact.Score.FinalScore;
        ScoreText = $"{artifact.Score.FinalScore:0} / 100";
        LocalScoreText = $"{artifact.Score.LocalScore:0} / 100";
        AiScoreText = artifact.Score.AiApplied ? $"{artifact.Score.AiScore:0} / 100" : "—";
        ConfidenceText = artifact.Score.AiApplied
            ? _localization.Format("Detail_ConfidenceValue", $"{artifact.Score.Confidence:0}")
            : _localization.Get("Detail_NoAi");
        HasAi = artifact.Score.Ai is { Succeeded: true };
        AiSummary = artifact.Score.Ai?.Summary ?? string.Empty;
        WhyText = artifact.Summary;
        NarrativeNote = artifact.NarrativeNote ?? string.Empty;
        HasNarrativeNote = !string.IsNullOrWhiteSpace(NarrativeNote);

        foreach (var reason in artifact.Score.Local.Reasons)
        {
            Features.Add(new FeatureRow(
                _localization.Get($"Feature_{reason.Feature}"),
                reason.Value,
                Math.Round(reason.Weight * 100, 1),
                reason.Weighted));
        }

        foreach (var reason in artifact.Score.Local.Reasons)
        {
            LocalReasons.Add(_localization.Format(reason.ExplanationKey, reason.Arguments?.ToArray() ?? Array.Empty<object?>()));
        }

        if (HasAi)
        {
            foreach (var reason in artifact.Score.Ai!.Reasons)
            {
                AiReasons.Add(reason);
            }
        }

        Facts.Add(new FactRow(_localization.Get("Disc_Path"), file.FullPath));
        Facts.Add(new FactRow(_localization.Get("Disc_Type"), string.IsNullOrEmpty(file.Extension) ? _localization.Get("Common_None") : file.Extension));
        Facts.Add(new FactRow(_localization.Get("Disc_Size"), FormatHelpers.FileSize(file.SizeBytes)));
        Facts.Add(new FactRow(_localization.Get("Disc_Created"), FormatHelpers.Date(file.CreatedUtc)));
        Facts.Add(new FactRow(_localization.Get("Disc_Modified"), FormatHelpers.Date(file.ModifiedUtc)));
        Facts.Add(new FactRow(_localization.Get("Disc_Accessed"), FormatHelpers.Date(file.AccessedUtc)));

        if (!string.IsNullOrEmpty(file.Sha256))
        {
            Facts.Add(new FactRow("SHA-256", file.Sha256!));
        }

        foreach (var nearby in artifact.Context.NearbyFiles)
        {
            RelatedFiles.Add(nearby);
        }

        OnPropertyChanged(nameof(HasRelatedFiles));

        BuildTimeline(artifact);
        OnPropertyChanged(nameof(HasTimeline));

        _ = LoadPreviewAsync(artifact);
    }

    private void BuildTimeline(FileArtifact artifact)
    {
        if (artifact.File.CreatedUtc is { } created)
        {
            Timeline.Add(new TimelinePoint(_localization.Get("Disc_Created"), FormatHelpers.Date(created)));
        }

        if (artifact.File.ModifiedUtc is { } modified)
        {
            Timeline.Add(new TimelinePoint(_localization.Get("Disc_Modified"), FormatHelpers.Date(modified)));
        }

        if (artifact.File.AccessedUtc is { } accessed)
        {
            Timeline.Add(new TimelinePoint(_localization.Get("Disc_Accessed"), FormatHelpers.Date(accessed)));
        }

    }

    private async Task LoadPreviewAsync(FileArtifact artifact)
    {
        try
        {
            var preview = await _preview.GetPreviewAsync(artifact.File).ConfigureAwait(true);

            switch (preview.Kind)
            {
                case PreviewKind.Text:
                    PreviewText = preview.Text ?? string.Empty;
                    HasPreviewText = !string.IsNullOrEmpty(PreviewText);
                    PreviewNote = preview.Note ?? string.Empty;
                    break;

                case PreviewKind.Image:
                    PreviewImagePath = preview.ImagePath;
                    HasPreviewImage = !string.IsNullOrEmpty(PreviewImagePath);
                    PreviewNote = preview.Note ?? string.Empty;
                    break;

                default:
                    PreviewNote = preview.Note ?? _localization.Get("Detail_PreviewUnavailable");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Preview generation failed");
            PreviewNote = _localization.Get("Detail_PreviewUnavailable");
        }
    }

    private void RefreshChrome()
    {
        OpenButtonText = _localization.Get("Disc_OpenFile");
        OpenFolderText = _localization.Get("Disc_OpenFolder");
        CopyPathText = _localization.Get("Common_Copy");
        BackText = _localization.Get("Detail_Back");
        EvidenceTitle = _localization.Get("Detail_Evidence");
        BreakdownTitle = _localization.Get("Detail_ScoreBreakdown");
        FactsTitle = _localization.Get("Detail_FileInfo");
        RelatedTitle = _localization.Get("Detail_RelatedFiles");
        PreviewTitle = _localization.Get("Detail_Preview");
        TimelineTitle = _localization.Get("Detail_Timeline");
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var artifact = Artifact;
        if (artifact is null)
        {
            return;
        }

        // Executables are never opened without an explicit second confirmation. If no confirmation
        // handler is wired up the file is refused outright rather than opened silently.
        if (ExecutableGuard.RequiresConfirmation(artifact.File))
        {
            if (ConfirmExecutableAsync is null)
            {
                _logger?.LogWarning("Refusing to open {Path}: no executable confirmation handler is available", artifact.FullPath);
                ShowStatus(_localization.Get("Error_Unexpected"));
                return;
            }

            var confirmed = await ConfirmExecutableAsync(artifact.FullPath).ConfigureAwait(true);
            if (!confirmed)
            {
                return;
            }
        }

        if (!_launcher.OpenFile(artifact.FullPath, out var error))
        {
            ShowStatus(error ?? _localization.Get("Error_Unexpected"));
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        var artifact = Artifact;
        if (artifact is null)
        {
            return;
        }

        if (!_launcher.OpenFolder(artifact.FullPath, out var error))
        {
            ShowStatus(error ?? _localization.Get("Error_Unexpected"));
        }
    }

    [RelayCommand]
    private void CopyPath()
    {
        var artifact = Artifact;
        if (artifact is null)
        {
            return;
        }

        ShowStatus(_clipboard.SetText(artifact.FullPath)
            ? _localization.Get("Common_Copied")
            : _localization.Get("Error_Unexpected"));
    }

    [RelayCommand]
    private void Back() => BackRequested?.Invoke(this, EventArgs.Empty);

    public event EventHandler? BackRequested;

    private void ShowStatus(string message)
    {
        StatusMessage = message;
        StatusVisible = !string.IsNullOrWhiteSpace(message);
    }
}

/// <summary>A single dated point on the artifact timeline.</summary>
public sealed record TimelinePoint(string Label, string Value);

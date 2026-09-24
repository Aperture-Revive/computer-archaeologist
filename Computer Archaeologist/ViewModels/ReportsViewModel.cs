using System.Collections.ObjectModel;
using System.Globalization;
using ComputerArchaeologist.Core.Infrastructure;
using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Reports;
using ComputerArchaeologist.Core.Utilities;
using ComputerArchaeologist.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using ComputerArchaeologist.Core.Storage;

namespace ComputerArchaeologist.ViewModels;

/// <summary>A labelled fact row shown in the report header.</summary>
public sealed record ReportFact(string Label, string Value);

/// <summary>One year on the machine-history timeline.</summary>
public sealed record TimelineRow(int Year, string Count, string? Highlight, double Score);

/// <summary>The Reports page. Renders the full report inside the application.</summary>
public sealed partial class ReportsViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly INavigationService _navigation;
    private readonly ILocalizationService _localization;
    private readonly IUiDispatcher _ui;
    private readonly ILogger<ReportsViewModel>? _logger;

    [ObservableProperty]
    private bool _hasReport;

    [ObservableProperty]
    private string _title = string.Empty;

    [ObservableProperty]
    private string _generatedAt = string.Empty;

    [ObservableProperty]
    private string _overview = string.Empty;

    [ObservableProperty]
    private string _aiObservations = string.Empty;

    [ObservableProperty]
    private string _finalSummary = string.Empty;

    [ObservableProperty]
    private string _narrativeSource = string.Empty;

    [ObservableProperty]
    private string _timelineTitle = string.Empty;

    [ObservableProperty]
    private string _emptyText = string.Empty;

    [ObservableProperty]
    private string _emptyHint = string.Empty;

    [ObservableProperty]
    private string _statusMessage = string.Empty;

    [ObservableProperty]
    private bool _statusVisible;

    public ReportsViewModel(
        AppState state,
        INavigationService navigation,
        ILocalizationService localization,
        IUiDispatcher ui,
        ILogger<ReportsViewModel>? logger = null)
    {
        _state = state;
        _navigation = navigation;
        _localization = localization;
        _ui = ui;
        _logger = logger;

        _state.SessionChanged += (_, _) => _ui.Post(Rebuild);
        _localization.LanguageChanged += (_, _) => _ui.Post(Rebuild);

        Rebuild();
    }

    public ObservableCollection<ReportFact> Facts { get; } = new();

    public ObservableCollection<ReportSectionViewModel> Sections { get; } = new();

    public ObservableCollection<TimelineRow> Timeline { get; } = new();

    public ObservableCollection<string> Warnings { get; } = new();

    private void Rebuild()
    {
        Facts.Clear();
        Sections.Clear();
        Timeline.Clear();
        Warnings.Clear();

        EmptyText = _localization.Get("Report_NoReport");
        EmptyHint = _localization.Get("Report_NoReportHint");
        TimelineTitle = _localization.Get("Report_Timeline");

        var report = _state.Report;
        HasReport = report is not null;

        if (report is null)
        {
            Title = _localization.Get("Report_Title");
            GeneratedAt = string.Empty;
            Overview = string.Empty;
            AiObservations = string.Empty;
            FinalSummary = string.Empty;
            NarrativeSource = string.Empty;
            return;
        }

        Title = string.IsNullOrWhiteSpace(report.Title) ? _localization.Get("Report_Title") : report.Title;
        GeneratedAt = _localization.Format(
            "Report_GeneratedAt",
            report.GeneratedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture));
        Overview = report.Overview;
        AiObservations = report.AiObservations;
        FinalSummary = report.FinalSummary;
        NarrativeSource = _localization.Get(report.NarrativeFromAi ? "Report_Narrative_Ai" : "Report_Narrative_Local");

        Facts.Add(new ReportFact(_localization.Get("Report_Fact_Discovered"), report.FilesDiscovered.ToString("N0", CultureInfo.CurrentCulture)));
        Facts.Add(new ReportFact(_localization.Get("Report_Fact_Candidates"), report.CandidatesExamined.ToString("N0", CultureInfo.CurrentCulture)));
        Facts.Add(new ReportFact(_localization.Get("Report_Fact_AiAnalyzed"), report.FilesAnalyzedByAi.ToString("N0", CultureInfo.CurrentCulture)));
        Facts.Add(new ReportFact(_localization.Get("Report_Fact_Discoveries"), report.DiscoveriesCount.ToString("N0", CultureInfo.CurrentCulture)));
        Facts.Add(new ReportFact(_localization.Get("Report_Fact_Duration"), FormatHelpers.Duration(report.Duration)));
        Facts.Add(new ReportFact(_localization.Get("Report_Fact_Source"), report.DiscoverySource));
        Facts.Add(new ReportFact(
            _localization.Get("Report_Fact_Scope"),
            string.IsNullOrWhiteSpace(report.Scope)
                ? _localization.Get("Report_Scope_WholeMachine")
                : report.Scope!));
        Facts.Add(new ReportFact(_localization.Get("Report_Fact_Narrative"), NarrativeSource));

        foreach (var section in report.Sections)
        {
            if (!section.IsEmpty)
            {
                Sections.Add(new ReportSectionViewModel(section, _localization, _navigation));
            }
        }

        foreach (var entry in report.Timeline)
        {
            Timeline.Add(new TimelineRow(
                entry.Year,
                _localization.Format("Report_Timeline_Count", entry.ArtifactCount.ToString("N0", CultureInfo.CurrentCulture)),
                entry.HighlightName,
                entry.HighlightScore));
        }

        foreach (var warning in report.Warnings)
        {
            Warnings.Add(_localization.Get(warning));
        }
    }

    [RelayCommand]
    private void OpenArtifact(FileArtifact? artifact)
    {
        if (artifact is not null)
        {
            _navigation.Navigate(INavigationService.Detail, artifact);
        }
    }

    [RelayCommand]
    private void GoToDiscoveries() => _navigation.Navigate(INavigationService.Discoveries, null);

    [RelayCommand]
    private void GoToArchaeology() => _navigation.Navigate(INavigationService.Archaeology, "start");

    [RelayCommand]
    private async Task ExportAsync()
    {
        var report = _state.Report;
        if (report is null)
        {
            return;
        }

        try
        {
            Directory.CreateDirectory(AppPaths.ExportsDirectory);
            var markdown = ReportMarkdownWriter.Write(report, _localization);
            var path = Path.Combine(
                AppPaths.ExportsDirectory,
                $"computer-archaeology-report-{DateTime.Now:yyyyMMdd-HHmmss}.md");

            await File.WriteAllTextAsync(path, markdown).ConfigureAwait(true);

            StatusMessage = _localization.Format("Report_Exported", path);
            StatusVisible = true;
            _logger?.LogInformation("Report exported to {Path}", path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            StatusMessage = _localization.Get("Report_ExportFailed");
            StatusVisible = true;
            _logger?.LogWarning(ex, "Report export failed");
        }
    }
}

/// <summary>A rendered report section with its localized heading.</summary>
public sealed class ReportSectionViewModel
{
    public ReportSectionViewModel(ReportSection section, ILocalizationService localization, INavigationService navigation)
    {
        Title = localization.Get(section.TitleKey);
        Intro = string.IsNullOrWhiteSpace(section.IntroKey) ? null : localization.Get(section.IntroKey!);
        Artifacts = section.Artifacts
            .Select(a => new ReportArtifactViewModel(a, localization, navigation))
            .ToArray();
    }

    public string Title { get; }

    public string? Intro { get; }

    public IReadOnlyList<ReportArtifactViewModel> Artifacts { get; }
}

/// <summary>One discovery card inside a report section.</summary>
public sealed class ReportArtifactViewModel
{
    public ReportArtifactViewModel(FileArtifact artifact, ILocalizationService localization, INavigationService navigation)
    {
        Artifact = artifact;
        FileName = artifact.FileName;
        FullPath = artifact.FullPath;
        Score = $"{artifact.Score.FinalScore:0} / 100";
        ScoreValue = artifact.Score.FinalScore;
        Category = localization.Get($"Category_{artifact.Category}");
        Created = FormatHelpers.Date(artifact.File.CreatedUtc);
        Modified = FormatHelpers.Date(artifact.File.ModifiedUtc);
        Size = FormatHelpers.FileSize(artifact.File.SizeBytes);
        Summary = artifact.Summary;
        Note = artifact.NarrativeNote;
        Reasons = artifact.Evidence;
        Confidence = artifact.Score.Confidence > 0
            ? localization.Format("Detail_ConfidenceValue", $"{artifact.Score.Confidence:0}")
            : null;
        OpenCommand = new RelayCommand(() => navigation.Navigate(INavigationService.Detail, artifact));
    }

    public FileArtifact Artifact { get; }

    public string FileName { get; }

    public string FullPath { get; }

    public string Score { get; }

    public double ScoreValue { get; }

    public string Category { get; }

    public string Created { get; }

    public string Modified { get; }

    public string Size { get; }

    public string Summary { get; }

    public string? Note { get; }

    public IReadOnlyList<string> Reasons { get; }

    public string? Confidence { get; }

    public RelayCommand OpenCommand { get; }
}

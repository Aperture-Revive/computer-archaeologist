using System.Collections.ObjectModel;
using ComputerArchaeologist.Core.Ai;
using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Utilities;
using ComputerArchaeologist.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ComputerArchaeologist.Core.Storage;

namespace ComputerArchaeologist.ViewModels;

/// <summary>How the discoveries list is ordered. These are orderings, not a ranking of worth.</summary>
public enum DiscoverySortMode
{
    MostInteresting,
    Oldest,
    Strangest,
    MostPersonal,
    MostTechnical,
    Largest,
}

/// <summary>
/// Presentation wrapper around one artifact. Per-item commands live on the item so that XAML
/// templates never need an ElementName lookup across a template name scope.
/// </summary>
public sealed class ArtifactCardViewModel
{
    public ArtifactCardViewModel(FileArtifact artifact, DiscoveriesViewModel owner)
    {
        Artifact = artifact;
        OpenDetailsCommand = new RelayCommand(() => owner.OpenDetails(artifact));
    }

    public FileArtifact Artifact { get; }

    public RelayCommand OpenDetailsCommand { get; }
}

/// <summary>The Discoveries page: every artifact that survived the run, sortable and filterable.</summary>
public sealed partial class DiscoveriesViewModel : ObservableObject
{
    private readonly AppState _state;
    private readonly INavigationService _navigation;
    private readonly ILocalizationService _localization;
    private readonly IUiDispatcher _ui;

    private readonly List<FileArtifact> _all = new();

    [ObservableProperty]
    private bool _isEmpty = true;

    [ObservableProperty]
    private string _subtitle = string.Empty;

    [ObservableProperty]
    private int _sortIndex;

    [ObservableProperty]
    private string _selectedCategory = "all";

    [ObservableProperty]
    private bool _hasArtifacts;

    public DiscoveriesViewModel(
        AppState state,
        INavigationService navigation,
        ILocalizationService localization,
        IUiDispatcher ui)
    {
        _state = state;
        _navigation = navigation;
        _localization = localization;
        _ui = ui;

        _state.SessionChanged += (_, _) => _ui.Post(Rebuild);
        _localization.LanguageChanged += (_, _) => _ui.Post(Rebuild);

        Rebuild();
    }

    /// <summary>The filtered, sorted projection bound to the list.</summary>
    public ObservableCollection<ArtifactCardViewModel> Artifacts { get; } = new();

    public ObservableCollection<string> Categories { get; } = new();

    partial void OnSortIndexChanged(int value) => Apply();

    partial void OnSelectedCategoryChanged(string value) => Apply();

    private void Rebuild()
    {
        _all.Clear();
        _all.AddRange(_state.Discoveries);

        Categories.Clear();
        Categories.Add("all");
        foreach (var category in _all.Select(a => a.Category).Distinct().OrderBy(c => c, StringComparer.Ordinal))
        {
            Categories.Add(category);
        }

        HasArtifacts = _all.Count > 0;
        IsEmpty = _all.Count == 0;
        Subtitle = IsEmpty
            ? _localization.Get("Disc_Empty")
            : _localization.Format("Disc_Subtitle", _all.Count);

        Apply();
    }

    private void Apply()
    {
        var mode = (DiscoverySortMode)Math.Clamp(SortIndex, 0, 5);

        IEnumerable<FileArtifact> query = _all;

        if (!string.Equals(SelectedCategory, "all", StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(SelectedCategory))
        {
            query = query.Where(a => string.Equals(a.Category, SelectedCategory, StringComparison.Ordinal));
        }

        query = mode switch
        {
            DiscoverySortMode.Oldest => query.OrderBy(a => a.File.CreatedUtc ?? a.File.ModifiedUtc ?? DateTimeOffset.MaxValue),
            DiscoverySortMode.Strangest => query.OrderByDescending(a => a.Score.Local.Unusualness + (a.Score.Ai?.Unusualness ?? 0)),
            DiscoverySortMode.MostPersonal => query.OrderByDescending(a => (a.Score.Ai?.PersonalSignificance ?? 0) + a.Score.Local.PersonalArtifact),
            DiscoverySortMode.MostTechnical => query.OrderByDescending(a => a.Score.Ai?.TechnicalSignificance ?? 0),
            DiscoverySortMode.Largest => query.OrderByDescending(a => a.File.SizeBytes),
            _ => query.OrderByDescending(a => a.Score.FinalScore),
        };

        Artifacts.Clear();
        foreach (var artifact in query)
        {
            Artifacts.Add(new ArtifactCardViewModel(artifact, this));
        }
    }

    /// <summary>Opens the detail page. Also used by the card commands.</summary>
    public void OpenDetails(FileArtifact? artifact)
    {
        if (artifact is null)
        {
            return;
        }

        _navigation.Navigate(INavigationService.Detail, artifact);
    }

    [RelayCommand]
    private void GoToArchaeology() => _navigation.Navigate(INavigationService.Archaeology, "start");

    [RelayCommand]
    private void GoToReports() => _navigation.Navigate(INavigationService.Reports, null);
}

/// <summary>Formatting helpers that the XAML bindings use indirectly through the view model.</summary>
public static class ArtifactDisplay
{
    public static string Score(FileArtifact artifact) => $"{artifact.Score.FinalScore:0} / 100";

    public static string Created(FileArtifact artifact) => FormatHelpers.Date(artifact.File.CreatedUtc);

    public static string Modified(FileArtifact artifact) => FormatHelpers.Date(artifact.File.ModifiedUtc);

    public static string Accessed(FileArtifact artifact) => FormatHelpers.Date(artifact.File.AccessedUtc);

    public static string Size(FileArtifact artifact) => FormatHelpers.FileSize(artifact.File.SizeBytes);
}

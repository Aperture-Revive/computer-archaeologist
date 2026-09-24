using ComputerArchaeologist.ViewModels;
using Microsoft.UI.Xaml.Controls;

namespace ComputerArchaeologist.Views;

/// <summary>Discoveries list. Clicking a card opens the detail page.</summary>
public sealed partial class DiscoveriesPage : AppPage
{
    public DiscoveriesPage()
    {
        ViewModel = ResolveViewModel<DiscoveriesViewModel>();
        InitializeComponent();
    }

    public DiscoveriesViewModel ViewModel { get; }

    private void OnArtifactClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is ArtifactCardViewModel card)
        {
            ViewModel.OpenDetails(card.Artifact);
        }
    }
}

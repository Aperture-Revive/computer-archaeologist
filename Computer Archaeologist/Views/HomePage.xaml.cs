using ComputerArchaeologist.ViewModels;

namespace ComputerArchaeologist.Views;

/// <summary>Home page code-behind: nothing but lifecycle wiring.</summary>
public sealed partial class HomePage : AppPage
{
    public HomePage()
    {
        ViewModel = ResolveViewModel<HomeViewModel>();
        InitializeComponent();
    }

    public HomeViewModel ViewModel { get; }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Refresh();
    }
}

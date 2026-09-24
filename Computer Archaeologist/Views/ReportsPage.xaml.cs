using ComputerArchaeologist.ViewModels;

namespace ComputerArchaeologist.Views;

/// <summary>Reports page code-behind. The report is rebuilt from shared state on every navigation.</summary>
public sealed partial class ReportsPage : AppPage
{
    public ReportsPage()
    {
        ViewModel = ResolveViewModel<ReportsViewModel>();
        InitializeComponent();
    }

    public ReportsViewModel ViewModel { get; }
}

using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace ComputerArchaeologist.Views;

/// <summary>
/// Base page for every screen.
/// <para>
/// WinUI's XAML compiler does not support a generic base type for a page, and a
/// <see cref="Frame"/> always constructs pages through their parameterless constructor, so each page
/// resolves its own view model from the application container and exposes it as a strongly typed
/// property. Business logic still lives entirely in the view model.
/// </para>
/// </summary>
public abstract class AppPage : Page
{
    /// <summary>Resolves the view model, assigns it as the data context and returns it.</summary>
    protected TViewModel ResolveViewModel<TViewModel>() where TViewModel : class
    {
        var viewModel = App.Services.GetRequiredService<TViewModel>();
        DataContext = viewModel;
        return viewModel;
    }
}

using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;

namespace ComputerArchaeologist.Views;

/// <summary>
/// Discovery detail page. The only view concern handled here is the executable confirmation dialog,
/// which must never be skipped (specification section 47).
/// </summary>
public sealed partial class DiscoveryDetailPage : AppPage
{
    private readonly ILocalizationService _localization;

    public DiscoveryDetailPage()
    {
        ViewModel = ResolveViewModel<DiscoveryDetailViewModel>();
        InitializeComponent();
        _localization = App.Services.GetRequiredService<ILocalizationService>();

        ViewModel.ConfirmExecutableAsync = ConfirmExecutableAsync;
        ViewModel.BackRequested += (_, _) =>
        {
            if (!App.Services.GetRequiredService<Services.INavigationService>().CanGoBack)
            {
                App.Services.GetRequiredService<Services.INavigationService>().Navigate(Services.INavigationService.Discoveries, null);
                return;
            }

            App.Services.GetRequiredService<Services.INavigationService>().GoBack();
        };
    }

    public DiscoveryDetailViewModel ViewModel { get; }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.Load(e.Parameter as FileArtifact);
    }

    /// <summary>Second confirmation before an executable is handed to the shell.</summary>
    private async Task<bool> ConfirmExecutableAsync(string path)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = _localization.Get("Detail_ExecutableTitle"),
            Content = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = _localization.Get("Detail_ExecutableMessage"), TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap },
                    new TextBlock
                    {
                        Text = path,
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                        Style = (Microsoft.UI.Xaml.Style)Microsoft.UI.Xaml.Application.Current.Resources["CaptionTextBlockStyle"],
                    },
                },
            },
            PrimaryButtonText = _localization.Get("Detail_OpenAnyway"),
            CloseButtonText = _localization.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        return await dialog.ShowAsync() == ContentDialogResult.Primary;
    }
}

using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace ComputerArchaeologist.Views;

/// <summary>
/// Settings page. The code-behind only supplies the two things a view model must not own: the
/// native folder picker and the confirmation dialog.
/// </summary>
public sealed partial class SettingsPage : AppPage
{
    private readonly ILocalizationService _localization;

    public SettingsPage()
    {
        ViewModel = ResolveViewModel<SettingsViewModel>();
        InitializeComponent();
        _localization = App.Services.GetRequiredService<ILocalizationService>();

        ViewModel.PickFolderAsync = PickFolderAsync;
        ViewModel.ResetToDefaultsRequested += OnResetRequested;
    }

    public SettingsViewModel ViewModel { get; }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        ViewModel.ReloadFromSettings();
    }

    private void OnResetRequested(object? sender, EventArgs e) => _ = ConfirmResetAsync();

    private async Task ConfirmResetAsync()
    {
        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = _localization.Get("Settings_ResetConfirm_Title"),
            Content = _localization.Get("Settings_ResetConfirm_Message"),
            PrimaryButtonText = _localization.Get("Settings_ResetDefaults"),
            CloseButtonText = _localization.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Close,
        };

        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            ViewModel.ResetToDefaults();
        }
    }

    private static async Task<string?> PickFolderAsync()
    {
        var handle = App.MainWindowHandle;
        if (handle == IntPtr.Zero)
        {
            return null;
        }

        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);

        try
        {
            var folder = await picker.PickSingleFolderAsync();
            return folder?.Path;
        }
        catch (Exception)
        {
            return null;
        }
    }
}

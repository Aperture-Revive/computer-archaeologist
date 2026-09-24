using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Options;
using ComputerArchaeologist.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Storage.Pickers;

namespace ComputerArchaeologist.Views;

/// <summary>
/// The Archaeology page. Its code-behind owns exactly two view concerns: the scope dialog and the
/// start gesture. Everything else is the view model's job.
/// </summary>
public sealed partial class ArchaeologyPage : AppPage
{
    private readonly ILocalizationService _localization;
    private readonly ArchaeologyOptions _options;
    private readonly ILogger<ArchaeologyPage>? _logger;
    private bool _dialogOpen;

    /// <summary>
    /// Set when navigation asked for a run to start. The scope dialog needs a loaded page, because a
    /// ContentDialog cannot be shown before <c>XamlRoot</c> exists, so the request is deferred to the
    /// Loaded event.
    /// </summary>
    private bool _startRequestedWhileNavigating;

    public ArchaeologyPage()
    {
        ViewModel = ResolveViewModel<ArchaeologyViewModel>();
        InitializeComponent();
        _localization = App.Services.GetRequiredService<ILocalizationService>();
        _options = App.Services.GetRequiredService<ArchaeologyOptions>();
        _logger = App.Services.GetService<ILogger<ArchaeologyPage>>();

        ViewModel.StartRequested += OnStartRequested;
        Loaded += OnPageLoaded;
    }

    public ArchaeologyViewModel ViewModel { get; }

    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        if (e.Parameter is string directive && string.Equals(directive, "start", StringComparison.Ordinal))
        {
            _startRequestedWhileNavigating = true;
        }
    }

    private void OnPageLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnPageLoaded;

        if (_startRequestedWhileNavigating)
        {
            _startRequestedWhileNavigating = false;
            _ = ShowScopeDialogAsync();
        }
    }

    private void OnStartRequested(object? sender, EventArgs e) => _ = ShowScopeDialogAsync();

    private async Task ShowScopeDialogAsync()
    {
        if (_dialogOpen || ViewModel.IsRunning)
        {
            return;
        }

        if (XamlRoot is null)
        {
            _logger?.LogDebug("The scope dialog was requested before the page finished loading");
            return;
        }

        _dialogOpen = true;
        try
        {
            var roots = await PickScopeAsync();
            if (roots is null)
            {
                _logger?.LogInformation("The scope dialog was dismissed; no run was started");
                return;
            }

            _logger?.LogInformation("Scope chosen: {Count} root(s)", roots.Count);
            await ViewModel.RunAsync(roots);
        }
        catch (Exception ex)
        {
            // A failure while opening or answering the dialog must be visible, never swallowed.
            _logger?.LogError(ex, "The archaeology scope dialog failed");
        }
        finally
        {
            _dialogOpen = false;
        }
    }

    /// <summary>Shows the scope chooser. Returns null when the user cancelled.</summary>
    private async Task<IReadOnlyList<string>?> PickScopeAsync()
    {
        var group = "archaeology-scope";

        var entire = new RadioButton
        {
            GroupName = group,
            IsChecked = true,
            Content = _localization.Get("Scope_Entire"),
        };

        var drivesPanel = new StackPanel { Spacing = 4 };
        RadioButton? firstDrive = null;
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (!drive.IsReady || drive.DriveType != DriveType.Fixed)
                {
                    continue;
                }

                var button = new RadioButton
                {
                    GroupName = group,
                    Content = _localization.Format("Scope_Drive", drive.Name.TrimEnd('\\')),
                    Tag = drive.RootDirectory.FullName,
                };

                firstDrive ??= button;
                drivesPanel.Children.Add(button);
            }
        }
        catch (IOException)
        {
            // No drive list available; "entire computer" still works.
        }

        var folders = new RadioButton
        {
            GroupName = group,
            Content = _localization.Get("Scope_Folders"),
        };

        // The persistent "restrict discovery to" list is the default selection, so what the dialog
        // shows is exactly what the run will do.
        var selectedRoots = _options.IncludedRoots
            .Where(r => !string.IsNullOrWhiteSpace(r) && Directory.Exists(r))
            .ToList();
        var rootList = new StackPanel { Spacing = 4, Margin = new Thickness(28, 4, 0, 0) };
        var emptyHint = new TextBlock
        {
            Text = _localization.Get("Scope_SelectedNone"),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"],
            Margin = new Thickness(28, 0, 0, 0),
        };

        void RefreshRoots()
        {
            rootList.Children.Clear();
            foreach (var root in selectedRoots.ToArray())
            {
                var row = new Grid { ColumnSpacing = 8 };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

                var text = new TextBlock
                {
                    Text = root,
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    VerticalAlignment = VerticalAlignment.Center,
                };
                var remove = new Button
                {
                    Content = new FontIcon { Glyph = "\uE711", FontSize = 12 },
                    Padding = new Thickness(6, 2, 6, 2),
                };
                remove.Click += (_, _) =>
                {
                    selectedRoots.Remove(root);
                    RefreshRoots();
                };

                Grid.SetColumn(text, 0);
                Grid.SetColumn(remove, 1);
                row.Children.Add(text);
                row.Children.Add(remove);
                rootList.Children.Add(row);
            }

            emptyHint.Visibility = selectedRoots.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        if (selectedRoots.Count > 0)
        {
            folders.IsChecked = true;
            entire.IsChecked = false;
        }

        RefreshRoots();

        var choose = new Button
        {
            Content = _localization.Get("Scope_ChooseFolder"),
            Margin = new Thickness(28, 0, 0, 0),
        };

        choose.Click += async (_, _) =>
        {
            var picked = await PickFolderAsync();
            if (!string.IsNullOrWhiteSpace(picked) &&
                !selectedRoots.Contains(picked!, StringComparer.OrdinalIgnoreCase))
            {
                selectedRoots.Add(picked!);
                folders.IsChecked = true;
                RefreshRoots();
            }
        };

        var content = new StackPanel { Spacing = 10, MinWidth = 420 };
        content.Children.Add(new TextBlock
        {
            Text = _localization.Get("Scope_Subtitle"),
            TextWrapping = TextWrapping.Wrap,
            Style = (Style)Application.Current.Resources["BodyTextBlockStyle"],
        });
        content.Children.Add(entire);
        if (drivesPanel.Children.Count > 0)
        {
            content.Children.Add(drivesPanel);
        }

        content.Children.Add(folders);
        content.Children.Add(choose);
        content.Children.Add(emptyHint);
        content.Children.Add(rootList);

        var dialog = new ContentDialog
        {
            XamlRoot = XamlRoot,
            Title = _localization.Get("Scope_Title"),
            Content = new ScrollViewer { Content = content, MaxHeight = 460 },
            PrimaryButtonText = _localization.Get("Scope_Start"),
            CloseButtonText = _localization.Get("Common_Cancel"),
            DefaultButton = ContentDialogButton.Primary,
        };

        var result = await dialog.ShowAsync();
        if (result != ContentDialogResult.Primary)
        {
            return null;
        }

        if (folders.IsChecked == true)
        {
            // "Selected folders" with nothing selected is treated as "entire computer"; the hint in
            // the dialog says so before the user commits.
            return selectedRoots.Count == 0 ? Array.Empty<string>() : selectedRoots.ToArray();
        }

        foreach (var child in drivesPanel.Children)
        {
            if (child is RadioButton { IsChecked: true, Tag: string root })
            {
                return new[] { root };
            }
        }

        return Array.Empty<string>();
    }

    private static async Task<string?> PickFolderAsync()
    {
        var picker = new FolderPicker();
        picker.FileTypeFilter.Add("*");
        picker.SuggestedStartLocation = PickerLocationId.ComputerFolder;

        var handle = App.MainWindowHandle;
        if (handle == IntPtr.Zero)
        {
            return null;
        }

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

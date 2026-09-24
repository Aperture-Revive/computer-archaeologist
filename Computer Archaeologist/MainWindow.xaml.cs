using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Services;
using Microsoft.Extensions.Logging;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
using Windows.UI;

namespace ComputerArchaeologist;

/// <summary>
/// The single application window: a Task Manager style title bar plus a NavigationView shell and the
/// content frame.
/// <para>
/// The code-behind only wires navigation, window chrome and the caption button colours. Every piece of
/// business logic lives in a view model or a service.
/// </para>
/// </summary>
public sealed partial class MainWindow : Window
{
    private const int DefaultTitleBarHeight = 48;

    private readonly INavigationService _navigation;
    private readonly IThemeService _theme;
    private readonly ILocalizationService _localization;
    private readonly ILogger<MainWindow>? _logger;
    private bool _suppressSelection;

    public MainWindow(
        INavigationService navigation,
        IThemeService theme,
        ILocalizationService localization,
        ILogger<MainWindow>? logger = null)
    {
        _navigation = navigation;
        _theme = theme;
        _localization = localization;
        _logger = logger;

        InitializeComponent();

        Title = _localization.Get("App_DisplayName");
        AppTitleText.Text = Title;
        _localization.LanguageChanged += OnLanguageChanged;

        _theme.Register(this);
        SetUpTitleBar();

        _navigation.Initialize(ContentFrame);

        ContentFrame.Navigated += (_, args) =>
        {
            _suppressSelection = true;
            try
            {
                ShellNavigation.SelectedItem = args.SourcePageType switch
                {
                    Type t when t == typeof(Views.ArchaeologyPage) => ArchaeologyItem,
                    Type t when t == typeof(Views.DiscoveriesPage) => DiscoveriesItem,
                    Type t when t == typeof(Views.DiscoveryDetailPage) => DiscoveriesItem,
                    Type t when t == typeof(Views.ReportsPage) => ReportsItem,
                    Type t when t == typeof(Views.SettingsPage) => SettingsItem,
                    _ => HomeItem,
                };
            }
            finally
            {
                _suppressSelection = false;
            }
        };

        _navigation.Navigate(INavigationService.Home, null);
        ResizeAndCenter();
        Closed += (_, _) => _localization.LanguageChanged -= OnLanguageChanged;
    }

    private void OnLanguageChanged(object? sender, EventArgs e)
    {
        Title = _localization.Get("App_DisplayName");
        AppTitleText.Text = Title;
    }

    private void OnNavigationSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (_suppressSelection)
        {
            return;
        }

        if (args.SelectedItem is NavigationViewItem { Tag: string tag })
        {
            _navigation.Navigate(tag, null);
        }
    }

    /// <summary>Turns the app mark strip into a real, themable title bar.</summary>
    private void SetUpTitleBar()
    {
        try
        {
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(AppTitleBar);

            var titleBar = AppWindow.TitleBar;
            titleBar.ExtendsContentIntoTitleBar = true;

            // Tall follows the Windows 11 Task Manager proportions; older builds simply keep Standard.
            try
            {
                titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Tall title bars are not supported on this build");
            }

            var height = titleBar.Height > 0 ? titleBar.Height : DefaultTitleBarHeight;
            AppTitleBar.Height = height;
            ShellNavigation.IsTitleBarAutoPaddingEnabled = false;

            ApplyTitleBarColors();

            if (Content is FrameworkElement root)
            {
                root.ActualThemeChanged += (_, _) => ApplyTitleBarColors();
            }
        }
        catch (Exception ex)
        {
            // A chrome failure must never stop the application from showing its content.
            _logger?.LogWarning(ex, "The custom title bar could not be configured");
        }
    }

    /// <summary>Matches the caption buttons to the active Light/Dark theme and the Mica backdrop.</summary>
    private void ApplyTitleBarColors()
    {
        try
        {
            var titleBar = AppWindow.TitleBar;
            var isDark = (Content as FrameworkElement)?.ActualTheme == ElementTheme.Dark;

            titleBar.ButtonBackgroundColor = Colors.Transparent;
            titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;

            if (isDark)
            {
                titleBar.ButtonForegroundColor = Color.FromArgb(255, 0xF3, 0xF3, 0xF3);
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 0x8A, 0x8A, 0x8A);
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonHoverForegroundColor = Colors.White;
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF);
                titleBar.ButtonPressedForegroundColor = Colors.White;
            }
            else
            {
                titleBar.ButtonForegroundColor = Color.FromArgb(255, 0x1A, 0x1A, 0x1A);
                titleBar.ButtonInactiveForegroundColor = Color.FromArgb(255, 0x8A, 0x8A, 0x8A);
                titleBar.ButtonHoverBackgroundColor = Color.FromArgb(0x1A, 0x00, 0x00, 0x00);
                titleBar.ButtonHoverForegroundColor = Colors.Black;
                titleBar.ButtonPressedBackgroundColor = Color.FromArgb(0x2E, 0x00, 0x00, 0x00);
                titleBar.ButtonPressedForegroundColor = Colors.Black;
            }
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Caption button colours could not be applied");
        }
    }

    /// <summary>Gives the window a comfortable default size and centres it on the active display.</summary>
    private void ResizeAndCenter()
    {
        try
        {
            var appWindow = AppWindow;
            var area = DisplayArea.GetFromWindowId(appWindow.Id, DisplayAreaFallback.Primary);
            if (area is null)
            {
                return;
            }

            const int width = 1360;
            const int height = 900;
            var work = area.WorkArea;
            var w = Math.Min(width, Math.Max(720, work.Width - 120));
            var h = Math.Min(height, Math.Max(560, work.Height - 120));

            appWindow.Resize(new SizeInt32(w, h));
            appWindow.Move(new PointInt32(
                work.X + ((work.Width - w) / 2),
                work.Y + ((work.Height - h) / 2)));
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Window sizing was skipped");
        }
    }
}

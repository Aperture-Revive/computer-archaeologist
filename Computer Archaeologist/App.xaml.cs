using ComputerArchaeologist.Core.Infrastructure;
using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Settings;
using ComputerArchaeologist.ViewModels;
using ComputerArchaeologist.Infrastructure;
using ComputerArchaeologist.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ComputerArchaeologist.Core.Storage;

namespace ComputerArchaeologist;

/// <summary>
/// Application entry point. Owns the dependency-injection container, loads settings, applies the
/// saved theme and language, and creates the single main window.
/// </summary>
public partial class App : Microsoft.UI.Xaml.Application
{
    private Microsoft.UI.Xaml.Window? _window;
    private ILogger<App>? _logger;

    public App()
    {
        InitializeComponent();

        UnhandledException += OnUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    /// <summary>The process-wide service provider. View models resolve everything from here.</summary>
    public static IServiceProvider Services { get; private set; } = null!;

    /// <summary>The main window, needed by folder pickers and dialogs that require an owner handle.</summary>
    public static Microsoft.UI.Xaml.Window? MainWindow { get; private set; }

    /// <summary>Win32 handle of the main window, or <see cref="IntPtr.Zero"/> before it exists.</summary>
    public static IntPtr MainWindowHandle
    {
        get
        {
            if (MainWindow is null)
            {
                return IntPtr.Zero;
            }

            try
            {
                return WinRT.Interop.WindowNative.GetWindowHandle(MainWindow);
            }
            catch (Exception)
            {
                return IntPtr.Zero;
            }
        }
    }

    protected override async void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        try
        {
            var collection = new ServiceCollection();
            collection.AddComputerArchaeologistCore();
            collection.AddComputerArchaeologistApp();
            Services = collection.BuildServiceProvider(new ServiceProviderOptions
            {
                ValidateOnBuild = false,
                ValidateScopes = false,
            });

            _logger = Services.GetRequiredService<ILogger<App>>();
            _logger.LogInformation("Computer Archaeologist starting");

            var settings = Services.GetRequiredService<ISettingsService>();
            await settings.LoadAsync().ConfigureAwait(true);

            if (settings.LastLoadFailed)
            {
                _logger.LogWarning("Settings fell back to defaults");
            }

            var localization = Services.GetRequiredService<ILocalizationService>();
            localization.SetLanguage(settings.App.Language);
            Loc.Service = localization;

            Services.GetRequiredService<IThemeService>().Apply(settings.App.Theme);

            // Load the archived state before any page exists, so Home shows the previous run and
            // Reports can reopen the last report straight away.
            var state = Services.GetRequiredService<AppState>();
            await state.InitializeAsync().ConfigureAwait(true);

            _window = Services.GetRequiredService<MainWindow>();
            MainWindow = _window;
            _window.Activate();

            _logger.LogInformation(
                "Computer Archaeologist ready. Data folder: {Folder}, language: {Language}, restored session: {SessionId}",
                AppPaths.Root,
                localization.CurrentLanguage,
                state.CurrentSession?.SessionId ?? "none");
        }
        catch (Exception ex)
        {
            _logger?.LogCritical(ex, "Start-up failed");
            throw;
        }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unhandled UI exception: {Message}", e.Message);
    }

    private void OnDomainUnhandledException(object? sender, System.UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
        {
            _logger?.LogCritical(exception, "Unhandled application exception");
        }
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        _logger?.LogError(e.Exception, "Unobserved task exception");
        e.SetObserved();
    }
}

using ComputerArchaeologist.Core.Analysis;
using ComputerArchaeologist.Services;
using ComputerArchaeologist.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using ComputerArchaeologist.Core.Storage;

namespace ComputerArchaeologist.Infrastructure;

/// <summary>Registers the UI-layer services and view models on top of the Core registrations.</summary>
public static class AppServiceCollectionExtensions
{
    public static IServiceCollection AddComputerArchaeologistApp(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        // ---- UI infrastructure ----
        services.AddSingleton<IUiDispatcher, UiDispatcher>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<INavigationService, NavigationService>();
        services.AddSingleton<IFileLauncherService, FileLauncherService>();
        services.AddSingleton<IClipboardService, ClipboardService>();
        services.AddSingleton<IFilePreviewService, FilePreviewService>();
        services.AddSingleton<ArchaeologySettings>();

        // ---- shell ----
        services.AddSingleton<MainWindow>();

        // ---- shared state ----
        services.AddSingleton<AppState>();

        // ---- view models ----
        // Singletons: they subscribe to long-lived events (session changes, language changes) for the
        // lifetime of the process, and the archaeology page must survive navigation.
        services.AddSingleton<HomeViewModel>();
        services.AddSingleton<ArchaeologyViewModel>();
        services.AddSingleton<DiscoveriesViewModel>();
        services.AddSingleton<ReportsViewModel>();
        services.AddSingleton<SettingsViewModel>();
        services.AddSingleton<DiscoveryDetailViewModel>();

        return services;
    }
}

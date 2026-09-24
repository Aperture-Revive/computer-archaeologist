using ComputerArchaeologist.Core.Ai;
using ComputerArchaeologist.Core.Analysis;
using ComputerArchaeologist.Core.Discovery;
using ComputerArchaeologist.Core.Localization;
using ComputerArchaeologist.Core.Models;
using ComputerArchaeologist.Core.Options;
using ComputerArchaeologist.Core.Orchestration;
using ComputerArchaeologist.Core.Reports;
using ComputerArchaeologist.Core.Security;
using ComputerArchaeologist.Core.Settings;
using ComputerArchaeologist.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace ComputerArchaeologist.Core.Infrastructure;

/// <summary>
/// The single place where the object graph is assembled. View models resolve services from here;
/// <c>new LocalFileDiscoveryService()</c> never appears in application code.
/// </summary>
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddComputerArchaeologistCore(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        AppPaths.EnsureCreated();

        services.AddLogging(builder =>
        {
            builder.SetMinimumLevel(LogLevel.Information);
            builder.AddProvider(new FileLoggerProvider(AppPaths.LogDirectory));
        });

        // ---- settings + options (process-wide singletons, mutated in place by SettingsService) ----
        services.AddSingleton<AppOptions>();
        services.AddSingleton<OpenAiOptions>();
        services.AddSingleton<DiscoveryOptions>();
        services.AddSingleton<ArchaeologyOptions>();
        services.AddSingleton<InterestingnessWeightsOptions>();
        services.AddSingleton<ExclusionPolicyProvider>();

        // ---- localisation ----
        services.AddSingleton<ILocalizationService>(sp => new ReswLocalizationService(
            sp.GetService<ILogger<ReswLocalizationService>>()));

        // ---- security ----
        services.AddSingleton<ISecureStorage, WindowsSecureStorage>();

        // ---- discovery ----
        // No external index and no third-party service: the application walks the local file system
        // itself, in parallel and with hard budgets.
        services.AddSingleton<IFileDiscoveryService, LocalFileDiscoveryService>();

        // ---- analysis ----
        services.AddSingleton<IFileAnalysisService, FileAnalysisService>();
        services.AddSingleton<InterestingnessCalculator>();

        // ---- AI + reporting ----
        services.AddSingleton<IAiAnalysisService, AiAnalysisService>();
        services.AddSingleton<IReportGenerationService, ReportGenerationService>();

        // ---- persistence ----
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IScanRepository, JsonScanRepository>();

        // ---- orchestration ----
        services.AddSingleton<IArchaeologyPipeline, ArchaeologyPipeline>();

        return services;
    }
}

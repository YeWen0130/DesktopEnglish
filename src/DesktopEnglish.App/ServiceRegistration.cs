using DesktopEnglish.Data;
using DesktopEnglish.Services;
using DesktopEnglish.ViewModels;
using DesktopEnglish.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Extensions.Logging;

namespace DesktopEnglish;

/// <summary>
/// Composition root. All services are registered here so no class needs to reach for a global container.
/// </summary>
public static class ServiceRegistration
{
    public static IServiceCollection AddApplicationServices(
        this IServiceCollection services,
        IConfiguration configuration,
        Serilog.ILogger bootstrapLogger)
    {
        // Options bound from JSON; these are defaults, the database holds the user's overrides.
        services.Configure<LoggingOptions>(configuration.GetSection("App:Logging"));

        var appOptions = new AppOptions();
        configuration.GetSection(AppOptions.SectionName).Bind(appOptions);
        services.AddSingleton(appOptions);

        // Serilog as the single logging backend.
        services.AddLogging(builder =>
        {
            builder.ClearProviders();
            builder.AddProvider(new SerilogLoggerProvider(bootstrapLogger, dispose: true));
        });

        // Data access: a factory keeps contexts short-lived and safe across UI and background threads.
        // The connection string and the interceptor come from SqliteConfiguration so every context commits
        // durably; see that type for why the application no longer runs on SQLite defaults.
        services.AddDbContextFactory<AppDbContext>(options =>
            options
                .UseSqlite(SqliteConfiguration.BuildConnectionString(AppPaths.DatabasePath))
                .AddInterceptors(new SqliteDurabilityInterceptor()));

        services.AddSingleton<DatabaseMaintenanceService>();

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IDispatcherService>(sp =>
            new DispatcherService(sp.GetRequiredService<System.Windows.Application>().Dispatcher));
        services.AddSingleton<IReminderStateService, ReminderStateService>();
        services.AddSingleton<IReviewScheduler, ReviewScheduler>();
        services.AddSingleton<IStartupService, StartupService>();
        services.AddSingleton<SingleInstanceGuard>();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IWordService, WordService>();
        services.AddSingleton<IWordImportService, WordImportService>();
        services.AddSingleton<IExampleProvider, NullExampleProvider>();
        services.AddSingleton<IStatisticsService, StatisticsService>();
        services.AddSingleton<IWordSelectionService, WordSelectionService>();
        services.AddSingleton<IStudySessionService, StudySessionService>();
        services.AddSingleton<IStudyBatchService, StudyBatchService>();
        services.AddSingleton<IReminderPlanner, ReminderPlanner>();
        services.AddSingleton<SeedDataService>();
        services.AddSingleton<INotificationService, ToastNotificationService>();
        services.AddSingleton<IToastActivationHandler, ToastActivationHandler>();
        services.AddSingleton<IStudyLauncher, StudyLauncher>();

        // The background reminder loop runs for the lifetime of the host.
        services.AddHostedService<ReminderScheduler>();

        // View models.
        services.AddSingleton<MainWindowViewModel>();
        services.AddTransient<DashboardViewModel>();
        services.AddTransient<WordsViewModel>();
        services.AddTransient<StudyViewModel>();
        services.AddTransient<SettingsViewModel>();

        return services;
    }
}



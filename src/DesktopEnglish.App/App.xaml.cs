using System.Windows;
using System.Windows.Threading;
using DesktopEnglish.Data;
using DesktopEnglish.Logging;
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
/// Application entry point. Startup order matters: configuration, then logging, then database, then the
/// background reminder loop, then the tray icon. The main window is deliberately not opened at launch.
/// </summary>
public partial class App : Application, IShellController
{
    private readonly CancellationTokenSource _shutdownCts = new();
    private IHost? _host;
    private TrayHost? _tray;
    private SingleInstanceGuard? _guard;
    private bool _isShuttingDown;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        AppPaths.EnsureCreated();

        var configuration = AppConfiguration.BuildConfiguration();
        var options = new AppOptions();
        configuration.GetSection(AppOptions.SectionName).Bind(options);

        var logger = AppConfiguration.CreateBootstrapLogger(options.Logging);
        Log.Logger = logger;

        try
        {
            // Only one instance may run; a later launch just surfaces the existing window.
            _guard = new SingleInstanceGuard(
                new SerilogLoggerFactory(logger).CreateLogger<SingleInstanceGuard>());

            if (!_guard.TryAcquire())
            {
                Shutdown();
                return;
            }

            _host = BuildHost(configuration, logger);

            // The database is verified - and repaired if it was damaged - before the reminder loop can read
            // from it. Doing this before the host starts keeps background services away from the file while
            // its journal mode is being pinned.
            var databaseReport = await _host.Services
                .GetRequiredService<DatabaseMaintenanceService>()
                .EnsureHealthyAsync(_shutdownCts.Token);

            // Start on a background thread: a BackgroundService started from the UI thread would post its
            // continuations back onto the WPF dispatcher, which would let the reminder loop block the UI.
            await Task.Run(() => _host.StartAsync(_shutdownCts.Token));

            // Data must be ready before the reminder loop produces its first tick.
            await InitializeDataAsync(databaseReport, _shutdownCts.Token);
            await InitializeSettingsAsync(_shutdownCts.Token);

            _tray = TrayHost.Create(
                _host.Services,
                (ToastNotificationService)_host.Services.GetRequiredService<INotificationService>(),
                _host.Services.GetRequiredService<ILogger<TrayHost>>());
            _tray.Show();
    
            // Subscribing initializes the toast AUMID/COM registration; that native work stays off the UI thread.
            _ = Task.Run(() => _host.Services.GetRequiredService<IToastActivationHandler>().Start());

            var dispatcherService = _host.Services.GetRequiredService<IDispatcherService>();
            _guard.StartListening(() => dispatcherService.Invoke(ShowMainWindow));

            logger.Information("DesktopEnglish started; running in the system tray.");
        }
        catch (Exception ex)
        {
            logger.Fatal(ex, "Startup failed.");
            MessageBox.Show(
                $"程序启动失败：{ex.Message}\n\n详细信息已写入日志：{AppPaths.LogDirectory}",
                "DesktopEnglish",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
        }
    }

    private IHost BuildHost(IConfigurationRoot configuration, Serilog.ILogger logger) =>
        Host.CreateDefaultBuilder()
            .UseSerilog(logger)
            .ConfigureServices(services =>
            {
                services.AddSingleton<IConfiguration>(configuration);
                // The App instance is both the WPF application object (used to reach the dispatcher)
                // and the shell controller the tray menu drives.
                services.AddSingleton<System.Windows.Application>(this);
                services.AddSingleton<IShellController>(this);
                services.AddApplicationServices(configuration, logger);
            })
            .Build();

    /// <summary>
    /// Migrates the schema and seeds the dictionary before the scheduler starts, so the first reminder tick
    /// already has words to choose from.
    /// </summary>
    private async Task InitializeDataAsync(DatabaseMaintenanceReport report, CancellationToken cancellationToken)
    {
        var factory = _host!.Services.GetRequiredService<IDbContextFactory<AppDbContext>>();

        await using (var context = await factory.CreateDbContextAsync(cancellationToken))
        {
            await context.Database.MigrateAsync(cancellationToken);
        }

        var seed = _host.Services.GetRequiredService<SeedDataService>();

        // After a recovery the dictionary can be missing whatever lived on the lost pages, so the embedded
        // word list is re-imported. Existing words and their review state are left untouched.
        if (report.WasRecovered)
        {
            await seed.EnsureWordListCompleteAsync(cancellationToken);
        }
        else
        {
            await seed.SeedIfEmptyAsync(cancellationToken);
        }
    }

    private Task InitializeSettingsAsync(CancellationToken cancellationToken) =>
        _host!.Services.GetRequiredService<ISettingsService>().LoadAsync(cancellationToken);

    /// <inheritdoc />
    public void ShowMainWindow()
    {
        var window = GetOrCreateMainWindow();

        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();

        if (window.DataContext is MainWindowViewModel vm)
        {
            vm.ShowDashboard();
        }
    }

    /// <inheritdoc />
    public void ShowSettings()
    {
        var window = GetOrCreateMainWindow();

        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();

        if (window.DataContext is MainWindowViewModel vm)
        {
            vm.Navigate(AppPage.Settings);
        }
    }

    /// <inheritdoc />
    public void StartStudy()
    {
        var window = GetOrCreateMainWindow();

        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();

        if (window.DataContext is MainWindowViewModel vm)
        {
            vm.StartStudy();
        }
    }

    private MainWindow GetOrCreateMainWindow()
    {
        var existing = Windows.OfType<MainWindow>().FirstOrDefault();
        if (existing is not null)
        {
            return existing;
        }

        var window = new MainWindow
        {
            DataContext = _host!.Services.GetRequiredService<MainWindowViewModel>(),
        };

        // Closing the window hides it; the reminder service keeps running in the tray.
        window.Closing += (_, args) =>
        {
            if (_isShuttingDown)
            {
                return;
            }

            args.Cancel = true;
            window.Hide();
        };

        return window;
    }

    /// <summary>Requests an orderly shutdown from the tray menu.</summary>
    public void RequestShutdown() => Dispatcher.Invoke(Shutdown);

    protected override void OnExit(ExitEventArgs e)
    {
        if (!_isShuttingDown)
        {
            _isShuttingDown = true;

            try
            {
                _shutdownCts.Cancel();

                if (_tray is not null)
                {
                    _tray.DisposeAsync().AsTask().Wait(TimeSpan.FromSeconds(3));
                }

                if (_host is not null)
                {
                    _host.StopAsync(TimeSpan.FromSeconds(5)).GetAwaiter().GetResult();
                    _host.Dispose();
                }

                _guard?.Dispose();
                _shutdownCts.Dispose();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error while shutting down.");
            }
            finally
            {
                Log.Information("DesktopEnglish stopped.");
                Log.CloseAndFlush();
            }
        }

        base.OnExit(e);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unhandled dispatcher exception.");

        // Keep the app alive: a UI fault should not tear down the reminder service.
        e.Handled = true;
    }

    private static void OnDomainUnhandledException(object sender, UnhandledExceptionEventArgs e) =>
        Log.Fatal(e.ExceptionObject as Exception, "Unhandled domain exception.");

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        Log.Error(e.Exception, "Unobserved task exception.");
        e.SetObserved();
    }
}

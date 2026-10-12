using System.Windows;
using System.Windows.Threading;
using DesktopEnglish.Data;
using DesktopEnglish.Installation;
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

        var mode = StartupCommandLine.Parse(e.Args);

        var configuration = AppConfiguration.BuildConfiguration();
        var options = new AppOptions();
        configuration.GetSection(AppOptions.SectionName).Bind(options);

        if (mode == StartupMode.Uninstall)
        {
            // A file sink under the data directory would keep that directory locked while the uninstaller
            // deletes it, so uninstall runs on an in-memory logger and leaves no file behind.
            var uninstallLogger = AppConfiguration.CreateMemoryLogger(options.Logging);
            Log.Logger = uninstallLogger;
            RunUninstall(uninstallLogger);
            return;
        }

        AppPaths.EnsureCreated();

        var logger = AppConfiguration.CreateBootstrapLogger(options.Logging);
        Log.Logger = logger;

        try
        {
            // Only one instance may run; a later launch just surfaces the existing window.
            _guard = new SingleInstanceGuard(
                new SerilogLoggerFactory(logger).CreateLogger<SingleInstanceGuard>());

            if (!_guard.TryAcquire())
            {
                _guard.SignalExistingInstance();
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

            // Keep the uninstall entry pointing at this executable: the app is portable, so its path can
            // change between launches and Windows would otherwise offer to uninstall the old location.
            RegisterUninstallEntry(logger);

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
                $"程序启动失败:{ex.Message}\n\n详细信息已写入日志:{AppPaths.LogDirectory}",
                "DesktopEnglish",
                MessageBoxButton.OK,
                MessageBoxImage.Error);

            Shutdown();
        }
    }



    private void RunUninstall(Serilog.ILogger logger)
    {
        // Never let an uninstall failure leave the app half-removed without a record of why.
        try
        {
            var logFactory = new SerilogLoggerFactory(logger);
            var service = new UninstallService(
                new CurrentUserRegistry(),
                new ApplicationStopper(),
                new UninstallEnvironment(),
                logFactory.CreateLogger<UninstallService>());

            var report = service.Uninstall();

            var message = report.ExecutableDeletionScheduled
                ? "卸载完成,程序文件将在几秒后自动删除。"
                : $"卸载完成,但程序文件未能自动删除,请手动删除:{report.ExecutablePath}";

            MessageBox.Show(
                $"{message}\n\n用户数据目录已删除:{report.DataDirectory}",
                AppInstallInfo.DisplayName,
                MessageBoxButton.OK,
                MessageBoxImage.Information);

            logger.Information("Uninstall completed; scheduling shutdown.");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Uninstall failed.");
            MessageBox.Show(
                $"卸载失败:{ex.Message}",
                AppInstallInfo.DisplayName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        finally
        {
            Log.CloseAndFlush();
        }

        Shutdown();
    }

    private void RegisterUninstallEntry(Serilog.ILogger logger)
    {
        try
        {
            var service = _host!.Services.GetRequiredService<IUninstallService>();
            service.Register();
        }
        catch (Exception ex)
        {
            // Registration is best effort; the app still runs without an entry in Apps & features.
            logger.Warning(ex, "Could not register the uninstall entry.");
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

    /// <inheritdoc />
    public void RequestUninstall()
    {
        var confirm = MessageBox.Show(
            "确定要卸载“背单词”吗?\n\n将删除程序文件、开机启动项、通知登记和全部学习数据,且无法恢复。",
            AppInstallInfo.DisplayName,
            MessageBoxButton.OKCancel,
            MessageBoxImage.Warning,
            MessageBoxResult.Cancel);

        if (confirm != MessageBoxResult.OK)
        {
            return;
        }

        // Restart the same executable in uninstall mode. Doing the removal in a fresh process lets the
        // current one shut down cleanly first, so its database and file locks are released.
        try
        {
            var executable = Environment.ProcessPath
                ?? throw new InvalidOperationException("Could not resolve the running executable path.");

            var startInfo = new System.Diagnostics.ProcessStartInfo
            {
                FileName = executable,
                Arguments = AppInstallInfo.UninstallSwitch,
                UseShellExecute = true,
            };

            System.Diagnostics.Process.Start(startInfo);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Could not start the uninstaller.");
            MessageBox.Show(
                $"无法启动卸载程序:{ex.Message}",
                AppInstallInfo.DisplayName,
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            return;
        }

        Shutdown();
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
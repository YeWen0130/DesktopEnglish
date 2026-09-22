using System.Reflection;
using DesktopEnglish.Data;
using DesktopEnglish.Services;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

namespace DesktopEnglish.Logging;

/// <summary>
/// Builds the application configuration and the Serilog logger.
/// Defaults are read from an embedded JSON file so single-file publish keeps working, then an optional
/// real file under the data directory lets the user override them.
/// </summary>
public static class AppConfiguration
{
    public const string EmbeddedDefaultsName = "DesktopEnglish.appsettings.json";

    /// <summary>
    /// Loads defaults from the embedded resource and layers the user's optional override on top.
    /// </summary>
    public static IConfigurationRoot BuildConfiguration()
    {
        var builder = new ConfigurationBuilder();

        // AddJsonStream only reads the stream when Build() runs, so handing it the original manifest
        // stream inside a using block would dispose it too early. Copy it into memory first; the
        // configuration provider then owns the copy for the rest of its lifetime.
        using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(EmbeddedDefaultsName))
        {
            if (stream is not null)
            {
                var copy = new MemoryStream();
                stream.CopyTo(copy);
                copy.Position = 0;
                builder.AddJsonStream(copy);
            }
        }

        // A file the user can edit; absent by default, and never required for the app to start.
        builder.AddJsonFile(AppPaths.UserSettingsJsonPath, optional: true, reloadOnChange: false);

        return builder.Build();
    }

    /// <summary>Creates the logger used before the host exists, so startup failures are still recorded.</summary>
    public static Serilog.ILogger CreateBootstrapLogger(LoggingOptions options) => CreateLogger(options);

    public static Serilog.ILogger CreateLogger(LoggingOptions options)
    {
        AppPaths.EnsureCreated();

        var level = Enum.TryParse<LogEventLevel>(options.MinimumLevel, ignoreCase: true, out var parsed)
            ? parsed
            : LogEventLevel.Information;

        var configuration = new LoggerConfiguration()
            .MinimumLevel.Is(level)
            // Entity Framework logs every SQL statement at Information; that buries the messages that
            // matter, so keep it quiet unless something actually fails.
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .WriteTo.File(
                Path.Combine(AppPaths.LogDirectory, "desktopenglish-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: Math.Max(1, options.RetainedFileCount),
                shared: true,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {SourceContext}: {Message:lj}{NewLine}{Exception}");

        if (options.WriteToConsole)
        {
            configuration.WriteTo.Console();
        }

        return configuration.CreateLogger();
    }
}

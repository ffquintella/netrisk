using System;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Model.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Sinks.SystemConsole.Themes;

namespace GUIClient;

public static class LoggingBootstrapper
{
    public static void RegisterLogging(IServiceCollection services)
    {
        services.AddSingleton<Serilog.ILogger>(sp =>
        {
            var config = sp.GetRequiredService<LoggingConfiguration>();
            var logFilePath = GetLogFileName(config);
            var loggerConf = new LoggerConfiguration()
                .MinimumLevel.Override("Default", config.DefaultLogLevel)
                .MinimumLevel.Override("Microsoft", config.MicrosoftLogLevel)
                .MinimumLevel.Override("System", config.SystemLogLevel)
                .WriteTo.Console(theme: AnsiConsoleTheme.Code)
                // Matches the rolling configuration used by API / BackgroundJobs / WebSite:
                // the sink owns the date suffix and retention, rather than the filename being
                // stamped once at startup.
                .WriteTo.File(logFilePath, fileSizeLimitBytes: 10000000, rollOnFileSizeLimit: true,
                    rollingInterval: RollingInterval.Day);

            switch (config.DefaultLogLevel)
            {
                case LogEventLevel.Debug:
                    loggerConf.MinimumLevel.Debug();
                    break;
                case LogEventLevel.Information:
                    loggerConf.MinimumLevel.Information();
                    break;
                case LogEventLevel.Warning:
                    loggerConf.MinimumLevel.Warning();
                    break;
            }

            var logger = loggerConf.CreateLogger();
            Log.Logger = logger;
            logger.Information("Logging initialized");
            return logger;
        });

        services.AddSingleton<ILoggerFactory>(sp =>
            new SerilogLoggerFactory(sp.GetRequiredService<Serilog.ILogger>()));
    }

    private static string GetLogFileName(LoggingConfiguration config)
    {
        var appPersDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NRGUIClient");
        Directory.CreateDirectory(appPersDir);
        var logPath = Path.Combine(appPersDir, "logs");

        if (!Directory.Exists(logPath))
        {
            Directory.CreateDirectory(logPath);
        }

        // Deliberately undated: the File sink appends the date itself when rolling daily. Stamping
        // the date here instead meant a client left open past midnight kept writing to the file for
        // the day it was launched.
        var logFile = Path.Combine(logPath, "nr-gui.log");

        return logFile;
    }
}

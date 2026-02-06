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
                .WriteTo.File(logFilePath);

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
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var logPath = Path.Combine(appPersDir, "logs");

        if (!Directory.Exists(logPath))
        {
            Directory.CreateDirectory(logPath);
        }
        var logFile = Path.Combine(logPath, $"log-{date}.txt");

        return logFile;
    }
}

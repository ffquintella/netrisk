﻿using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Model.Configuration;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;
using Serilog.Sinks.SystemConsole.Themes;
using Splat;
using LogLevel = Microsoft.Extensions.Logging.LogLevel;

namespace GUIClient;

public static class LoggingBootstrapper
{
    public static void RegisterLogging(IMutableDependencyResolver services, IReadonlyDependencyResolver resolver)
    {
        var config = resolver.GetService<LoggingConfiguration>();
        if (config == null) throw new Exception("Could not load configuration");
        var logFilePath = GetLogFileName(config, resolver);
        var loggerConf = new LoggerConfiguration()
            .MinimumLevel.Override("Default", config.DefaultLogLevel)
            .MinimumLevel.Override("Microsoft", config.MicrosoftLogLevel)
            .MinimumLevel.Override("System", config.SystemLogLevel)
            //.WriteTo.Console()
            .WriteTo.Console(theme: AnsiConsoleTheme.Code)
            .WriteTo.File(logFilePath);
            //.WriteTo.RollingFile(logFilePath, fileSizeLimitBytes: config.LimitBytes)
            //.CreateLogger();

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
        
        var factory = new SerilogLoggerFactory(logger);
        
        Log.Logger = logger;
        
        //services.RegisterConstant<ILoggerFactory>(factory);
        services.RegisterConstant<Serilog.ILogger>(logger);
        
        logger.Information("Logging initialized");
        
        services.RegisterLazySingleton<ILoggerFactory>(() => factory);
        
        /*services.RegisterLazySingleton(() =>
        {
            return factory.CreateLogger("Default");
        });*/
    }

    private static string GetLogFileName(LoggingConfiguration config,
        IReadonlyDependencyResolver resolver)
    {
        
        var appPersDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "NRGUIClient");
        Directory.CreateDirectory(appPersDir);
        var date = DateTime.Now.ToString("yyyy-MM-dd");
        var logPath = Path.Combine( appPersDir, "logs");

        if (!Directory.Exists(logPath))
        {
            Directory.CreateDirectory(logPath);
        }
        var logFile = Path.Combine( logPath, $"log-{date}.txt");
        

        return logFile;
    }
}
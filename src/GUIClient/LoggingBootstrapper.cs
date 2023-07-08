using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Model.Configuration;
using Serilog;
using Serilog.Extensions.Logging;
using Splat;

namespace GUIClient;

public static class LoggingBootstrapper
{
    public static void RegisterLogging(IMutableDependencyResolver services, IReadonlyDependencyResolver resolver)
    {
        var config = resolver.GetService<LoggingConfiguration>();
        if (config == null) throw new Exception("Could not load configuration");
        var logFilePath = GetLogFileName(config, resolver);
        var logger = new LoggerConfiguration()
            .MinimumLevel.Override("Default", config.DefaultLogLevel)
            .MinimumLevel.Override("Microsoft", config.MicrosoftLogLevel)
            .WriteTo.Console()
            .WriteTo.RollingFile(logFilePath, fileSizeLimitBytes: config.LimitBytes)
            .CreateLogger();
        var factory = new SerilogLoggerFactory(logger);
        
        services.RegisterConstant<ILoggerFactory>(factory);
        
        /*services.RegisterLazySingleton(() =>
        {
            return factory.CreateLogger("Default");
        });*/
    }

    private static string GetLogFileName(LoggingConfiguration config,
        IReadonlyDependencyResolver resolver)
    {
        
        var logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SRGUIClient");
        Directory.CreateDirectory(logDir);
        var logPath = Path.Combine( logDir, "logs");

        if (!Directory.Exists(logPath))
        {
            Directory.CreateDirectory(logPath);
        }

        return logPath;
    }
}
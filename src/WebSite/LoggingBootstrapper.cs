using System.Runtime.InteropServices;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using ILogger = Serilog.ILogger;

namespace WebSite;

public static class LoggingBootstrapper
{
    public static void RegisterLogging(IServiceCollection services, IConfiguration config)
    {
         string logDir = "";
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            logDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/nrserver";
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            logDir = Path.Combine( "/var/log/" , "nrserver");
        if(RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            logDir = Path.Combine( "/tmp/" , "nrserver");
        Directory.CreateDirectory(logDir);

        var logFile = Path.Combine(logDir, "nr-website.log");

        LoggingLevelSwitch defaultLoggingLevel = new LoggingLevelSwitch();
        switch (config["Logging:LogLevel:Default"])
        {
            case "Information":
                defaultLoggingLevel.MinimumLevel = LogEventLevel.Information;
                break;
            case "Warning":
                defaultLoggingLevel.MinimumLevel = LogEventLevel.Warning;
                break;
            case "Error":
                defaultLoggingLevel.MinimumLevel = LogEventLevel.Error;
                break;
            case "Debug":
                defaultLoggingLevel.MinimumLevel = LogEventLevel.Debug;
                break;
            case "Fatal":
                defaultLoggingLevel.MinimumLevel = LogEventLevel.Fatal;
                break;
            case "Verbose":
                defaultLoggingLevel.MinimumLevel = LogEventLevel.Verbose;
                break;
            default:
                defaultLoggingLevel.MinimumLevel = LogEventLevel.Warning;
                break;
        }
        
        LoggingLevelSwitch microsoftLoggingLevel = new LoggingLevelSwitch();
        switch (config["Logging:LogLevel:Microsoft"])
        {
            case "Information":
                microsoftLoggingLevel.MinimumLevel = LogEventLevel.Information;
                break;
            case "Warning":
                microsoftLoggingLevel.MinimumLevel = LogEventLevel.Warning;
                break;
            case "Error":
                microsoftLoggingLevel.MinimumLevel = LogEventLevel.Error;
                break;
            case "Debug":
                microsoftLoggingLevel.MinimumLevel = LogEventLevel.Debug;
                break;
            case "Fatal":
                microsoftLoggingLevel.MinimumLevel = LogEventLevel.Fatal;
                break;
            case "Verbose":
                microsoftLoggingLevel.MinimumLevel = LogEventLevel.Verbose;
                break;
            default:
                microsoftLoggingLevel.MinimumLevel = LogEventLevel.Warning;
                break;
        }

        Logger? logger;
        if (defaultLoggingLevel.MinimumLevel == LogEventLevel.Debug)
        {
            logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .MinimumLevel.Override("Default", defaultLoggingLevel)
                .MinimumLevel.Override("Microsoft", microsoftLoggingLevel)
                .WriteTo.Console()
                .WriteTo.RollingFile(logFile, fileSizeLimitBytes: 10000)
                .CreateLogger();
        }
        else
        {
            logger = new LoggerConfiguration()
                .MinimumLevel.Override("Default", defaultLoggingLevel)
                .MinimumLevel.Override("Microsoft", microsoftLoggingLevel)
                .WriteTo.Console()
                .WriteTo.RollingFile(logFile, fileSizeLimitBytes: 10000)
                .CreateLogger();
        }

        
        
        
        var factory = new SerilogLoggerFactory(logger);

        Log.Logger = logger;
        
        services.AddSingleton<ILoggerFactory>(factory);
        
        services.AddSingleton<ILogger>(logger);
    }
}
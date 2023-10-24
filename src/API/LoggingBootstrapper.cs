using System.Runtime.InteropServices;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using Serilog.Extensions.Logging;
using ILogger = Serilog.ILogger;

namespace API;

public static class LoggingBootstrapper
{
    public static void RegisterLogging(IServiceCollection services,IConfiguration config)
    {
        string logDir = "";
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            logDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData) + "/netrisk-api";
        if(RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            logDir = Path.Combine( "/var/log/" , "netrisk");
        if(RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            logDir = Path.Combine( "/tmp/" , "netrisk-api");
        Directory.CreateDirectory(logDir);

        var logFile = Path.Combine(logDir, "nr-api.log");
        
        var levelSwitch = new LoggingLevelSwitch();

        LoggingLevelSwitch defaultLoggingLevel = new LoggingLevelSwitch();
        switch (config["Logging:LogLevel:Default"])
        {
            case "Information":
                defaultLoggingLevel.MinimumLevel = LogEventLevel.Information;
                levelSwitch.MinimumLevel = LogEventLevel.Information;
                break;
            case "Warning":
                defaultLoggingLevel.MinimumLevel = LogEventLevel.Warning;
                levelSwitch.MinimumLevel = LogEventLevel.Warning;
                break;
            case "Error":
                defaultLoggingLevel.MinimumLevel = LogEventLevel.Error;
                levelSwitch.MinimumLevel = LogEventLevel.Error;
                break;
            case "Debug":
                defaultLoggingLevel.MinimumLevel = LogEventLevel.Debug;
                levelSwitch.MinimumLevel = LogEventLevel.Debug;
                break;
            case "Fatal":
                defaultLoggingLevel.MinimumLevel = LogEventLevel.Fatal;
                levelSwitch.MinimumLevel = LogEventLevel.Fatal;
                break;
            case "Verbose":
                defaultLoggingLevel.MinimumLevel = LogEventLevel.Verbose;
                levelSwitch.MinimumLevel = LogEventLevel.Verbose;
                break;
            default:
                defaultLoggingLevel.MinimumLevel = LogEventLevel.Warning;
                levelSwitch.MinimumLevel = LogEventLevel.Warning;
                break;
        }
        var mSLevelSwitch = new LoggingLevelSwitch();
        LoggingLevelSwitch microsoftLoggingLevel = new LoggingLevelSwitch();
        switch (config["Logging:LogLevel:Microsoft"])
        {
            case "Information":
                microsoftLoggingLevel.MinimumLevel = LogEventLevel.Information;
                mSLevelSwitch.MinimumLevel = LogEventLevel.Information;
                break;
            case "Warning":
                microsoftLoggingLevel.MinimumLevel = LogEventLevel.Warning;
                mSLevelSwitch.MinimumLevel = LogEventLevel.Warning;
                break;
            case "Error":
                microsoftLoggingLevel.MinimumLevel = LogEventLevel.Error;
                mSLevelSwitch.MinimumLevel = LogEventLevel.Error;
                break;
            case "Debug":
                microsoftLoggingLevel.MinimumLevel = LogEventLevel.Debug;
                mSLevelSwitch.MinimumLevel = LogEventLevel.Debug;
                break;
            case "Fatal":
                microsoftLoggingLevel.MinimumLevel = LogEventLevel.Fatal;
                mSLevelSwitch.MinimumLevel = LogEventLevel.Fatal;
                break;
            case "Verbose":
                microsoftLoggingLevel.MinimumLevel = LogEventLevel.Verbose;
                mSLevelSwitch.MinimumLevel = LogEventLevel.Verbose;
                break;
            default:
                microsoftLoggingLevel.MinimumLevel = LogEventLevel.Warning;
                mSLevelSwitch.MinimumLevel = LogEventLevel.Warning;
                break;
        }

        Logger? logger;

        logger = new LoggerConfiguration()
            .MinimumLevel.ControlledBy(levelSwitch)
            .MinimumLevel.Override("Microsoft", mSLevelSwitch)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", mSLevelSwitch)
            .MinimumLevel.Override("Pomelo.EntityFrameworkCore", mSLevelSwitch)
            .WriteTo.Console()
            .WriteTo.File(logFile, fileSizeLimitBytes: 1000000, rollOnFileSizeLimit: true, rollingInterval: RollingInterval.Day)
            .CreateLogger();
        

        
        var factory = new SerilogLoggerFactory(logger);

        Log.Logger = logger;
        
        services.AddSingleton<ILoggerFactory>(factory);
        
        services.AddSingleton<ILogger>(logger);
        

    }
}
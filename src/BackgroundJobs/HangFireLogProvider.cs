using System;
using Hangfire.Logging;
using Serilog;
using Serilog.Core;

namespace BackgroundJobs;

public class HangFireLogProvider : ILogProvider
{
    public ILog GetLogger(string name)
    {
        return new HangefireLogger(name);
    }

    public class HangefireLogger : ILog
    {
        private readonly ILogger _serilogLogger;

        public HangefireLogger(string name)
        {
            //_log4Netlogger = log4net.LogManager.GetLogger("Hangfire", name);
            _serilogLogger = Serilog.Log.Logger;
        }

        public bool Log(LogLevel logLevel, Func<string>? messageFunc, Exception? exception)
        {
            if (exception == null) return false;
            if(messageFunc == null) return false;
                
            
            switch (logLevel)
            {
                case (LogLevel.Debug):
                    _serilogLogger.Debug(exception, messageFunc());
                    break;
                case (LogLevel.Error):
                    _serilogLogger.Error(exception, messageFunc());
                    break;
                case (LogLevel.Fatal):
                    _serilogLogger.Fatal(exception, messageFunc());
                    break;
                case (LogLevel.Info):
                    _serilogLogger.Information(exception, messageFunc());
                    break;
                case (LogLevel.Trace):
                    _serilogLogger.Verbose(exception, messageFunc());
                    break;
                case (LogLevel.Warn):
                    _serilogLogger.Warning(exception, messageFunc());
                    break;
            }

            return true;
        }
    }
}
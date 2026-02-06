using System;
using ClientServices;
using Serilog;
using ILogger = Serilog.ILogger;

namespace ClientServices.Services;

public class ServiceBase
{
    protected ILogger Logger { get; } = ServiceProviderAccessor.GetRequiredService<ILogger>();

    protected static T GetService<T>() where T : notnull
    {
        var result = ServiceProviderAccessor.GetRequiredService<T>();
        return result;
    }
}

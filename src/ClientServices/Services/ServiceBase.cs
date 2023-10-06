using Splat;
using ILogger = Serilog.ILogger;

namespace ClientServices.Services;

public class ServiceBase
{
    
    protected ILogger Logger { get; } = GetService<ILogger>();

    protected static T GetService<T>()
    {
        var result = Locator.Current.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    } 
}
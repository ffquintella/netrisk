using Splat;
using ILogger = Serilog.ILogger;

namespace ClientServices.Services.Importers;

public class BaseImporter
{
    protected ILogger Logger { get; } = GetService<ILogger>();

    protected static T GetService<T>()
    {
        var result = Locator.Current.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    } 
}
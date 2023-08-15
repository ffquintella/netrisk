using ClientServices.Interfaces;
using Splat;

namespace ClientServices.Services;
using Serilog;

public class ServiceBase
{
    protected ILogger _logger;
    protected IRestService _restService;
    
    public ServiceBase(
        IRestService restService)
    {
        _restService = restService;
        //_logger = Log.Logger;

        _logger = GetService<ILogger>();
    }
    
    protected static T GetService<T>()
    {
        var result = Locator.Current.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    } 
}
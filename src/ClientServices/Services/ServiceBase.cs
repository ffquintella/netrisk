using ClientServices.Interfaces;

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
        _logger = Log.Logger;
    }
}
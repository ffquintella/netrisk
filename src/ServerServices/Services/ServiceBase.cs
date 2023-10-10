using DAL;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class ServiceBase
{
    protected ILogger Logger { get; }
    protected DALService DalService { get; }

    protected ServiceBase(ILogger logger, DALService dalService)
    {
        Logger = logger;
        DalService = dalService;
    }
    
}
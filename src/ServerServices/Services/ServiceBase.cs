using DAL;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class ServiceBase
{
    protected ILogger Logger { get; }
    protected IDalService DalService { get; }

    protected ServiceBase(ILogger logger, IDalService dalService)
    {
        Logger = logger;
        DalService = dalService;
    }
    
}
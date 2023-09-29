using DAL;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class ServiceBase
{
    protected ILogger Logger { get; }
    protected DALManager DalManager { get; }

    protected ServiceBase(ILogger logger, DALManager dalManager)
    {
        Logger = logger;
        DalManager = dalManager;
    }
    
}
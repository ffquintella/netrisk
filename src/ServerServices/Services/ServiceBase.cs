using DAL;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class ServiceBase
{
    protected ILogger Logger { get; }
    protected DALManager DALManager { get; }
    
    public ServiceBase(ILogger logger, DALManager dalManager)
    {
        Logger = logger;
        DALManager = dalManager;
    }
    
}
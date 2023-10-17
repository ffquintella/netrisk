using Serilog;
using ServerServices.Services;

namespace BackgroundJobs.Jobs;

public class BaseJob
{
    public ILogger Log { get; }
    public DALService DalService { get; }
    public BaseJob(ILogger logger, DALService dalService)
    {
        Log = logger;
        DalService = dalService;
    }
}
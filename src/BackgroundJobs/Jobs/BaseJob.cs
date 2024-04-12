using Serilog;
using ServerServices.Services;

namespace BackgroundJobs.Jobs;

public class BaseJob
{
    public ILogger Log { get; }
    public DalService DalService { get; }
    public BaseJob(ILogger logger, DalService dalService)
    {
        Log = logger;
        DalService = dalService;
    }
}
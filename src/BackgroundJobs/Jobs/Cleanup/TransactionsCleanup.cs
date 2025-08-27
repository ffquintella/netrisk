using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Cleanup;

public class TransactionsCleanup: BaseJob, IJob
{
    private IFaceIDService FaceIDService { get; }
    
    public TransactionsCleanup(ILogger logger, IFaceIDService faceIDService, DalService dal): base(logger, dal)
    {
        FaceIDService = faceIDService;
    }

    public void Run()
    {
        Log.Information("Starting Transactions Cleanup Job");

        try
        {
            FaceIDService.CleanUpExpiredTransactionsAsync();
            Log.Information("Transactions Cleanup Job completed successfully");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error occurred during Transactions Cleanup Job");
        }
        
        Log.Information("Transactions Cleanup Job finished");
    }
}
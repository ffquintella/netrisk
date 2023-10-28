using ServerServices.Services;
using Serilog;

namespace BackgroundJobs.Jobs.Cleanup;

public class FileCleanup: BaseJob, IJob
{
    public FileCleanup(ILogger logger, DALService dal): base(logger, dal)
    {
        
    }

    public void Run()
    {
        using var context = DalService.GetContext();
        
        Console.WriteLine("Cleaning orphan files");
        
        // Deleting files that has no reference to them
        var files = context.NrFiles.Where(f => f.RiskId == null && f.MitigationId == null);

        var fcount = files.Count();
        
        context.NrFiles.RemoveRange(files);
        context.SaveChanges();
        
        Log.Information("Cleaned {Count} orphan files", fcount);
    }
}
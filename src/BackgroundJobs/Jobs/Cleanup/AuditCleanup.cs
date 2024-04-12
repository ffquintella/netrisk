using Serilog;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Cleanup;

public class AuditCleanup:  BaseJob, IJob
{
    
    public AuditCleanup(ILogger logger, DalService dal): base(logger, dal)
    {
        
    }

    public void Run()
    {
        using var context = DalService.GetContext();
        
        Console.WriteLine("Cleaning audits");
        
        // Deleting audits that are older then 90 days
        var audits = context.Audits.Where(x => x.DateTime < DateTime.Now.AddDays(-90));
        
        context.Audits.RemoveRange(audits);
        context.SaveChanges();
        
        Log.Information("Cleaned audits older then 90 days");
    }
}
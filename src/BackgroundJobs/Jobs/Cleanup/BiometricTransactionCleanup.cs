using Serilog;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Cleanup;

public class BiometricTransactionCleanup:  BaseJob, IJob
{
    public BiometricTransactionCleanup(ILogger logger, DalService dal): base(logger, dal)
    {
        
    }

    public void Run()
    {
        using var context = DalService.GetContext();
        
        Console.WriteLine("Cleaning biometric transactions");
        
        // Deleting audits that are older then 90 days
        var transactions = context.BiometricTransactions.Where(x => x.StartTime < DateTime.Now.AddDays(-90));
        
        context.BiometricTransactions.RemoveRange(transactions);
        context.SaveChanges();
        
        Log.Information("Cleaned biometric transactions older then 90 days");
    }
}
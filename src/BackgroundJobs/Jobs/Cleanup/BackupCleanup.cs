using Serilog;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Cleanup;

public class BackupCleanup:  BaseJob, IJob
{
    public BackupCleanup(ILogger logger, DalService dalService) : base(logger, dalService)
    {
    }

    public void Run()
    {
        Console.WriteLine("Cleaning old backups");

        if (!Directory.Exists(@"/backups"))
        {
            Log.Error("Backup directory not found");
            return;
        }
        
        string[] files = Directory.GetFiles(@"/backups");

        foreach (string file in files)
        {
            FileInfo fi = new FileInfo(file);
            if (fi.CreationTime < DateTime.Now.AddMonths(-1))
                fi.Delete();
        }
        
        Log.Information("Cleaned backups older then 30 days");
    }
}
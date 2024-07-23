using System.Runtime.InteropServices.JavaScript;
using Serilog;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Backup;

public class BackupWork:  BaseJob, IJob
{
     
    DatabaseService DatabaseService { get; }
     
    public BackupWork(ILogger logger, DalService dalService, DatabaseService dbservice) : base(logger, dalService)
    {
        DatabaseService = dbservice;
    }

    public void Run()
    {
        Console.WriteLine("Starting database backup");
        try
        {
            Log.Information("Starting database backup");
            //DatabaseService.Backup("/tmp/bck");
            DatabaseService.Backup();
        }
        catch (Exception e)
        {
            
            Log.Error("Error during backup: {Message}", e.Message);
            throw;
        }
        
    }
}
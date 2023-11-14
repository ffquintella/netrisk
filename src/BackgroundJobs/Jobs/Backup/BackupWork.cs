using System.Runtime.InteropServices.JavaScript;
using Serilog;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Backup;

public class BackupWork:  BaseJob, IJob
{
     
    DatabaseService DatabaseService { get; }
     
    public BackupWork(ILogger logger, DALService dalService, DatabaseService dbservice) : base(logger, dalService)
    {
        DatabaseService = dbservice;
    }

    public void Run()
    {
        try
        {
            Log.Information("Starting database backup");
            DatabaseService.Backup();
        }
        catch (Exception e)
        {
            
            Log.Error("Error during backup: {Message}", e.Message);
            throw;
        }
        
    }
}
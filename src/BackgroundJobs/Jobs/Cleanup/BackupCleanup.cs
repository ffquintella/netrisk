﻿using Serilog;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Cleanup;

public class BackupCleanup:  BaseJob, IJob
{
    public BackupCleanup(ILogger logger, DALService dalService) : base(logger, dalService)
    {
    }

    public void Run()
    {
        Console.WriteLine("Cleaning old backups");
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
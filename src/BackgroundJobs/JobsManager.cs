using BackgroundJobs.Jobs.Cleanup;
using Hangfire;
using Microsoft.Extensions.DependencyInjection;

namespace BackgroundJobs;

public static class JobsManager
{
    public static void ConfigureScheduledJobs()
    {
        
        //RecurringJob
        //    .AddOrUpdate("AuditCleanup",
        //        () => auditCleanup!.Run(), Cron.Minutely);
        BackgroundJob
            .Enqueue<AuditCleanup>(x=> x.Run());
    }
}
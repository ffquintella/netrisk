using DAL.Entities;
using ServerServices.Security;
using ServerServices.Services;
using Serilog;

namespace BackgroundJobs.Jobs.Cleanup;

public class FileCleanup: BaseJob, IJob
{
    /// <summary>
    /// How long a file may sit without a parent before it is considered abandoned. An upload exists
    /// parentless for the length of one dialog (see <c>FileAccessAuthorizer.EnsureCanReadAsync</c>), and
    /// a report PDF is created empty and filled in afterwards, so a file is never judged on the day it
    /// was written.
    /// </summary>
    public static readonly TimeSpan GracePeriod = TimeSpan.FromHours(24);

    public FileCleanup(ILogger logger, DalService dal): base(logger, dal)
    {
        
    }

    public void Run()
    {
        using var context = DalService.GetContext();
        
        Console.WriteLine("Cleaning orphan files");

        var cutoff = DateTime.Now - GracePeriod;

        // An orphan has no parent of ANY kind. The parent columns are listed once, in FileParents, so a
        // new attachment target is protected here the moment it is added there; the earlier
        // `RiskId == null && MitigationId == null` deleted incident, response-plan, risk-acceptance and
        // assessment-evidence attachments daily. A report PDF hangs off Report.FileId from the other
        // side, so it is excluded in the query. Only the id and the parent columns are loaded — never
        // the content blob.
        var candidates = context.NrFiles
            .Where(f => f.Timestamp < cutoff && !f.Reports.Any())
            .Select(f => new NrFile
            {
                Id = f.Id,
                RiskId = f.RiskId,
                MitigationId = f.MitigationId,
                RiskAcceptanceId = f.RiskAcceptanceId,
                IncidentId = f.IncidentId,
                IncidentResponsePlanId = f.IncidentResponsePlanId,
                IncidentResponsePlanExecutionId = f.IncidentResponsePlanExecutionId,
                IncidentResponsePlanTaskId = f.IncidentResponsePlanTaskId,
                IncidentResponsePlanTaskExecutionId = f.IncidentResponsePlanTaskExecutionId,
                AssessmentRunAnswerId = f.AssessmentRunAnswerId
            })
            .ToList();

        var orphans = candidates.Where(f => FileParents.Declared(f).Count == 0).ToList();

        foreach (var orphan in orphans)
            context.NrFiles.Remove(new NrFile { Id = orphan.Id, Name = string.Empty, UniqueName = string.Empty, Content = [] });

        context.SaveChanges();
        
        Log.Information("Cleaned {Count} orphan files", orphans.Count);
    }
}

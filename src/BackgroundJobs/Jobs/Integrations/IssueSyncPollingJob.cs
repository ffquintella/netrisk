using System;
using System.Threading.Tasks;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace BackgroundJobs.Jobs.Integrations;

/// <summary>
/// The polling fallback for issue-tracker sync (Track 4 milestone 4.2.3).
///
/// A webhook is the primary path and is near-instant; this exists for the many instances that cannot
/// reach NetRisk with one — a Jira Data Center behind a firewall, a GitLab in a private network. Each
/// connection carries its own interval and the service decides which are due, so a five-minute poller
/// and a one-hour poller coexist without two jobs.
/// </summary>
public class IssueSyncPollingJob(
    ILogger logger,
    DalService dalService,
    IIssueTrackerService issueTrackers)
    : BaseJob(logger, dalService), IJob
{
    public void Run()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        try
        {
            var result = await issueTrackers.PollDueConnectionsAsync(DateTime.UtcNow);

            if (result.Examined == 0) return;

            Log.Information(
                "Issue-sync poll examined {Examined} link(s): {Changed} changed, {Applied} applied, "
                + "{Conflicts} conflict(s), {Errors} error(s)",
                result.Examined, result.Changed, result.Applied, result.Conflicts, result.Errors);

            // The conflicts are named individually: last-writer-wins already happened, and a count alone
            // would not tell an operator which finding quietly changed direction.
            foreach (var message in result.Messages) Log.Debug("Issue-sync: {Message}", message);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "The issue-tracker polling pass failed");
        }
    }
}

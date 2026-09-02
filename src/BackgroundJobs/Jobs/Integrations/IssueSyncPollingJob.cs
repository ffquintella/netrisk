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
    IIssueTrackerService issueTrackers,
    IJiraIntegrationService jira)
    : BaseJob(logger, dalService), IJob
{
    public void Run()
    {
        RunAsync().GetAwaiter().GetResult();
    }

    private async Task RunAsync()
    {
        await PollIssueLinksAsync();

        // Separate pass, same job. The Jira Service Management mirror runs on the connection's own
        // poll interval and is driven from here rather than from a second recurring job, so an
        // operator has one schedule to reason about per connection instead of two that could disagree
        // about a queue feeding a linked finding.
        await MirrorServiceManagementAsync();
    }

    private async Task PollIssueLinksAsync()
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

    private async Task MirrorServiceManagementAsync()
    {
        try
        {
            var result = await jira.SyncDueServiceManagementAsync(DateTime.UtcNow);

            if (result.RequestsExamined == 0 && result.Errors == 0) return;

            Log.Information(
                "Jira Service Management mirror examined {Examined} request(s) across {Queues} "
                + "queue(s): {Created} new, {Updated} updated, {Cycles} SLA cycle(s), "
                + "{Breaches} new breach(es), {Errors} error(s)",
                result.RequestsExamined, result.QueuesExamined, result.RequestsCreated,
                result.RequestsUpdated, result.SlaCyclesRecorded, result.Breaches, result.Errors);

            foreach (var message in result.Messages) Log.Debug("JSM mirror: {Message}", message);
        }
        catch (Exception ex)
        {
            // Caught separately from the link poll: a broken Jira facet must not stop the
            // issue-tracker poll that the other three providers depend on.
            Log.Error(ex, "The Jira Service Management mirror pass failed");
        }
    }
}

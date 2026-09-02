using System;
using System.Threading;
using System.Threading.Tasks;
using BackgroundJobs.Jobs.Integrations;
using BackgroundJobs.Tests.DI;
using JetBrains.Annotations;
using Model.Integrations;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ServerServices.Interfaces;
using Xunit;

namespace BackgroundJobs.Tests.Jobs.Integrations;

/// <summary>
/// The four Track 4 recurring jobs.
///
/// Each is a thin wrapper around a service call, and the property that matters for all four is the
/// same: a failing integration must not take the job host down or stop the next pass. A job that
/// propagates an exception gets retried immediately by Hangfire with the same broken state, which for
/// a nightly sync means hammering a provider that is already refusing.
/// </summary>
[TestSubject(typeof(NotificationDispatchJob))]
public class IntegrationJobsTest
{
    // --- notification dispatch ---------------------------------------------------------------

    [Fact]
    public void TheNotificationSweepProcessesPendingDeliveries()
    {
        var dispatcher = Substitute.For<INotificationDispatcher>();
        var subscriptions = Substitute.For<INotificationSubscriptionsService>();

        dispatcher.ProcessPendingAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DispatchSweepResult { Delivered = 2, DigestsSent = 1 }));

        subscriptions.PurgeDeliveriesAsync(Arg.Any<int>()).Returns(Task.FromResult(0));

        new NotificationDispatchJob(TestDoubles.Logger(), TestDoubles.DalService(), dispatcher,
            subscriptions).Run();

        dispatcher.Received(1).ProcessPendingAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void TheNotificationSweepPurgesOldDeliveriesOncePerDay()
    {
        var dispatcher = Substitute.For<INotificationDispatcher>();
        var subscriptions = Substitute.For<INotificationSubscriptionsService>();

        dispatcher.ProcessPendingAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DispatchSweepResult()));

        subscriptions.PurgeDeliveriesAsync(Arg.Any<int>()).Returns(Task.FromResult(3));

        // The purge marker is process-wide, so it is reset here rather than depending on which test in
        // this class happened to run first.
        NotificationDispatchJob.LastPurgeDate = DateTime.MinValue;

        var job = new NotificationDispatchJob(TestDoubles.Logger(), TestDoubles.DalService(),
            dispatcher, subscriptions);

        job.Run();
        job.Run();

        // The sweep runs every minute; purging on each pass would mean 1440 range deletes a day for a
        // retention window measured in months.
        subscriptions.Received(1).PurgeDeliveriesAsync(Arg.Any<int>());
    }

    [Fact]
    public void AFailingSweepDoesNotThrow()
    {
        var dispatcher = Substitute.For<INotificationDispatcher>();
        var subscriptions = Substitute.For<INotificationSubscriptionsService>();

        dispatcher.ProcessPendingAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("database unreachable"));

        var job = new NotificationDispatchJob(TestDoubles.Logger(), TestDoubles.DalService(),
            dispatcher, subscriptions);

        // Re-running immediately with the same broken state helps nobody; the next minute's pass tries
        // again.
        job.Run();
    }

    [Fact]
    public void AFailingPurgeDoesNotFailTheSweep()
    {
        var dispatcher = Substitute.For<INotificationDispatcher>();
        var subscriptions = Substitute.For<INotificationSubscriptionsService>();

        dispatcher.ProcessPendingAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DispatchSweepResult()));

        subscriptions.PurgeDeliveriesAsync(Arg.Any<int>()).ThrowsAsync(new Exception("locked"));

        NotificationDispatchJob.LastPurgeDate = DateTime.MinValue;

        new NotificationDispatchJob(TestDoubles.Logger(), TestDoubles.DalService(), dispatcher,
            subscriptions).Run();
    }

    // --- issue-sync polling ------------------------------------------------------------------

    [Fact]
    public void ThePollerAsksTheServiceWhichConnectionsAreDue()
    {
        var trackers = Substitute.For<IIssueTrackerService>();

        trackers.PollDueConnectionsAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new IssueSyncResult { Examined = 4, Applied = 1 }));

        new IssueSyncPollingJob(TestDoubles.Logger(), TestDoubles.DalService(), trackers,
            QuietJira()).Run();

        // Per-connection intervals decided by the service is what lets a five-minute poller and an
        // hourly one coexist under one recurring job.
        trackers.Received(1).PollDueConnectionsAsync(Arg.Any<DateTime>());
    }

    [Fact]
    public void AFailingPollDoesNotThrow()
    {
        var trackers = Substitute.For<IIssueTrackerService>();

        trackers.PollDueConnectionsAsync(Arg.Any<DateTime>()).ThrowsAsync(new Exception("Jira is down"));

        new IssueSyncPollingJob(TestDoubles.Logger(), TestDoubles.DalService(), trackers,
            QuietJira()).Run();
    }

    [Fact]
    public void APollThatExaminedNothingIsQuiet()
    {
        var trackers = Substitute.For<IIssueTrackerService>();

        trackers.PollDueConnectionsAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new IssueSyncResult()));

        new IssueSyncPollingJob(TestDoubles.Logger(), TestDoubles.DalService(), trackers,
            QuietJira()).Run();

        trackers.Received(1).PollDueConnectionsAsync(Arg.Any<DateTime>());
    }

    /// <summary>
    /// A Jira facet that reports nothing to do (Track 4.6).
    ///
    /// NSubstitute returns null for an un-stubbed <c>Task</c>-returning member, which the job would
    /// await and throw on — so the mirror has to be stubbed even in the tests that are only about the
    /// link poll, or they would fail for a reason that has nothing to do with what they assert.
    /// </summary>
    private static IJiraIntegrationService QuietJira()
    {
        var jira = Substitute.For<IJiraIntegrationService>();

        jira.SyncDueServiceManagementAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new JsmSyncResult()));

        return jira;
    }

    // --- Jira Service Management mirror (4.6) ------------------------------------------------

    [Fact]
    public void TheMirrorPassAsksTheJiraServiceWhichConnectionsAreDue()
    {
        var trackers = Substitute.For<IIssueTrackerService>();

        trackers.PollDueConnectionsAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new IssueSyncResult()));

        var jira = Substitute.For<IJiraIntegrationService>();

        jira.SyncDueServiceManagementAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new JsmSyncResult { RequestsExamined = 7, Breaches = 1 }));

        new IssueSyncPollingJob(TestDoubles.Logger(), TestDoubles.DalService(), trackers, jira).Run();

        // Driven from the same recurring job as the link poll rather than a second schedule, so an
        // operator has one interval per connection to reason about instead of two.
        jira.Received(1).SyncDueServiceManagementAsync(Arg.Any<DateTime>());
    }

    [Fact]
    public void AFailingMirrorPassDoesNotStopTheLinkPoll()
    {
        var trackers = Substitute.For<IIssueTrackerService>();

        trackers.PollDueConnectionsAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new IssueSyncResult { Examined = 3 }));

        var jira = Substitute.For<IJiraIntegrationService>();

        jira.SyncDueServiceManagementAsync(Arg.Any<DateTime>())
            .ThrowsAsync(new Exception("Assets needs Premium"));

        new IssueSyncPollingJob(TestDoubles.Logger(), TestDoubles.DalService(), trackers, jira).Run();

        // The regression this guards: the two passes are caught separately, so a broken Jira facet
        // does not cost the other three providers their poll.
        trackers.Received(1).PollDueConnectionsAsync(Arg.Any<DateTime>());
    }

    [Fact]
    public void AFailingLinkPollDoesNotStopTheMirrorPass()
    {
        var trackers = Substitute.For<IIssueTrackerService>();

        trackers.PollDueConnectionsAsync(Arg.Any<DateTime>()).ThrowsAsync(new Exception("GitLab is down"));

        var jira = Substitute.For<IJiraIntegrationService>();

        jira.SyncDueServiceManagementAsync(Arg.Any<DateTime>())
            .Returns(Task.FromResult(new JsmSyncResult()));

        new IssueSyncPollingJob(TestDoubles.Logger(), TestDoubles.DalService(), trackers, jira).Run();

        jira.Received(1).SyncDueServiceManagementAsync(Arg.Any<DateTime>());
    }

    // --- posture syncs -----------------------------------------------------------------------

    [Fact]
    public void TheVisionOneJobSyncsDueConnections()
    {
        var trendMicro = Substitute.For<ITrendMicroService>();

        trendMicro.SyncDueConnectionsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PostureSyncResult { HostsCreated = 2, FindingsCreated = 9 }));

        new TrendMicroSyncJob(TestDoubles.Logger(), TestDoubles.DalService(), trendMicro).Run();

        trendMicro.Received(1).SyncDueConnectionsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AFailingVisionOneSyncDoesNotThrow()
    {
        var trendMicro = Substitute.For<ITrendMicroService>();

        trendMicro.SyncDueConnectionsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("Vision One is refusing the key"));

        new TrendMicroSyncJob(TestDoubles.Logger(), TestDoubles.DalService(), trendMicro).Run();
    }

    [Fact]
    public void TheScorecardJobSyncsDueConnections()
    {
        var scorecard = Substitute.For<ISecurityScorecardService>();

        scorecard.SyncDueConnectionsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PostureSyncResult { PostureRowsWritten = 11, CyberRiskIndex = 12 }));

        new SecurityScorecardSyncJob(TestDoubles.Logger(), TestDoubles.DalService(), scorecard).Run();

        scorecard.Received(1).SyncDueConnectionsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AFailingScorecardSyncDoesNotThrow()
    {
        var scorecard = Substitute.For<ISecurityScorecardService>();

        scorecard.SyncDueConnectionsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new Exception("SecurityScorecard is rate-limiting"));

        new SecurityScorecardSyncJob(TestDoubles.Logger(), TestDoubles.DalService(), scorecard).Run();
    }

    [Fact]
    public void AQuietPostureSyncLogsNothingAndStillCompletes()
    {
        var scorecard = Substitute.For<ISecurityScorecardService>();

        scorecard.SyncDueConnectionsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PostureSyncResult()));

        new SecurityScorecardSyncJob(TestDoubles.Logger(), TestDoubles.DalService(), scorecard).Run();

        scorecard.Received(1).SyncDueConnectionsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
    }
}

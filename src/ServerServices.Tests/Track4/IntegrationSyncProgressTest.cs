using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model.Integrations;
using Model.Messages;
using ServerServices.Integrations;
using ServerServices.Interfaces;
using ServerServices.Tests.Mock;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// The progress trail and the start/finish notifications, over the real sync services (Track 4).
///
/// Both exist for the same reason: a posture sync is a long-running server-side process, and the
/// product had no way to say so. There is no push channel to the desktop client — no SignalR anywhere —
/// so a run that took twenty minutes showed a busy spinner and nothing else, a run that failed after
/// fifteen showed a toast the operator had already walked away from, and a *scheduled* run showed
/// nothing at all. The sync-log row could only say "Running", which is the least useful thing to know
/// about a sync that has been running for twenty minutes.
/// </summary>
[TestSubject(typeof(IntegrationSyncRun))]
[TestSubject(typeof(IntegrationSyncNotifier))]
public class IntegrationSyncProgressTest : InMemoryServiceTestBase
{
    private readonly ITrendMicroService _visionOne;

    public IntegrationSyncProgressTest()
    {
        _visionOne = GetService<ITrendMicroService>();

        Seed(ctx =>
        {
            ctx.Entities.Add(new Entity
            {
                Id = 7, DefinitionName = "Acme", DefinitionVersion = "1", Status = "active"
            });

            // One enabled administrator, who is the audience for the notifications, plus a disabled
            // one and a non-administrator who must not be.
            ctx.Users.Add(new User
            {
                Value = 1, Name = "admin", Login = "admin", Email = "admin@acme.com", Enabled = true,
                Admin = true, Type = "local", Salt = "s", Password = Encoding.UTF8.GetBytes("p")
            });

            ctx.Users.Add(new User
            {
                Value = 2, Name = "ex-admin", Login = "ex", Email = "ex@acme.com", Enabled = false,
                Admin = true, Type = "local", Salt = "s", Password = Encoding.UTF8.GetBytes("p")
            });

            ctx.Users.Add(new User
            {
                Value = 3, Name = "analyst", Login = "analyst", Email = "a@acme.com", Enabled = true,
                Admin = false, Type = "local", Salt = "s", Password = Encoding.UTF8.GetBytes("p")
            });
        });
    }

    // --- fixtures ---------------------------------------------------------------------------

    private Task<TrendMicroConnectionView> ConnectionAsync(bool vulnerabilities = false,
        bool riskScores = false) =>
        _visionOne.CreateConnectionAsync(new TrendMicroConnection
        {
            Name = "Acme Vision One", Region = "eu", BaseUrl = "", EntityId = 7, Enabled = true,
            SyncIntervalHours = 24, SyncVulnerabilities = vulnerabilities, SyncRiskScores = riskScores
        }, "api-key");

    private void StubProvider() =>
        FakeOutboundHttpClient
            .RuleFor("/asrm/vulnerableDevices", """{"items":[]}""")
            .RuleFor("/asrm/attackSurfaceDevices",
                """{"items":[{"id":"agent-1","name":"db-prod-01","ip":["10.0.0.5"]}]}""");

    private async Task<IntegrationSyncLog> TheRunAsync()
    {
        await using var db = OpenContext();

        return await db.IntegrationSyncLogs.OrderBy(l => l.Id).LastAsync();
    }

    private async Task<Message[]> NotificationsAsync()
    {
        await using var db = OpenContext();

        return await db.Messages
            .Where(m => m.ChatId == (int)ChatTypes.Jobs)
            .OrderBy(m => m.Id)
            .ToArrayAsync();
    }

    // --- the trail --------------------------------------------------------------------------

    [Fact]
    public async Task ASyncWritesAProgressTrail()
    {
        var view = await ConnectionAsync();
        StubProvider();

        await _visionOne.SyncAsync(view.Id);

        var run = await TheRunAsync();

        Assert.False(string.IsNullOrWhiteSpace(run.ProgressLog),
            "The run recorded no progress trail at all.");
    }

    [Fact]
    public async Task TheTrailNamesEachStepTheSyncWentThrough()
    {
        var view = await ConnectionAsync(vulnerabilities: true, riskScores: true);
        StubProvider();

        await _visionOne.SyncAsync(view.Id);

        var trail = (await TheRunAsync()).ProgressLog!;

        // The steps are the answer to "what was it doing when it stopped", so each phase the sync can
        // spend minutes inside has to be one of them.
        Assert.Contains("started:", trail);
        Assert.Contains("inventory:", trail);
        Assert.Contains("risk-scores:", trail);
        Assert.Contains("cves:", trail);
        Assert.Contains("finished:", trail);
    }

    [Fact]
    public async Task TheTrailSaysWhichStepsWereSkippedAndWhy()
    {
        var view = await ConnectionAsync(vulnerabilities: false, riskScores: false);
        StubProvider();

        await _visionOne.SyncAsync(view.Id);

        var trail = (await TheRunAsync()).ProgressLog!;

        // A step that is absent from the trail is indistinguishable from a step that crashed before it
        // could log, which is the reading that sends somebody looking for a bug in a disabled feature.
        Assert.Contains("does not sync vulnerabilities", trail);
        Assert.Contains("does not sync risk scores", trail);
    }

    [Fact]
    public async Task TheTrailReportsWhatEachStepCounted()
    {
        var view = await ConnectionAsync();
        StubProvider();

        await _visionOne.SyncAsync(view.Id);

        var trail = (await TheRunAsync()).ProgressLog!;

        // One device is stubbed, and the trail has to say so — "requesting the inventory" followed by
        // nothing is the shape of both a slow provider and an empty tenant.
        Assert.Contains("(1 item(s))", trail);
    }

    [Fact]
    public async Task TheTrailIsTimestampedInUtcLikeTheRowThatCarriesIt()
    {
        var view = await ConnectionAsync();
        StubProvider();

        await _visionOne.SyncAsync(view.Id);

        var run = await TheRunAsync();

        // A trail timestamped in local time cannot be compared against started_at on its own row,
        // which is the first thing anybody does with it.
        Assert.Contains($"[{run.StartedAt:yyyy-MM-dd HH}", run.ProgressLog!);
        Assert.Contains("Z]", run.ProgressLog!);
    }

    [Fact]
    public async Task AFailedSyncKeepsTheTrailUpToWhereItFailed()
    {
        var view = await ConnectionAsync();

        // The inventory call is the first thing the sync does after resolving the credential.
        FakeOutboundHttpClient.DefaultResponse = new OutboundHttpResponse { StatusCode = 500 };

        await _visionOne.SyncAsync(view.Id);

        var run = await TheRunAsync();

        Assert.Equal(IntegrationSyncStatus.Failed, run.Status);

        // The whole point. A failed run's trail is the only record of how far it got, and it must
        // survive the failure that produced it — the error message alone does not say which step.
        Assert.Contains("started:", run.ProgressLog!);
        Assert.Contains("inventory:", run.ProgressLog!);
        Assert.Contains("finished:", run.ProgressLog!);
    }

    // --- the notifications ------------------------------------------------------------------

    [Fact]
    public async Task ASyncAnnouncesItsStartAndItsFinish()
    {
        var view = await ConnectionAsync();
        StubProvider();

        await _visionOne.SyncAsync(view.Id);

        var notifications = await NotificationsAsync();

        Assert.Equal(2, notifications.Length);
        Assert.Contains("started", notifications[0].Message1);
        Assert.Contains("finished", notifications[1].Message1);
    }

    [Fact]
    public async Task TheAnnouncementsNameTheProviderAndTheConnection()
    {
        var view = await ConnectionAsync();
        StubProvider();

        await _visionOne.SyncAsync(view.Id);

        var notifications = await NotificationsAsync();

        // An operator with four connections needs to know which one this is about, and the notification
        // centre shows the message text and nothing else.
        Assert.All(notifications, m =>
        {
            Assert.Contains("Trend Micro Vision One", m.Message1);
            Assert.Contains(view.Name, m.Message1);
        });
    }

    [Fact]
    public async Task TheFinishAnnouncementCarriesTheCounts()
    {
        var view = await ConnectionAsync();
        StubProvider();

        await _visionOne.SyncAsync(view.Id);

        var finish = (await NotificationsAsync()).Last();

        Assert.Contains("host(s) created", finish.Message1);
    }

    [Fact]
    public async Task TheAnnouncementsGoToTheNotificationCentreChat()
    {
        var view = await ConnectionAsync();
        StubProvider();

        await _visionOne.SyncAsync(view.Id);

        // ChatTypes.Jobs specifically: that is the chat the desktop client's notification badge polls
        // and NotificationsViewModel lists. Any other chat id is a message nothing displays.
        await using var db = OpenContext();

        Assert.All(await db.Messages.ToListAsync(),
            m => Assert.Equal((int)ChatTypes.Jobs, m.ChatId));
    }

    [Fact]
    public async Task OnlyEnabledAdministratorsAreNotified()
    {
        var view = await ConnectionAsync();
        StubProvider();

        await _visionOne.SyncAsync(view.Id);

        var recipients = (await NotificationsAsync()).Select(m => m.UserId).Distinct().ToArray();

        // User 1 is the enabled administrator; 2 is a disabled one and 3 is not an administrator.
        Assert.Equal([1], recipients);
    }

    [Fact]
    public async Task AStartedSyncIsAnnouncedAsInformation()
    {
        var view = await ConnectionAsync();
        StubProvider();

        await _visionOne.SyncAsync(view.Id);

        Assert.Equal((int)MessageType.Information, (await NotificationsAsync())[0].Type);
    }

    [Fact]
    public async Task AFailedSyncIsAnnouncedAsAnErrorWithTheReason()
    {
        var view = await ConnectionAsync();

        FakeOutboundHttpClient.DefaultResponse = new OutboundHttpResponse { StatusCode = 500 };

        await _visionOne.SyncAsync(view.Id);

        var finish = (await NotificationsAsync()).Last();

        // Error type, so the notification centre colours it as one — and the reason in the text,
        // because "the sync failed" with no reason costs the operator a trip to the server log.
        Assert.Equal((int)MessageType.Error, finish.Type);
        Assert.Contains("failed", finish.Message1);
        Assert.True(finish.Message1!.Length > "Trend Micro Vision One synchronization failed: Acme Vision One.".Length,
            $"The failure notification carried no reason: {finish.Message1}");
    }

    [Fact]
    public async Task ARunIsAnnouncedOnceEvenThoughEveryBranchReachesTheCompletionPath()
    {
        var view = await ConnectionAsync();

        FakeOutboundHttpClient.DefaultResponse = new OutboundHttpResponse { StatusCode = 500 };

        await _visionOne.SyncAsync(view.Id);

        // The success branch, each catch and the run's disposal all reach the completion path. A run
        // announced twice reads as two runs, which for a failure is two incidents.
        var finishes = (await NotificationsAsync())
            .Count(m => m.Message1!.Contains("failed") || m.Message1.Contains("finished"));

        Assert.Equal(1, finishes);
    }

    [Fact]
    public async Task ARefusedDuplicateAnnouncesNothing()
    {
        var view = await ConnectionAsync();
        StubProvider();

        await using (var db = OpenContext())
        {
            db.IntegrationSyncLogs.Add(new IntegrationSyncLog
            {
                Integration = IntegrationKind.TrendMicroVisionOne,
                ConnectionId = view.Id,
                ConnectionName = view.Name,
                StartedAt = DateTime.UtcNow.AddMinutes(-1),
                Status = IntegrationSyncStatus.Running
            });

            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<Model.Exceptions.IntegrationSyncBusyException>(
            () => _visionOne.SyncAsync(view.Id));

        // A refused duplicate is not a run, so it must not produce a "started" the operator reads as
        // one — the guard exists precisely because something fires these in bursts.
        Assert.Empty(await NotificationsAsync());
    }

    // --- the row lifecycle ------------------------------------------------------------------

    [Fact]
    public async Task TheRunIsSettledRatherThanLeftRunning()
    {
        var view = await ConnectionAsync();
        StubProvider();

        await _visionOne.SyncAsync(view.Id);

        var run = await TheRunAsync();

        Assert.NotEqual(IntegrationSyncStatus.Running, run.Status);
        Assert.NotNull(run.FinishedAt);
    }

    [Fact]
    public async Task TheRunRowSpansTheSyncRatherThanBeingWrittenAtTheEnd()
    {
        var view = await ConnectionAsync();
        StubProvider();

        await _visionOne.SyncAsync(view.Id);

        var run = await TheRunAsync();

        // started_at is claimed before the first provider call and finished_at after the last, so the
        // row describes a span. The three integrations that used to insert an already-finished row set
        // both to "now", which is why a sync in progress was indistinguishable from no sync at all.
        Assert.True(run.StartedAt <= run.FinishedAt,
            $"The run started at {run.StartedAt:u} and finished at {run.FinishedAt:u}.");
    }
}

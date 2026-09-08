using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Integrations;
using ServerServices.Integrations;
using ServerServices.Integrations.SecurityScorecard;
using ServerServices.Integrations.TrendMicro;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// One synchronization per connection at a time, and no Running row that stays Running (Track 4).
///
/// The screen that produced these tests showed eleven Vision One runs, all Running, all started within
/// three seconds of each other, for a connection that has exactly one. Two separate defects were needed
/// to get there and both are covered below:
///
///  * nothing refused a second sync while the first was in flight, so anything that fires the request
///    twice — the desktop client's reliable REST wrapper retrying a POST on a 5xx with no delay, the
///    daily job colliding with a manual run, two operators — got two real runs writing the same hosts
///    and findings;
///  * a run whose process stopped before it could record an outcome left a row that said Running
///    forever, which is both the misleading display and, once the guard above exists, a connection that
///    can never be synced again.
/// </summary>
[TestSubject(typeof(IntegrationSyncLedger))]
public class IntegrationSyncSingleFlightTest : InMemoryServiceTestBase
{
    private readonly ITrendMicroService _visionOne;
    private readonly ISecurityScorecardService _scorecard;

    private static readonly DateTime Now = new(2026, 9, 8, 17, 0, 0, DateTimeKind.Utc);

    public IntegrationSyncSingleFlightTest()
    {
        _visionOne = GetService<ITrendMicroService>();
        _scorecard = GetService<ISecurityScorecardService>();

        Seed(ctx =>
        {
            ctx.Entities.Add(new Entity
            {
                Id = 7, DefinitionName = "Acme", DefinitionVersion = "1", Status = "active"
            });

            ctx.Users.Add(new User
            {
                Value = 1, Name = "analyst", Login = "analyst", Email = "a@acme.com", Enabled = true,
                Type = "local", Salt = "s", Password = Encoding.UTF8.GetBytes("p")
            });
        });
    }

    // --- fixtures ---------------------------------------------------------------------------

    private Task<TrendMicroConnectionView> VisionOneConnectionAsync(string name = "Acme Vision One",
        int intervalHours = 24) =>
        _visionOne.CreateConnectionAsync(new TrendMicroConnection
        {
            Name = name, Region = "eu", BaseUrl = "", EntityId = 7, Enabled = true,
            SyncIntervalHours = intervalHours, SyncVulnerabilities = false, SyncRiskScores = false
        }, "api-key");

    private Task<SecurityScorecardConnectionView> ScorecardConnectionAsync(string name = "Acme SSC") =>
        _scorecard.CreateConnectionAsync(new SecurityScorecardConnection
        {
            Name = name, Domain = "acme.com", BaseUrl = "", EntityId = 7, Enabled = true,
            SyncIntervalHours = 24, SyncVulnerabilities = false, SyncIssues = false
        }, "api-token");

    private void StubVisionOne() =>
        FakeOutboundHttpClient
            .RuleFor("/asrm/vulnerableDevices", """{"items":[]}""")
            .RuleFor("/asrm/highRiskDevices", """{"items":[]}""")
            .RuleFor("/asrm/attackSurfaceDevices",
                """{"items":[{"id":"agent-1","name":"db-prod-01","ip":["10.0.0.5"]}]}""");

    /// <summary>Writes the row a run in flight would have left, <paramref name="ageMinutes"/> old.</summary>
    private async Task<int> RunningRowAsync(IntegrationKind kind, int connectionId, string connectionName,
        double ageMinutes)
    {
        await using var db = OpenContext();

        var row = new IntegrationSyncLog
        {
            Integration = kind,
            ConnectionId = connectionId,
            ConnectionName = connectionName,
            StartedAt = DateTime.UtcNow.AddMinutes(-ageMinutes),
            Status = IntegrationSyncStatus.Running
        };

        db.IntegrationSyncLogs.Add(row);
        await db.SaveChangesAsync();

        return row.Id;
    }

    // --- one run per connection -------------------------------------------------------------

    [Fact]
    public async Task ASecondVisionOneSyncIsRefusedWhileTheFirstIsStillRunning()
    {
        var view = await VisionOneConnectionAsync();
        StubVisionOne();

        await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, view.Id, view.Name, ageMinutes: 1);

        var thrown = await Assert.ThrowsAsync<IntegrationSyncBusyException>(
            () => _visionOne.SyncAsync(view.Id));

        Assert.Equal(TrendMicroService.ProviderName, thrown.Provider);
        Assert.Equal(view.Name, thrown.ConnectionName);
    }

    [Fact]
    public async Task ARefusedSyncNeverReachesTheProvider()
    {
        var view = await VisionOneConnectionAsync();
        StubVisionOne();

        await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, view.Id, view.Name, ageMinutes: 1);

        FakeOutboundHttpClient.Requests.Clear();

        await Assert.ThrowsAsync<IntegrationSyncBusyException>(() => _visionOne.SyncAsync(view.Id));

        // The point of the guard: the duplicate does no work. Eleven of these ran a full inventory
        // pass each, against the same provider, writing the same hosts.
        Assert.Empty(FakeOutboundHttpClient.Requests);
    }

    [Fact]
    public async Task ARefusedSyncAddsNoRowToTheLog()
    {
        var view = await VisionOneConnectionAsync();
        StubVisionOne();

        await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, view.Id, view.Name, ageMinutes: 1);

        await Assert.ThrowsAsync<IntegrationSyncBusyException>(() => _visionOne.SyncAsync(view.Id));

        await using var db = OpenContext();

        // A refused duplicate is not a run. Ten extra rows saying so is the screen this exists to
        // prevent, so the claim removes its own row when it loses.
        Assert.Single(db.IntegrationSyncLogs);
    }

    [Fact]
    public async Task TheRefusalSaysWhenTheRunItLostToStarted()
    {
        var view = await VisionOneConnectionAsync();
        StubVisionOne();

        await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, view.Id, view.Name, ageMinutes: 4);

        var thrown = await Assert.ThrowsAsync<IntegrationSyncBusyException>(
            () => _visionOne.SyncAsync(view.Id));

        // The operator's next question after "why did nothing happen" is "since when", because that is
        // what separates a sync in progress from one that is stuck.
        Assert.True(thrown.StartedAtUtc <= DateTime.UtcNow.AddMinutes(-3),
            $"The refusal reported {thrown.StartedAtUtc:u} rather than the running row's start time.");

        Assert.Contains("has been running since", thrown.Message);
        Assert.Contains("Only one run per connection", thrown.Message);
    }

    [Fact]
    public async Task ASyncForADifferentConnectionIsNotBlocked()
    {
        var busy = await VisionOneConnectionAsync("Busy");
        var idle = await VisionOneConnectionAsync("Idle");
        StubVisionOne();

        await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, busy.Id, busy.Name, ageMinutes: 1);

        // The claim is per connection, not per provider: a tenant with two Vision One connections
        // syncs both at once, and that was never the problem.
        var result = await _visionOne.SyncAsync(idle.Id);

        Assert.Equal(0, result.Errors);
    }

    [Fact]
    public async Task TwoClaimsOnOneConnectionResolveToTheOneThatStartedFirst()
    {
        var view = await VisionOneConnectionAsync();

        // The claim at the level the race happens: two callers that both find the ledger empty. Written
        // against the ledger rather than through two parallel SyncAsync calls because the in-memory
        // provider completes synchronously — parallel tasks there run one after another and never
        // interleave, so a test built that way would pass against a check-then-insert claim too.
        await using var first = OpenContext();

        var winner = await IntegrationSyncLedger.ClaimAsync(first, IntegrationKind.TrendMicroVisionOne,
            view.Id, view.Name, TrendMicroService.ProviderName, DateTime.UtcNow);

        await using var second = OpenContext();

        var thrown = await Assert.ThrowsAsync<IntegrationSyncBusyException>(() =>
            IntegrationSyncLedger.ClaimAsync(second, IntegrationKind.TrendMicroVisionOne, view.Id,
                view.Name, TrendMicroService.ProviderName, DateTime.UtcNow));

        Assert.Equal(winner.StartedAt, thrown.StartedAtUtc);

        await using var check = OpenContext();

        // The loser's own row is gone: it inserted one to resolve the race and takes it back when it
        // loses, so the log carries runs and not attempts.
        Assert.Equal(winner.Id, check.IntegrationSyncLogs.Single(l => l.ConnectionId == view.Id).Id);
    }

    [Fact]
    public async Task AConnectionCanBeSyncedAgainOnceItsRunFinished()
    {
        var view = await VisionOneConnectionAsync();
        StubVisionOne();

        await _visionOne.SyncAsync(view.Id);

        // The guard must hold the connection for the duration of a run and not one moment longer.
        var second = await _visionOne.SyncAsync(view.Id);

        Assert.Equal(0, second.Errors);

        await using var db = OpenContext();

        Assert.Equal(2, db.IntegrationSyncLogs.Count(l => l.ConnectionId == view.Id
                                                          && l.Status == IntegrationSyncStatus.Succeeded));
    }

    [Fact]
    public async Task TheScorecardSyncIsSingleFlightToo()
    {
        var view = await ScorecardConnectionAsync();

        await RunningRowAsync(IntegrationKind.SecurityScorecard, view.Id, view.Name, ageMinutes: 1);

        var thrown = await Assert.ThrowsAsync<IntegrationSyncBusyException>(
            () => _scorecard.SyncAsync(view.Id));

        Assert.Equal(SecurityScorecardService.ProviderName, thrown.Provider);
    }

    [Fact]
    public async Task AVisionOneRunDoesNotBlockAScorecardRun()
    {
        var visionOne = await VisionOneConnectionAsync();
        var scorecard = await ScorecardConnectionAsync();

        // Both providers share the one ledger table, and connection ids are per table — so a claim
        // that matched on the connection id alone would have them refusing each other.
        await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, visionOne.Id, visionOne.Name,
            ageMinutes: 1);

        await _scorecard.SyncAsync(scorecard.Id);

        await using var db = OpenContext();

        Assert.Contains(db.IntegrationSyncLogs,
            l => l.Integration == IntegrationKind.SecurityScorecard
                 && l.Status != IntegrationSyncStatus.Running);
    }

    // --- a Running row is never permanent ---------------------------------------------------

    [Fact]
    public async Task AStaleRunningRowDoesNotBlockANewSyncForever()
    {
        var view = await VisionOneConnectionAsync();
        StubVisionOne();

        await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, view.Id, view.Name,
            ageMinutes: IntegrationSyncLedger.StaleAfter.TotalMinutes + 30);

        // Without the reaper the guard is a trap: one killed process and the operator can never sync
        // that connection again, by hand or on schedule.
        var result = await _visionOne.SyncAsync(view.Id);

        Assert.Equal(0, result.Errors);
    }

    [Fact]
    public async Task AStaleRunningRowIsSettledAsFailedAndSaysWhy()
    {
        var view = await VisionOneConnectionAsync();
        StubVisionOne();

        var stale = await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, view.Id, view.Name,
            ageMinutes: IntegrationSyncLedger.StaleAfter.TotalMinutes + 30);

        await _visionOne.SyncAsync(view.Id);

        await using var db = OpenContext();

        var row = db.IntegrationSyncLogs.Single(l => l.Id == stale);

        Assert.Equal(IntegrationSyncStatus.Failed, row.Status);
        Assert.NotNull(row.FinishedAt);
        Assert.Contains("abandoned", row.ErrorMessage!);
    }

    [Fact]
    public async Task ARunningRowInsideTheHorizonIsLeftAlone()
    {
        var view = await VisionOneConnectionAsync();

        var fresh = await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, view.Id, view.Name,
            ageMinutes: IntegrationSyncLedger.StaleAfter.TotalMinutes - 30);

        await Assert.ThrowsAsync<IntegrationSyncBusyException>(() => _visionOne.SyncAsync(view.Id));

        await using var db = OpenContext();

        // A large tenant's sync takes minutes, not hours; reaping one that is still working would put
        // a second run on top of it, which is the behaviour being removed.
        Assert.Equal(IntegrationSyncStatus.Running, db.IntegrationSyncLogs.Single(l => l.Id == fresh).Status);
    }

    [Fact]
    public async Task ReapingSettlesOnlyTheProvidersOwnStaleRows()
    {
        var visionOne = await VisionOneConnectionAsync();
        var scorecard = await ScorecardConnectionAsync();

        var theirs = await RunningRowAsync(IntegrationKind.SecurityScorecard, scorecard.Id, scorecard.Name,
            ageMinutes: IntegrationSyncLedger.StaleAfter.TotalMinutes + 30);

        await using (var db = OpenContext())
        {
            var reaped = await IntegrationSyncLedger.ReapAbandonedAsync(db,
                IntegrationKind.TrendMicroVisionOne, DateTime.UtcNow, visionOne.Id);

            Assert.Equal(0, reaped);
        }

        await using var check = OpenContext();

        Assert.Equal(IntegrationSyncStatus.Running,
            check.IntegrationSyncLogs.Single(l => l.Id == theirs).Status);
    }

    // --- the scheduled pass -----------------------------------------------------------------

    [Fact]
    public async Task TheDailyPassSkipsABusyConnectionAndSyncsTheRest()
    {
        var busy = await VisionOneConnectionAsync("Busy");
        var idle = await VisionOneConnectionAsync("Idle");
        StubVisionOne();

        await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, busy.Id, busy.Name, ageMinutes: 1);

        // The refusal is an exception, so the loop over due connections had to learn to keep going:
        // one busy connection must not cost every connection after it its nightly sync.
        var combined = await _visionOne.SyncDueConnectionsAsync(Now);

        Assert.Equal(1, combined.HostsCreated);
        Assert.Contains(combined.Messages, m => m.Contains("Only one run per connection"));

        await using var db = OpenContext();

        Assert.Contains(db.IntegrationSyncLogs,
            l => l.ConnectionId == idle.Id && l.Status == IntegrationSyncStatus.Succeeded);
    }

    [Fact]
    public async Task TheDailyPassReapsAStaleRowBeforeDecidingWhatIsDue()
    {
        var view = await VisionOneConnectionAsync();
        StubVisionOne();

        var stale = await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, view.Id, view.Name,
            ageMinutes: IntegrationSyncLedger.StaleAfter.TotalMinutes + 30);

        var combined = await _visionOne.SyncDueConnectionsAsync(Now);

        Assert.Equal(1, combined.HostsCreated);

        await using var db = OpenContext();

        Assert.Equal(IntegrationSyncStatus.Failed, db.IntegrationSyncLogs.Single(l => l.Id == stale).Status);
    }

    // --- the completion write ---------------------------------------------------------------

    [Fact]
    public async Task AnUndecryptableScorecardTokenStillCompletesTheSyncLogRow()
    {
        var view = await ScorecardConnectionAsync();

        await using (var db = OpenContext())
        {
            var stored = db.SecurityScorecardConnections.Single(c => c.Id == view.Id);
            stored.EncryptedApiToken = "enc:v2:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("nope"));
            await db.SaveChangesAsync();
        }

        // Vision One's copy of this bug was fixed; SecurityScorecard's was not. Decryption sat one line
        // below the claim and above the try, so the row it had just written was never completed — and
        // a permanently Running row is exactly what the operator was looking at.
        await Assert.ThrowsAsync<SecretProtectionException>(() => _scorecard.SyncAsync(view.Id));

        await using var check = OpenContext();

        var log = check.IntegrationSyncLogs.Single(l => l.ConnectionId == view.Id);

        Assert.Equal(IntegrationSyncStatus.Failed, log.Status);
        Assert.NotNull(log.FinishedAt);
        Assert.Contains("could not be decrypted", log.ErrorMessage!);
    }

    [Fact]
    public async Task AnUndecryptableScorecardTokenLeavesTheConnectionSyncableAgain()
    {
        var view = await ScorecardConnectionAsync();

        await using (var db = OpenContext())
        {
            var stored = db.SecurityScorecardConnections.Single(c => c.Id == view.Id);
            stored.EncryptedApiToken = "enc:v2:" + Convert.ToBase64String(Encoding.UTF8.GetBytes("nope"));
            await db.SaveChangesAsync();
        }

        await Assert.ThrowsAsync<SecretProtectionException>(() => _scorecard.SyncAsync(view.Id));

        // The consequence that matters now that a Running row refuses the next sync: a failure must
        // not hand the connection a lock it never releases. Re-entering the token has to be enough.
        var thrown = await Assert.ThrowsAsync<SecretProtectionException>(() => _scorecard.SyncAsync(view.Id));

        Assert.Contains("could not be decrypted", thrown.Message);
    }
}

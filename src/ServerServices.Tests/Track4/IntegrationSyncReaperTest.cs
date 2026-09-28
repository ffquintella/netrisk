using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using ServerServices.Integrations;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// The hourly sweep that settles runs left Running by a process that stopped.
///
/// The screen that produced this class showed a Vision One run still Running three days on. Nothing
/// was wrong with the settling logic; what was missing was any reason for it to run, since its only
/// caller was the next sync of the connection whose process had died.
/// </summary>
[TestSubject(typeof(IntegrationSyncReaper))]
public class IntegrationSyncReaperTest : InMemoryServiceTestBase
{
    private readonly IIntegrationSyncReaper _reaper;

    private static readonly DateTime Now = new(2026, 9, 28, 12, 0, 0, DateTimeKind.Utc);

    public IntegrationSyncReaperTest()
    {
        _reaper = GetService<IIntegrationSyncReaper>();

        // The notifier fans out to enabled administrators, so without one the announcement assertion
        // would pass vacuously against an empty inbox.
        Seed(ctx => ctx.Users.Add(new User
        {
            Value = 1, Name = "root", Login = "root", Enabled = true, Admin = true, Type = "local",
            Salt = "s", Password = Encoding.UTF8.GetBytes("p"), Email = "root@x"
        }));
    }

    private async Task<int> RunningRowAsync(IntegrationKind kind, string name, TimeSpan age,
        int connectionId = 1)
    {
        await using var db = OpenContext();

        var row = new IntegrationSyncLog
        {
            Integration = kind,
            ConnectionId = connectionId,
            ConnectionName = name,
            StartedAt = Now - age,
            Status = IntegrationSyncStatus.Running
        };

        db.IntegrationSyncLogs.Add(row);
        await db.SaveChangesAsync();

        return row.Id;
    }

    private IntegrationSyncLog Read(int id)
    {
        using var db = OpenContext();
        return db.IntegrationSyncLogs.Single(l => l.Id == id);
    }

    // --- what it settles -------------------------------------------------------------------

    [Fact]
    public async Task ItSettlesARunWhoseProcessStoppedWithoutBeingToldWhichIntegration()
    {
        var stuck = await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, "Trend",
            IntegrationSyncLedger.StaleAfter + TimeSpan.FromDays(3));

        var reaped = await _reaper.ReapAsync(Now);

        Assert.Equal(stuck, Assert.Single(reaped).LogId);

        var row = Read(stuck);

        Assert.Equal(IntegrationSyncStatus.Failed, row.Status);
        Assert.Equal(Now, row.FinishedAt);
        Assert.Contains("abandoned", row.ErrorMessage);
    }

    [Fact]
    public async Task ItSweepsEveryIntegrationInOnePass()
    {
        var stale = IntegrationSyncLedger.StaleAfter + TimeSpan.FromHours(1);

        var visionOne = await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, "Trend", stale);
        var scorecard = await RunningRowAsync(IntegrationKind.SecurityScorecard, "SSC", stale);
        var jira = await RunningRowAsync(IntegrationKind.JiraAssets, "Assets", stale);

        var reaped = await _reaper.ReapAsync(Now);

        // The kind-scoped reaper had to be told which provider to look at, which is why three
        // different stuck rows could each wait for a sync that was never going to come.
        Assert.Equal([visionOne, scorecard, jira], reaped.Select(r => r.LogId).Order());

        Assert.All([visionOne, scorecard, jira],
            id => Assert.Equal(IntegrationSyncStatus.Failed, Read(id).Status));
    }

    [Fact]
    public async Task ItLeavesARunThatIsStillInsideTheHorizonAlone()
    {
        // A large tenant's sync takes minutes, not hours. Settling one that is still working would
        // let a second run start on top of it.
        var working = await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, "Trend",
            IntegrationSyncLedger.StaleAfter - TimeSpan.FromMinutes(1));

        Assert.Empty(await _reaper.ReapAsync(Now));

        Assert.Equal(IntegrationSyncStatus.Running, Read(working).Status);
    }

    [Fact]
    public async Task ItLeavesAlreadySettledRunsAlone()
    {
        var finished = await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, "Trend",
            IntegrationSyncLedger.StaleAfter + TimeSpan.FromHours(5));

        await using (var db = OpenContext())
        {
            var row = db.IntegrationSyncLogs.Single(l => l.Id == finished);
            row.Status = IntegrationSyncStatus.Succeeded;
            row.FinishedAt = Now.AddHours(-4);
            await db.SaveChangesAsync();
        }

        Assert.Empty(await _reaper.ReapAsync(Now));

        Assert.Equal(IntegrationSyncStatus.Succeeded, Read(finished).Status);
    }

    [Fact]
    public async Task ASecondSweepFindsNothingLeftToDo()
    {
        await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, "Trend",
            IntegrationSyncLedger.StaleAfter + TimeSpan.FromHours(2));

        Assert.Single(await _reaper.ReapAsync(Now));

        // Settling is what makes the hourly cadence safe: the same run cannot be reported twice.
        Assert.Empty(await _reaper.ReapAsync(Now.AddHours(1)));
    }

    [Fact]
    public async Task NothingStuckIsNotAnEvent()
    {
        Assert.Empty(await _reaper.ReapAsync(Now));
    }

    // --- what it leaves behind for the operator ---------------------------------------------

    [Fact]
    public async Task ItWritesWhyIntoTheTrailTheOperatorIsAlreadyReading()
    {
        var stuck = await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, "Trend",
            IntegrationSyncLedger.StaleAfter + TimeSpan.FromDays(3));

        await using (var db = OpenContext())
        {
            var row = db.IntegrationSyncLogs.Single(l => l.Id == stuck);
            row.ProgressLog = "[2026-09-25 14:55:31Z] cves: Vulnerability records received.";
            await db.SaveChangesAsync();
        }

        await _reaper.ReapAsync(Now);

        var trail = Read(stuck).ProgressLog;

        // Appended, not replaced: the last line the dead run managed to write is the only evidence
        // of where it stopped, and it is the first thing anybody diagnosing this looks at.
        Assert.Contains("cves: Vulnerability records received.", trail);
        Assert.Contains("reaped:", trail);
    }

    [Fact]
    public async Task ItReportsHowLongTheRunHadBeenStuck()
    {
        await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, "Trend", TimeSpan.FromDays(3));

        var reaped = Assert.Single(await _reaper.ReapAsync(Now));

        Assert.Equal(TimeSpan.FromDays(3), reaped.Age);
        Assert.Equal("Trend", reaped.ConnectionName);
        Assert.Equal(IntegrationKind.TrendMicroVisionOne, reaped.Kind);

        // Days, not "72 hours": the age is read by a person deciding how long they have been blind.
        Assert.Contains("3 day(s)", Read(reaped.LogId).ErrorMessage);
    }

    [Fact]
    public async Task ItAnnouncesEachSettledRun()
    {
        await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, "Trend",
            IntegrationSyncLedger.StaleAfter + TimeSpan.FromHours(3));

        await _reaper.ReapAsync(Now);

        await using var db = OpenContext();

        // A stuck run is silent by construction — the process that would have announced its failure
        // is the one that died — so the sweep is the only thing that can raise it.
        Assert.Contains(db.Messages.ToList(),
            m => m.Message1 != null && m.Message1.Contains("Trend"));
    }

    // --- the guard it unblocks ---------------------------------------------------------------

    [Fact]
    public async Task AReapedConnectionCanBeClaimedAgain()
    {
        await RunningRowAsync(IntegrationKind.TrendMicroVisionOne, "Trend",
            IntegrationSyncLedger.StaleAfter + TimeSpan.FromDays(3), connectionId: 7);

        await _reaper.ReapAsync(Now);

        await using var db = OpenContext();

        // The stuck row does not merely look wrong, it refuses every sync of that connection. This
        // is the half of the bug an operator actually feels.
        var claim = await IntegrationSyncLedger.ClaimAsync(db, IntegrationKind.TrendMicroVisionOne,
            7, "Trend", "Trend Micro Vision One", Now);

        Assert.Equal(IntegrationSyncStatus.Running, claim.Status);
    }
}

using System;
using System.Threading.Tasks;
using BackgroundJobs.Jobs.Governance;
using BackgroundJobs.Jobs.Integrations;
using BackgroundJobs.Tests.DI;
using JetBrains.Annotations;
using Model.RiskFlags;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ServerServices.Interfaces;
using Xunit;

namespace BackgroundJobs.Tests.Jobs.Governance;

/// <summary>
/// Stage 9.5 (S46 §4.6, §8) — the nightly reconciliation of the derived flags. A thin wrapper: it calls the
/// service once and never propagates a failure. Its place in the night is pinned: after the KEV (05:00) and
/// EPSS (05:20) syncs, so a CVE listed or delisted overnight moves flag 3 the same night, and before the
/// risk-acceptance expiry passes (06:00, 06:15), so a Gate A onset is escalated before they read the register.
/// </summary>
[TestSubject(typeof(RiskFlagsDerivationJob))]
public class RiskFlagsDerivationJobTest
{
    [Fact]
    public void TestTheJobReconcilesEveryOpenRiskOnce()
    {
        var flags = Substitute.For<IRiskFlagsService>();
        flags.RefreshAllAsync().Returns(new RiskFlagsRefreshSummary { RisksEvaluated = 3, FlagsReverted = 1 });

        new RiskFlagsDerivationJob(TestDoubles.Logger(), TestDoubles.DalService(), flags).Run();

        flags.Received(1).RefreshAllAsync();
        flags.DidNotReceive().RefreshAsync(Arg.Any<int>());
    }

    [Fact]
    public void TestAFailingPassDoesNotThrow()
    {
        var flags = Substitute.For<IRiskFlagsService>();
        flags.RefreshAllAsync().ThrowsAsync(new InvalidOperationException("database down"));

        Assert.Null(Record.Exception(() =>
            new RiskFlagsDerivationJob(TestDoubles.Logger(), TestDoubles.DalService(), flags).Run()));
    }

    /// <summary>"m h * * *" — minute and hour of a daily cron.</summary>
    private static TimeSpan At(string cron)
    {
        var parts = cron.Split(' ');
        Assert.Equal(new[] { "*", "*", "*" }, parts[2..]);
        return new TimeSpan(int.Parse(parts[1]), int.Parse(parts[0]), 0);
    }

    [Fact]
    public void TestTheJobRunsAfterTheSignalSyncsAndBeforeTheExpiryPasses()
    {
        var derivation = At(RiskFlagsDerivationJob.Cron);

        Assert.True(derivation > At(KevCatalogueSyncJob.Cron));
        Assert.True(derivation > At(EpssSyncJob.Cron));
        // The finding-level expiry pass runs at 06:00 and the risk-level one at 06:15 (JobsManager).
        Assert.True(derivation < TimeSpan.FromHours(6));
        Assert.Equal("RiskFlagsDerivation", RiskFlagsDerivationJob.JobId);
    }
}

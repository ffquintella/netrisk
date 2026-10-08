using System;
using System.Linq;
using BackgroundJobs.Jobs.Governance;
using BackgroundJobs.Tests.DI;
using JetBrains.Annotations;
using Model.Monitoring;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ServerServices.Interfaces;
using Xunit;

namespace BackgroundJobs.Tests.Jobs.Governance;

/// <summary>
/// Stage 9.8 (S49 §4.10, §8 J1–J3) — the daily KRI evaluation. A thin wrapper: it calls the service once and never
/// propagates a failure. Its place in the morning is pinned, as the S27 track asks ("the order relative to the existing
/// jobs is preserved and tested"): after the reconciliation of the derived flags and the two expiry passes, and before
/// the review cadence that tells owners and managers about the risks a breach flagged for review.
/// </summary>
[TestSubject(typeof(KriEvaluationJob))]
public class KriEvaluationJobTest
{
    /// <summary>J1 — one pass over every KRI per run.</summary>
    [Fact]
    public void TestJ1_TheJobEvaluatesEveryKriOnce()
    {
        var monitoring = Substitute.For<IMonitoringService>();
        monitoring.EvaluateAllAsync().Returns(new KriEvaluationSummary { KrisEvaluated = 4, EpisodesOpened = 1, TriggersRaised = 3 });

        new KriEvaluationJob(TestDoubles.Logger(), TestDoubles.DalService(), monitoring).Run();

        monitoring.Received(1).EvaluateAllAsync();
    }

    /// <summary>J2 — a failing pass is logged, never thrown: Hangfire would retry it at once against the same failure.</summary>
    [Fact]
    public void TestJ2_AFailingPassDoesNotThrow()
    {
        var monitoring = Substitute.For<IMonitoringService>();
        monitoring.EvaluateAllAsync().ThrowsAsync(new InvalidOperationException("database down"));

        Assert.Null(Record.Exception(() =>
            new KriEvaluationJob(TestDoubles.Logger(), TestDoubles.DalService(), monitoring).Run()));
    }

    /// <summary>"m h * * *" — minute and hour of a daily cron.</summary>
    private static TimeSpan At(string cron)
    {
        var parts = cron.Split(' ');
        Assert.Equal(new[] { "*", "*", "*" }, parts[2..]);
        return new TimeSpan(int.Parse(parts[1]), int.Parse(parts[0]), 0);
    }

    /// <summary>
    /// J3 — after the flags reconciliation (05:40) and the risk-level expiry pass (06:15), before the review cadence
    /// (07:30), and in a minute no other governance job uses. Moving it after the cadence would leave an overnight
    /// breach out of that morning's "flagged for review" message.
    /// </summary>
    [Fact]
    public void TestJ3_TheJobRunsBetweenTheFlagsAndTheReviewCadence()
    {
        var evaluation = At(KriEvaluationJob.Cron);

        Assert.True(evaluation > At(RiskFlagsDerivationJob.Cron));
        // The finding-level expiry pass runs at 06:00 and the risk-level one at 06:15 (JobsManager).
        Assert.True(evaluation > new TimeSpan(6, 15, 0));
        Assert.True(evaluation < At(RiskReviewCadenceJob.Cron));

        // 02:20 residual, 02:30 retention, 05:40 flags, 06:15 expiry, 07:30 cadence, 08:00 campaigns.
        var taken = new[] { "20 2 * * *", "30 2 * * *", RiskFlagsDerivationJob.Cron, "15 6 * * *", RiskReviewCadenceJob.Cron, "0 8 * * *" }
            .Select(At);
        Assert.DoesNotContain(evaluation, taken);
        Assert.Equal("KriEvaluation", KriEvaluationJob.JobId);
    }

    /// <summary>The cadence keeps its 07:30 — turning it into a constant for J3 did not move it.</summary>
    [Fact]
    public void TestTheReviewCadenceStillRunsAtHalfPastSeven() =>
        Assert.Equal(new TimeSpan(7, 30, 0), At(RiskReviewCadenceJob.Cron));
}

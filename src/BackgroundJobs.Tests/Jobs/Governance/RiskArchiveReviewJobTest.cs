using System;
using System.Linq;
using BackgroundJobs.Jobs.Governance;
using BackgroundJobs.Tests.DI;
using JetBrains.Annotations;
using Model.DecisionCycle;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ServerServices.Interfaces;
using Xunit;

namespace BackgroundJobs.Tests.Jobs.Governance;

/// <summary>
/// Stage 9.9 (S50 §4.2, §8 J1–J3) — the quarterly-review notice of the archive. A thin wrapper: it calls the sweep once
/// and never propagates a failure; its place in the morning is pinned after the KRI evaluation (which may reopen an
/// archive whose KRI breached overnight) and before the review cadence.
/// </summary>
[TestSubject(typeof(RiskArchiveReviewJob))]
public class RiskArchiveReviewJobTest
{
    /// <summary>J1 — one sweep per run.</summary>
    [Fact]
    public void TestJ1_TheJobSweepsOnce()
    {
        var archives = Substitute.For<IRiskArchiveService>();
        archives.NotifyDueReviewsAsync().Returns(new RiskArchiveReviewSweepSummary { Live = 3, Due = 1, Notified = 1 });

        new RiskArchiveReviewJob(TestDoubles.Logger(), TestDoubles.DalService(), archives).Run();

        archives.Received(1).NotifyDueReviewsAsync();
    }

    /// <summary>J2 — a failing sweep is logged, never thrown: Hangfire would retry it at once against the same failure.</summary>
    [Fact]
    public void TestJ2_AFailingSweepDoesNotThrow()
    {
        var archives = Substitute.For<IRiskArchiveService>();
        archives.NotifyDueReviewsAsync().ThrowsAsync(new InvalidOperationException("database down"));

        Assert.Null(Record.Exception(() =>
            new RiskArchiveReviewJob(TestDoubles.Logger(), TestDoubles.DalService(), archives).Run()));
    }

    private static TimeSpan At(string cron)
    {
        var parts = cron.Split(' ');
        Assert.Equal(new[] { "*", "*", "*" }, parts[2..]);
        return new TimeSpan(int.Parse(parts[1]), int.Parse(parts[0]), 0);
    }

    /// <summary>
    /// J3 — after the KRI evaluation (06:45), before the review cadence (07:30), in a minute no other governance job uses.
    /// Before the KRI evaluation it would announce the review of an archive a breach reopens minutes later.
    /// </summary>
    [Fact]
    public void TestJ3_TheSweepRunsBetweenTheKriEvaluationAndTheCadence()
    {
        var sweep = At(RiskArchiveReviewJob.Cron);

        Assert.True(sweep > At(KriEvaluationJob.Cron));
        Assert.True(sweep < At(RiskReviewCadenceJob.Cron));

        var taken = new[]
            {
                "20 2 * * *", "30 2 * * *", RiskFlagsDerivationJob.Cron, "15 6 * * *", KriEvaluationJob.Cron,
                RiskReviewCadenceJob.Cron, "0 8 * * *"
            }
            .Select(At);
        Assert.DoesNotContain(sweep, taken);
        Assert.Equal("RiskArchiveReview", RiskArchiveReviewJob.JobId);
    }
}

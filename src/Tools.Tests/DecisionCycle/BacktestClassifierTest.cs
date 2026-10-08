using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Model.DecisionCycle;
using Tools.DecisionCycle;
using Xunit;

namespace Tools.Tests.DecisionCycle;

/// <summary>
/// Stage 9.9 (S50 §4.4, D6, §8 BC1–BC12) — incident backtesting. The case the track names for this stage is BC3: a
/// scenario registered after the incident is never counted as having foreseen it — the date is the test, and a link an
/// assessor drew does not override it.
/// </summary>
[TestSubject(typeof(BacktestClassifier))]
public class BacktestClassifierTest
{
    private static readonly DateTime Occurred = new(2026, 6, 15, 10, 0, 0, DateTimeKind.Utc);

    private static BacktestLink Link(int id, DateTime registered, params string[] dismissals) =>
        new(id, registered, dismissals);

    /// <summary>BC1 — not assessed is neither foreseen nor unforeseen, whatever the links would say.</summary>
    [Fact]
    public void TestBC1_NotAssessedIsItsOwnOutcome()
    {
        Assert.Equal(BacktestOutcome.NotAssessed, BacktestClassifier.Classify(false, Occurred, []));
        Assert.Equal(BacktestOutcome.NotAssessed,
            BacktestClassifier.Classify(false, Occurred, [Link(1, Occurred.AddYears(-1))]));
    }

    /// <summary>BC2 — assessed with no matching risk: not foreseen.</summary>
    [Fact]
    public void TestBC2_AssessedWithNoMatchIsNotForeseen() =>
        Assert.Equal(BacktestOutcome.NotForeseen, BacktestClassifier.Classify(true, Occurred, []));

    /// <summary>
    /// BC3 (T199) — a risk registered after the incident, or at the very same instant, does not count as foreseeing it:
    /// the outcome is "registered after the occurrence", never foreseen. Comparing with ≤ instead of &lt;, or the dates
    /// the other way round, fails here.
    /// </summary>
    [Theory]
    [InlineData(0)]            // the same instant: not before
    [InlineData(1)]            // a second later
    [InlineData(86_400)]       // the day after
    [InlineData(31_536_000)]   // a year later
    public void TestBC3_ARiskRegisteredAfterTheIncidentNeverCountsAsForeseen(int secondsAfter)
    {
        var outcome = BacktestClassifier.Classify(true, Occurred, [Link(1, Occurred.AddSeconds(secondsAfter))]);

        Assert.Equal(BacktestOutcome.RegisteredAfterOccurrence, outcome);
        Assert.False(BacktestClassifier.Foresees(Occurred.AddSeconds(secondsAfter), Occurred));
    }

    /// <summary>BC4 — a second earlier is enough: registered strictly before, and not cut, is foreseen and treated.</summary>
    [Fact]
    public void TestBC4_ARiskRegisteredBeforeAndNotCutIsForeseenAndTreated()
    {
        Assert.True(BacktestClassifier.Foresees(Occurred.AddSeconds(-1), Occurred));
        Assert.Equal(BacktestOutcome.ForeseenTreated,
            BacktestClassifier.Classify(true, Occurred, [Link(1, Occurred.AddSeconds(-1))]));
    }

    /// <summary>
    /// BC5 — a post-incident link beside a pre-incident one does not change the answer, in either direction: the outcome
    /// is decided by the risks registered before, and only by them.
    /// </summary>
    [Fact]
    public void TestBC5_OnlyTheRisksRegisteredBeforeDecide()
    {
        var before = Link(1, Occurred.AddDays(-30), BacktestClassifier.Archived);
        var after = Link(2, Occurred.AddDays(1)); // not cut — but written after the fact

        Assert.Equal(BacktestOutcome.ForeseenDismissed, BacktestClassifier.Classify(true, Occurred, [before, after]));
        Assert.Equal(BacktestOutcome.ForeseenTreated,
            BacktestClassifier.Classify(true, Occurred, [Link(1, Occurred.AddDays(-30)), Link(2, Occurred.AddDays(1), "closed")]));
    }

    /// <summary>BC6 — every foreseeing risk cut when it happened: the false negative of the cut.</summary>
    [Fact]
    public void TestBC6_EveryForeseeingRiskCutIsAFalseNegative() =>
        Assert.Equal(BacktestOutcome.ForeseenDismissed, BacktestClassifier.Classify(true, Occurred,
            [Link(1, Occurred.AddDays(-90), BacktestClassifier.Accepted), Link(2, Occurred.AddDays(-10), BacktestClassifier.Closed)]));

    /// <summary>BC7 — the occurrence is the earliest of start, report and creation.</summary>
    [Fact]
    public void TestBC7_TheOccurrenceIsTheEarliestKnownDate()
    {
        var report = Occurred.AddDays(2);
        var created = Occurred.AddDays(3);

        Assert.Equal(Occurred, BacktestClassifier.OccurrenceOf(Occurred, report, created));
        Assert.Equal(report, BacktestClassifier.OccurrenceOf(null, report, created));
        Assert.Equal(report, BacktestClassifier.OccurrenceOf(created.AddDays(10), report, created)); // a start after both is not believed
        Assert.Equal(Occurred, BacktestClassifier.OccurrenceOf(null, created, Occurred)); // report typed later than creation
    }

    /// <summary>BC8 — archived: within the window from archiving to reopening; open-ended while live.</summary>
    [Fact]
    public void TestBC8_ArchivedWithinItsWindow()
    {
        var history = new RiskCutHistory([new ArchiveWindow(Occurred.AddDays(-10), Occurred.AddDays(5))], null, [], []);

        Assert.Equal(new[] { BacktestClassifier.Archived }, BacktestClassifier.DismissalReasons(history, Occurred));
        Assert.Empty(BacktestClassifier.DismissalReasons(history, Occurred.AddDays(-11)));
        Assert.Empty(BacktestClassifier.DismissalReasons(history, Occurred.AddDays(5))); // reopened at that instant
        Assert.Equal(new[] { BacktestClassifier.Archived }, BacktestClassifier.DismissalReasons(
            new RiskCutHistory([new ArchiveWindow(Occurred.AddDays(-10), null)], null, [], []), Occurred.AddYears(1)));
    }

    /// <summary>BC9 — accepted: valid from its start to before its expiry, cut short by a revocation.</summary>
    [Fact]
    public void TestBC9_AcceptedWhileTheAcceptanceWasValid()
    {
        var valid = new AcceptanceWindow(Occurred.AddDays(-30), Occurred.AddDays(30), null);
        var revoked = new AcceptanceWindow(Occurred.AddDays(-30), Occurred.AddDays(30), Occurred.AddDays(-1));
        var expired = new AcceptanceWindow(Occurred.AddDays(-60), Occurred, null);

        Assert.Contains(BacktestClassifier.Accepted, BacktestClassifier.DismissalReasons(new([], null, [valid], []), Occurred));
        Assert.Empty(BacktestClassifier.DismissalReasons(new([], null, [revoked], []), Occurred));
        Assert.Empty(BacktestClassifier.DismissalReasons(new([], null, [expired], []), Occurred));
    }

    /// <summary>BC10 — closed by the current closure only from its date; the latest decision at the time, not a later one.</summary>
    [Fact]
    public void TestBC10_ClosureAndTheDecisionInForceAtTheTime()
    {
        Assert.Contains(BacktestClassifier.Closed,
            BacktestClassifier.DismissalReasons(new([], Occurred.AddDays(-1), [], []), Occurred));
        Assert.Empty(BacktestClassifier.DismissalReasons(new([], Occurred.AddDays(1), [], []), Occurred));

        var decisions = new List<DatedDecision>
        {
            new(Occurred.AddDays(-20), RiskDecisionKind.MonitorAccept),
            new(Occurred.AddDays(-5), RiskDecisionKind.TreatInCycle),
            new(Occurred.AddDays(3), RiskDecisionKind.Archive)
        };
        Assert.Empty(BacktestClassifier.DismissalReasons(new([], null, [], decisions), Occurred));
        Assert.Equal(new[] { BacktestClassifier.DecidedMonitorAccept },
            BacktestClassifier.DismissalReasons(new([], null, [], decisions), Occurred.AddDays(-10)));
        Assert.Equal(new[] { BacktestClassifier.DecidedArchive },
            BacktestClassifier.DismissalReasons(new([], null, [], decisions), Occurred.AddDays(4)));
    }

    /// <summary>BC11 — the period's counts and rates; no assessment and no foreseen incident read null, never 0 %.</summary>
    [Fact]
    public void TestBC11_TheRatesOfAPeriod()
    {
        var report = new BacktestReportDto();
        BacktestClassifier.Summarize(report, new (IncidentKind, BacktestOutcome)[]
        {
            (IncidentKind.Incident, BacktestOutcome.NotForeseen),
            (IncidentKind.Incident, BacktestOutcome.RegisteredAfterOccurrence),
            (IncidentKind.NearMiss, BacktestOutcome.ForeseenTreated),
            (IncidentKind.Incident, BacktestOutcome.ForeseenDismissed),
            (IncidentKind.NearMiss, BacktestOutcome.NotAssessed)
        });

        Assert.Equal((3, 2, 4, 1), (report.Incidents, report.NearMisses, report.Assessed, report.NotAssessed));
        Assert.Equal(0.5, report.UnforeseenRate);      // (1 + 1) of 4 assessed
        Assert.Equal(0.5, report.FalseNegativeRate);   // 1 dismissed of 2 foreseen
        Assert.Equal(0.8, report.AssessedShare);

        var empty = new BacktestReportDto();
        BacktestClassifier.Summarize(empty, [(IncidentKind.Incident, BacktestOutcome.NotAssessed)]);
        Assert.Null(empty.UnforeseenRate);
        Assert.Null(empty.FalseNegativeRate);
        Assert.Equal(0d, empty.AssessedShare);
    }

    /// <summary>BC12 — the unforeseen rate counts a post-incident registration with the unforeseen, never with the foreseen.</summary>
    [Fact]
    public void TestBC12_APostIncidentRegistrationRaisesTheUnforeseenRate()
    {
        var links = new[] { Link(7, Occurred.AddHours(2)) };
        var outcome = BacktestClassifier.Classify(true, Occurred, links);

        var report = new BacktestReportDto();
        BacktestClassifier.Summarize(report, [(IncidentKind.Incident, outcome)]);

        Assert.Equal(1d, report.UnforeseenRate);
        Assert.Null(report.FalseNegativeRate);
        Assert.Equal(0, report.ForeseenTreated + report.ForeseenDismissed);
    }
}

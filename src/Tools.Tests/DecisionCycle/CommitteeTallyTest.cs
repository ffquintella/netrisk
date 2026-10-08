using System;
using DAL.Enums;
using JetBrains.Annotations;
using Model.DecisionCycle;
using Tools.DecisionCycle;
using Xunit;

namespace Tools.Tests.DecisionCycle;

/// <summary>
/// Stage 9.9 (S50 §4.6, D9, §8 CT1–CT6) — the collegiate rule of a risk committee: <em>k</em> approvals of the eligible
/// members; approved at the <em>k</em>-th, rejected the moment it can no longer get there; the archive's pure rules beside it
/// (AR1–AR3).
/// </summary>
[TestSubject(typeof(CommitteeTally))]
public class CommitteeTallyTest
{
    /// <summary>CT1 — the k-th approval decides, whatever is still to vote.</summary>
    [Theory]
    [InlineData(2, 5, 2, 0, 0)]
    [InlineData(3, 3, 3, 0, 0)]
    [InlineData(2, 5, 2, 2, 1)]
    public void TestCT1_TheKthApprovalApproves(int required, int eligible, int approvals, int rejections, int abstentions) =>
        Assert.Equal(CommitteeTallyOutcome.Approved,
            CommitteeTally.Evaluate(required, eligible, approvals, rejections, abstentions));

    /// <summary>CT2 — rejected as soon as the outstanding voters cannot make up the difference; an abstention counts against.</summary>
    [Theory]
    [InlineData(2, 3, 0, 2, 0)]   // two rejections of three: one left, two needed
    [InlineData(3, 4, 1, 1, 1)]   // one left, two needed
    [InlineData(2, 2, 1, 0, 1)]   // an abstention is not an approval
    public void TestCT2_UnreachableIsRejected(int required, int eligible, int approvals, int rejections, int abstentions) =>
        Assert.Equal(CommitteeTallyOutcome.Rejected,
            CommitteeTally.Evaluate(required, eligible, approvals, rejections, abstentions));

    /// <summary>CT3 — still reachable is pending, even after a rejection.</summary>
    [Theory]
    [InlineData(2, 3, 0, 0, 0)]
    [InlineData(2, 3, 1, 0, 0)]
    [InlineData(2, 4, 0, 2, 0)]
    public void TestCT3_ReachableIsPending(int required, int eligible, int approvals, int rejections, int abstentions) =>
        Assert.Equal(CommitteeTallyOutcome.Pending,
            CommitteeTally.Evaluate(required, eligible, approvals, rejections, abstentions));

    /// <summary>CT4 — a decision can be opened only when enough members may vote.</summary>
    [Fact]
    public void TestCT4_ReachabilityAtOpening()
    {
        Assert.True(CommitteeTally.Reachable(2, 2));
        Assert.False(CommitteeTally.Reachable(3, 2));
    }

    /// <summary>CT5 — votes beyond the eligible count (members who left after voting) never make a decision outstanding forever.</summary>
    [Fact]
    public void TestCT5_MoreVotesThanEligibleIsNotNegative() =>
        Assert.Equal(CommitteeTallyOutcome.Rejected, CommitteeTally.Evaluate(2, 1, 1, 1, 0));

    /// <summary>CT6 — nonsense counts are refused, not computed.</summary>
    [Fact]
    public void TestCT6_NegativeCountsAreRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => CommitteeTally.Evaluate(0, 2, 0, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => CommitteeTally.Evaluate(2, -1, 0, 0, 0));
    }

    // --- the archive's pure rules -------------------------------------------------------------------------------

    /// <summary>AR1 — live only while the risk is closed by the archive's own, still existing, closure.</summary>
    [Fact]
    public void TestAR1_LiveNeedsTheArchivesOwnClosure()
    {
        Assert.Equal(RiskArchiveState.Live, ArchiveRules.StateOf(RiskArchiveStatus.Archived, 9, "Closed", true));
        Assert.Equal(RiskArchiveState.Superseded, ArchiveRules.StateOf(RiskArchiveStatus.Archived, 9, "Closed", false));
        Assert.Equal(RiskArchiveState.Superseded, ArchiveRules.StateOf(RiskArchiveStatus.Archived, 9, "New", true));
        Assert.Equal(RiskArchiveState.Superseded, ArchiveRules.StateOf(RiskArchiveStatus.Archived, null, "Closed", true));
        Assert.Equal(RiskArchiveState.Reopened, ArchiveRules.StateOf(RiskArchiveStatus.Reopened, null, "New", false));
    }

    /// <summary>AR2 — quarterly, and due on the date itself.</summary>
    [Fact]
    public void TestAR2_QuarterlyAndDueOnTheDate()
    {
        var archived = new DateTime(2026, 1, 31, 9, 0, 0, DateTimeKind.Utc);
        var due = ArchiveRules.NextReviewDue(archived);

        Assert.Equal(new DateTime(2026, 4, 30, 9, 0, 0, DateTimeKind.Utc), due);
        Assert.False(ArchiveRules.IsReviewDue(due, due.AddSeconds(-1)));
        Assert.True(ArchiveRules.IsReviewDue(due, due));
    }

    /// <summary>AR3 — an event reopens only an archive that watches its type.</summary>
    [Fact]
    public void TestAR3_OnlyAWatchedTypeReopens()
    {
        var watched = new[] { ReassessmentTriggerType.NewRegulation, ReassessmentTriggerType.NewDataOrKriBreach };

        Assert.True(ArchiveRules.Reopens(watched, ReassessmentTriggerType.NewDataOrKriBreach));
        Assert.False(ArchiveRules.Reopens(watched, ReassessmentTriggerType.NewAiModel));
        Assert.False(ArchiveRules.Reopens([], ReassessmentTriggerType.NewRegulation));
    }
}

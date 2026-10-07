using System;
using JetBrains.Annotations;
using Model.RiskFlags;
using Tools.RiskFlags;
using Xunit;

namespace Tools.Tests.RiskFlags;

/// <summary>Stage 9.5 (S46 §4.9, §8 ND1–ND6) — the "next decision" column of the Top Risks list.</summary>
[TestSubject(typeof(NextDecisionResolver))]
public class NextDecisionResolverTest
{
    private static readonly DateTime Now = new(2026, 10, 7, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>ND1 — Gate A wins over every dated event and is overdue since its onset.</summary>
    [Fact]
    public void TestND1_GateAWinsAndIsOverdueSinceItsOnset()
    {
        var next = NextDecisionResolver.Resolve(new NextDecisionInput
        {
            GateA = true,
            GateASince = Now.AddDays(-2),
            AcceptanceExpiresAt = Now.AddDays(1),
            NextManagementReviewAt = Now.AddHours(1),
            HasDecision = true
        }, Now);

        Assert.Equal(NextDecisionKind.GateAEscalation, next.Kind);
        Assert.Equal(Now.AddDays(-2), next.DueAt);
        Assert.True(next.Overdue);

        // An onset the caller could not date is due now.
        Assert.Equal(Now, NextDecisionResolver.Resolve(new NextDecisionInput { GateA = true }, Now).DueAt);
    }

    /// <summary>ND2 — without Gate A, the nearest of acceptance expiry, review and task due date.</summary>
    [Theory]
    [InlineData(5, 9, 7, NextDecisionKind.AcceptanceExpiry)]
    [InlineData(9, 5, 7, NextDecisionKind.ManagementReview)]
    [InlineData(9, 7, 5, NextDecisionKind.MitigationTaskDue)]
    public void TestND2_TheNearestDatedEventIsNext(int acceptance, int review, int task, NextDecisionKind expected)
    {
        var next = NextDecisionResolver.Resolve(new NextDecisionInput
        {
            AcceptanceExpiresAt = Now.AddDays(acceptance),
            NextManagementReviewAt = Now.AddDays(review),
            EarliestOpenTaskDueAt = Now.AddDays(task)
        }, Now);

        Assert.Equal(expected, next.Kind);
        Assert.Equal(Now.AddDays(System.Math.Min(acceptance, System.Math.Min(review, task))), next.DueAt);
        Assert.False(next.Overdue);
    }

    /// <summary>ND3 — an event already past is still the next decision, marked overdue.</summary>
    [Fact]
    public void TestND3_APastEventIsOverdue()
    {
        var next = NextDecisionResolver.Resolve(new NextDecisionInput
        {
            NextManagementReviewAt = Now.AddDays(-10),
            EarliestOpenTaskDueAt = Now.AddDays(3)
        }, Now);

        Assert.Equal(NextDecisionKind.ManagementReview, next.Kind);
        Assert.True(next.Overdue);
    }

    /// <summary>ND4 — nothing scheduled and nothing ever decided: the decision is pending.</summary>
    [Fact]
    public void TestND4_NothingScheduledAndNothingDecidedIsPending()
    {
        var next = NextDecisionResolver.Resolve(new NextDecisionInput(), Now);

        Assert.Equal(NextDecisionKind.DecisionPending, next.Kind);
        Assert.Null(next.DueAt);
        Assert.False(next.Overdue);
    }

    /// <summary>ND5 — nothing scheduled after a decision: None, not pending.</summary>
    [Fact]
    public void TestND5_NothingScheduledAfterADecisionIsNone()
    {
        Assert.Equal(NextDecisionKind.None,
            NextDecisionResolver.Resolve(new NextDecisionInput { HasDecision = true }, Now).Kind);
    }

    /// <summary>ND6 — a tie goes to the acceptance expiry, which reopens the risk when it lapses.</summary>
    [Fact]
    public void TestND6_ATieGoesToTheAcceptanceExpiry()
    {
        var at = Now.AddDays(4);
        var next = NextDecisionResolver.Resolve(new NextDecisionInput
        {
            AcceptanceExpiresAt = at, NextManagementReviewAt = at, EarliestOpenTaskDueAt = at
        }, Now);

        Assert.Equal(NextDecisionKind.AcceptanceExpiry, next.Kind);
        Assert.Throws<ArgumentNullException>(() => NextDecisionResolver.Resolve(null!, Now));
    }
}

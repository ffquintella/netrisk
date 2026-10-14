using System;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Model;
using Model.Status;
using ServerServices.Findings;
using Xunit;

namespace ServerServices.Tests.Track3;

/// <summary>
/// The bridge between the legacy workflow column and the finding lifecycle (S39 D-04, T239).
///
/// "Open means the same everywhere" holds only while three things agree: the closing map the desktop
/// close routes through, <see cref="ClosedStatuses"/> (the Master Dashboard and the host summary),
/// and the transitions the state machine accepts. Each of those is pinned here.
/// </summary>
[TestSubject(typeof(LegacyFindingStatus))]
public class LegacyFindingStatusTest
{
    [Theory]
    [InlineData(IntStatus.Closed, FindingStatus.Mitigated)]
    [InlineData(IntStatus.Mitigated, FindingStatus.Mitigated)]
    [InlineData(IntStatus.Fixed, FindingStatus.Mitigated)]
    [InlineData(IntStatus.Solved, FindingStatus.Mitigated)]
    [InlineData(IntStatus.FixVerified, FindingStatus.Mitigated)]
    [InlineData(IntStatus.Completed, FindingStatus.Mitigated)]
    [InlineData(IntStatus.Retired, FindingStatus.Mitigated)]
    [InlineData(IntStatus.Deleted, FindingStatus.Mitigated)]
    [InlineData(IntStatus.Cancelled, FindingStatus.Mitigated)]
    [InlineData(IntStatus.Outdated, FindingStatus.Mitigated)]
    [InlineData(IntStatus.Rejected, FindingStatus.FalsePositive)]
    [InlineData(IntStatus.NotRelevant, FindingStatus.OutOfScope)]
    [InlineData(IntStatus.FixNotRequired, FindingStatus.OutOfScope)]
    [InlineData(IntStatus.Duplicated, FindingStatus.Duplicate)]
    public void TestEveryClosingWorkflowStatusMapsToAClosedLifecycleState(IntStatus legacy, FindingStatus expected)
    {
        var target = LegacyFindingStatus.ClosingTarget((int)legacy);

        Assert.Equal(expected, target);
        Assert.False(target!.Value.IsOpen());
    }

    /// <summary>
    /// The regression this task fixes in its sharpest form: four values the desktop close dialog offers
    /// (Mitigated, Outdated) or that version 77 already mapped to a closed lifecycle state (FixVerified,
    /// FixNotRequired) were still "open" for <see cref="ClosedStatuses"/>. The two sets must be one.
    /// </summary>
    [Fact]
    public void TestClosedStatusesIsExactlyTheClosingMap()
    {
        var closing = LegacyFindingStatus.ClosingTargets.Keys.OrderBy(v => v).ToList();
        var closed = ClosedStatuses.Values.OrderBy(v => v).ToList();

        Assert.Equal(closing, closed);
    }

    [Theory]
    [InlineData(IntStatus.New)]
    [InlineData(IntStatus.Verified)]
    [InlineData(IntStatus.AwaitingFix)]
    [InlineData(IntStatus.Prioritized)]
    [InlineData(IntStatus.Reopened)]
    public void TestOpenWorkflowStatusesHaveNoClosingTarget(IntStatus legacy)
    {
        Assert.Null(LegacyFindingStatus.ClosingTarget((int)legacy));
        Assert.True(ClosedStatuses.IsOpen((int)legacy));
    }

    [Theory]
    [InlineData(FindingStatus.Active)]
    [InlineData(FindingStatus.Verified)]
    public void TestClosingAnOpenFindingMovesTheLifecycleThroughALegalTransition(FindingStatus current)
    {
        foreach (var (legacy, target) in LegacyFindingStatus.ClosingTargets)
        {
            Assert.Equal(target, LegacyFindingStatus.TransitionForClose(legacy, current));
            Assert.True(FindingStatusMachine.CanTransition(current, target),
                $"{current} -> {target} (legacy {legacy}) is refused by the state machine.");
        }
    }

    /// <summary>
    /// A risk-accepted finding is still exposed, so the close does not skip the lifecycle: it asks for
    /// the move and lets the server refuse it (an acceptance is revoked, not closed over).
    /// </summary>
    [Fact]
    public void TestClosingARiskAcceptedFindingStillAsksTheLifecycle()
    {
        var transition = LegacyFindingStatus.TransitionForClose((int)IntStatus.Fixed, FindingStatus.RiskAccepted);

        Assert.Equal(FindingStatus.Mitigated, transition);
        Assert.False(FindingStatusMachine.CanTransition(FindingStatus.RiskAccepted, FindingStatus.Mitigated));
    }

    [Theory]
    [InlineData(FindingStatus.Mitigated, IntStatus.Rejected)]
    [InlineData(FindingStatus.FalsePositive, IntStatus.Fixed)]
    [InlineData(FindingStatus.OutOfScope, IntStatus.Closed)]
    [InlineData(FindingStatus.Duplicate, IntStatus.NotRelevant)]
    [InlineData(FindingStatus.Mitigated, IntStatus.Fixed)]
    [InlineData(FindingStatus.FalsePositive, IntStatus.Rejected)]
    public void TestALifecycleThatAlreadyHoldsTheFindingClosedKeepsItsVerdict(FindingStatus current, IntStatus legacy)
    {
        Assert.Null(LegacyFindingStatus.TransitionForClose((int)legacy, current));
    }

    [Fact]
    public void TestClosingWithAnOpenWorkflowStatusIsRefused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            LegacyFindingStatus.TransitionForClose((int)IntStatus.Verified, FindingStatus.Active));
    }

    [Theory]
    [InlineData(FindingStatus.Mitigated, FindingStatus.Active)]
    [InlineData(FindingStatus.FalsePositive, FindingStatus.Active)]
    [InlineData(FindingStatus.OutOfScope, FindingStatus.Active)]
    [InlineData(FindingStatus.Duplicate, FindingStatus.Active)]
    [InlineData(FindingStatus.Active, null)]
    [InlineData(FindingStatus.Verified, null)]
    [InlineData(FindingStatus.RiskAccepted, null)]
    public void TestReopenMovesOnlyAClosedLifecycleBackToActive(FindingStatus current, FindingStatus? expected)
    {
        var transition = LegacyFindingStatus.TransitionForReopen(current);

        Assert.Equal(expected, transition);
        if (transition is { } target) Assert.True(FindingStatusMachine.CanTransition(current, target));
    }
}

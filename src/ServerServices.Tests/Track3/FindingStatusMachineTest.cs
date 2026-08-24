using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using ServerServices.Findings;
using Xunit;

namespace ServerServices.Tests.Track3;

/// <summary>
/// The finding-lifecycle transition matrix (Track 3 milestone 3.2.1).
///
/// Exercised exhaustively rather than by example: this is a closed state machine, and the whole
/// point of enforcing it in the service is that no caller can produce a state it did not sanction.
/// A matrix tested only on its happy paths is a matrix with holes.
/// </summary>
[TestSubject(typeof(FindingStatusMachine))]
public class FindingStatusMachineTest
{
    [Theory]
    [InlineData(FindingStatus.Active, FindingStatus.Verified)]
    [InlineData(FindingStatus.Active, FindingStatus.Mitigated)]
    [InlineData(FindingStatus.Verified, FindingStatus.Mitigated)]
    [InlineData(FindingStatus.Verified, FindingStatus.Active)]
    [InlineData(FindingStatus.Mitigated, FindingStatus.Active)]
    [InlineData(FindingStatus.FalsePositive, FindingStatus.Active)]
    [InlineData(FindingStatus.OutOfScope, FindingStatus.Active)]
    [InlineData(FindingStatus.RiskAccepted, FindingStatus.Active)]
    [InlineData(FindingStatus.Duplicate, FindingStatus.Active)]
    public void TestAllowedTransitions(FindingStatus from, FindingStatus to)
    {
        Assert.True(FindingStatusMachine.CanTransition(from, to));
    }

    [Theory]
    // Claiming a suppressed finding was fixed skips the record of it ever coming back.
    [InlineData(FindingStatus.FalsePositive, FindingStatus.Mitigated)]
    [InlineData(FindingStatus.RiskAccepted, FindingStatus.Mitigated)]
    [InlineData(FindingStatus.OutOfScope, FindingStatus.Verified)]
    // Re-triage starts from open, not sideways between suppressed states.
    [InlineData(FindingStatus.FalsePositive, FindingStatus.OutOfScope)]
    [InlineData(FindingStatus.RiskAccepted, FindingStatus.FalsePositive)]
    [InlineData(FindingStatus.Duplicate, FindingStatus.Mitigated)]
    public void TestRefusedTransitions(FindingStatus from, FindingStatus to)
    {
        Assert.False(FindingStatusMachine.CanTransition(from, to));
    }

    [Fact]
    public void TestEveryStateCanReturnToActive()
    {
        // A wrong suppression has to be reversible without editing the database.
        foreach (var status in System.Enum.GetValues<FindingStatus>().Where(s => s != FindingStatus.Active))
            Assert.True(FindingStatusMachine.CanTransition(status, FindingStatus.Active),
                $"{status} cannot be reopened");
    }

    [Fact]
    public void TestValidateRejectsSameState()
    {
        var ex = Assert.Throws<InvalidStateTransitionException>(() =>
            FindingStatusMachine.Validate(FindingStatus.Active, FindingStatus.Active, null, null));

        Assert.Equal(nameof(FindingStatus.Active), ex.FromState);
    }

    [Fact]
    public void TestValidateRejectsIllegalTransitionAndNamesTheAlternatives()
    {
        var ex = Assert.Throws<InvalidStateTransitionException>(() =>
            FindingStatusMachine.Validate(FindingStatus.FalsePositive, FindingStatus.Mitigated,
                "because", null));

        Assert.Equal(nameof(FindingStatus.FalsePositive), ex.FromState);
        Assert.Equal(nameof(FindingStatus.Mitigated), ex.ToState);
        // The message has to tell the caller what would have worked; a bare refusal makes them guess.
        Assert.Contains("Active", ex.Message);
    }

    [Theory]
    [InlineData(FindingStatus.FalsePositive)]
    [InlineData(FindingStatus.OutOfScope)]
    [InlineData(FindingStatus.RiskAccepted)]
    public void TestSuppressingTransitionsRequireAJustification(FindingStatus to)
    {
        var ex = Assert.Throws<InvalidParameterException>(() =>
            FindingStatusMachine.Validate(FindingStatus.Active, to, justification: "   ", duplicateOfId: null));

        Assert.Equal("justification", ex.ParameterName);
    }

    [Fact]
    public void TestMitigatedNeedsNoJustification()
    {
        // Marking something fixed is ordinary work; demanding prose for it would train people to
        // type "fixed" into the box, which is worse than not asking.
        FindingStatusMachine.Validate(FindingStatus.Active, FindingStatus.Mitigated, null, null);
    }

    [Fact]
    public void TestDuplicateRequiresACanonicalFinding()
    {
        var ex = Assert.Throws<InvalidParameterException>(() =>
            FindingStatusMachine.Validate(FindingStatus.Active, FindingStatus.Duplicate,
                "same as the other one", duplicateOfId: null));

        Assert.Equal("duplicateOfId", ex.ParameterName);
    }

    [Fact]
    public void TestFindingCannotBeADuplicateOfItself()
    {
        var ex = Assert.Throws<InvalidParameterException>(() =>
            FindingStatusMachine.Validate(FindingStatus.Active, FindingStatus.Duplicate,
                "same", duplicateOfId: 42, findingId: 42));

        Assert.Equal("duplicateOfId", ex.ParameterName);
    }

    [Theory]
    [InlineData(FindingStatus.FalsePositive, ReimportOutcome.KeepSuppressed)]
    [InlineData(FindingStatus.OutOfScope, ReimportOutcome.KeepSuppressed)]
    [InlineData(FindingStatus.RiskAccepted, ReimportOutcome.KeepSuppressed)]
    [InlineData(FindingStatus.Duplicate, ReimportOutcome.KeepSuppressed)]
    [InlineData(FindingStatus.Mitigated, ReimportOutcome.Reactivate)]
    [InlineData(FindingStatus.Active, ReimportOutcome.Touch)]
    [InlineData(FindingStatus.Verified, ReimportOutcome.Touch)]
    public void TestStickyTriageOnReimport(FindingStatus current, ReimportOutcome expected)
    {
        // The two load-bearing behaviours: a suppressed verdict survives the scanner disagreeing
        // with it, and a mitigated finding coming back is a regression.
        Assert.Equal(expected, FindingStatusMachine.OnSeenAgain(current));
    }

    [Theory]
    [InlineData(FindingStatus.Active, true)]
    [InlineData(FindingStatus.Verified, true)]
    [InlineData(FindingStatus.FalsePositive, false)]
    [InlineData(FindingStatus.RiskAccepted, false)]
    [InlineData(FindingStatus.Mitigated, false)]
    public void TestOnlyOpenStatesAccrueSla(FindingStatus status, bool accrues)
    {
        Assert.Equal(accrues, status.AccruesSla());
        Assert.Equal(accrues, status.IsOpen());
    }
}

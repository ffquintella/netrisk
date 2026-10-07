using System;
using DAL.Enums;
using JetBrains.Annotations;
using Model.TreatmentEconomics;
using Tools.TreatmentEconomics;
using Xunit;

namespace Tools.Tests.TreatmentEconomics;

/// <summary>
/// Stage 9.6 (S47 §4.9, §8 TL1–TL5) — the target risk level against the current level and the appetite.
/// </summary>
[TestSubject(typeof(TargetLevel))]
public class TargetLevelTest
{
    private static readonly DateOnly Today = new(2026, 10, 7);

    /// <summary>TL1 — at or below the target is met, on the score and on the expected loss.</summary>
    [Fact]
    public void TestTL1_AtOrBelowTheTargetIsMet()
    {
        var status = TargetLevel.Evaluate(4m, 50_000m, null, 4.0, 30_000, null, Today);

        Assert.True(status.ScoreMet);
        Assert.True(status.ExpectedLossMet);
        Assert.Equal(0, status.ScoreGap);
        Assert.Equal(-20_000, status.ExpectedLossGap);
    }

    /// <summary>TL2 — above the target is not met, and the gap is the distance still to go.</summary>
    [Fact]
    public void TestTL2_AboveTheTargetReportsTheGap()
    {
        var status = TargetLevel.Evaluate(3m, null, null, 6.5, null, null, Today);

        Assert.False(status.ScoreMet);
        Assert.Equal(3.5, status.ScoreGap!.Value, 6);
        Assert.Null(status.ExpectedLossMet);
        Assert.Contains("3.5 above the target", status.Explanation);
    }

    /// <summary>TL3 — within appetite when the target score is at or below the ceiling; unknown without either.</summary>
    [Fact]
    public void TestTL3_WithinAppetiteComparesTheTargetWithTheCeiling()
    {
        Assert.True(TargetLevel.Evaluate(5m, null, null, 7, null, 5.0, Today).WithinAppetite);

        var above = TargetLevel.Evaluate(6m, null, null, 7, null, 5.0, Today);
        Assert.False(above.WithinAppetite);
        Assert.Contains("escalated", above.Explanation);

        Assert.Null(TargetLevel.Evaluate(6m, null, null, 7, null, null, Today).WithinAppetite);
        Assert.Null(TargetLevel.Evaluate(null, 10m, null, 7, 20, 5.0, Today).WithinAppetite);
    }

    /// <summary>TL4 — overdue when the date has passed and a declared target is not met; not when it is met or still ahead.</summary>
    [Fact]
    public void TestTL4_OverdueOnlyWhenThePassedTargetIsNotMet()
    {
        var yesterday = Today.AddDays(-1);

        Assert.True(TargetLevel.Evaluate(3m, null, yesterday, 6, null, null, Today).Overdue);
        Assert.False(TargetLevel.Evaluate(3m, null, yesterday, 2, null, null, Today).Overdue);
        Assert.False(TargetLevel.Evaluate(3m, null, Today.AddDays(1), 6, null, null, Today).Overdue);
        Assert.False(TargetLevel.Evaluate(3m, null, Today, 6, null, null, Today).Overdue);
    }

    /// <summary>TL5 — without a current measure, met is unknown — never assumed.</summary>
    [Fact]
    public void TestTL5_NoCurrentMeasureIsUnknown()
    {
        var status = TargetLevel.Evaluate(3m, 10m, Today.AddDays(-30), null, null, 5.0, Today);

        Assert.Null(status.ScoreMet);
        Assert.Null(status.ExpectedLossMet);
        Assert.False(status.Overdue);
        Assert.Contains("no quantitative analysis", status.Explanation);
    }
}

/// <summary>Stage 9.6 (S47 §4.8, §8 AP1–AP3) — what an action-plan line lacks against Phase 5.</summary>
[TestSubject(typeof(ActionPlanCompleteness))]
public class ActionPlanCompletenessTest
{
    /// <summary>AP1 — owner, due date and criterion present, and evidence on a completed task: nothing missing.</summary>
    [Fact]
    public void TestAP1_ACompleteLineMissesNothing()
    {
        Assert.Empty(ActionPlanCompleteness.Missing(true, true, "MFA enforced for all admins",
            MitigationTaskStatus.Completed, "Conditional access policy CA-12, report attached to ticket 4411"));
    }

    /// <summary>AP2 — an open task lacks what it lacks, but not evidence: it is not done yet.</summary>
    [Fact]
    public void TestAP2_AnOpenTaskDoesNotNeedEvidenceYet()
    {
        Assert.Equal([ActionPlanElement.Owner, ActionPlanElement.DueDate, ActionPlanElement.AcceptanceCriterion],
            ActionPlanCompleteness.Missing(false, false, "  ", MitigationTaskStatus.Open, null));
    }

    /// <summary>AP3 — a task completed without evidence (before schema 95) reads as missing it; a cancelled one needs nothing.</summary>
    [Fact]
    public void TestAP3_ACompletedTaskWithoutEvidenceIsNotCompliant()
    {
        Assert.Equal([ActionPlanElement.CompletionEvidence],
            ActionPlanCompleteness.Missing(true, true, "Done when patched", MitigationTaskStatus.Completed, null));

        Assert.Empty(ActionPlanCompleteness.Missing(false, false, null, MitigationTaskStatus.Cancelled, null));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track8;

/// <summary>
/// Track 8 milestone 8.3 — the state machine, segregation of duties and the appetite gate, against a
/// real EF context.
///
/// These are the tests the milestone's acceptance criteria name: "no self-approval path exists
/// (test-proven, including admin)" and "above-threshold acceptances demonstrably require two distinct
/// qualified approvers". Each rule is asserted from the refusing side, because a control that has only
/// ever been tested from the allowing side is a control nobody has checked.
/// </summary>
[TestSubject(typeof(RiskWorkflowService))]
public class RiskWorkflowServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IRiskWorkflowService _workflow;

    public RiskWorkflowServiceInMemoryTest()
    {
        _workflow = GetService<IRiskWorkflowService>();
    }

    // --- fixtures -------------------------------------------------------------------------------

    private static User NewUser(int id, string name, bool admin = false) => new()
    {
        Value = id, Name = name, Login = name, Enabled = true, Admin = admin,
        Type = "local", Salt = "s", Password = System.Text.Encoding.UTF8.GetBytes("p"),
        Email = $"{name}@example.test"
    };

    private static Risk NewRisk(int id, string status = "New", int? owner = null, int? manager = null,
        int? submittedBy = null, int? entityId = null) => new()
    {
        Id = id,
        Status = status,
        Subject = $"Risk {id}",
        ReferenceId = $"R-{id}",
        Assessment = string.Empty,
        Notes = string.Empty,
        RiskCatalogMapping = string.Empty,
        ThreatCatalogMapping = string.Empty,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        Owner = owner,
        Manager = manager,
        SubmittedBy = submittedBy,
        EntityId = entityId
    };

    private static RiskScoring NewScoring(int riskId, float inherent, float? residual = null) => new()
    {
        Id = riskId, ScoringMethod = 1, CalculatedRisk = inherent,
        ClassicLikelihood = 3, ClassicImpact = 3, ResidualRisk = residual
    };

    private static Mitigation NewMitigation(int id, int riskId, int percent = 0) => new()
    {
        Id = id, RiskId = riskId, PlanningStrategy = 1, MitigationEffort = 1, MitigationCost = 1,
        MitigationOwner = 1, SubmittedBy = 1, MitigationPercent = percent,
        CurrentSolution = string.Empty, SecurityRequirements = string.Empty,
        SecurityRecommendations = string.Empty,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        PlanningDate = new DateOnly(2026, 6, 1)
    };

    private static MgmtReview NewReview(int id, int riskId, int nextStep, int reviewer = 1) => new()
    {
        Id = id, RiskId = riskId, Reviewer = reviewer, Review = 2, NextStep = nextStep,
        Comments = string.Empty, SubmissionDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
        NextReview = new DateOnly(2026, 8, 1)
    };

    /// <summary>Both switches on, which is what version 80 seeds.</summary>
    private void SeedWorkflowSettings(bool stateMachine = true, bool segregation = true,
        bool breakGlass = false)
    {
        Seed(ctx =>
        {
            ctx.Settings.Add(new Setting
                { Name = RiskWorkflowService.StateMachineSetting, Value = stateMachine ? "true" : "false" });
            ctx.Settings.Add(new Setting
                { Name = RiskWorkflowService.SegregationSetting, Value = segregation ? "true" : "false" });
            ctx.Settings.Add(new Setting
                { Name = RiskWorkflowService.BreakGlassSetting, Value = breakGlass ? "true" : "false" });
        });
    }

    // --- 8.3.1 state machine --------------------------------------------------------------------

    [Fact]
    public async Task TestMitigationPlannedIsRefusedWithoutAMitigation()
    {
        SeedWorkflowSettings();
        Seed(ctx => ctx.Risks.Add(NewRisk(1)));

        var ex = await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            _workflow.EnsureTransitionAllowedAsync(1, "New", RiskWorkflowService.StatusMitigationPlanned));

        Assert.Equal("New", ex.FromState);
        Assert.Equal(RiskWorkflowService.StatusMitigationPlanned, ex.ToState);
        Assert.Contains("before a mitigation exists", ex.Message);
    }

    [Fact]
    public async Task TestMitigationPlannedIsAllowedOnceAMitigationExists()
    {
        SeedWorkflowSettings();
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.Mitigations.Add(NewMitigation(1, 1));
        });

        await _workflow.EnsureTransitionAllowedAsync(1, "New", RiskWorkflowService.StatusMitigationPlanned);
    }

    [Fact]
    public async Task TestManagementReviewIsRefusedWithoutAReview()
    {
        SeedWorkflowSettings();
        Seed(ctx => ctx.Risks.Add(NewRisk(1)));

        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            _workflow.EnsureTransitionAllowedAsync(1, "New", RiskWorkflowService.StatusManagementReview));
    }

    [Fact]
    public async Task TestClosedIsRefusedWithNoReviewAndNoAcceptance()
    {
        SeedWorkflowSettings();
        Seed(ctx => ctx.Risks.Add(NewRisk(1)));

        var ex = await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            _workflow.EnsureTransitionAllowedAsync(1, "New", RiskWorkflowService.StatusClosed));

        Assert.Contains("without a management review or a live risk acceptance", ex.Message);
    }

    /// <summary>
    /// The subtle one. A review whose next step is "Request Risk review" means the reviewer asked for
    /// another look — the risk is not settled, and closing it anyway is exactly the shape of evidence
    /// an auditor rejects.
    /// </summary>
    [Fact]
    public async Task TestClosedIsRefusedWhenTheLatestReviewAskedForAnotherOne()
    {
        SeedWorkflowSettings();
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.MgmtReviews.Add(NewReview(1, 1, nextStep: 3));
            ctx.MgmtReviews.Add(NewReview(2, 1, nextStep: 1));
        });

        var ex = await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            _workflow.EnsureTransitionAllowedAsync(1, "New", RiskWorkflowService.StatusClosed));

        Assert.Contains("asked for another review", ex.Message);
    }

    [Fact]
    public async Task TestClosedIsAllowedAfterASettledReview()
    {
        SeedWorkflowSettings();
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.MgmtReviews.Add(NewReview(1, 1, nextStep: 3));
        });

        await _workflow.EnsureTransitionAllowedAsync(1, "New", RiskWorkflowService.StatusClosed);
    }

    [Fact]
    public async Task TestClosedIsRefusedWhileAReviewAwaitsCounterSignature()
    {
        SeedWorkflowSettings();
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));

            var review = NewReview(1, 1, nextStep: 3);
            review.RequiresCountersignature = true;
            ctx.MgmtReviews.Add(review);
        });

        var ex = await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            _workflow.EnsureTransitionAllowedAsync(1, "New", RiskWorkflowService.StatusClosed));

        Assert.Contains("counter-signature", ex.Message);
    }

    [Fact]
    public async Task TestClosedIsAllowedWithALiveAcceptanceAndNoReview()
    {
        SeedWorkflowSettings();
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "manager"));
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskAcceptances.Add(new RiskAcceptance
            {
                Id = 1, RiskId = 1, Name = "Q3 exception", AuthorizingManagerId = 1,
                BusinessJustification = "Compensating control in place.",
                StartDate = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(90),
                Status = RiskAcceptanceStatus.Active, CreatedAt = DateTime.UtcNow
            });
        });

        await _workflow.EnsureTransitionAllowedAsync(1, "New", RiskWorkflowService.StatusClosed);
    }

    /// <summary>An expired acceptance is not a live one, and must not hold a close open.</summary>
    [Fact]
    public async Task TestAnExpiredAcceptanceDoesNotPermitClosing()
    {
        SeedWorkflowSettings();
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "manager"));
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskAcceptances.Add(new RiskAcceptance
            {
                Id = 1, RiskId = 1, Name = "Lapsed", AuthorizingManagerId = 1,
                BusinessJustification = "Was compensating.",
                StartDate = DateTime.UtcNow.AddDays(-120), ExpiresAt = DateTime.UtcNow.AddDays(-1),
                Status = RiskAcceptanceStatus.Active, CreatedAt = DateTime.UtcNow.AddDays(-120)
            });
        });

        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            _workflow.EnsureTransitionAllowedAsync(1, "New", RiskWorkflowService.StatusClosed));
    }

    [Fact]
    public async Task TestAnUnchangedStatusIsNeverRefused()
    {
        SeedWorkflowSettings();
        Seed(ctx => ctx.Risks.Add(NewRisk(1, "Closed")));

        await _workflow.EnsureTransitionAllowedAsync(1, "Closed", "Closed");
    }

    [Fact]
    public async Task TestTheStateMachineCanBeTurnedOff()
    {
        SeedWorkflowSettings(stateMachine: false);
        Seed(ctx => ctx.Risks.Add(NewRisk(1)));

        await _workflow.EnsureTransitionAllowedAsync(1, "New", RiskWorkflowService.StatusClosed);
    }

    // --- 8.3.2 segregation of duties --------------------------------------------------------------

    [Theory]
    [InlineData("submitter")]
    [InlineData("owner")]
    [InlineData("manager")]
    public async Task TestARiskCannotBeDecidedBySomeoneTooCloseToIt(string relation)
    {
        SeedWorkflowSettings();

        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(7, "insider"));
            ctx.Risks.Add(NewRisk(1,
                owner: relation == "owner" ? 7 : 99,
                manager: relation == "manager" ? 7 : 98,
                submittedBy: relation == "submitter" ? 7 : 97));
        });

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            _workflow.EnsureSegregationOfDutiesAsync(1, 7, "review"));

        Assert.Equal("segregation_of_duties", ex.RuleName);
    }

    /// <summary>
    /// The acceptance criterion says "including admin". Administrators bypassed every other check in
    /// this product, and this is the one they must not.
    /// </summary>
    [Fact]
    public async Task TestAnAdministratorDoesNotBypassSegregationOfDuties()
    {
        SeedWorkflowSettings();

        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(7, "root", admin: true));
            ctx.Risks.Add(NewRisk(1, owner: 7));
        });

        await Assert.ThrowsAsync<RuleBrokenException>(() =>
            _workflow.EnsureSegregationOfDutiesAsync(1, 7, "accept"));
    }

    [Fact]
    public async Task TestAnUninvolvedUserPassesSegregationOfDuties()
    {
        SeedWorkflowSettings();

        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(8, "reviewer"));
            ctx.Risks.Add(NewRisk(1, owner: 7, manager: 6, submittedBy: 5));
        });

        await _workflow.EnsureSegregationOfDutiesAsync(1, 8, "review");
    }

    /// <summary>
    /// A stated reason is not enough on its own: break-glass has to be switched on as well, or the
    /// rule would be bypassable by anyone who can type a sentence.
    /// </summary>
    [Fact]
    public async Task TestBreakGlassIsRefusedWhileTheSettingIsOff()
    {
        SeedWorkflowSettings(breakGlass: false);

        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(7, "insider"));
            ctx.Risks.Add(NewRisk(1, owner: 7));
        });

        await Assert.ThrowsAsync<PermissionInvalidException>(() =>
            _workflow.EnsureSegregationOfDutiesAsync(1, 7, "accept", "the only manager is on leave"));
    }

    [Fact]
    public async Task TestBreakGlassIsAllowedWhenTheSettingIsOnAndAReasonIsGiven()
    {
        SeedWorkflowSettings(breakGlass: true);

        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(7, "insider"));
            ctx.Risks.Add(NewRisk(1, owner: 7));
        });

        await _workflow.EnsureSegregationOfDutiesAsync(1, 7, "accept", "the only manager is on leave");
    }

    /// <summary>Break-glass switched on does not silently exempt everybody — a reason is still needed.</summary>
    [Fact]
    public async Task TestBreakGlassStillNeedsAReason()
    {
        SeedWorkflowSettings(breakGlass: true);

        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(7, "insider"));
            ctx.Risks.Add(NewRisk(1, owner: 7));
        });

        await Assert.ThrowsAsync<RuleBrokenException>(() =>
            _workflow.EnsureSegregationOfDutiesAsync(1, 7, "accept"));
    }

    [Fact]
    public async Task TestSegregationCanBeTurnedOff()
    {
        SeedWorkflowSettings(segregation: false);

        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(7, "insider"));
            ctx.Risks.Add(NewRisk(1, owner: 7));
        });

        await _workflow.EnsureSegregationOfDutiesAsync(1, 7, "accept");
    }

    // --- 8.3.3 appetite ---------------------------------------------------------------------------

    [Fact]
    public async Task TestNoAppetiteConfiguredGatesNothingAndSaysSo()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(NewScoring(1, 9.5f));
        });

        var evaluation = await _workflow.EvaluateAppetiteAsync(1);

        Assert.False(evaluation.AppetiteConfigured);
        Assert.False(evaluation.ExceedsCeiling);
        Assert.False(evaluation.RequiresDualApproval);
        Assert.Contains("No risk appetite is configured", evaluation.Explanation);
    }

    [Fact]
    public async Task TestResidualAboveTheCeilingIsReportedAsSuch()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(NewScoring(1, 9.5f, residual: 8.0f));
            ctx.RiskAppetites.Add(new RiskAppetite
            {
                Id = 1, EntityId = null, MaxAcceptableResidual = 6, DualApprovalThreshold = 4,
                CreatedAt = DateTime.UtcNow
            });
        });

        var evaluation = await _workflow.EvaluateAppetiteAsync(1);

        Assert.True(evaluation.ExceedsCeiling);
        // Above the ceiling there is nothing to escalate to — the decision is refused outright, and
        // reporting both would tell a reviewer to go find a second approver who also cannot help.
        Assert.False(evaluation.RequiresDualApproval);
    }

    [Fact]
    public async Task TestResidualBetweenThresholdAndCeilingRequiresDualApproval()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(NewScoring(1, 9.5f, residual: 5.0f));
            ctx.RiskAppetites.Add(new RiskAppetite
            {
                Id = 1, EntityId = null, MaxAcceptableResidual = 6, DualApprovalThreshold = 4,
                CreatedAt = DateTime.UtcNow
            });
        });

        var evaluation = await _workflow.EvaluateAppetiteAsync(1);

        Assert.False(evaluation.ExceedsCeiling);
        Assert.True(evaluation.RequiresDualApproval);
    }

    /// <summary>
    /// An untreated risk has no residual score. Falling back to the inherent one is what stops it
    /// sailing past a ceiling that a treated risk is held to.
    /// </summary>
    [Fact]
    public async Task TestAnUnassessedResidualFallsBackToTheInherentScore()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(NewScoring(1, 9.5f));
            ctx.RiskAppetites.Add(new RiskAppetite
            {
                Id = 1, EntityId = null, MaxAcceptableResidual = 6, DualApprovalThreshold = 4,
                CreatedAt = DateTime.UtcNow
            });
        });

        var evaluation = await _workflow.EvaluateAppetiteAsync(1);

        Assert.Equal(9.5, evaluation.ResidualScore!.Value, 3);
        Assert.True(evaluation.ExceedsCeiling);
    }

    [Fact]
    public async Task TestAnEntityAppetiteOverridesTheGlobalOne()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1, entityId: 5));
            ctx.RiskScorings.Add(NewScoring(1, 9.5f, residual: 7.0f));
            ctx.RiskAppetites.Add(new RiskAppetite
            {
                Id = 1, EntityId = null, MaxAcceptableResidual = 6, DualApprovalThreshold = 4,
                CreatedAt = DateTime.UtcNow
            });
            ctx.RiskAppetites.Add(new RiskAppetite
            {
                Id = 2, EntityId = 5, MaxAcceptableResidual = 9, DualApprovalThreshold = 8,
                CreatedAt = DateTime.UtcNow
            });
        });

        var evaluation = await _workflow.EvaluateAppetiteAsync(1);

        Assert.Equal(2, evaluation.AppetiteId);
        Assert.False(evaluation.ExceedsCeiling);
    }

    [Fact]
    public async Task TestRisksAboveAppetiteAreCountedPerEntity()
    {
        Seed(ctx =>
        {
            ctx.RiskAppetites.Add(new RiskAppetite
            {
                Id = 1, EntityId = null, MaxAcceptableResidual = 5, DualApprovalThreshold = 4,
                CreatedAt = DateTime.UtcNow
            });

            ctx.Risks.Add(NewRisk(1, entityId: 5));
            ctx.RiskScorings.Add(NewScoring(1, 9f, residual: 8f));

            ctx.Risks.Add(NewRisk(2, entityId: 5));
            ctx.RiskScorings.Add(NewScoring(2, 3f, residual: 2f));

            // Closed risks are not anybody's problem any more.
            ctx.Risks.Add(NewRisk(3, "Closed", entityId: 5));
            ctx.RiskScorings.Add(NewScoring(3, 10f, residual: 10f));
        });

        var counts = await _workflow.CountRisksAboveAppetiteAsync();

        var entity = Assert.Single(counts);
        Assert.Equal(5, entity.EntityId);
        Assert.Equal(1, entity.Count);
    }

    [Fact]
    public async Task TestLegacyViolationsAreReportedAndNothingIsMutated()
    {
        SeedWorkflowSettings();
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1, RiskWorkflowService.StatusClosed));
            ctx.Risks.Add(NewRisk(2, RiskWorkflowService.StatusMitigationPlanned));
            ctx.Mitigations.Add(NewMitigation(1, 2));
        });

        var violations = await _workflow.FindLegacyViolationsAsync();

        var violation = Assert.Single(violations);
        Assert.Equal(1, violation.RiskId);

        await using var db = OpenContext();
        Assert.Equal(RiskWorkflowService.StatusClosed, db.Risks.Single(r => r.Id == 1).Status);
    }
}

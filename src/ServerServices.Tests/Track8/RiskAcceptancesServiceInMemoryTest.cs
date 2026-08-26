using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Governance;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track8;

/// <summary>
/// Track 8 milestone 8.1 — formal, expiring, authorized risk acceptance.
///
/// The invariant under test is the one the milestone exists for: a risk is only ever in an "accepted"
/// state with a live row naming an authorizer, carrying a justification, and expiring on a stated
/// date. Every way of getting around that — no justification, no expiry, a past expiry, your own
/// risk, a second live acceptance, a residual above the ceiling — is asserted from the refusing side.
/// </summary>
[TestSubject(typeof(RiskAcceptancesService))]
public class RiskAcceptancesServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IRiskAcceptancesService _service;

    public RiskAcceptancesServiceInMemoryTest()
    {
        _service = GetService<IRiskAcceptancesService>();
    }

    private static User NewUser(int id, string name, bool admin = false) => new()
    {
        Value = id, Name = name, Login = name, Enabled = true, Admin = admin,
        Type = "local", Salt = "s", Password = System.Text.Encoding.UTF8.GetBytes("p"),
        Email = $"{name}@example.test"
    };

    private static Risk NewRisk(int id, int? owner = null, int? manager = null, int? submittedBy = null)
        => new()
        {
            Id = id, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R-{id}",
            Assessment = string.Empty, Notes = string.Empty,
            RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
            SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            Owner = owner, Manager = manager, SubmittedBy = submittedBy
        };

    /// <summary>
    /// The baseline world: an administrator who is unrelated to the risk (so the band check passes
    /// and segregation of duties does not fire), a risk, and a mid-band score.
    /// </summary>
    private void SeedBaseline(float inherent = 5f, float? residual = null)
    {
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "cro", admin: true));
            ctx.Users.Add(NewUser(2, "owner"));
            ctx.Risks.Add(NewRisk(1, owner: 2, manager: 2, submittedBy: 2));
            ctx.RiskScorings.Add(new RiskScoring
            {
                Id = 1, ScoringMethod = 1, CalculatedRisk = inherent, ResidualRisk = residual,
                ClassicImpact = 3, ClassicLikelihood = 3
            });
            // `risk_levels` is a keyless entity, which the in-memory provider will not track, so the
            // bands cannot be seeded here. The pure mapping is covered directly by
            // TestBandResolutionPicksTheHighestLevelAtOrBelowTheScore below; with no bands configured
            // the service falls back to review_insignificant, which is what the permission assertion
            // in TestAcceptanceNeedsTheReviewPermissionForTheResidualBand expects.

            ctx.Settings.Add(new Setting { Name = RiskWorkflowService.SegregationSetting, Value = "true" });
            ctx.Settings.Add(new Setting { Name = RiskWorkflowService.BreakGlassSetting, Value = "false" });
        });
    }

    private static RiskAcceptanceRequest ValidRequest(DateTime? expiry = null) => new()
    {
        Name = "Q3 exception",
        BusinessJustification = "The vendor patch breaks the payment integration.",
        ExpiresAt = expiry ?? DateTime.UtcNow.AddDays(90)
    };

    // --- creation ---------------------------------------------------------------------------------

    [Fact]
    public async Task TestCreatingAnAcceptanceSnapshotsResidualAndWritesAReview()
    {
        SeedBaseline(inherent: 8f, residual: 4.5f);

        var acceptance = await _service.CreateAsync(1, ValidRequest(), actingUserId: 1);

        Assert.Equal(RiskAcceptanceStatus.Active, acceptance.Status);
        Assert.Equal(1, acceptance.RiskId);
        Assert.Equal(1, acceptance.AuthorizingManagerId);
        // The snapshot is the residual, not the inherent: the manager signed off on the risk as
        // treated, and re-scoring later must not retroactively change what they approved.
        Assert.Equal(4.5, acceptance.ResidualScoreSnapshot!.Value, 3);

        await using var db = OpenContext();
        var review = Assert.Single(db.MgmtReviews.ToList());
        Assert.Equal(1, review.RiskId);
        Assert.Equal(1, review.Reviewer);
        Assert.Contains("Risk accepted until", review.Comments);
    }

    [Fact]
    public async Task TestAcceptanceWithoutAJustificationIsRefused()
    {
        SeedBaseline();

        var request = ValidRequest();
        request.BusinessJustification = "   ";

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _service.CreateAsync(1, request, 1));

        Assert.Equal(nameof(request.BusinessJustification), ex.ParameterName);
    }

    [Fact]
    public async Task TestAcceptanceWithoutAnExpiryIsRefused()
    {
        SeedBaseline();

        var request = ValidRequest();
        request.ExpiresAt = null;

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _service.CreateAsync(1, request, 1));

        Assert.Equal(nameof(request.ExpiresAt), ex.ParameterName);
    }

    [Fact]
    public async Task TestAcceptanceExpiringInThePastIsRefused()
    {
        SeedBaseline();

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _service.CreateAsync(1, ValidRequest(DateTime.UtcNow.AddDays(-1)), 1));
    }

    /// <summary>
    /// The self-approval path, at the level that matters: the acceptance service, not just the
    /// workflow engine underneath it. User 2 owns, manages and submitted risk 1.
    /// </summary>
    [Fact]
    public async Task TestYouCannotAcceptYourOwnRisk()
    {
        SeedBaseline();

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            _service.CreateAsync(1, ValidRequest(), actingUserId: 2));

        Assert.Equal("segregation_of_duties", ex.RuleName);

        await using var db = OpenContext();
        Assert.Empty(db.RiskAcceptances.ToList());
    }

    [Fact]
    public async Task TestASecondLiveAcceptanceIsRefused()
    {
        SeedBaseline();

        await _service.CreateAsync(1, ValidRequest(), 1);

        var ex = await Assert.ThrowsAsync<DataAlreadyExistsException>(() =>
            _service.CreateAsync(1, ValidRequest(), 1));

        Assert.Contains("already has a live acceptance", ex.Message);
    }

    [Fact]
    public async Task TestResidualAboveTheAppetiteCeilingBlocksAcceptance()
    {
        SeedBaseline(inherent: 9f, residual: 8.5f);
        Seed(ctx => ctx.RiskAppetites.Add(new RiskAppetite
        {
            Id = 1, EntityId = null, MaxAcceptableResidual = 6, DualApprovalThreshold = 4,
            CreatedAt = DateTime.UtcNow
        }));

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => _service.CreateAsync(1, ValidRequest(), 1));

        Assert.Equal("risk_appetite_ceiling", ex.RuleName);

        await using var db = OpenContext();
        Assert.Empty(db.RiskAcceptances.ToList());
    }

    /// <summary>
    /// Above the dual-approval threshold the acceptance is recorded, but the review it writes is
    /// marked as needing a counter-signature — which is what holds the risk in review (8.3.4).
    /// </summary>
    [Fact]
    public async Task TestAboveTheDualApprovalThresholdTheReviewAwaitsACounterSignature()
    {
        SeedBaseline(inherent: 9f, residual: 5f);
        Seed(ctx => ctx.RiskAppetites.Add(new RiskAppetite
        {
            Id = 1, EntityId = null, MaxAcceptableResidual = 6, DualApprovalThreshold = 4,
            CreatedAt = DateTime.UtcNow
        }));

        await _service.CreateAsync(1, ValidRequest(), 1);

        await using var db = OpenContext();
        var review = Assert.Single(db.MgmtReviews.ToList());
        Assert.True(review.RequiresCountersignature);
        Assert.Null(review.SecondReviewerId);
    }

    /// <summary>
    /// A non-admin without the band's review permission cannot accept, even though nothing else
    /// about the request is wrong.
    /// </summary>
    [Fact]
    public async Task TestAcceptanceNeedsTheReviewPermissionForTheResidualBand()
    {
        SeedBaseline(inherent: 8f, residual: 7.5f);
        Seed(ctx => ctx.Users.Add(NewUser(3, "analyst")));

        var ex = await Assert.ThrowsAsync<PermissionInvalidException>(() =>
            _service.CreateAsync(1, ValidRequest(), actingUserId: 3));

        Assert.StartsWith("review_", ex.Permission);
    }

    /// <summary>
    /// The band mapping itself, tested against the seeded thresholds (Low ≥ 0, Medium ≥ 4, High ≥ 7,
    /// Very High ≥ 10.1) that version 1 puts in `risk_levels`.
    /// </summary>
    [Theory]
    [InlineData(0.0, "Low")]
    [InlineData(3.9, "Low")]
    [InlineData(4.0, "Medium")]
    [InlineData(6.9, "Medium")]
    [InlineData(7.0, "High")]
    [InlineData(10.0, "High")]
    [InlineData(10.5, "Very High")]
    public void TestBandResolutionPicksTheHighestLevelAtOrBelowTheScore(double score, string expected)
    {
        var levels = new List<RiskLevel>
        {
            new() { Value = 7.0m, Name = "High", DisplayName = "High", Color = "orangered" },
            new() { Value = 0.0m, Name = "Low", DisplayName = "Low", Color = "yellow" },
            new() { Value = 10.1m, Name = "Very High", DisplayName = "Very High", Color = "red" },
            new() { Value = 4.0m, Name = "Medium", DisplayName = "Medium", Color = "orange" }
        };

        Assert.Equal(expected, RiskAcceptancesService.ResolveBand(levels, score));
    }

    [Fact]
    public void TestBandResolutionFallsBackToTheNarrowestBandWhenNothingIsConfigured()
    {
        Assert.Equal("insignificant", RiskAcceptancesService.ResolveBand([], 9.0));
        Assert.Equal("insignificant",
            RiskAcceptancesService.ResolveBand(
                [new RiskLevel { Value = 0m, Name = "Low", DisplayName = "Low", Color = "y" }], null));
    }

    // --- renewal ----------------------------------------------------------------------------------

    [Fact]
    public async Task TestRenewalWritesANewRowAndKeepsThePredecessor()
    {
        SeedBaseline();

        var first = await _service.CreateAsync(1, ValidRequest(DateTime.UtcNow.AddDays(30)), 1);

        var renewal = await _service.RenewAsync(first.Id, new RiskAcceptanceRequest
        {
            BusinessJustification = "The replacement vendor is not certified until Q1.",
            ExpiresAt = DateTime.UtcNow.AddDays(120)
        }, 1);

        Assert.Equal(first.Id, renewal.RenewedFromId);
        Assert.Equal(RiskAcceptanceStatus.Active, renewal.Status);

        await using var db = OpenContext();
        var predecessor = db.RiskAcceptances.Single(a => a.Id == first.Id);

        // Renewed, not edited: the record of what was approved and until when has to survive.
        Assert.Equal(RiskAcceptanceStatus.Renewed, predecessor.Status);
        Assert.True(predecessor.ExpiresAt < renewal.ExpiresAt);
    }

    [Fact]
    public async Task TestRenewalNeedsAFreshJustification()
    {
        SeedBaseline();

        var first = await _service.CreateAsync(1, ValidRequest(), 1);

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _service.RenewAsync(first.Id, new RiskAcceptanceRequest
            {
                BusinessJustification = null, ExpiresAt = DateTime.UtcNow.AddDays(90)
            }, 1));
    }

    [Fact]
    public async Task TestARevokedAcceptanceCannotBeRenewed()
    {
        SeedBaseline();

        var first = await _service.CreateAsync(1, ValidRequest(), 1);
        await _service.RevokeAsync(first.Id, "The compensating control was removed.", 1);

        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            _service.RenewAsync(first.Id, ValidRequest(), 1));
    }

    // --- revocation -------------------------------------------------------------------------------

    [Fact]
    public async Task TestRevocationNeedsAReason()
    {
        SeedBaseline();

        var first = await _service.CreateAsync(1, ValidRequest(), 1);

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _service.RevokeAsync(first.Id, "  ", 1));
    }

    [Fact]
    public async Task TestRevocationFlagsTheRiskForReview()
    {
        SeedBaseline();

        var first = await _service.CreateAsync(1, ValidRequest(), 1);
        await _service.RevokeAsync(first.Id, "The compensating control was removed.", 1);

        await using var db = OpenContext();
        var risk = db.Risks.Single(r => r.Id == 1);

        Assert.True(risk.ReviewRequested);
        Assert.Contains("revoked", risk.ReviewRequestedReason!);
    }

    [Fact]
    public async Task TestRevokingTwiceIsRefused()
    {
        SeedBaseline();

        var first = await _service.CreateAsync(1, ValidRequest(), 1);
        await _service.RevokeAsync(first.Id, "reason", 1);

        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            _service.RevokeAsync(first.Id, "reason", 1));
    }

    // --- expiry -----------------------------------------------------------------------------------

    [Fact]
    public async Task TestExpiryMarksTheRowAndFlagsTheRisk()
    {
        SeedBaseline();

        var acceptance = await _service.CreateAsync(1, ValidRequest(DateTime.UtcNow.AddDays(1)), 1);

        var result = await _service.ProcessExpiryAsync(DateTime.UtcNow.AddDays(2));

        Assert.Single(result.Expired);

        await using var db = OpenContext();
        Assert.Equal(RiskAcceptanceStatus.Expired,
            db.RiskAcceptances.Single(a => a.Id == acceptance.Id).Status);
        Assert.True(db.Risks.Single(r => r.Id == 1).ReviewRequested);
    }

    [Fact]
    public async Task TestWarningsFireAtThirtyThenSevenDaysAndNeverTwice()
    {
        SeedBaseline();

        await _service.CreateAsync(1, ValidRequest(DateTime.UtcNow.AddDays(40)), 1);

        // T-25: inside the 30-day threshold.
        var first = await _service.ProcessExpiryAsync(DateTime.UtcNow.AddDays(15));
        Assert.Equal(30, Assert.Single(first.Warnings).DaysBefore);

        // Same threshold again: the job runs daily and must not repeat itself.
        var second = await _service.ProcessExpiryAsync(DateTime.UtcNow.AddDays(16));
        Assert.Empty(second.Warnings);

        // T-5: the smaller threshold fires once.
        var third = await _service.ProcessExpiryAsync(DateTime.UtcNow.AddDays(35));
        Assert.Equal(7, Assert.Single(third.Warnings).DaysBefore);

        var fourth = await _service.ProcessExpiryAsync(DateTime.UtcNow.AddDays(36));
        Assert.Empty(fourth.Warnings);
    }

    [Fact]
    public async Task TestExpiryIgnoresFindingLevelAcceptances()
    {
        SeedBaseline();

        Seed(ctx => ctx.RiskAcceptances.Add(new RiskAcceptance
        {
            Id = 500, RiskId = null, Name = "Finding suppression", AuthorizingManagerId = 1,
            BusinessJustification = "Compensating control.", StartDate = DateTime.UtcNow.AddDays(-10),
            ExpiresAt = DateTime.UtcNow.AddDays(-1), Status = RiskAcceptanceStatus.Active,
            CreatedAt = DateTime.UtcNow.AddDays(-10)
        }));

        var result = await _service.ProcessExpiryAsync(DateTime.UtcNow);

        // FindingLifecycleService owns that row; expiring it here would process it twice.
        Assert.Empty(result.Expired);

        await using var db = OpenContext();
        Assert.Equal(RiskAcceptanceStatus.Active, db.RiskAcceptances.Single(a => a.Id == 500).Status);
    }

    [Fact]
    public async Task TestExpiringListsOnlyRiskAcceptancesInsideTheWindow()
    {
        SeedBaseline();

        await _service.CreateAsync(1, ValidRequest(DateTime.UtcNow.AddDays(10)), 1);

        Assert.Single(await _service.GetExpiringAsync(30));
        Assert.Empty(await _service.GetExpiringAsync(5));
        await Assert.ThrowsAsync<InvalidParameterException>(() => _service.GetExpiringAsync(-1));
    }

    [Fact]
    public async Task TestGetActiveIgnoresExpiredAndRevokedRows()
    {
        SeedBaseline();

        var acceptance = await _service.CreateAsync(1, ValidRequest(), 1);
        Assert.NotNull(await _service.GetActiveAsync(1));

        await _service.RevokeAsync(acceptance.Id, "reason", 1);
        Assert.Null(await _service.GetActiveAsync(1));
    }
}

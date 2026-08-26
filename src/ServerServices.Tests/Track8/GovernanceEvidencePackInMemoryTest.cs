using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track8;

/// <summary>
/// The auditor evidence pack (Track 8 milestones 8.4.2 and 8.6.5).
///
/// The thing worth testing here is not the formatting — it is *what counts as evidence*. Three
/// decisions are load-bearing and each one is asserted below, because each has a plausible-looking
/// wrong version:
///
/// * an acceptance granted before the period and still in force belongs in the pack (the wrong
///   version filters on <c>created_at</c> and omits every standing exception);
/// * a campaign item nobody decided belongs in the pack (the wrong version filters on
///   <c>decided_at</c>, so an unreviewed quarter reads as a completed review);
/// * a truncated change list has to say so (the wrong version returns a full page and lets the
///   reader assume it is complete).
/// </summary>
[TestSubject(typeof(AuditTrailService))]
public class GovernanceEvidencePackInMemoryTest : InMemoryServiceTestBase
{
    private readonly IAuditTrailService _trail;

    private static readonly DateTime PeriodStart = new(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime PeriodEnd = new(2026, 6, 30, 23, 59, 59, DateTimeKind.Utc);

    public GovernanceEvidencePackInMemoryTest()
    {
        _trail = GetService<IAuditTrailService>();
    }

    private static Risk NewRisk(int id, int? entityId = null) => new()
    {
        Id = id, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R-{id}",
        Assessment = string.Empty, Notes = string.Empty,
        RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
        EntityId = entityId,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static User NewUser(int id, string name, string login) => new()
    {
        Value = id, Name = name, Login = login, Email = $"{login}@example.test",
        Enabled = true, Lockout = 0, Type = "local", Salt = "s", Password = new byte[60],
        Admin = false, RoleId = 1
    };

    private void SeedPeople()
    {
        SeedUnscoped(ctx =>
        {
            ctx.Users.Add(NewUser(10, "Ana Approver", "ana"));
            ctx.Users.Add(NewUser(11, "Bob Reviewer", "bob"));
            ctx.Users.Add(NewUser(12, "Cleo Countersigner", "cleo"));
        });
    }

    [Fact]
    public async Task TestThePackStatesItsOwnScopeAndRequester()
    {
        SeedPeople();

        var pack = await _trail.GetEvidencePackAsync(null, PeriodStart, PeriodEnd, "Ana (ana, #10)");

        Assert.Null(pack.EntityId);
        Assert.Equal("(all entities)", pack.EntityName);
        Assert.Equal(PeriodStart, pack.FromUtc);
        Assert.Equal(PeriodEnd, pack.ToUtc);
        Assert.Equal("Ana (ana, #10)", pack.RequestedBy);
        Assert.NotEqual(default, pack.GeneratedAtUtc);
    }

    /// <summary>
    /// An exception granted last year and still in force is the single most relevant fact about an
    /// entity's posture. A pack that filtered on creation date would omit it and evidence the wrong
    /// thing.
    /// </summary>
    [Fact]
    public async Task TestAnAcceptanceGrantedBeforeThePeriodAndStillInForceIsIncluded()
    {
        SeedPeople();

        SeedUnscoped(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskAcceptances.Add(new RiskAcceptance
            {
                Id = 1, RiskId = 1, Name = "Legacy exception", AuthorizingManagerId = 10,
                StartDate = new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                ExpiresAt = new DateTime(2026, 12, 31, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2025, 9, 1, 0, 0, 0, DateTimeKind.Utc),
                Status = RiskAcceptanceStatus.Active,
                BusinessJustification = "Vendor contract runs to year end"
            });
        });

        var pack = await _trail.GetEvidencePackAsync(null, PeriodStart, PeriodEnd, "x");

        var acceptance = Assert.Single(pack.Acceptances);
        Assert.Equal("Legacy exception", acceptance.Name);
        Assert.Equal("Active", acceptance.Status);
        Assert.Contains("Ana Approver", acceptance.AuthorizingManager);
        Assert.Equal("Risk 1", acceptance.RiskSubject);
        Assert.False(acceptance.FromCampaign);
    }

    /// <summary>An acceptance that had already expired before the window opens is out of scope.</summary>
    [Fact]
    public async Task TestAnAcceptanceThatEndedBeforeThePeriodIsExcluded()
    {
        SeedPeople();

        SeedUnscoped(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskAcceptances.Add(new RiskAcceptance
            {
                Id = 1, RiskId = 1, Name = "Old and revoked", AuthorizingManagerId = 10,
                StartDate = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                ExpiresAt = new DateTime(2025, 6, 1, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                RevokedAt = new DateTime(2025, 3, 1, 0, 0, 0, DateTimeKind.Utc),
                RevokedById = 10, RevocationReason = "Superseded",
                Status = RiskAcceptanceStatus.Revoked
            });
        });

        var pack = await _trail.GetEvidencePackAsync(null, PeriodStart, PeriodEnd, "x");

        Assert.Empty(pack.Acceptances);
    }

    [Fact]
    public async Task TestARevokedAcceptanceCarriesWhoRevokedItAndWhy()
    {
        SeedPeople();

        SeedUnscoped(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskAcceptances.Add(new RiskAcceptance
            {
                Id = 1, RiskId = 1, Name = "Withdrawn exception", AuthorizingManagerId = 10,
                StartDate = new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc),
                ExpiresAt = new DateTime(2026, 10, 5, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 4, 5, 0, 0, 0, DateTimeKind.Utc),
                RevokedAt = new DateTime(2026, 5, 5, 0, 0, 0, DateTimeKind.Utc),
                RevokedById = 11, RevocationReason = "Control implemented",
                Status = RiskAcceptanceStatus.Revoked,
                ResidualScoreSnapshot = 3.5
            });
        });

        var acceptance = Assert.Single(
            (await _trail.GetEvidencePackAsync(null, PeriodStart, PeriodEnd, "x")).Acceptances);

        Assert.Contains("Bob Reviewer", acceptance.RevokedBy);
        Assert.Equal("Control implemented", acceptance.RevocationReason);
        Assert.Equal(3.5, acceptance.ResidualScoreSnapshot);
    }

    /// <summary>
    /// The 8.6.5 link: an acceptance created by a business reviewer's decision is marked as such, so
    /// an auditor can tell a manager's exception from a portal one without cross-referencing.
    /// </summary>
    [Fact]
    public async Task TestAnAcceptanceThatCameFromACampaignDecisionIsMarked()
    {
        SeedPeople();

        SeedUnscoped(ctx =>
        {
            ctx.Risks.Add(NewRisk(1, entityId: 5));
            ctx.RiskAcceptances.Add(new RiskAcceptance
            {
                Id = 1, RiskId = 1, Name = "Accepted in Q2 review", AuthorizingManagerId = 10,
                StartDate = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
                ExpiresAt = new DateTime(2027, 4, 10, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
                Status = RiskAcceptanceStatus.Active
            });
            ctx.RiskReviewCampaigns.Add(new RiskReviewCampaign
            {
                Id = 1, EntityId = 5, Name = "Q2 2026",
                PeriodStart = PeriodStart, PeriodEnd = PeriodEnd,
                DueDate = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                Status = RiskReviewCampaignStatus.Completed,
                CreatedAt = PeriodStart
            });
            ctx.RiskReviewCampaignItems.Add(new RiskReviewCampaignItem
            {
                Id = 1, CampaignId = 1, RiskId = 1, Rank = 1,
                Decision = RiskReviewDecision.Accepted, RiskAcceptanceId = 1,
                DecidedById = 11, DecidedAt = new DateTime(2026, 4, 10, 0, 0, 0, DateTimeKind.Utc),
                CreatedAt = PeriodStart
            });
        });

        var pack = await _trail.GetEvidencePackAsync(null, PeriodStart, PeriodEnd, "x");

        Assert.True(Assert.Single(pack.Acceptances).FromCampaign);

        var decision = Assert.Single(pack.CampaignDecisions);
        Assert.Equal("Q2 2026", decision.CampaignName);
        Assert.Equal("Accepted", decision.Decision);
        Assert.Equal(1, decision.Rank);
        Assert.Equal(1, decision.RiskAcceptanceId);
        Assert.Contains("Bob Reviewer", decision.DecidedBy);
    }

    /// <summary>
    /// A campaign item nobody decided is itself the finding. Selecting on the decision date instead
    /// of the campaign period would drop it, and a quarter nobody reviewed would read as a completed
    /// review with no risks in it.
    /// </summary>
    [Fact]
    public async Task TestAnUndecidedCampaignItemIsStillEvidence()
    {
        SeedPeople();

        SeedUnscoped(ctx =>
        {
            ctx.Risks.Add(NewRisk(1, entityId: 5));
            ctx.RiskReviewCampaigns.Add(new RiskReviewCampaign
            {
                Id = 1, EntityId = 5, Name = "Q2 2026",
                PeriodStart = PeriodStart, PeriodEnd = PeriodEnd,
                DueDate = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                Status = RiskReviewCampaignStatus.Overdue, CreatedAt = PeriodStart
            });
            ctx.RiskReviewCampaignItems.Add(new RiskReviewCampaignItem
            {
                Id = 1, CampaignId = 1, RiskId = 1, Decision = RiskReviewDecision.Pending,
                CreatedAt = PeriodStart
            });
        });

        var decision = Assert.Single(
            (await _trail.GetEvidencePackAsync(null, PeriodStart, PeriodEnd, "x")).CampaignDecisions);

        Assert.Equal("Pending", decision.Decision);
        Assert.Equal("Overdue", decision.CampaignStatus);
        Assert.Null(decision.DecidedBy);
        Assert.Null(decision.DecidedAt);
    }

    [Fact]
    public async Task TestAnEscalationNamesTheApproverItWentTo()
    {
        SeedPeople();

        SeedUnscoped(ctx =>
        {
            ctx.Risks.Add(NewRisk(1, entityId: 5));
            ctx.RiskReviewCampaigns.Add(new RiskReviewCampaign
            {
                Id = 1, EntityId = 5, Name = "Q2 2026",
                PeriodStart = PeriodStart, PeriodEnd = PeriodEnd,
                DueDate = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                Status = RiskReviewCampaignStatus.Open, CreatedAt = PeriodStart
            });
            ctx.RiskReviewCampaignItems.Add(new RiskReviewCampaignItem
            {
                Id = 1, CampaignId = 1, RiskId = 1, Decision = RiskReviewDecision.Escalated,
                DecidedById = 11, DecidedAt = new DateTime(2026, 5, 2, 0, 0, 0, DateTimeKind.Utc),
                EscalatedToId = 12, DecisionNotes = "Above our appetite", CreatedAt = PeriodStart
            });
        });

        var decision = Assert.Single(
            (await _trail.GetEvidencePackAsync(null, PeriodStart, PeriodEnd, "x")).CampaignDecisions);

        Assert.Equal("Escalated", decision.Decision);
        Assert.Contains("Cleo Countersigner", decision.EscalatedTo);
        Assert.Equal("Above our appetite", decision.DecisionNotes);
    }

    /// <summary>
    /// A campaign for a different entity than the one asked about must not appear. The pack is a
    /// per-entity disclosure and leaking another business unit's review into it is the same class of
    /// mistake as a missing query filter.
    /// </summary>
    [Fact]
    public async Task TestThePackForOneEntityExcludesAnotherEntitysCampaign()
    {
        SeedPeople();

        SeedUnscoped(ctx =>
        {
            ctx.Entities.Add(new Entity
            {
                Id = 5, DefinitionName = "organization", DefinitionVersion = "1",
                Created = PeriodStart, Updated = PeriodStart, CreatedBy = 10, UpdatedBy = 10,
                Status = "active"
            });
            ctx.EntitiesProperties.Add(new EntitiesProperty
            {
                Id = 1, Entity = 5, Type = "name", Value = "Retail Bank",
                Name = "name", OldValue = ""
            });

            ctx.Risks.Add(NewRisk(1, entityId: 5));
            ctx.Risks.Add(NewRisk(2, entityId: 6));

            foreach (var entityId in new[] { 5, 6 })
            {
                ctx.RiskReviewCampaigns.Add(new RiskReviewCampaign
                {
                    Id = entityId, EntityId = entityId, Name = $"Q2 2026 unit {entityId}",
                    PeriodStart = PeriodStart, PeriodEnd = PeriodEnd,
                    DueDate = new DateTime(2026, 7, 15, 0, 0, 0, DateTimeKind.Utc),
                    Status = RiskReviewCampaignStatus.Open, CreatedAt = PeriodStart
                });
                ctx.RiskReviewCampaignItems.Add(new RiskReviewCampaignItem
                {
                    Id = entityId, CampaignId = entityId,
                    RiskId = entityId == 5 ? 1 : 2,
                    Decision = RiskReviewDecision.Accepted, CreatedAt = PeriodStart
                });
            }
        });

        var pack = await _trail.GetEvidencePackAsync(5, PeriodStart, PeriodEnd, "x");

        Assert.Equal("Retail Bank", pack.EntityName);
        Assert.Equal(5, Assert.Single(pack.CampaignDecisions).CampaignId);
    }

    /// <summary>The entity name lives in a property row; a missing one degrades rather than throws.</summary>
    [Fact]
    public async Task TestAnEntityWithNoNamePropertyFallsBackToItsId()
    {
        SeedPeople();

        var pack = await _trail.GetEvidencePackAsync(77, PeriodStart, PeriodEnd, "x");

        Assert.Equal("#77", pack.EntityName);
    }

    [Fact]
    public async Task TestACountersignedReviewCarriesBothSignatories()
    {
        SeedPeople();

        SeedUnscoped(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.MgmtReviews.Add(new MgmtReview
            {
                Id = 1, RiskId = 1, Reviewer = 11,
                SubmissionDate = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc),
                Review = 1, NextStep = 1, Comments = "Treat, then re-rate",
                NextReview = new DateOnly(2026, 11, 10),
                RequiresCountersignature = true, SecondReviewerId = 12,
                SecondReviewAt = new DateTime(2026, 5, 12, 0, 0, 0, DateTimeKind.Utc)
            });
        });

        var review = Assert.Single(
            (await _trail.GetEvidencePackAsync(null, PeriodStart, PeriodEnd, "x")).Reviews);

        Assert.Contains("Bob Reviewer", review.Reviewer);
        Assert.Contains("Cleo Countersigner", review.SecondReviewer);
        Assert.True(review.RequiresCountersignature);
        Assert.Equal("Treat, then re-rate", review.Comments);
    }

    /// <summary>
    /// A deliberate segregation-of-duties break has to survive into the pack. It is the row an
    /// auditor is looking for, and recording it without exporting it would be pointless.
    /// </summary>
    [Fact]
    public async Task TestABreakGlassOverrideReasonReachesTheEvidence()
    {
        SeedPeople();

        SeedUnscoped(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.MgmtReviews.Add(new MgmtReview
            {
                Id = 1, RiskId = 1, Reviewer = 11,
                SubmissionDate = new DateTime(2026, 5, 10, 0, 0, 0, DateTimeKind.Utc),
                Review = 1, NextStep = 1, Comments = "Reviewed",
                NextReview = new DateOnly(2026, 11, 10),
                SegregationOverrideReason = "Sole approver on site during incident"
            });
        });

        var review = Assert.Single(
            (await _trail.GetEvidencePackAsync(null, PeriodStart, PeriodEnd, "x")).Reviews);

        Assert.Equal("Sole approver on site during incident", review.SegregationOverrideReason);
    }

    [Fact]
    public async Task TestAReviewOutsideThePeriodIsExcluded()
    {
        SeedPeople();

        SeedUnscoped(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.MgmtReviews.Add(new MgmtReview
            {
                Id = 1, RiskId = 1, Reviewer = 11,
                SubmissionDate = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                Review = 1, NextStep = 1, Comments = "Too early",
                NextReview = new DateOnly(2026, 8, 1)
            });
        });

        Assert.Empty((await _trail.GetEvidencePackAsync(null, PeriodStart, PeriodEnd, "x")).Reviews);
    }

    /// <summary>
    /// A cut-off change list says so. Silently returning the limit reads as a complete trail, which
    /// is the difference between evidence and a misleading document.
    /// </summary>
    [Fact]
    public async Task TestATruncatedChangeListIsFlagged()
    {
        SeedPeople();

        SeedUnscoped(ctx => ctx.Risks.Add(NewRisk(1)));

        // Every save through the interceptor writes trail rows; three risk edits are enough to exceed
        // a limit of two.
        for (var i = 0; i < 3; i++)
        {
            using var ctx = OpenContext();
            var risk = ctx.Risks.First(r => r.Id == 1);
            risk.Subject = $"Renamed {i}";
            ctx.SaveChanges();
        }

        var wide = await _trail.GetEvidencePackAsync(null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow.AddDays(1), "x");

        Assert.False(wide.ChangesTruncated);
        Assert.True(wide.Changes.Count >= 3);

        var clipped = await _trail.GetEvidencePackAsync(null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow.AddDays(1), "x",
            changeLimit: 2);

        Assert.True(clipped.ChangesTruncated);
        Assert.Equal(2, clipped.Changes.Count);
    }

    /// <summary>
    /// The change rows name the actor, not just a user id. This is what makes the pack readable
    /// without a second query against the user table.
    /// </summary>
    [Fact]
    public async Task TestChangeRowsCarryTheActorAndTheOldAndNewValues()
    {
        SeedPeople();

        SeedUnscoped(ctx => ctx.Risks.Add(NewRisk(1)));

        using (var ctx = OpenContext())
        {
            ctx.AuditActor = "ana";
            var risk = ctx.Risks.First(r => r.Id == 1);
            risk.Status = "Mitigation Planned";
            ctx.SaveChanges();
        }

        var pack = await _trail.GetEvidencePackAsync(null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow.AddDays(1), "x");

        var change = pack.Changes.Single(c => c.Field == nameof(Risk.Status));

        Assert.Equal("ana", change.Actor);
        Assert.Equal("New", change.OldValue);
        Assert.Equal("Mitigation Planned", change.NewValue);
        Assert.Equal(nameof(Risk), change.EntityType);
        Assert.Equal(1, change.EntityId);
    }

    /// <summary>Sections are ordered oldest first: the pack reads as a chronology.</summary>
    [Fact]
    public async Task TestTheChangeListReadsOldestFirst()
    {
        SeedPeople();

        SeedUnscoped(ctx => ctx.Risks.Add(NewRisk(1)));

        for (var i = 0; i < 3; i++)
        {
            using var ctx = OpenContext();
            var risk = ctx.Risks.First(r => r.Id == 1);
            risk.Subject = $"Renamed {i}";
            ctx.SaveChanges();
        }

        var changes = (await _trail.GetEvidencePackAsync(null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), DateTime.UtcNow.AddDays(1),
            "x")).Changes;

        for (var i = 1; i < changes.Count; i++)
            Assert.True(changes[i].OccurredAt >= changes[i - 1].OccurredAt);
    }

    [Fact]
    public async Task TestAnEmptyPeriodProducesAnEmptyPackRatherThanThrowing()
    {
        SeedPeople();

        var pack = await _trail.GetEvidencePackAsync(null,
            new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2020, 2, 1, 0, 0, 0, DateTimeKind.Utc), "x");

        Assert.Empty(pack.Acceptances);
        Assert.Empty(pack.Reviews);
        Assert.Empty(pack.CampaignDecisions);
        Assert.Empty(pack.Changes);
        Assert.False(pack.ChangesTruncated);
    }
}

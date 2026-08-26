using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Auditing;
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
/// Track 8 milestone 8.4 — the field-level audit trail.
///
/// The question this exists to answer is the one the existing JSON <c>audit</c> table cannot:
/// "who lowered this risk's impact from 4 to 2, and when". One row per changed field, indexed by
/// entity and time, so it is a query rather than a Serilog grep.
/// </summary>
[TestSubject(typeof(GovernanceAuditInterceptor))]
public class GovernanceAuditTrailInMemoryTest : InMemoryServiceTestBase
{
    private readonly IAuditTrailService _trail;

    public GovernanceAuditTrailInMemoryTest()
    {
        _trail = GetService<IAuditTrailService>();
    }

    private static Risk NewRisk(int id) => new()
    {
        Id = id, Status = "New", Subject = $"Risk {id}", ReferenceId = $"R-{id}",
        Assessment = string.Empty, Notes = string.Empty,
        RiskCatalogMapping = string.Empty, ThreatCatalogMapping = string.Empty,
        SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    [Fact]
    public void TestTheAuditedScopeIsAnAllowlistCoveringTheGovernanceAggregate()
    {
        // Not a global trail: one over a vulnerability import would write millions of rows nobody
        // reads. These are the records an auditor samples when testing ISO 27001 6.1.3.
        Assert.Contains(nameof(Risk), GovernanceAuditInterceptor.AuditedTypes);
        Assert.Contains(nameof(RiskScoring), GovernanceAuditInterceptor.AuditedTypes);
        Assert.Contains(nameof(Mitigation), GovernanceAuditInterceptor.AuditedTypes);
        Assert.Contains(nameof(MitigationTask), GovernanceAuditInterceptor.AuditedTypes);
        Assert.Contains(nameof(MgmtReview), GovernanceAuditInterceptor.AuditedTypes);
        Assert.Contains(nameof(RiskAcceptance), GovernanceAuditInterceptor.AuditedTypes);
        Assert.Contains(nameof(RiskAppetite), GovernanceAuditInterceptor.AuditedTypes);

        Assert.DoesNotContain(nameof(Vulnerability), GovernanceAuditInterceptor.AuditedTypes);
    }

    [Fact]
    public async Task TestCreatingARiskWritesOneSummaryRowNotOnePerField()
    {
        Seed(ctx => ctx.Risks.Add(NewRisk(1)));

        var rows = await _trail.GetForRecordAsync(nameof(Risk), 1);

        // On a create every field "changed"; thirty rows saying so make the trail harder to read,
        // not more complete.
        var row = Assert.Single(rows);
        Assert.Equal(AuditLogAction.Create, row.Action);
        Assert.Equal(string.Empty, row.Field);
        Assert.Contains("Subject=Risk 1", row.NewValue);
    }

    [Fact]
    public async Task TestChangingAFieldRecordsTheOldAndNewValue()
    {
        Seed(ctx => ctx.Risks.Add(NewRisk(1)));

        await using (var db = OpenContext())
        {
            var risk = db.Risks.Single(r => r.Id == 1);
            risk.Status = "Mitigation Planned";
            risk.Subject = "Renamed";
            await db.SaveChangesAsync();
        }

        var rows = await _trail.GetForRecordAsync(nameof(Risk), 1);

        var status = Assert.Single(rows, r => r.Field == nameof(Risk.Status));
        Assert.Equal("New", status.OldValue);
        Assert.Equal("Mitigation Planned", status.NewValue);
        Assert.Equal(AuditLogAction.Update, status.Action);

        // One save, one correlation id — a multi-field edit reads back as one action.
        var subject = Assert.Single(rows, r => r.Field == nameof(Risk.Subject));
        Assert.Equal(status.CorrelationId, subject.CorrelationId);
    }

    /// <summary>
    /// The churn columns are excluded. A trail whose signal is buried under `last_update` changes is
    /// a trail nobody reads.
    /// </summary>
    [Fact]
    public async Task TestChurnColumnsAreNotRecorded()
    {
        Seed(ctx => ctx.Risks.Add(NewRisk(1)));

        await using (var db = OpenContext())
        {
            var risk = db.Risks.Single(r => r.Id == 1);
            risk.LastUpdate = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var rows = await _trail.GetForRecordAsync(nameof(Risk), 1);

        Assert.DoesNotContain(rows, r => r.Field == nameof(Risk.LastUpdate));
    }

    [Fact]
    public async Task TestAWriteWithNoUserIsAttributedToTheSystemRatherThanToNobody()
    {
        Seed(ctx => ctx.Risks.Add(NewRisk(1)));

        var row = Assert.Single(await _trail.GetForRecordAsync(nameof(Risk), 1));

        Assert.Equal(DAL.Context.AuditableContext.SystemActor, row.Actor);
        Assert.Null(row.UserId);
    }

    [Fact]
    public async Task TestTheAggregateTrailReachesScoringsMitigationsReviewsAndAcceptances()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(new User
            {
                Value = 1, Name = "cro", Login = "cro", Enabled = true, Type = "local", Salt = "s",
                Password = Encoding.UTF8.GetBytes("p"), Email = "cro@x.test"
            });
            ctx.Risks.Add(NewRisk(1));
            ctx.RiskScorings.Add(new RiskScoring
                { Id = 1, ScoringMethod = 1, CalculatedRisk = 5f, ClassicImpact = 3, ClassicLikelihood = 3 });
            ctx.Mitigations.Add(new Mitigation
            {
                Id = 1, RiskId = 1, PlanningStrategy = 1, MitigationEffort = 1, MitigationCost = 1,
                MitigationOwner = 1, SubmittedBy = 1, MitigationPercent = 10,
                CurrentSolution = string.Empty, SecurityRequirements = string.Empty,
                SecurityRecommendations = string.Empty,
                SubmissionDate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                LastUpdate = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
                PlanningDate = new DateOnly(2026, 6, 1)
            });
            ctx.RiskAcceptances.Add(new RiskAcceptance
            {
                Id = 1, RiskId = 1, Name = "Exception", AuthorizingManagerId = 1,
                BusinessJustification = "Compensating control.",
                StartDate = DateTime.UtcNow, ExpiresAt = DateTime.UtcNow.AddDays(30),
                Status = RiskAcceptanceStatus.Active, CreatedAt = DateTime.UtcNow
            });
        });

        var rows = await _trail.GetForRiskAsync(1);
        var types = rows.Select(r => r.EntityType).Distinct().ToList();

        // "Who changed what on this risk" has to mean the aggregate, not one table — somebody asking
        // the question does not know how it is split up.
        Assert.Contains(nameof(Risk), types);
        Assert.Contains(nameof(RiskScoring), types);
        Assert.Contains(nameof(Mitigation), types);
        Assert.Contains(nameof(RiskAcceptance), types);
    }

    [Fact]
    public async Task TestADeletionIsRecordedWithWhatWasThere()
    {
        Seed(ctx => ctx.Risks.Add(NewRisk(1)));

        await using (var db = OpenContext())
        {
            db.Risks.Remove(db.Risks.Single(r => r.Id == 1));
            await db.SaveChangesAsync();
        }

        var rows = await _trail.GetForRecordAsync(nameof(Risk), 1);

        var deletion = Assert.Single(rows, r => r.Action == AuditLogAction.Delete);
        Assert.Contains("Subject=Risk 1", deletion.OldValue);
    }

    [Fact]
    public async Task TestRetentionRemovesRowsPastTheConfiguredWindowAndKeepsTheRest()
    {
        Seed(ctx =>
        {
            ctx.Settings.Add(new Setting { Name = AuditTrailService.RetentionSetting, Value = "30" });
            ctx.AuditLogs.Add(new AuditLog
            {
                Id = 1, EntityType = nameof(Risk), EntityId = 1, Field = "Status", Action = AuditLogAction.Update,
                Actor = "system", OccurredAt = DateTime.UtcNow.AddDays(-90)
            });
            ctx.AuditLogs.Add(new AuditLog
            {
                Id = 2, EntityType = nameof(Risk), EntityId = 1, Field = "Status", Action = AuditLogAction.Update,
                Actor = "system", OccurredAt = DateTime.UtcNow.AddDays(-5)
            });
        });

        var removed = await _trail.ApplyRetentionAsync(DateTime.UtcNow);

        Assert.Equal(1, removed);

        await using var db = OpenContext();
        Assert.Single(db.AuditLogs.Where(a => a.EntityType == nameof(Risk)).ToList());
    }

    [Fact]
    public async Task TestRetentionUsesTheDefaultWindowWhenNothingIsConfigured()
    {
        Seed(ctx => ctx.AuditLogs.Add(new AuditLog
        {
            Id = 1, EntityType = nameof(Risk), EntityId = 1, Field = "Status",
            Action = AuditLogAction.Update, Actor = "system",
            OccurredAt = DateTime.UtcNow.AddDays(-(AuditTrailService.DefaultRetentionDays + 10))
        }));

        Assert.Equal(1, await _trail.ApplyRetentionAsync(DateTime.UtcNow));
    }

    [Fact]
    public async Task TestTheEvidencePeriodFiltersByTime()
    {
        Seed(ctx =>
        {
            ctx.Risks.Add(NewRisk(1));
            ctx.AuditLogs.Add(new AuditLog
            {
                Id = 900, EntityType = nameof(Risk), EntityId = 1, Field = "Status",
                Action = AuditLogAction.Update, Actor = "admin",
                OccurredAt = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            });
        });

        var inside = await _trail.GetForEntityPeriodAsync(null,
            new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc));

        var outside = await _trail.GetForEntityPeriodAsync(null,
            new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.Contains(inside, r => r.Id == 900);
        Assert.DoesNotContain(outside, r => r.Id == 900);
    }
}

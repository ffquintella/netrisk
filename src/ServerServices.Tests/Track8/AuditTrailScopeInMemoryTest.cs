using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Auditing;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track8;

/// <summary>
/// The field-level trail honours the caller's entity scope. <c>audit_logs</c> has no entity id, so before the fix
/// <c>GetForRecordAsync</c> served any record's history to any reader — a reader scoped to unit A read the field changes
/// of unit B's risks, scorings, mitigations, reviews, acceptances and indicators — and <c>GetForRiskAsync</c> matched the
/// risk's and its scoring's rows on the id alone. Both now look the record up through the entity-scoped set of its type
/// first (<see cref="AuditTrailService.RecordScopes"/>), and a record the caller cannot see is not found.
/// </summary>
[TestSubject(typeof(AuditTrailService))]
public class AuditTrailScopeInMemoryTest : InMemoryServiceTestBase
{
    private const int UnitA = 100;
    private const int UnitB = 200;

    /// <summary>Every seeded record of unit A has this id, and every record of unit B the other one.</summary>
    private const int InA = 1;
    private const int InB = 2;

    private static readonly DateTime Created = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IAuditTrailService _trail;

    public AuditTrailScopeInMemoryTest()
    {
        _trail = GetService<IAuditTrailService>();

        // Planted unscoped, as an administrator would have created them; the interceptor writes each one's Create row.
        SeedUnscoped(ctx =>
        {
            ctx.Users.Add(new User
            {
                Value = 1, Name = "cro", Login = "cro", Enabled = true, Type = "local", Salt = "s",
                Password = Encoding.UTF8.GetBytes("p"), Email = "cro@x.test"
            });

            foreach (var (id, unit) in new[] { (InA, UnitA), (InB, UnitB) })
            {
                ctx.Risks.Add(new Risk
                {
                    Id = id, EntityId = unit, Status = "New", Subject = $"Unit {unit} risk", ReferenceId = $"R-{id}",
                    Assessment = string.Empty, Notes = string.Empty, RiskCatalogMapping = string.Empty,
                    ThreatCatalogMapping = string.Empty, SubmissionDate = Created, LastUpdate = Created
                });
                ctx.RiskScorings.Add(new RiskScoring
                    { Id = id, ScoringMethod = 1, CalculatedRisk = 5f, ClassicImpact = 3, ClassicLikelihood = 3 });
                ctx.Mitigations.Add(new Mitigation
                {
                    Id = id, RiskId = id, PlanningStrategy = 1, MitigationEffort = 1, MitigationCost = 1,
                    MitigationOwner = 1, SubmittedBy = 1, MitigationPercent = 10, CurrentSolution = string.Empty,
                    SecurityRequirements = string.Empty, SecurityRecommendations = string.Empty,
                    SubmissionDate = Created, LastUpdate = Created, PlanningDate = new DateOnly(2026, 6, 1)
                });
                ctx.MitigationTasks.Add(new MitigationTask
                    { Id = id, MitigationId = id, Title = $"Unit {unit} task", CreatedAt = Created });
                ctx.MgmtReviews.Add(new MgmtReview
                {
                    Id = id, RiskId = id, SubmissionDate = Created, Review = 1, Reviewer = 1, NextStep = 1,
                    Comments = $"Unit {unit} review", NextReview = new DateOnly(2026, 12, 1)
                });
                ctx.RiskAcceptances.Add(new RiskAcceptance
                {
                    Id = id, RiskId = id, EntityId = unit, Name = $"Unit {unit} exception", AuthorizingManagerId = 1,
                    BusinessJustification = "Compensating control.", StartDate = Created, ExpiresAt = Created.AddDays(90),
                    Status = RiskAcceptanceStatus.Active, CreatedAt = Created
                });
                ctx.Kris.Add(new Kri
                {
                    Id = id, EntityId = unit, Name = $"Unit {unit} indicator", Source = "manual", Unit = "%",
                    ToleranceThreshold = 5m, ToleranceRationale = "Board appetite.", MaxReadingAgeDays = 30,
                    CreatedAt = Created
                });
            }
        });
    }

    /// <summary>The older types the generic reader serves, one owned by each unit.</summary>
    public static TheoryData<string> ScopedTypes() =>
    [
        nameof(Risk), nameof(RiskScoring), nameof(Mitigation), nameof(MitigationTask), nameof(MgmtReview),
        nameof(RiskAcceptance), nameof(Kri)
    ];

    // --- GetForRecordAsync --------------------------------------------------------------------------------------------

    /// <summary>Regression: before the fix this returned unit B's rows to a reader scoped to unit A.</summary>
    [Theory]
    [MemberData(nameof(ScopedTypes))]
    public async Task TestAScopedReaderIsRefusedAnotherUnitsRecordTrail(string entityType)
    {
        Assert.NotEmpty(await _trail.GetForRecordAsync(entityType, InB));

        ScopeTo(UnitA);

        var ex = await Assert.ThrowsAsync<DataNotFoundException>(() => _trail.GetForRecordAsync(entityType, InB));
        Assert.Equal(entityType, ex.DatabaseName);
    }

    [Theory]
    [MemberData(nameof(ScopedTypes))]
    public async Task TestAScopedReaderReadsItsOwnUnitsRecordTrail(string entityType)
    {
        ScopeTo(UnitA);

        var rows = await _trail.GetForRecordAsync(entityType, InA);

        Assert.NotEmpty(rows);
        Assert.All(rows, r => Assert.Equal((entityType, InA), (r.EntityType, r.EntityId)));
    }

    [Theory]
    [MemberData(nameof(ScopedTypes))]
    public async Task TestAnUnrestrictedReaderReadsEveryUnitsRecordTrail(string entityType)
    {
        ScopeToEverything();

        Assert.NotEmpty(await _trail.GetForRecordAsync(entityType, InA));
        Assert.NotEmpty(await _trail.GetForRecordAsync(entityType, InB));
    }

    [Fact]
    public async Task TestAReaderWithNoAssignmentIsRefusedEveryRecordTrail()
    {
        ScopeToNothing();

        await Assert.ThrowsAsync<DataNotFoundException>(() => _trail.GetForRecordAsync(nameof(Risk), InA));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _trail.GetForRecordAsync(nameof(Risk), InB));
    }

    /// <summary>
    /// Once the record is gone its entity cannot be established, so a scoped reader is refused — the trail stays readable
    /// to an unrestricted one, as it always was.
    /// </summary>
    [Fact]
    public async Task TestADeletedRecordsTrailIsRefusedToAScopedReaderAndServedToAnUnrestrictedOne()
    {
        await using (var db = OpenContext())
        {
            db.MgmtReviews.Remove(db.MgmtReviews.Single(r => r.Id == InA));
            await db.SaveChangesAsync();
        }

        ScopeTo(UnitA);
        await Assert.ThrowsAsync<DataNotFoundException>(() => _trail.GetForRecordAsync(nameof(MgmtReview), InA));

        ScopeToEverything();
        Assert.Contains(await _trail.GetForRecordAsync(nameof(MgmtReview), InA), r => r.Action == AuditLogAction.Delete);
    }

    /// <summary>A BIA describes a process, which carries no scope (S43 D12): every reader of the register reads it.</summary>
    [Fact]
    public async Task TestAnOrganizationWideTypeIsServedToAScopedReader()
    {
        SeedUnscoped(ctx => ctx.BusinessImpactAnalyses.Add(new BusinessImpactAnalysis
            { Id = 7, EntityId = 900, RtoMinutes = 240, AssessedAt = Created, CreatedAt = Created }));

        ScopeTo(UnitA);

        Assert.NotEmpty(await _trail.GetForRecordAsync(nameof(BusinessImpactAnalysis), 7));
    }

    /// <summary>Fail closed: a type with no scope decision is refused to a scoped reader rather than served.</summary>
    [Fact]
    public async Task TestATypeWithNoScopeDecisionIsRefusedToAScopedReader()
    {
        Assert.DoesNotContain(nameof(Vulnerability), AuditTrailService.RecordScopes.Keys);

        ScopeTo(UnitA);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _trail.GetForRecordAsync(nameof(Vulnerability), InA));
    }

    // --- GetForRiskAsync ----------------------------------------------------------------------------------------------

    /// <summary>
    /// Regression: the aggregate trail resolved the children through filtered sets but matched the risk's and its scoring's
    /// rows on the id alone, so a reader scoped to unit A read unit B's risk subject and scores.
    /// </summary>
    [Fact]
    public async Task TestTheRiskTrailIsRefusedForAnotherUnitsRisk()
    {
        Assert.Contains(await _trail.GetForRiskAsync(InB), r => r.EntityType == nameof(Risk));

        ScopeTo(UnitA);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _trail.GetForRiskAsync(InB));
    }

    [Fact]
    public async Task TestTheRiskTrailServesTheReadersOwnRiskAggregate()
    {
        ScopeTo(UnitA);

        var types = (await _trail.GetForRiskAsync(InA)).Select(r => r.EntityType).ToHashSet();

        Assert.Superset(types, new HashSet<string>
        {
            nameof(Risk), nameof(RiskScoring), nameof(Mitigation), nameof(MitigationTask), nameof(MgmtReview),
            nameof(RiskAcceptance)
        });
    }

    // --- the table itself ---------------------------------------------------------------------------------------------

    /// <summary>A newly audited type must come with a scope decision, or a scoped reader is refused its trail.</summary>
    [Fact]
    public void TestEveryAuditedTypeHasAScopeDecision()
    {
        Assert.Empty(GovernanceAuditInterceptor.AuditedTypes.Where(t => !AuditTrailService.RecordScopes.ContainsKey(t)));
    }

    /// <summary>
    /// The lookup is only a scope check if the set it goes through is filtered by scope, and keyed by the id the interceptor
    /// writes. A type is its own owner, except a scoring, whose id is its risk's and which has no filter of its own.
    /// </summary>
    [Fact]
    public void TestEveryScopeOwnerIsFilteredByEntityScopeAndKeyedByAnInt()
    {
        using var db = OpenContext();

        foreach (var (type, scope) in AuditTrailService.RecordScopes.Where(s => s.Value.Owner != null))
        {
            var owner = db.Model.FindEntityType(scope.Owner!);
            Assert.True(owner != null, $"{type}: {scope.Owner!.Name} is not a mapped entity");
            Assert.True(owner!.GetDeclaredQueryFilters().Count > 0, $"{type}: {scope.Owner.Name} has no scope filter");

            var key = owner.FindPrimaryKey()!.Properties;
            Assert.True(key is [{ ClrType: var clr }] && clr == typeof(int), $"{type}: {scope.Owner.Name} key is not one int");

            Assert.True(scope.Owner.Name == type || (type, scope.Owner) == (nameof(RiskScoring), typeof(Risk)),
                $"{type} is checked through {scope.Owner.Name}");
        }
    }

    /// <summary>Declaring a scoped type organization-wide would serve it to every reader: only unfiltered types may be.</summary>
    [Fact]
    public void TestOnlyUnfilteredTypesAreOrganizationWide()
    {
        using var db = OpenContext();

        var organizationWide = AuditTrailService.RecordScopes.Where(s => s.Value.Owner == null).Select(s => s.Key).ToList();
        Assert.NotEmpty(organizationWide);

        foreach (var type in organizationWide)
        {
            var mapped = db.Model.GetEntityTypes().Single(e => e.ClrType.Name == type);
            Assert.True(mapped.GetDeclaredQueryFilters().Count == 0, $"{type} is filtered by scope but declared organization-wide");
        }
    }
}

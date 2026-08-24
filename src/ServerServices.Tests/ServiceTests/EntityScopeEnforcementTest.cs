using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Model.Exceptions;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// The negative half of Track 2 milestone 2.3.2's definition of done: a user scoped to entity A
/// must not be able to read, update or delete an entity-B record through <b>any</b> service — and
/// that includes whatever feeds exports and reports.
///
/// Enforcement is the model-level query filter in <c>NRDbContext.EntityScope</c>, so these tests
/// deliberately go through the ordinary service methods rather than a special scoped overload:
/// the point is that a service which never thinks about scoping still cannot cross the boundary.
/// </summary>
public class EntityScopeEnforcementTest : InMemoryServiceTestBase
{
    private const int EntityA = 100;
    private const int EntityB = 200;

    private readonly IRisksService _risks;
    private readonly IVulnerabilitiesService _vulnerabilities;
    private readonly IHostsService _hosts;
    private readonly IIncidentsService _incidents;
    private readonly IAssessmentsService _assessments;
    private readonly IExportService _export;

    public EntityScopeEnforcementTest()
    {
        _risks = GetService<IRisksService>();
        _vulnerabilities = GetService<IVulnerabilitiesService>();
        _hosts = GetService<IHostsService>();
        _incidents = GetService<IIncidentsService>();
        _assessments = GetService<IAssessmentsService>();
        _export = GetService<IExportService>();

        // Both entities' data is planted unscoped, as an administrator would have created it.
        SeedUnscoped(ctx =>
        {
            ctx.Entities.Add(NewEntity(EntityA));
            ctx.Entities.Add(NewEntity(EntityB));

            ctx.Risks.Add(NewRisk(1, EntityA));
            ctx.Risks.Add(NewRisk(2, EntityB));
            ctx.RiskScorings.Add(new RiskScoring { Id = 1, CalculatedRisk = 5f });
            ctx.RiskScorings.Add(new RiskScoring { Id = 2, CalculatedRisk = 5f });

            ctx.Vulnerabilities.Add(NewVuln(1, EntityA));
            ctx.Vulnerabilities.Add(NewVuln(2, EntityB));

            ctx.Hosts.Add(NewHost(1, EntityA));
            ctx.Hosts.Add(NewHost(2, EntityB));

            ctx.Incidents.Add(NewIncident(1, EntityA));
            ctx.Incidents.Add(NewIncident(2, EntityB));

            ctx.Assessments.Add(NewAssessment(1, EntityA));
            ctx.Assessments.Add(NewAssessment(2, EntityB));
        });
    }

    private static Entity NewEntity(int id) => new()
    {
        Id = id, DefinitionName = "organizationUnit", DefinitionVersion = "1",
        Status = "active", Created = DateTime.Now, Updated = DateTime.Now
    };

    private static Risk NewRisk(int id, int entityId) => new()
    {
        Id = id, Status = "New", Subject = $"R{id}", ReferenceId = $"R{id}", Assessment = "", Notes = "",
        RiskCatalogMapping = "", ThreatCatalogMapping = "", EntityId = entityId,
        SubmissionDate = DateTime.Now, LastUpdate = DateTime.Now
    };

    private static Vulnerability NewVuln(int id, int entityId) => new()
    {
        Id = id, Title = $"V{id}", Status = 1, Severity = "3", EntityId = entityId,
        FirstDetection = new DateTime(2026, 1, 1), LastDetection = new DateTime(2026, 1, 2), DetectionCount = 1
    };

    private static Host NewHost(int id, int entityId) => new()
    {
        Id = id, HostName = $"H{id}", Ip = $"10.0.0.{id}", EntityId = entityId, Source = "test",
        Status = 1, RegistrationDate = DateTime.Now, LastVerificationDate = DateTime.Now
    };

    private static Incident NewIncident(int id, int entityId) => new()
    {
        Id = id, Name = $"I{id}", Description = "d", EntityId = entityId, Status = 1
    };

    private static Assessment NewAssessment(int id, int entityId) => new()
    {
        Id = id, Name = $"A{id}", EntityId = entityId
    };

    // ---------------------------------------------------------------- reads

    [Fact]
    public async Task RiskListOnlyShowsTheCallersEntity()
    {
        ScopeTo(EntityA);

        var risks = await _risks.GetAllAsync(notStatus: null);

        Assert.Single(risks);
        Assert.Equal(1, risks[0].Id);
    }

    [Fact]
    public void ReadingAnotherEntitysRiskByIdIsNotFound()
    {
        ScopeTo(EntityA);

        Assert.NotNull(_risks.GetRisk(1));
        Assert.Throws<DataNotFoundException>(() => _risks.GetRisk(2));
    }

    [Fact]
    public async Task ReadingAnotherEntitysVulnerabilityByIdIsNotFound()
    {
        ScopeTo(EntityA);

        Assert.NotNull(await _vulnerabilities.GetByIdAsync(1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _vulnerabilities.GetByIdAsync(2));
    }

    [Fact]
    public void ReadingAnotherEntitysHostByIdIsNotFound()
    {
        ScopeTo(EntityA);

        Assert.NotNull(_hosts.GetById(1));
        Assert.Throws<DataNotFoundException>(() => _hosts.GetById(2));
    }

    [Fact]
    public async Task ReadingAnotherEntitysIncidentByIdIsNotFound()
    {
        ScopeTo(EntityA);

        Assert.NotNull(await _incidents.GetByIdAsync(1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => _incidents.GetByIdAsync(2));
    }

    [Fact]
    public void ReadingAnotherEntitysAssessmentByIdReturnsNothing()
    {
        ScopeTo(EntityA);

        Assert.NotNull(_assessments.Get(1));
        Assert.Null(_assessments.Get(2));
    }

    [Fact]
    public void AssessmentListOnlyShowsTheCallersEntity()
    {
        ScopeTo(EntityA);

        var list = _assessments.List();

        Assert.Single(list);
        Assert.Equal(1, list[0].Id);
    }

    // ---------------------------------------------------------------- writes

    [Fact]
    public void DeletingAnotherEntitysRiskIsRefused()
    {
        ScopeTo(EntityA);

        Assert.Throws<DataNotFoundException>(() => _risks.DeleteRisk(2));

        // And the row is still there when an administrator looks.
        ScopeToEverything();
        Assert.NotNull(_risks.GetRisk(2));
    }

    [Fact]
    public void DeletingAnotherEntitysVulnerabilityIsRefused()
    {
        ScopeTo(EntityA);

        Assert.Throws<DataNotFoundException>(() => _vulnerabilities.Delete(2));

        ScopeToEverything();
        Assert.NotNull(_vulnerabilities.GetById(2));
    }

    [Fact]
    public void DeletingAnotherEntitysHostIsRefused()
    {
        ScopeTo(EntityA);

        Assert.Throws<DataNotFoundException>(() => _hosts.Delete(2));

        ScopeToEverything();
        Assert.NotNull(_hosts.GetById(2));
    }

    [Fact]
    public async Task DeletingAnotherEntitysIncidentIsRefused()
    {
        ScopeTo(EntityA);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _incidents.DeleteByIdAsync(2));

        ScopeToEverything();
        Assert.NotNull(await _incidents.GetByIdAsync(2));
    }

    [Fact]
    public void UpdatingAnotherEntitysVulnerabilityDoesNotTakeEffect()
    {
        ScopeTo(EntityA);

        // UpdateStatus resolves the row first, so an out-of-scope id is simply not found.
        Assert.Throws<DataNotFoundException>(() => _vulnerabilities.UpdateStatus(2, 4));

        ScopeToEverything();
        Assert.Equal(1, _vulnerabilities.GetById(2).Status);
    }

    // ---------------------------------------------------------------- deny by default

    [Fact]
    public async Task AUserWithNoEntityAssignmentSeesNothing()
    {
        ScopeToNothing();

        Assert.Empty(await _risks.GetAllAsync(notStatus: null));
        Assert.Empty(_assessments.List());
        Assert.Throws<DataNotFoundException>(() => _risks.GetRisk(1));
    }

    [Fact]
    public async Task AdministratorsAndJobsAreUnrestricted()
    {
        ScopeToEverything();

        var risks = await _risks.GetAllAsync(notStatus: null);

        Assert.Equal(2, risks.Count);
    }

    [Fact]
    public async Task AUserAssignedToBothEntitiesSeesBoth()
    {
        ScopeTo(EntityA, EntityB);

        var risks = await _risks.GetAllAsync(notStatus: null);

        Assert.Equal(2, risks.Count);
    }

    // ---------------------------------------------------------------- exports

    [Fact]
    public async Task ExportsCarryOnlyTheCallersEntity()
    {
        ScopeTo(EntityA);

        // The export service formats whatever it is handed, so the guarantee has to come from
        // the query that feeds it — which is exactly what the model-level filter gives.
        var risks = await _risks.GetAllAsync(notStatus: null);
        var bytes = await _export.ExportAsync(risks, ExportFormat.Csv, "Risks");

        var csv = System.Text.Encoding.UTF8.GetString(bytes);

        Assert.Contains("R1", csv);
        Assert.DoesNotContain("R2", csv);
    }

    // ---------------------------------------------------------------- writes into another entity

    [Fact]
    public void CreatingARecordInAnotherEntityIsRefused()
    {
        ScopeTo(EntityA);

        using var ctx = OpenContext();
        ctx.Risks.Add(NewRisk(99, EntityB));

        Assert.Throws<DAL.Exceptions.EntityScopeViolationException>(() => ctx.SaveChanges());
    }

    [Fact]
    public void MovingOwnRecordIntoAnotherEntityIsRefused()
    {
        ScopeTo(EntityA);

        using var ctx = OpenContext();
        var mine = ctx.Risks.First(r => r.Id == 1);
        mine.EntityId = EntityB;

        Assert.Throws<DAL.Exceptions.EntityScopeViolationException>(() => ctx.SaveChanges());
    }

    [Fact]
    public void ANewRecordIsFiledInTheCallersOnlyEntity()
    {
        ScopeTo(EntityA);

        using (var ctx = OpenContext())
        {
            var created = NewRisk(99, EntityA);
            created.EntityId = null;
            ctx.Risks.Add(created);
            ctx.SaveChanges();
        }

        using var check = OpenContext();
        Assert.Equal(EntityA, check.Risks.First(r => r.Id == 99).EntityId);
    }

    [Fact]
    public void ACallerWithTwoEntitiesMustSayWhichOne()
    {
        ScopeTo(EntityA, EntityB);

        using var ctx = OpenContext();
        var created = NewRisk(99, EntityA);
        created.EntityId = null;
        ctx.Risks.Add(created);

        // With more than one entity in scope there is no safe default to pick.
        Assert.Throws<DAL.Exceptions.EntityScopeViolationException>(() => ctx.SaveChanges());
    }

    [Fact]
    public void AnAdministratorMayWriteAnywhere()
    {
        ScopeToEverything();

        using var ctx = OpenContext();
        ctx.Risks.Add(NewRisk(99, EntityB));
        ctx.SaveChanges();

        Assert.Equal(EntityB, ctx.Risks.First(r => r.Id == 99).EntityId);
    }
}

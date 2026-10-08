using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.APITests;
using API.Tests.Mock;
using DAL.Context;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using ServerServices.Governance;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// <c>GET /AuditTrail/{type}/{id}</c> and <c>GET /Risks/{id}/AuditTrail</c> honour the caller's entity scope, end to end
/// through the controller: the real <see cref="AuditTrailService"/> over an in-memory database whose contexts carry a
/// scope, as a scoped user's would. Before the fix a reader scoped to unit A got a 200 with unit B's field changes — the
/// risk's subject, its scores, its mitigation — because <c>audit_logs</c> has no entity id and nothing looked at the record.
/// Out of scope is a 404 here, as on every other read.
/// </summary>
[TestSubject(typeof(AuditTrailController))]
public class AuditTrailScopeTest : BaseControllerTest
{
    private const int UnitA = 100;
    private const int UnitB = 200;
    private const int InA = 1;
    private const int InB = 2;

    private readonly InMemoryDalService _dal = new(Guid.NewGuid().ToString());

    public AuditTrailScopeTest()
    {
        var created = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        using var db = _dal.GetContext();
        db.Users.Add(new User
        {
            Value = 1, Name = "cro", Login = "cro", Enabled = true, Type = "local", Salt = "s",
            Password = Encoding.UTF8.GetBytes("p"), Email = "cro@x.test"
        });

        foreach (var (id, unit) in new[] { (InA, UnitA), (InB, UnitB) })
        {
            db.Risks.Add(new Risk
            {
                Id = id, EntityId = unit, Status = "New", Subject = $"Unit {unit} risk", ReferenceId = $"R-{id}",
                Assessment = string.Empty, Notes = string.Empty, RiskCatalogMapping = string.Empty,
                ThreatCatalogMapping = string.Empty, SubmissionDate = created, LastUpdate = created
            });
            db.RiskScorings.Add(new RiskScoring
                { Id = id, ScoringMethod = 1, CalculatedRisk = 5f, ClassicImpact = 3, ClassicLikelihood = 3 });
            db.Mitigations.Add(new Mitigation
            {
                Id = id, RiskId = id, PlanningStrategy = 1, MitigationEffort = 1, MitigationCost = 1,
                MitigationOwner = 1, SubmittedBy = 1, MitigationPercent = 10, CurrentSolution = string.Empty,
                SecurityRequirements = string.Empty, SecurityRecommendations = string.Empty,
                SubmissionDate = created, LastUpdate = created, PlanningDate = new DateOnly(2026, 6, 1)
            });
        }

        // The interceptor writes each record's Create row on this save.
        db.SaveChanges();
    }

    private IAuditTrailService TrailScopedTo(EntityScope scope)
    {
        _dal.Scope = scope;
        return new AuditTrailService(Substitute.For<Serilog.ILogger>(), _dal);
    }

    private AuditTrailController GenericReader(EntityScope scope)
    {
        var trail = TrailScopedTo(scope);
        return ResolveController<AuditTrailController>(s => s.AddSingleton(trail));
    }

    private RiskGovernanceController RiskReader(EntityScope scope)
    {
        var trail = TrailScopedTo(scope);
        return ResolveController<RiskGovernanceController>(s => s.AddSingleton(trail));
    }

    /// <summary>Regression: before the fix this was a 200 carrying unit B's rows.</summary>
    [Theory]
    [InlineData(nameof(Risk))]
    [InlineData(nameof(RiskScoring))]
    [InlineData(nameof(Mitigation))]
    public async Task TestAnotherUnitsRecordTrailIsNotFound(string entityType)
    {
        var result = await GenericReader(EntityScope.ForEntities([UnitA])).GetForRecord(entityType, InB);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Theory]
    [InlineData(nameof(Risk))]
    [InlineData(nameof(RiskScoring))]
    [InlineData(nameof(Mitigation))]
    public async Task TestTheReadersOwnRecordTrailIsServed(string entityType)
    {
        var result = await GenericReader(EntityScope.ForEntities([UnitB])).GetForRecord(entityType, InB);

        var rows = Assert.IsType<List<AuditLog>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.All(rows, r => Assert.Equal((entityType, InB), (r.EntityType, r.EntityId)));
        Assert.NotEmpty(rows);
    }

    [Fact]
    public async Task TestAnUnrestrictedReaderIsServedEveryUnitsRecordTrail()
    {
        var reader = GenericReader(EntityScope.Unrestricted);

        Assert.NotEmpty(Assert.IsType<List<AuditLog>>(
            Assert.IsType<OkObjectResult>((await reader.GetForRecord(nameof(Risk), InA)).Result).Value));
        Assert.NotEmpty(Assert.IsType<List<AuditLog>>(
            Assert.IsType<OkObjectResult>((await reader.GetForRecord(nameof(Risk), InB)).Result).Value));
    }

    /// <summary>The refusals the scope check sits behind are unchanged: an unaudited type is still a 400, not a 404.</summary>
    [Fact]
    public async Task TestAnUnauditedTypeIsStillABadRequestForAScopedReader()
    {
        var result = await GenericReader(EntityScope.ForEntities([UnitA])).GetForRecord("Vulnerability", InA);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    /// <summary>Regression: the risk trail matched the risk's own rows on the id alone, so this was a 200 too.</summary>
    [Fact]
    public async Task TestAnotherUnitsRiskTrailIsNotFound()
    {
        var result = await RiskReader(EntityScope.ForEntities([UnitA])).GetAuditTrail(InB);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestTheReadersOwnRiskTrailIsServed()
    {
        var result = await RiskReader(EntityScope.ForEntities([UnitB])).GetAuditTrail(InB);

        var rows = Assert.IsType<List<AuditLog>>(Assert.IsType<OkObjectResult>(result.Result).Value);
        Assert.Contains(rows, r => (r.EntityType, r.EntityId) == (nameof(Risk), InB));
        Assert.Contains(rows, r => (r.EntityType, r.EntityId) == (nameof(Mitigation), InB));
    }
}

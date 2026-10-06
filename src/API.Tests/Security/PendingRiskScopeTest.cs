using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.APITests;
using API.Tests.Mock;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using DAL.Exceptions;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Governance;
using NSubstitute;
using ServerServices.Filtering;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// Stage 9.2 (S42 §6, §8) — the pending-risk endpoints honour the caller's entity scope, end to end
/// through the controller: the real <see cref="RisksService"/> over an in-memory database whose
/// contexts carry a scope, as a scoped user's would. Before the fix the queue had no filter, so a
/// triager scoped to unit B listed, promoted and dismissed unit A's hypotheses; and a cross-entity
/// write surfaced as a 500 because the controller's catch-all swallowed the exception the
/// <c>EntityScopeViolationMiddleware</c> turns into a 403.
/// </summary>
[TestSubject(typeof(RiskGovernanceController))]
public class PendingRiskScopeTest : BaseControllerTest
{
    private const int UnitA = 100;
    private const int UnitB = 200;
    private const int InA = 1;
    private const int InB = 2;

    private readonly InMemoryDalService _dal = new(Guid.NewGuid().ToString());

    public PendingRiskScopeTest()
    {
        using var db = _dal.GetContext();
        db.Users.Add(new User
        {
            Value = 1, Name = "triager", Login = "triager", Enabled = true, Type = "local", Salt = "s",
            Password = Encoding.UTF8.GetBytes("p"), Email = "t@x.test"
        });
        db.Categories.Add(new Category { Value = 1, Name = "Operational" });
        db.Sources.Add(new Source { Value = 1, Name = "Assessment" });
        foreach (var unit in new[] { UnitA, UnitB })
            db.Entities.Add(new Entity
            {
                Id = unit, DefinitionName = "organizationUnit", DefinitionVersion = "2.5", Status = "active",
                Created = DateTime.UtcNow, Updated = DateTime.UtcNow
            });
        db.PendingRisks.Add(Pending(InA, UnitA));
        db.PendingRisks.Add(Pending(InB, UnitB));
        db.SaveChanges();
    }

    private static PendingRisk Pending(int id, int entityId) => new()
    {
        Id = id, AssessmentId = 3, AssessmentAnswerId = id, EntityId = entityId,
        Subject = Encoding.UTF8.GetBytes($"Pending {id}"), Score = 5f, Comment = "c",
        SubmissionDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), Status = PendingRiskStatus.Pending
    };

    /// <summary>The controller over the real service, every context scoped to <paramref name="units"/>.</summary>
    private RiskGovernanceController ScopedTo(params int[] units)
    {
        _dal.Scope = EntityScope.ForEntities(units);

        var risks = new RisksService(_dal, Substitute.For<IRolesService>(),
            Substitute.For<IEntityFilterMapperProvider>(), Substitute.For<IUsersService>(),
            Substitute.For<INotificationEventPublisher>(), Substitute.For<IRiskWorkflowService>());

        return ResolveController<RiskGovernanceController>(s => s.AddSingleton<IRisksService>(risks));
    }

    private PendingRiskStatus StatusOf(int id)
    {
        using var db = _dal.GetContext(bypassEntityScope: true);
        return db.PendingRisks.Single(p => p.Id == id).Status;
    }

    [Fact]
    public async Task TestTheQueueListsOnlyTheCallersEntity()
    {
        var result = await ScopedTo(UnitB).GetPending();

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var rows = Assert.IsType<List<PendingRiskListing>>(ok.Value);
        Assert.Equal(new[] { InB }, rows.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task TestPromotingAnotherEntitysRowIsNotFound()
    {
        var result = await ScopedTo(UnitB).PromotePending(InA, new PendingRiskPromotion());

        Assert.IsType<NotFoundResult>(result.Result);
        Assert.Equal(PendingRiskStatus.Pending, StatusOf(InA));
    }

    [Fact]
    public async Task TestDismissingAnotherEntitysRowIsNotFound()
    {
        var result = await ScopedTo(UnitB).DismissPending(InA, new PendingRiskDismissal { Reason = "Not ours." });

        Assert.IsType<NotFoundResult>(result);
        Assert.Equal(PendingRiskStatus.Pending, StatusOf(InA));
    }

    /// <summary>A cross-entity promotion reaches the middleware (403), instead of the 500 it used to be.</summary>
    [Fact]
    public async Task TestPromotingIntoAnotherEntityIsAScopeViolationNotA500()
    {
        await Assert.ThrowsAsync<EntityScopeViolationException>(() =>
            ScopedTo(UnitA).PromotePending(InA, new PendingRiskPromotion { EntityId = UnitB }));

        Assert.Equal(PendingRiskStatus.Pending, StatusOf(InA));
    }

    /// <summary>A hypothesis filed into another entity reaches the middleware (403), not a 500.</summary>
    [Fact]
    public async Task TestRegisteringAHypothesisInAnotherEntityIsAScopeViolation()
    {
        await Assert.ThrowsAsync<EntityScopeViolationException>(() =>
            ScopedTo(UnitA).CreateHypothesis(new HypothesisRequest { Subject = "x", EntityId = UnitB }));
    }

    /// <summary>A hypothesis registered in A is filed under A and is not in B's queue.</summary>
    [Fact]
    public async Task TestAHypothesisRegisteredInOneUnitIsNotListedInAnother()
    {
        var created = Assert.IsType<CreatedResult>(
            (await ScopedTo(UnitA).CreateHypothesis(new HypothesisRequest { Subject = "Lab VLAN reachable" })).Result);
        var listing = Assert.IsType<PendingRiskListing>(created.Value);
        Assert.Equal(UnitA, listing.EntityId);

        var ofB = Assert.IsType<List<PendingRiskListing>>(
            Assert.IsType<OkObjectResult>((await ScopedTo(UnitB).GetPending()).Result).Value);
        Assert.DoesNotContain(ofB, r => r.Id == listing.Id);
    }
}

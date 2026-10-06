using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using DAL.Exceptions;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.2 (S42 §5.4, §8 Z1–Z11) — the hypothesis queue is entity-scoped. Before this,
/// <c>pending_risks</c> was the one register with no scope: a hypothesis a submitter in unit A wrote
/// was listed to, promoted by and dismissed by a triager scoped to unit B. Z1, Z2, Z4, Z6 and Z8 fail
/// on the code without the <c>PendingRisk</c> query filter and the promotion's entity default; the
/// others pin the creation rule that came with them.
/// </summary>
[TestSubject(typeof(RisksService))]
public class HypothesisScopeInMemoryTest : InMemoryServiceTestBase
{
    private const int UnitA = 100;
    private const int UnitB = 200;
    private const int Author = 7;
    private const int InA = 1;
    private const int InB = 2;
    private const int Global = 3;

    private IRisksService Risks => GetService<IRisksService>();

    public HypothesisScopeInMemoryTest()
    {
        SeedUnscoped(ctx =>
        {
            ctx.Users.Add(new User
            {
                Value = Author, Name = "author", Login = "author", Enabled = true, Type = "local", Salt = "s",
                Password = Encoding.UTF8.GetBytes("p"), Email = "author@x.test"
            });
            ctx.Categories.Add(new Category { Value = 1, Name = "Operational" });
            ctx.Sources.Add(new Source { Value = 1, Name = "Assessment" });
            ctx.Entities.Add(Unit(UnitA));
            ctx.Entities.Add(Unit(UnitB));
            ctx.PendingRisks.Add(Pending(InA, UnitA));
            ctx.PendingRisks.Add(Pending(InB, UnitB));
            ctx.PendingRisks.Add(Pending(Global, null));
        });
    }

    private static Entity Unit(int id) => new()
    {
        Id = id, DefinitionName = "organizationUnit", DefinitionVersion = "2.5", Status = "active",
        Created = DateTime.UtcNow, Updated = DateTime.UtcNow
    };

    private static PendingRisk Pending(int id, int? entityId) => new()
    {
        Id = id, AssessmentId = 3, AssessmentAnswerId = id, EntityId = entityId,
        Subject = Encoding.UTF8.GetBytes($"Pending {id}"), Score = 5f, Comment = "From the assessment.",
        SubmissionDate = new DateTime(2026, 5, 1, 0, 0, 0, DateTimeKind.Utc), Status = PendingRiskStatus.Pending
    };

    /// <summary>The stored row, read unscoped — the assertion is about the database, not the caller's view of it.</summary>
    private PendingRisk Stored(int id)
    {
        ScopeToEverything();
        using var db = OpenContext();
        return db.PendingRisks.Single(p => p.Id == id);
    }

    // --- list ------------------------------------------------------------------------------------

    /// <summary>Z1 — a scoped triager lists only their entity's rows; an unrestricted one lists all three.</summary>
    [Fact]
    public async Task TestZ1_TheQueueListsOnlyTheCallersEntity()
    {
        ScopeTo(UnitB);
        Assert.Equal(new[] { InB }, (await Risks.GetPendingRisksAsync()).Select(p => p.Id).ToArray());

        ScopeToEverything();
        Assert.Equal(3, (await Risks.GetPendingRisksAsync()).Count);
    }

    /// <summary>
    /// Z2 — the end-to-end leak the review found: a hypothesis registered by a user scoped to A is
    /// filed under A and is not visible to a triager scoped to B.
    /// </summary>
    [Fact]
    public async Task TestZ2_AHypothesisWrittenInOneUnitIsNotListedInAnother()
    {
        ScopeTo(UnitA);
        var created = await Risks.CreateHypothesisAsync(new HypothesisRequest { Subject = "Lab VLAN reachable" }, Author);
        Assert.Equal(UnitA, created.EntityId);

        ScopeTo(UnitB);
        Assert.DoesNotContain(await Risks.GetPendingRisksAsync(), p => p.Id == created.Id);

        ScopeTo(UnitA);
        Assert.Contains(await Risks.GetPendingRisksAsync(), p => p.Id == created.Id);
    }

    /// <summary>Z3 — an organization-wide (NULL) row is visible to no scoped caller, as a risk without an entity.</summary>
    [Fact]
    public async Task TestZ3_AnOrganizationWideRowIsHiddenFromScopedCallers()
    {
        ScopeTo(UnitA, UnitB);

        Assert.DoesNotContain(await Risks.GetPendingRisksAsync(), p => p.Id == Global);
    }

    // --- promote -----------------------------------------------------------------------------------

    /// <summary>Z4 — promoting another entity's row is not-found, and nothing is written.</summary>
    [Fact]
    public async Task TestZ4_PromotingAnotherEntitysRowIsNotFound()
    {
        ScopeTo(UnitB);

        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Risks.PromotePendingRiskAsync(InA, new PendingRiskPromotion(), Author));

        Assert.Equal(PendingRiskStatus.Pending, Stored(InA).Status);
        await using var db = OpenContext();
        Assert.Empty(db.Risks);
    }

    /// <summary>
    /// Z5 — the risk lands in the hypothesis's entity when the triager does not move it, even for a
    /// triager holding several entities (whom the write guard would otherwise refuse a NULL entity).
    /// </summary>
    [Fact]
    public async Task TestZ5_APromotedRiskIsFiledWhereTheHypothesisWas()
    {
        ScopeTo(UnitA, UnitB);

        var risk = await Risks.PromotePendingRiskAsync(InA, new PendingRiskPromotion(), Author);

        Assert.Equal(UnitA, risk.EntityId);
    }

    /// <summary>Z6 — moving the promoted risk to an entity outside the caller's scope is refused; nothing is written.</summary>
    [Fact]
    public async Task TestZ6_PromotingIntoAnEntityOutsideTheScopeIsRefused()
    {
        ScopeTo(UnitA);

        await Assert.ThrowsAsync<EntityScopeViolationException>(() =>
            Risks.PromotePendingRiskAsync(InA, new PendingRiskPromotion { EntityId = UnitB }, Author));

        Assert.Equal(PendingRiskStatus.Pending, Stored(InA).Status);
        await using var db = OpenContext();
        Assert.Empty(db.Risks);
    }

    // --- dismiss -----------------------------------------------------------------------------------

    /// <summary>Z7 — dismissing another entity's row is not-found, and the row is untouched.</summary>
    [Fact]
    public async Task TestZ7_DismissingAnotherEntitysRowIsNotFound()
    {
        ScopeTo(UnitB);

        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Risks.DismissPendingRiskAsync(InA, "Not ours.", Author));

        var row = Stored(InA);
        Assert.Equal(PendingRiskStatus.Pending, row.Status);
        Assert.Null(row.DismissalReason);
    }

    // --- create ------------------------------------------------------------------------------------

    /// <summary>Z8 — a hypothesis cannot be filed into an entity outside the caller's scope; nothing is written.</summary>
    [Fact]
    public async Task TestZ8_AHypothesisCannotBeFiledOutsideTheCallersScope()
    {
        ScopeTo(UnitA);

        await Assert.ThrowsAsync<EntityScopeViolationException>(() =>
            Risks.CreateHypothesisAsync(new HypothesisRequest { Subject = "x", EntityId = UnitB }, Author));

        ScopeToEverything();
        await using var db = OpenContext();
        Assert.Equal(3, db.PendingRisks.Count());
    }

    /// <summary>Z9 — a caller holding several entities must say which one; a caller holding none cannot file at all.</summary>
    [Fact]
    public async Task TestZ9_AnAmbiguousOrEmptyScopeIsRefused()
    {
        ScopeTo(UnitA, UnitB);
        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Risks.CreateHypothesisAsync(new HypothesisRequest { Subject = "x" }, Author));
        Assert.Equal(nameof(HypothesisRequest.EntityId), ex.ParameterName);

        var chosen = await Risks.CreateHypothesisAsync(new HypothesisRequest { Subject = "x", EntityId = UnitB }, Author);
        Assert.Equal(UnitB, chosen.EntityId);

        ScopeToNothing();
        await Assert.ThrowsAsync<EntityScopeViolationException>(() =>
            Risks.CreateHypothesisAsync(new HypothesisRequest { Subject = "y" }, Author));
    }

    /// <summary>Z10 — an entity that does not exist is not found.</summary>
    [Fact]
    public async Task TestZ10_AnUnknownEntityIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Risks.CreateHypothesisAsync(new HypothesisRequest { Subject = "x", EntityId = 999 }, Author));
    }

    /// <summary>Z11 — an unrestricted caller may file organization-wide, and no scoped caller then sees it.</summary>
    [Fact]
    public async Task TestZ11_AnUnrestrictedCallerFilesOrganizationWide()
    {
        var created = await Risks.CreateHypothesisAsync(new HypothesisRequest { Subject = "Org-wide" }, Author);
        Assert.Null(created.EntityId);

        ScopeTo(UnitA);
        Assert.DoesNotContain(await Risks.GetPendingRisksAsync(), p => p.Id == created.Id);
    }
}

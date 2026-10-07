using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Model.Exceptions;
using Model.Risks.Chain;
using NSubstitute;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.Risks;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.1 (S41 §8) — <see cref="RiskChainService"/> against the real model on the EF in-memory
/// provider: S1–S20, Y0, Y1 and A1.
///
/// Scope is exercised through the ordinary service methods with <c>ScopeTo</c>, as
/// <c>EntityScopeEnforcementTest</c> does: the point is that a chain query which never thinks about
/// scoping still cannot cross the boundary, because every query it makes is filtered.
/// </summary>
[TestSubject(typeof(RiskChainService))]
public class RiskChainServiceInMemoryTest : RiskChainTestBase
{
    // --- S1–S8: writes --------------------------------------------------------------------------

    [Fact]
    public async Task TestS1_ALinkToAProcessIsDeclaredAtTheProcessLevelWithItsAuthor()
    {
        AddRisk(1, UnitA);
        var before = DateTime.UtcNow.AddSeconds(-1);

        var result = await Chain.AddLinkAsync(1, new RiskChainLinkCreateDto { EntityId = Process }, Author, RiskManager());

        Assert.True(result.Created);
        var link = result.Link;
        Assert.Equal(RiskChainLevel.Process, link.Level);
        Assert.Equal(RiskChainLinkOrigin.Declared, link.Origin);
        Assert.Equal(Author, link.CreatedById);
        Assert.Equal("businessProcess", link.TargetType);
        Assert.Equal("Enrolment", link.TargetName);
        Assert.Equal(DateTimeKind.Utc, link.CreatedAt.Kind);
        Assert.InRange(link.CreatedAt, before, DateTime.UtcNow.AddSeconds(1));
        Assert.Null(link.UpdatedAt);

        var stored = Assert.Single(LinksOf(1));
        Assert.Equal(Process, stored.EntityId);
        Assert.Null(stored.HostId);
    }

    [Fact]
    public async Task TestS2_ALinkToAHostIsAnAsset()
    {
        AddRisk(1, UnitA);

        var link = (await Chain.AddLinkAsync(1, new RiskChainLinkCreateDto { HostId = HostA }, Author,
            RiskManagerWithHosts())).Link;

        Assert.Equal(RiskChainLevel.Asset, link.Level);
        Assert.Equal(HostA, link.HostId);
        Assert.Null(link.EntityId);
        Assert.Equal("host", link.TargetType);
        Assert.Equal("host-1", link.TargetName);
        Assert.False(link.IsRedacted);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData(Process, HostA)]
    public async Task TestS3_NeitherOrBothTargetsIsInvalid(int? entityId, int? hostId)
    {
        AddRisk(1, UnitA);

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Chain.AddLinkAsync(1,
            new RiskChainLinkCreateDto { EntityId = entityId, HostId = hostId }, Author, Admin()));

        Assert.Equal("target", ex.ParameterName);
        Assert.Empty(LinksOf(1));
    }

    [Fact]
    public async Task TestS4_AMissingRiskAndARiskOutOfScopeAreBothNotFound()
    {
        AddRisk(2, UnitB);

        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Chain.AddLinkAsync(999, new RiskChainLinkCreateDto { EntityId = Process }, Author, RiskManager()));

        ScopeTo(UnitA);
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Chain.AddLinkAsync(2, new RiskChainLinkCreateDto { EntityId = Process }, Author, RiskManager()));

        Assert.Empty(LinksOf(2));
    }

    [Fact]
    public async Task TestS5_AMissingEntityAndAHostOutOfScopeAreBothNotFound()
    {
        AddRisk(1, UnitA);

        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Chain.AddLinkAsync(1, new RiskChainLinkCreateDto { EntityId = 9999 }, Author, RiskManager()));

        ScopeTo(UnitA);
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Chain.AddLinkAsync(1, new RiskChainLinkCreateDto { HostId = HostB }, Author, RiskManagerWithHosts()));

        Assert.Empty(LinksOf(1));
    }

    [Fact]
    public async Task TestS6_AUnitIsNotANodeOfTheChain()
    {
        AddRisk(1, UnitA);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Chain.AddLinkAsync(1, new RiskChainLinkCreateDto { EntityId = UnitA }, Author, RiskManager()));

        Assert.Equal("entity_not_in_chain", ex.RuleName);

        var person = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Chain.AddLinkAsync(1, new RiskChainLinkCreateDto { EntityId = Person }, Author, RiskManager()));
        Assert.Equal("entity_not_in_chain", person.RuleName);

        Assert.Empty(LinksOf(1));
    }

    [Fact]
    public async Task TestS7_DeclaringTheSameLinkTwiceIsAConflict()
    {
        AddRisk(1, UnitA);
        await Chain.AddLinkAsync(1, new RiskChainLinkCreateDto { EntityId = Process }, Author, RiskManager());

        await Assert.ThrowsAsync<DataAlreadyExistsException>(() =>
            Chain.AddLinkAsync(1, new RiskChainLinkCreateDto { EntityId = Process }, Author, RiskManager()));

        Assert.Single(LinksOf(1));
    }

    [Fact]
    public async Task TestS8_DeclaringALegacyLinkPromotesIt()
    {
        AddRisk(1, UnitA);
        AddLegacyRow(1, Process);
        var legacyId = AddLink(1, Process, origin: RiskChainLinkOrigin.Legacy);

        var result = await Chain.AddLinkAsync(1, new RiskChainLinkCreateDto { EntityId = Process }, Author,
            RiskManager());

        Assert.False(result.Created);
        Assert.Equal(legacyId, result.Link.Id);
        Assert.Equal(RiskChainLinkOrigin.Declared, result.Link.Origin);
        Assert.NotNull(result.Link.UpdatedAt);

        var stored = Assert.Single(LinksOf(1));
        Assert.Equal(RiskChainLinkOrigin.Declared, stored.Origin);
        Assert.NotNull(stored.UpdatedAt);
    }

    // --- S9: delete -----------------------------------------------------------------------------

    [Fact]
    public async Task TestS9_DeletingADeclaredLinkWithNoLegacyRowRemovesIt()
    {
        AddRisk(1, UnitA);
        var linkId = AddLink(1, Process);

        var result = await Chain.DeleteLinkAsync(1, linkId, RiskManager());

        Assert.True(result.Deleted);
        Assert.Empty(LinksOf(1));
    }

    [Fact]
    public async Task TestS9_DeletingAPromotedLinkStillOnTheEntityFieldDemotesIt()
    {
        AddRisk(1, UnitA);
        AddLegacyRow(1, Process);
        AddLink(1, Process, origin: RiskChainLinkOrigin.Legacy);
        var promoted = (await Chain.AddLinkAsync(1, new RiskChainLinkCreateDto { EntityId = Process }, Author,
            RiskManager())).Link;

        var result = await Chain.DeleteLinkAsync(1, promoted.Id, RiskManager());

        Assert.False(result.Deleted);
        Assert.Equal(RiskChainLinkOrigin.Legacy, result.Demoted!.Origin);
        Assert.NotNull(result.Demoted.UpdatedAt);

        var stored = Assert.Single(LinksOf(1));
        Assert.Equal(promoted.Id, stored.Id);
        Assert.Equal(RiskChainLinkOrigin.Legacy, stored.Origin);
        AssertCoexistenceInvariants(1);
    }

    [Fact]
    public async Task TestS9_ALegacyLinkCannotBeDeletedFromTheChain()
    {
        AddRisk(1, UnitA);
        AddLegacyRow(1, Process);
        var legacyId = AddLink(1, Process, origin: RiskChainLinkOrigin.Legacy);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Chain.DeleteLinkAsync(1, legacyId, RiskManager()));

        Assert.Equal("legacy_link", ex.RuleName);
        Assert.Single(LinksOf(1));
    }

    [Fact]
    public async Task TestS9_DeletingAnotherRisksLinkIsNotFound()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        var linkOfRisk2 = AddLink(2, Process);

        await Assert.ThrowsAsync<DataNotFoundException>(() => Chain.DeleteLinkAsync(1, linkOfRisk2, RiskManager()));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Chain.DeleteLinkAsync(1, 9999, RiskManager()));

        Assert.Single(LinksOf(2));
    }

    // --- S10–S13: projection and node queries --------------------------------------------------

    [Fact]
    public async Task TestS10_ARiskWithNoLinkProjectsFiveEmptyLevels()
    {
        AddRisk(1, UnitA);

        var chain = await Chain.GetRiskChainAsync(1, RiskManager());

        Assert.Equal(1, chain.RiskId);
        Assert.Equal(UnitA, chain.ScopeEntityId);
        Assert.Equal("Unit A", chain.ScopeEntityName);
        Assert.Equal(Enum.GetValues<RiskChainLevel>(), chain.Levels.Select(l => l.Level).ToArray());
        Assert.All(chain.Levels, l => Assert.Empty(l.Links));
        Assert.Equal(Enum.GetValues<RiskChainLevel>(), chain.MissingLevels.ToArray());
    }

    /// <summary>S11 — the methodology's edge case for this stage: a missing middle link makes the risk
    /// invisible in no query.</summary>
    [Fact]
    public async Task TestS11_AMissingMiddleLinkHidesNothing()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        AddLink(1, Objective);
        AddLink(1, Process);
        AddLink(1, Data);
        AddLink(2, Process);

        var chain = await Chain.GetRiskChainAsync(1, RiskManager());
        Assert.Empty(chain.Levels.Single(l => l.Level == RiskChainLevel.ItService).Links);
        Assert.Equal(new[] { RiskChainLevel.ItService, RiskChainLevel.Asset }, chain.MissingLevels.ToArray());
        Assert.Equal("Student records", Assert.Single(chain.Levels.Single(l => l.Level == RiskChainLevel.Data).Links).TargetName);

        var underObjective = await Chain.GetRisksByEntityAsync(Objective, inferred: true);
        var risk2 = Assert.Single(underObjective, m => m.RiskId == 2);
        Assert.True(risk2.Inferred);
        Assert.Equal(Process, risk2.ViaEntityId);
        Assert.Equal(RiskChainLevel.Process, risk2.ViaLevel);
        Assert.Equal("Enrolment", risk2.ViaEntityName);
        Assert.False(Assert.Single(underObjective, m => m.RiskId == 1).Inferred);

        // Inference only goes up: the service serving P does not inherit P's risks. Risk 1 is there,
        // but through the data the service handles, not through P.
        var underService = await Chain.GetRisksByEntityAsync(Service, inferred: true);
        Assert.DoesNotContain(underService, m => m.RiskId == 2);
        var risk1 = Assert.Single(underService);
        Assert.Equal(Data, risk1.ViaEntityId);
        Assert.Equal(RiskChainLevel.Data, risk1.ViaLevel);

        // No existing query became an inner join with the chain.
        Assert.Equal(2, (await Risks.GetAllAsync(notStatus: null)).Count);
    }

    [Fact]
    public async Task TestS12_ARiskLinkedDirectlyAndBelowIsReportedOnceAsDirect()
    {
        AddRisk(1, UnitA);
        AddLink(1, Process);
        AddLink(1, Service);
        AddLink(1, PortalApp);

        var matches = await Chain.GetRisksByEntityAsync(Process, inferred: true);

        var match = Assert.Single(matches);
        Assert.False(match.Inferred);
        Assert.Null(match.ViaEntityId);
    }

    [Fact]
    public async Task TestS12_AnInferredRiskIsReportedViaTheShallowestNode()
    {
        AddRisk(1, UnitA);
        AddLink(1, PortalApp);
        AddLink(1, Service);

        var match = Assert.Single(await Chain.GetRisksByEntityAsync(Process, inferred: true));

        Assert.True(match.Inferred);
        Assert.Equal(Service, match.ViaEntityId);
        Assert.Equal(RiskChainLevel.ItService, match.ViaLevel);

        // Not asked to infer: only direct links count.
        Assert.Empty(await Chain.GetRisksByEntityAsync(Process, inferred: false));
    }

    [Fact]
    public async Task TestS13_ANodeQueryReturnsAClosedRiskWithItsStatusAndCoverageDoesNotCountIt()
    {
        AddRisk(1, UnitA, status: "Closed");
        AddLink(1, Process);

        var match = Assert.Single(await Chain.GetRisksByEntityAsync(Process, inferred: false));
        Assert.Equal("Closed", match.Status);
        Assert.Equal("Risk 1", match.Subject);

        var coverage = await Chain.GetCriticalProcessCoverageAsync();
        var row = coverage.Rows.Single(r => r.ProcessId == Process);
        Assert.False(row.Covered);
        Assert.Equal(0, row.DirectOpenRiskCount);
    }

    // --- S14–S15: scope -------------------------------------------------------------------------

    private void SeedTwoTenants()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitB);

        AddLink(1, Process);
        AddLink(1, hostId: HostA);
        AddLink(1, hostId: HostB);

        AddLink(2, Process);
        AddLink(2, Service);
        AddLink(2, PortalApp);
        AddLink(2, Process2);
        AddLink(2, Service2);
        AddLink(2, hostId: HostA);
    }

    [Fact]
    public async Task TestS14_AnotherTenantsRiskIsAbsentFromEveryChainQuery()
    {
        SeedTwoTenants();
        ScopeTo(UnitA);

        Assert.Equal([1], (await Chain.GetRisksByEntityAsync(Process, false)).Select(m => m.RiskId));
        Assert.Equal([1], (await Chain.GetRisksByEntityAsync(Objective, true)).Select(m => m.RiskId));

        // R2 would reach the service through the application, and the process through both.
        Assert.Empty(await Chain.GetRisksByEntityAsync(Service, false));
        Assert.Empty(await Chain.GetRisksByEntityAsync(Service, true));
        Assert.Empty(await Chain.GetRisksByEntityAsync(Process2, true));

        Assert.Equal([1], (await Chain.GetRisksByHostAsync(HostA, RiskManagerWithHosts())).Select(m => m.RiskId));

        await Assert.ThrowsAsync<DataNotFoundException>(() => Chain.GetRiskChainAsync(2, RiskManagerWithHosts()));

        // R1's own link to a host it cannot see is not in its projection.
        var chain = await Chain.GetRiskChainAsync(1, RiskManagerWithHosts());
        var asset = chain.Levels.Single(l => l.Level == RiskChainLevel.Asset).Links;
        Assert.Equal([HostA], asset.Select(l => l.HostId));
    }

    [Fact]
    public async Task TestS15_CoverageUnderScopeCountsOnlyVisibleRisks()
    {
        SeedTwoTenants();

        var everything = await Chain.GetCriticalProcessCoverageAsync();
        Assert.False(everything.IsScopeRestricted);
        Assert.True(everything.Rows.Single(r => r.ProcessId == Process2).Covered);
        Assert.Equal(2, everything.Rows.Single(r => r.ProcessId == Process).DirectOpenRiskCount);

        ScopeTo(UnitA);
        var scoped = await Chain.GetCriticalProcessCoverageAsync();

        Assert.True(scoped.IsScopeRestricted);

        var research = scoped.Rows.Single(r => r.ProcessId == Process2);
        Assert.False(research.Covered);
        Assert.Equal(0, research.DirectOpenRiskCount);
        Assert.Equal(0, research.InferredOpenRiskCount);

        var enrolment = scoped.Rows.Single(r => r.ProcessId == Process);
        Assert.True(enrolment.Covered);
        Assert.Equal(1, enrolment.DirectOpenRiskCount);
        Assert.Equal(0, enrolment.InferredOpenRiskCount);

        Assert.Equal(2, scoped.CriticalProcessCount);
        Assert.Equal(1, scoped.CoveredCount);
        Assert.Equal(0.5m, scoped.CoverageRatio);
    }

    // --- S16–S18: node query errors -------------------------------------------------------------

    [Fact]
    public async Task TestS16_AnEntityQueryOnAMissingEntityOrAUnitIsRefused()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => Chain.GetRisksByEntityAsync(9999, false));

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Chain.GetRisksByEntityAsync(UnitA, true));
        Assert.Equal("entity_not_in_chain", ex.RuleName);
    }

    [Fact]
    public async Task TestS17_AMissingHostAndAHostOutOfScopeGetTheSameAnswer()
    {
        ScopeTo(UnitA);

        var missing = await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Chain.GetRisksByHostAsync(9999, RiskManagerWithHosts()));
        var hidden = await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Chain.GetRisksByHostAsync(HostB, RiskManagerWithHosts()));

        Assert.Equal(missing.GetType(), hidden.GetType());
        Assert.Equal(missing.DatabaseName, hidden.DatabaseName);
    }

    [Fact]
    public async Task TestS18_DeletingALinkToAHostOutOfScopeIsNotFoundAndLeavesTheRow()
    {
        AddRisk(1, UnitA);
        var hidden = AddLink(1, hostId: HostB);

        ScopeTo(UnitA);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Chain.DeleteLinkAsync(1, hidden, RiskManagerWithHosts()));

        Assert.Equal(hidden, Assert.Single(LinksOf(1)).Id);
    }

    // --- S19: the hosts permission --------------------------------------------------------------

    /// <summary>
    /// A host that exists, one that does not and one out of scope all get the same refusal — and the
    /// service never opens a context to find out which is which, so the route cannot be an existence
    /// oracle. The double below records every context requested.
    /// </summary>
    [Fact]
    public async Task TestS19_AHostTargetWithoutHostsIsRefusedBeforeAnyQuery()
    {
        var dal = Substitute.For<IDalService>();
        var service = new RiskChainService(Substitute.For<Serilog.ILogger>(), dal);

        foreach (var hostId in new[] { HostA, 9999, HostB })
        {
            var ex = await Assert.ThrowsAsync<PermissionInvalidException>(() =>
                service.AddLinkAsync(1, new RiskChainLinkCreateDto { HostId = hostId }, Author, RiskManager()));
            Assert.Equal("hosts", ex.Permission);
        }

        await Assert.ThrowsAsync<PermissionInvalidException>(() => service.GetRisksByHostAsync(HostA, RiskManager()));
        await Assert.ThrowsAsync<PermissionInvalidException>(() => service.GetRisksByHostAsync(9999, RiskManager()));

        // No principal at all is "no hosts", never "unrestricted" (S41 §11, R9).
        await Assert.ThrowsAsync<PermissionInvalidException>(() => service.GetRisksByHostAsync(HostA, null));
        await Assert.ThrowsAsync<PermissionInvalidException>(() =>
            service.AddLinkAsync(1, new RiskChainLinkCreateDto { HostId = HostA }, Author, null));

        dal.DidNotReceive().GetContext(Arg.Any<bool>(), Arg.Any<bool>());
    }

    [Fact]
    public async Task TestS19_DeletingAHostLinkWithoutHostsIsRefused()
    {
        AddRisk(1, UnitA);
        var hostLink = AddLink(1, hostId: HostA);

        await Assert.ThrowsAsync<PermissionInvalidException>(() => Chain.DeleteLinkAsync(1, hostLink, RiskManager()));

        Assert.Single(LinksOf(1));
    }

    [Fact]
    public async Task TestS19_TheProjectionRedactsAHostLinkWithoutHosts()
    {
        AddRisk(1, UnitA);
        AddLink(1, hostId: HostA);

        var chain = await Chain.GetRiskChainAsync(1, RiskManager());

        var link = Assert.Single(chain.Levels.Single(l => l.Level == RiskChainLevel.Asset).Links);
        Assert.True(link.IsRedacted);
        Assert.Null(link.HostId);
        Assert.Null(link.TargetName);
        Assert.DoesNotContain(RiskChainLevel.Asset, chain.MissingLevels);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestS19_WithHostsOrAdminTheSameCallsPass(bool admin)
    {
        AddRisk(1, UnitA);
        var user = admin ? Admin() : RiskManagerWithHosts();

        var link = (await Chain.AddLinkAsync(1, new RiskChainLinkCreateDto { HostId = HostA }, Author, user)).Link;
        Assert.Equal(HostA, link.HostId);

        Assert.Equal([1], (await Chain.GetRisksByHostAsync(HostA, user)).Select(m => m.RiskId));

        var projected = Assert.Single((await Chain.GetRiskChainAsync(1, user)).Levels
            .Single(l => l.Level == RiskChainLevel.Asset).Links);
        Assert.False(projected.IsRedacted);
        Assert.Equal("host-1", projected.TargetName);

        Assert.True((await Chain.DeleteLinkAsync(1, link.Id, user)).Deleted);
    }

    // --- S20: the unique-index race -------------------------------------------------------------

    /// <summary>
    /// Stands in for the loser of a race on <c>uq_risk_chain_links_risk_id_entity_id</c>: any save that
    /// would insert a chain link fails the way MariaDB 1062 does. The in-memory provider enforces no
    /// unique index, so this is the only way to reach the branch here; DAL.IntegrationTests runs the
    /// real race.
    /// </summary>
    private sealed class DuplicateLinkInterceptor : SaveChangesInterceptor
    {
        public int Thrown { get; private set; }

        public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
        {
            ThrowOnLinkInsert(eventData.Context);
            return base.SavingChanges(eventData, result);
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            ThrowOnLinkInsert(eventData.Context);
            return base.SavingChangesAsync(eventData, result, cancellationToken);
        }

        private void ThrowOnLinkInsert(DbContext? context)
        {
            if (context is null) return;
            if (!context.ChangeTracker.Entries<RiskChainLink>().Any(e => e.State == EntityState.Added)) return;

            Thrown++;
            throw new DbUpdateException("An error occurred while saving the entity changes.",
                new InvalidOperationException(
                    "Duplicate entry '1-10' for key 'uq_risk_chain_links_risk_id_entity_id'"));
        }
    }

    private sealed class RacingDal(DbContextOptions<NRDbContext> options) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false) => new(options);

        public EntityScope GetCurrentEntityScope() => EntityScope.Unrestricted;
    }

    private static (RacingDal Dal, DuplicateLinkInterceptor Interceptor) NewRacingDatabase()
    {
        var interceptor = new DuplicateLinkInterceptor();
        var options = new DbContextOptionsBuilder<NRDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        var dal = new RacingDal(options);

        using var ctx = dal.GetContext();
        AddEntity(ctx, Process, "businessProcess", "Enrolment", ("criticality", "5"));
        ctx.Risks.Add(new Risk
        {
            Id = 1, Status = "New", Subject = "Risk 1", ReferenceId = "R1", Assessment = "", Notes = "",
            RiskCatalogMapping = "", ThreatCatalogMapping = "", SubmissionDate = DateTime.UtcNow,
            LastUpdate = DateTime.UtcNow
        });
        ctx.SaveChanges();

        return (dal, interceptor);
    }

    [Fact]
    public async Task TestS20_LosingTheRaceOnAPostIsAConflict()
    {
        var (dal, interceptor) = NewRacingDatabase();
        var service = new RiskChainService(Substitute.For<Serilog.ILogger>(), dal);

        await Assert.ThrowsAsync<DataAlreadyExistsException>(() =>
            service.AddLinkAsync(1, new RiskChainLinkCreateDto { EntityId = Process }, Author, RiskManager()));

        Assert.Equal(1, interceptor.Thrown);
    }

    [Fact]
    public void TestS20_LosingTheRaceInTheLegacyMirrorStillSavesTheLegacyRow()
    {
        var (dal, interceptor) = NewRacingDatabase();
        var risks = new RisksService(dal, Substitute.For<IRolesService>(),
            Substitute.For<ServerServices.Filtering.IEntityFilterMapperProvider>(), Substitute.For<IUsersService>(),
            Substitute.For<INotificationEventPublisher>(), Substitute.For<IRiskWorkflowService>());

        risks.AssociateRiskWithEntity(1, Process);

        Assert.Equal(1, interceptor.Thrown);

        using var db = dal.GetContext();
        Assert.Equal([Process],
            db.Risks.Where(r => r.Id == 1).SelectMany(r => r.Entities).Select(e => e.Id).ToList());
        Assert.Empty(db.RiskChainLinks.ToList());

        // The failed attempt's audit rows for a link that was never written were dropped, not saved.
        Assert.DoesNotContain(db.AuditLogs.ToList(), a => a.EntityType == nameof(RiskChainLink));
        Assert.DoesNotContain(db.Audits.ToList(), a => a.TableName == nameof(RiskChainLink));
    }

    [Fact]
    public void TestS20_OnlyADuplicateOnTheChainIndexesIsRecognised()
    {
        Assert.True(RiskChainPersistence.IsDuplicateLink(new DbUpdateException("x",
            new Exception("Duplicate entry '3-4' for key 'uq_risk_chain_links_risk_id_host_id'"))));
        Assert.False(RiskChainPersistence.IsDuplicateLink(new DbUpdateException("x",
            new Exception("Duplicate entry 'a' for key 'uq_secret_vault_connections_name'"))));
        Assert.False(RiskChainPersistence.IsDuplicateLink(new DbUpdateException("x")));
    }

    // --- Y0, Y1, A1 -----------------------------------------------------------------------------

    private static string SourceOf(string relative, [CallerFilePath] string thisFile = "")
    {
        // .../src/ServerServices.Tests/Track9/RiskChainServiceInMemoryTest.cs → .../src
        var src = new FileInfo(thisFile).Directory!.Parent!.Parent!.FullName;
        var path = Path.Combine(src, relative);
        Assert.True(File.Exists(path), $"{path} is missing.");

        var code = File.ReadAllText(path);
        code = Regex.Replace(code, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        code = Regex.Replace(code, @"//[^\n]*", " ");
        return code;
    }

    /// <summary>
    /// Y0 — the scope of every count this stage adds comes from the query filters alone, so no chain
    /// query and no statistic it changed may switch them off. Checked on the source, comments stripped,
    /// because a bypass added later would pass every behavioural test that does not happen to seed a
    /// second tenant.
    /// </summary>
    [Theory]
    [InlineData("ServerServices/Governance/RiskChainService.cs")]
    [InlineData("ServerServices/Governance/RiskChainGraphLoader.cs")]
    [InlineData("ServerServices/Governance/RiskChainPersistence.cs")]
    [InlineData("ServerServices/Services/StatisticsService.cs")]
    public void TestY0_NoChainQueryBypassesTheScopeFilters(string file)
    {
        var code = SourceOf(file);

        Assert.DoesNotContain("IgnoreQueryFilters", code);
        Assert.DoesNotContain("bypassEntityScope", code);
    }

    /// <summary>
    /// Y1 (RiskChainSchemaConfiguration) — every definition and property <see cref="RiskChainSchema"/>
    /// reads exists in the configuration the API loads, with the referenced definition and the
    /// multiplicity the graph expects. A rename on either side would otherwise disconnect the chain
    /// without an error anywhere.
    /// </summary>
    [Fact]
    public async Task TestY1_RiskChainSchemaConfigurationMatchesTheEntitiesConfiguration()
    {
        var configuration = await GetService<IEntitiesService>().GetEntitiesConfigurationAsync();
        var definitions = configuration.Definitions;

        foreach (var definition in RiskChainSchema.ChainDefinitions)
        {
            // activity arrives with T242; it is mapped already and checked only once it exists.
            if (definition == RiskChainSchema.ActivityDefinition && !definitions.ContainsKey(definition)) continue;

            Assert.True(definitions.ContainsKey(definition), $"Chain definition '{definition}' is not configured.");
            Assert.True(definitions[definition].Properties.ContainsKey(RiskChainSchema.NameProperty),
                $"'{definition}' has no name property; the chain shows nodes by name.");
        }

        foreach (var edge in RiskChainSchema.Edges.Where(e => !e.IsParentEdge))
        {
            Assert.True(definitions.ContainsKey(edge.OwnerDefinition), $"'{edge.OwnerDefinition}' is not configured.");

            var properties = definitions[edge.OwnerDefinition].Properties;
            Assert.True(properties.ContainsKey(edge.Property!),
                $"'{edge.OwnerDefinition}.{edge.Property}' is not configured.");

            var property = properties[edge.Property!];
            Assert.Equal($"Definition({edge.ReferencedDefinition})", property.Type);
            Assert.Equal(edge.Multiple, property.Multiple);
        }

        var criticality = definitions[RiskChainSchema.ProcessDefinition].Properties[RiskChainSchema.CriticalityProperty];
        Assert.Equal("Integer", criticality.Type);
        Assert.True(criticality.Nullable);
        Assert.False(criticality.Multiple);

        var isActive = definitions[RiskChainSchema.ProcessDefinition].Properties[RiskChainSchema.IsActiveProperty];
        Assert.Equal("Boolean", isActive.Type);

        // New properties on an existing type must be nullable, or every saved process stops saving.
        Assert.True(definitions[RiskChainSchema.ProcessDefinition].Properties["strategicObjectives"].Nullable);

        // The service's technical owner is the one mandatory reference of the new types.
        var owner = definitions[RiskChainSchema.ItServiceDefinition].Properties["technicalOwner"];
        Assert.Equal("Definition(person)", owner.Type);
        Assert.False(owner.Nullable);

        // 2.6 since Stage 9.5 added securityClassificationLevel.sensitive (S46 §4.4); the 9.1 types are unchanged.
        Assert.Equal("2.6", configuration.Version);
    }

    /// <summary>A1 — declaring and deleting a link each write the field-level trail.</summary>
    [Fact]
    public async Task TestA1_LinkingAndUnlinkingAreAudited()
    {
        AddRisk(1, UnitA);

        var link = (await Chain.AddLinkAsync(1, new RiskChainLinkCreateDto { EntityId = Process }, Author,
            RiskManager())).Link;
        await Chain.DeleteLinkAsync(1, link.Id, RiskManager());

        var rows = Read(ctx => ctx.AuditLogs.Where(a => a.EntityType == "RiskChainLink").OrderBy(a => a.Id).ToList());

        var create = Assert.Single(rows, r => r.Action == AuditLogAction.Create);
        Assert.Contains("RiskId=1", create.NewValue);
        Assert.Contains($"EntityId={Process}", create.NewValue);

        var delete = Assert.Single(rows, r => r.Action == AuditLogAction.Delete);
        Assert.Contains("Origin=Declared", delete.OldValue);
    }

    [Fact]
    public async Task TestA1_APromotionIsAuditedAsAnOriginChange()
    {
        AddRisk(1, UnitA);
        AddLegacyRow(1, Process);
        var legacyId = AddLink(1, Process, origin: RiskChainLinkOrigin.Legacy);

        await Chain.AddLinkAsync(1, new RiskChainLinkCreateDto { EntityId = Process }, Author, RiskManager());

        var update = Assert.Single(Read(ctx => ctx.AuditLogs
            .Where(a => a.EntityType == "RiskChainLink" && a.Action == AuditLogAction.Update).ToList()));
        Assert.Equal(legacyId, update.EntityId);
        Assert.Equal(nameof(RiskChainLink.Origin), update.Field);
        Assert.Equal("Legacy", update.OldValue);
        Assert.Equal("Declared", update.NewValue);
    }
}

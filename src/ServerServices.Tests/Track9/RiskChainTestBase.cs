using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Tools.Risks;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// The organisation every Stage 9.1 service test starts from (S41 §8), seeded unscoped as an
/// administrator would have created it:
/// <code>
///   objective 1 "Grow enrolment"
///     └─ process 10 "Enrolment" (criticality 5, applications = 40)
///          ├─ service 20 "Student portal" (processes = 10, applications = 30, data = 50)
///          │    ├─ application 30 "Portal app"
///          │    └─ data 50 "Student records"
///          └─ application 40 "CRM"
///   process 11 "Research" (criticality 4) ── service 21 "Lab services" (processes = 11)
///   units 100 "Unit A" and 200 "Unit B" (scope, not chain); person 60
///   hosts 1 (Unit A) and 2 (Unit B); user 7
/// </code>
/// Risks are added per test with <see cref="AddRisk"/>, each with its scoring row, because an
/// <c>Include</c> on a required navigation inner-joins and an unseeded principal would make a test
/// pass measuring nothing (Track 9, Gate 2 rule 4).
/// </summary>
public abstract class RiskChainTestBase : InMemoryServiceTestBase
{
    protected const int Objective = 1;
    protected const int Process = 10;
    protected const int Process2 = 11;
    protected const int Service = 20;
    protected const int Service2 = 21;
    protected const int PortalApp = 30;
    protected const int Crm = 40;
    protected const int Data = 50;
    protected const int Person = 60;
    protected const int UnitA = 100;
    protected const int UnitB = 200;
    protected const int HostA = 1;
    protected const int HostB = 2;
    protected const int Author = 7;

    protected IRiskChainService Chain => GetService<IRiskChainService>();

    protected IRisksService Risks => GetService<IRisksService>();

    protected RiskChainTestBase()
    {
        SeedUnscoped(ctx =>
        {
            ctx.Users.Add(new User
            {
                Value = Author, Name = "analyst", Login = "analyst", Enabled = true, Type = "local", Salt = "s",
                Password = Encoding.UTF8.GetBytes("p"), Email = "analyst@x.test"
            });

            AddEntity(ctx, UnitA, "organizationUnit", "Unit A");
            AddEntity(ctx, UnitB, "organizationUnit", "Unit B");
            AddEntity(ctx, Person, "person", "Ana");

            AddEntity(ctx, Objective, "strategicObjective", "Grow enrolment", ("isActive", "True"));
            AddEntity(ctx, Process, "businessProcess", "Enrolment",
                ("strategicObjectives", Objective.ToString()), ("criticality", "5"), ("isActive", "True"),
                ("applications", Crm.ToString()));
            AddEntity(ctx, Process2, "businessProcess", "Research", ("criticality", "4"), ("isActive", "True"));
            AddEntity(ctx, Service, "itService", "Student portal",
                ("processes", Process.ToString()), ("applications", PortalApp.ToString()),
                ("data", Data.ToString()), ("technicalOwner", Person.ToString()), ("isActive", "True"));
            AddEntity(ctx, Service2, "itService", "Lab services",
                ("processes", Process2.ToString()), ("technicalOwner", Person.ToString()), ("isActive", "True"));
            AddEntity(ctx, PortalApp, "application", "Portal app");
            AddEntity(ctx, Crm, "application", "CRM");
            AddEntity(ctx, Data, "organizationData", "Student records");

            ctx.Hosts.Add(NewHost(HostA, UnitA));
            ctx.Hosts.Add(NewHost(HostB, UnitB));
        });
    }

    // --- seeding -------------------------------------------------------------------------------

    private static int _propertyId = 1000;

    protected static void AddEntity(AuditableContext ctx, int id, string definition, string name,
        params (string Type, string Value)[] properties)
    {
        ctx.Entities.Add(new Entity
        {
            Id = id, DefinitionName = definition, DefinitionVersion = "2.5", Status = "active",
            Created = DateTime.UtcNow, Updated = DateTime.UtcNow
        });

        foreach (var (type, value) in properties.Prepend(("name", name)))
        {
            ctx.EntitiesProperties.Add(new EntitiesProperty
            {
                Id = System.Threading.Interlocked.Increment(ref _propertyId),
                Entity = id, Type = type, Value = value, OldValue = "", Name = type
            });
        }
    }

    protected static Host NewHost(int id, int entityId) => new()
    {
        Id = id, HostName = $"host-{id}", Ip = $"10.0.0.{id}", EntityId = entityId, Source = "test",
        Status = 1, RegistrationDate = DateTime.UtcNow, LastVerificationDate = DateTime.UtcNow
    };

    protected void AddRisk(int id, int? scopeEntityId, string status = "New", float score = 5f) =>
        SeedUnscoped(ctx =>
        {
            ctx.Risks.Add(new Risk
            {
                Id = id, Status = status, Subject = $"Risk {id}", ReferenceId = $"R{id}", Assessment = "",
                Notes = "", RiskCatalogMapping = "", ThreatCatalogMapping = "", EntityId = scopeEntityId,
                SubmissionDate = DateTime.UtcNow, LastUpdate = DateTime.UtcNow
            });
            ctx.RiskScorings.Add(new RiskScoring
                { Id = id, ScoringMethod = 1, CalculatedRisk = score, ClassicImpact = 3, ClassicLikelihood = 3 });
        });

    /// <summary>Plants a link directly, as a previous save would have left it.</summary>
    protected int AddLink(int riskId, int? entityId = null, int? hostId = null,
        RiskChainLinkOrigin origin = RiskChainLinkOrigin.Declared)
    {
        var link = new RiskChainLink
        {
            RiskId = riskId, EntityId = entityId, HostId = hostId, Origin = origin,
            ChainLevel = hostId is not null ? RiskChainLevel.Asset : LevelOf(entityId!.Value),
            CreatedAt = DateTime.UtcNow, CreatedById = origin == RiskChainLinkOrigin.Declared ? Author : null
        };

        SeedUnscoped(ctx => ctx.RiskChainLinks.Add(link));
        return link.Id;
    }

    /// <summary>Plants a legacy <c>risk_to_entity</c> row with no mirror, as data predating Stage 9.1.</summary>
    protected void AddLegacyRow(int riskId, int entityId) =>
        SeedUnscoped(ctx => ctx.Set<Dictionary<string, object>>("RiskToEntity")
            .Add(new Dictionary<string, object> { ["RiskId"] = riskId, ["EntityId"] = entityId }));

    private RiskChainLevel LevelOf(int entityId) =>
        Read(ctx => RiskChainSchema.LevelOf(ctx.Entities.Single(e => e.Id == entityId).DefinitionName))
        ?? throw new InvalidOperationException($"Entity {entityId} is not a chain node.");

    // --- reading -------------------------------------------------------------------------------

    /// <summary>Reads the database as an administrator would, whatever scope the test has set.</summary>
    protected T Read<T>(Func<AuditableContext, T> read)
    {
        var result = default(T)!;
        SeedUnscoped(ctx => result = read(ctx));
        return result;
    }

    protected List<RiskChainLink> LinksOf(int riskId) =>
        Read(ctx => ctx.RiskChainLinks.Where(l => l.RiskId == riskId).OrderBy(l => l.Id).ToList());

    protected List<int> LegacyRowsOf(int riskId) =>
        Read(ctx => ctx.Risks.Where(r => r.Id == riskId).SelectMany(r => r.Entities).Select(e => e.Id)
            .OrderBy(id => id).ToList());

    /// <summary>
    /// The two coexistence invariants of S41 §5.4: (i) every Legacy link of the risk has its
    /// <c>risk_to_entity</c> row; (ii) every <c>risk_to_entity</c> row whose entity is of a chain type has
    /// a link, of either origin.
    /// </summary>
    protected void AssertCoexistenceInvariants(int riskId)
    {
        var legacyRows = Read(ctx => ctx.Risks.Where(r => r.Id == riskId).SelectMany(r => r.Entities)
            .Select(e => new { e.Id, e.DefinitionName }).ToList());
        var links = LinksOf(riskId);

        foreach (var link in links.Where(l => l.Origin == RiskChainLinkOrigin.Legacy))
            Assert.True(legacyRows.Any(r => r.Id == link.EntityId),
                $"Invariant (i): Legacy link {link.Id} to entity {link.EntityId} has no risk_to_entity row.");

        foreach (var row in legacyRows.Where(r => RiskChainSchema.LevelOf(r.DefinitionName) is not null))
            Assert.True(links.Any(l => l.EntityId == row.Id),
                $"Invariant (ii): risk_to_entity row ({riskId}, {row.Id}) has no chain link.");
    }

    // --- principals ----------------------------------------------------------------------------

    protected static ClaimsPrincipal Principal(params Claim[] claims) =>
        new(new ClaimsIdentity(claims.Prepend(new Claim(ClaimTypes.Name, "analyst")), "test"));

    /// <summary>A risk manager with no <c>hosts</c> permission and not an admin.</summary>
    protected static ClaimsPrincipal RiskManager() =>
        Principal(new Claim("Permission", "riskmanagement"), new Claim(ClaimTypes.Sid, Author.ToString()));

    protected static ClaimsPrincipal RiskManagerWithHosts() =>
        Principal(new Claim("Permission", "riskmanagement"), new Claim("Permission", "hosts"));

    protected static ClaimsPrincipal Admin() => Principal(new Claim(ClaimTypes.Role, "Admin"));
}

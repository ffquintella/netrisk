using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.2 (S42 §8) — a near miss distinguished from an incident (T153): the kind is stored and
/// validated, defaults to Incident for a client that never sends it, and the one aggregate that calls
/// rows "open incidents" — the Master Dashboard posture — leaves near misses out.
/// </summary>
[TestSubject(typeof(IncidentsService))]
public class NearMissInMemoryTest : InMemoryServiceTestBase
{
    private const int Reporter = 7;
    private const int Unit = 1;

    private IIncidentsService Incidents => GetService<IIncidentsService>();

    private static readonly User Actor = new()
    {
        Value = Reporter, Name = "handler", Login = "handler", Enabled = true, Type = "local", Salt = "s",
        Password = Encoding.UTF8.GetBytes("p"), Email = "handler@x.test"
    };

    public NearMissInMemoryTest()
    {
        Seed(ctx =>
        {
            ctx.Users.Add(new User
            {
                Value = Reporter, Name = "handler", Login = "handler", Enabled = true, Type = "local", Salt = "s",
                Password = Encoding.UTF8.GetBytes("p"), Email = "handler@x.test"
            });

            var unit = new Entity
            {
                Id = Unit, DefinitionName = "organizationUnit", DefinitionVersion = "2.5", Status = "active",
                Created = DateTime.UtcNow, Updated = DateTime.UtcNow
            };
            unit.EntitiesProperties.Add(new EntitiesProperty
            {
                Id = Unit, Entity = Unit, Type = "name", Value = "Unit A", OldValue = "", Name = "name"
            });
            ctx.Entities.Add(unit);
        });
    }

    private static Incident NewIncident(string name, IncidentKind? kind = null)
    {
        var incident = new Incident
        {
            Name = name, Description = "Phishing e-mail to the finance team", Year = 2026, Sequence = 1,
            Category = "phishing", EntityId = Unit
        };
        if (kind != null) incident.Kind = kind.Value;
        return incident;
    }

    /// <summary>N1 — a client that never sends the kind keeps today's meaning: an incident.</summary>
    [Fact]
    public async Task TestN1_AnIncidentWithoutAKindIsAnIncident()
    {
        var created = await Incidents.CreateAsync(NewIncident("2026-1"), Actor);

        Assert.Equal(IncidentKind.Incident, (await Incidents.GetByIdAsync(created.Id)).Kind);
    }

    /// <summary>
    /// N2 — a near miss is stored as such, keeps its threat category, and is numbered in the same
    /// yearly sequence as incidents (one register of events, two kinds).
    /// </summary>
    [Fact]
    public async Task TestN2_ANearMissIsStoredWithItsCategoryInTheSameSequence()
    {
        await Incidents.CreateAsync(NewIncident("2026-1"), Actor);
        var nearMiss = NewIncident("2026-2", IncidentKind.NearMiss);
        nearMiss.Sequence = 2;

        var created = await Incidents.CreateAsync(nearMiss, Actor);

        var stored = await Incidents.GetByIdAsync(created.Id);
        Assert.Equal(IncidentKind.NearMiss, stored.Kind);
        Assert.Equal("phishing", stored.Category);
        Assert.Equal(3, await Incidents.GetNextSequenceAsync(2026));
    }

    /// <summary>N3 — an incident reclassified as a near miss on update keeps the new kind.</summary>
    [Fact]
    public async Task TestN3_TheKindCanBeChangedOnUpdate()
    {
        var created = await Incidents.CreateAsync(NewIncident("2026-1"), Actor);

        var edit = await Incidents.GetByIdAsync(created.Id);
        edit.Kind = IncidentKind.NearMiss;
        await Incidents.UpdateAsync(edit, Actor);

        Assert.Equal(IncidentKind.NearMiss, (await Incidents.GetByIdAsync(created.Id)).Kind);
    }

    /// <summary>N4 — a kind that is neither is refused on create, and nothing is written.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public async Task TestN4_AnUndefinedKindIsRefusedOnCreate(int kind)
    {
        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Incidents.CreateAsync(NewIncident("2026-1", (IncidentKind)kind), Actor));

        Assert.Equal(nameof(Incident.Kind), ex.ParameterName);
        await using var db = OpenContext();
        Assert.Empty(db.Incidents);
    }

    /// <summary>N5 — and on update, where the stored kind is kept.</summary>
    [Fact]
    public async Task TestN5_AnUndefinedKindIsRefusedOnUpdate()
    {
        var created = await Incidents.CreateAsync(NewIncident("2026-1", IncidentKind.NearMiss), Actor);

        var edit = await Incidents.GetByIdAsync(created.Id);
        edit.Kind = (IncidentKind)9;

        await Assert.ThrowsAsync<InvalidParameterException>(() => Incidents.UpdateAsync(edit, Actor));
        Assert.Equal(IncidentKind.NearMiss, (await Incidents.GetByIdAsync(created.Id)).Kind);
    }

    /// <summary>
    /// N6 — the Master Dashboard counts an open incident and not an open near miss: a near miss is not
    /// an incident, and counting it would penalize an entity's posture for reporting what did not
    /// happen. The near miss is still in the incidents list.
    /// </summary>
    [Fact]
    public async Task TestN6_TheMasterDashboardDoesNotCountANearMissAsAnOpenIncident()
    {
        Seed(ctx =>
        {
            ctx.Incidents.Add(new Incident
            {
                Id = 1, Name = "2026-1", Description = "d", EntityId = Unit, Status = (int)IntStatus.New,
                Kind = IncidentKind.Incident
            });
            ctx.Incidents.Add(new Incident
            {
                Id = 2, Name = "2026-2", Description = "d", EntityId = Unit, Status = (int)IntStatus.New,
                Kind = IncidentKind.NearMiss
            });
        });

        var dashboard = await GetService<IMasterDashboardService>().GetMasterDashboardAsync(useCache: false);

        Assert.Equal(1, dashboard.Entities.Single(e => e.EntityName == "Unit A").OpenIncidents);
        Assert.Equal(1, dashboard.Totals.OpenIncidents);
        Assert.Equal(2, (await Incidents.GetAllAsync()).Count);
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Auditing;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// M58, T279 (S39 D-15, §9.2): an incident carries a severity and a UTC resolution date the server owns, the server's
/// stamps are UTC, and an incident is audited field by field — without the free-text reporter name.
/// </summary>
[TestSubject(typeof(IncidentsService))]
public class IncidentResolutionInMemoryTest : InMemoryServiceTestBase
{
    private const int Unit = 10, OtherUnit = 20;
    private static readonly User Admin = new() { Value = 1, Name = "Admin", Type = "local", Admin = true, Lang = "en" };
    private static readonly DateTime ClientSent = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IIncidentsService _svc;

    public IncidentResolutionInMemoryTest() => _svc = GetService<IIncidentsService>();

    private static Incident NewIncident(int id, int status, int entity = Unit) => new()
    {
        Id = id, Year = 2026, Sequence = id, Name = $"INC-{id}", Description = "d", Category = "phishing",
        Status = status, EntityId = entity, CreatedById = 1
    };

    private Incident Stored(int id)
    {
        using var db = OpenContext();
        return db.Incidents.Single(i => i.Id == id);
    }

    /// <summary>Regression (D-15): creation and update were stamped with DateTime.Now, the server's local time.</summary>
    [Fact]
    public async Task TestTheServerStampsAreUtc()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var created = await _svc.CreateAsync(NewIncident(0, (int)IntStatus.New), Admin);
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.Equal(DateTimeKind.Utc, created.CreationDate.Kind);
        Assert.InRange(created.CreationDate, before, after);
        Assert.InRange(created.LastUpdate, before, after);

        var updated = await _svc.UpdateAsync(NewIncident(created.Id, (int)IntStatus.Active), Admin);

        Assert.Equal(DateTimeKind.Utc, updated.LastUpdate.Kind);
        Assert.InRange(updated.LastUpdate, before, DateTime.UtcNow.AddSeconds(1));
    }

    [Fact]
    public async Task TestCreatingAClosedIncidentStampsItsResolutionAndAnOpenOneIgnoresTheClients()
    {
        var closed = NewIncident(0, (int)IntStatus.Closed);
        closed.ResolvedAt = ClientSent;
        var open = NewIncident(0, (int)IntStatus.New);
        open.Sequence = 2;
        open.ResolvedAt = ClientSent;

        var closedId = (await _svc.CreateAsync(closed, Admin)).Id;
        var openId = (await _svc.CreateAsync(open, Admin)).Id;

        Assert.NotEqual(ClientSent, Stored(closedId).ResolvedAt);
        Assert.InRange(Stored(closedId).ResolvedAt!.Value, DateTime.UtcNow.AddMinutes(-1), DateTime.UtcNow.AddSeconds(1));
        Assert.Null(Stored(openId).ResolvedAt);
    }

    /// <summary>
    /// Open → closed stamps; closed → another closed status keeps the date; closed → open clears it; the date the client
    /// sends is never taken.
    /// </summary>
    [Fact]
    public async Task TestTheResolutionDateFollowsTheStatusTransitions()
    {
        Seed(ctx => ctx.Incidents.Add(NewIncident(1, (int)IntStatus.Active)));

        var closing = NewIncident(1, (int)IntStatus.Closed);
        closing.ResolvedAt = ClientSent;
        await _svc.UpdateAsync(closing, Admin);
        var resolved = Stored(1).ResolvedAt;
        Assert.NotNull(resolved);
        Assert.NotEqual(ClientSent, resolved);

        var stillClosed = NewIncident(1, (int)IntStatus.Solved);
        stillClosed.ResolvedAt = ClientSent;
        await _svc.UpdateAsync(stillClosed, Admin);
        Assert.Equal(resolved, Stored(1).ResolvedAt);

        var reopened = NewIncident(1, (int)IntStatus.Active);
        reopened.ResolvedAt = ClientSent;
        await _svc.UpdateAsync(reopened, Admin);
        Assert.Null(Stored(1).ResolvedAt);
    }

    /// <summary>An incident closed before the column existed has no recorded closure; an edit must not invent one.</summary>
    [Fact]
    public async Task TestALegacyClosedIncidentKeepsItsUnknownResolution()
    {
        Seed(ctx => ctx.Incidents.Add(NewIncident(1, (int)IntStatus.Closed)));

        var edit = NewIncident(1, (int)IntStatus.Closed);
        edit.Description = "edited";
        await _svc.UpdateAsync(edit, Admin);

        Assert.Null(Stored(1).ResolvedAt);
    }

    /// <summary>The rule on its own: "now", "previous" or "none" after a transition (null previous status = a create).</summary>
    [Theory]
    [InlineData(null, (int)IntStatus.Closed, "now")]
    [InlineData(null, (int)IntStatus.New, "none")]
    [InlineData((int)IntStatus.Active, (int)IntStatus.Closed, "now")]
    [InlineData((int)IntStatus.Closed, (int)IntStatus.Solved, "previous")]
    [InlineData((int)IntStatus.Closed, (int)IntStatus.Closed, "previous")]
    [InlineData((int)IntStatus.Closed, (int)IntStatus.Active, "none")]
    [InlineData((int)IntStatus.New, (int)IntStatus.Active, "none")]
    public void TestResolvedAtAfterIsTheTransitionRule(int? before, int after, string expected)
    {
        var now = new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc);
        var previous = now.AddDays(-3);

        var result = IncidentsService.ResolvedAtAfter(before, before is null ? null : previous, after, now);

        Assert.Equal(expected switch { "now" => now, "previous" => previous, _ => (DateTime?)null }, result);
    }

    [Fact]
    public async Task TestTheSeverityIsStoredAndAnUndefinedOneIsRefused()
    {
        var incident = NewIncident(0, (int)IntStatus.New);
        incident.Severity = IncidentSeverity.High;
        var id = (await _svc.CreateAsync(incident, Admin)).Id;
        Assert.Equal(IncidentSeverity.High, Stored(id).Severity);

        var cleared = NewIncident(id, (int)IntStatus.New);
        await _svc.UpdateAsync(cleared, Admin);
        Assert.Null(Stored(id).Severity);

        var invalid = NewIncident(0, (int)IntStatus.New);
        invalid.Severity = (IncidentSeverity)9;
        await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.CreateAsync(invalid, Admin));
        var invalidUpdate = NewIncident(id, (int)IntStatus.New);
        invalidUpdate.Severity = (IncidentSeverity)0;
        await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.UpdateAsync(invalidUpdate, Admin));
    }

    /// <summary>Regression (D-15): incidents were outside the per-field trail; a status and severity edit left no row.</summary>
    [Fact]
    public async Task TestAnIncidentIsAuditedFieldByField()
    {
        Seed(ctx => ctx.Incidents.Add(NewIncident(1, (int)IntStatus.Active)));

        var edit = NewIncident(1, (int)IntStatus.Closed);
        edit.Severity = IncidentSeverity.Critical;
        await _svc.UpdateAsync(edit, Admin);

        using var db = OpenContext();
        var rows = db.AuditLogs.Where(a => a.EntityType == nameof(Incident) && a.EntityId == 1).ToList();

        Assert.Contains(nameof(Incident), GovernanceAuditInterceptor.AuditedTypes);
        Assert.Contains(rows, r => r.Action == AuditLogAction.Update && r.Field == nameof(Incident.Status)
                                   && r.OldValue == ((int)IntStatus.Active).ToString() && r.NewValue == ((int)IntStatus.Closed).ToString());
        Assert.Contains(rows, r => r.Field == nameof(Incident.Severity) && r.OldValue == null && r.NewValue == "Critical");
        Assert.Contains(rows, r => r.Field == nameof(Incident.ResolvedAt) && r.OldValue == null && r.NewValue != null);
        Assert.DoesNotContain(rows, r => r.Field == nameof(Incident.LastUpdate));
    }

    /// <summary>The external reporter's typed name is personal data the trail has no purpose for (LGPD art. 6, III).</summary>
    [Fact]
    public async Task TestTheReporterNameNeverReachesTheTrail()
    {
        var incident = NewIncident(0, (int)IntStatus.New);
        incident.ReportedBy = "Jane Roe";
        var id = (await _svc.CreateAsync(incident, Admin)).Id;

        var edit = NewIncident(id, (int)IntStatus.New);
        edit.ReportedBy = "John Doe";
        await _svc.UpdateAsync(edit, Admin);

        using var db = OpenContext();
        var rows = db.AuditLogs.Where(a => a.EntityType == nameof(Incident)).ToList();

        Assert.Contains(rows, r => r.Action == AuditLogAction.Create);
        Assert.DoesNotContain(rows, r => (r.NewValue ?? "").Contains("Jane Roe") || (r.OldValue ?? "").Contains("Jane Roe"));
        Assert.DoesNotContain(rows, r => r.Field == nameof(Incident.ReportedBy));
    }

    /// <summary>An incident's trail is read through the incident's scope: another unit's is not found.</summary>
    [Fact]
    public async Task TestAnIncidentsTrailIsReadInsideItsEntityOnly()
    {
        SeedUnscoped(ctx =>
        {
            ctx.Incidents.Add(NewIncident(1, (int)IntStatus.New, Unit));
            ctx.Incidents.Add(NewIncident(2, (int)IntStatus.New, OtherUnit));
        });
        var trail = GetService<IAuditTrailService>();

        ScopeTo(Unit);

        Assert.Single(await trail.GetForRecordAsync(nameof(Incident), 1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => trail.GetForRecordAsync(nameof(Incident), 2));
    }
}

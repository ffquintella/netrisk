using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Auditing;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;
using HostServiceEntity = DAL.Entities.HostsService;

namespace ServerServices.Tests.Track8;

/// <summary>
/// S38 §5.4 (T234) — hosts and their services join the field-level trail, minus the two columns
/// every import pass stamps. The History tab is only worth opening if an edit a person made is not
/// buried under one row per host per scan, so the "writes nothing" cases matter as much as the
/// "writes a row" ones.
/// </summary>
[TestSubject(typeof(GovernanceAuditInterceptor))]
public class HostAuditTrailInMemoryTest : InMemoryServiceTestBase
{
    private readonly IAuditTrailService _trail;
    private readonly IHostsService _hosts;

    private static readonly DateTime Registered = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>The CLR name the interceptor records; <c>nameof</c> on the alias would say the alias.</summary>
    private const string HostServiceType = nameof(DAL.Entities.HostsService);

    public HostAuditTrailInMemoryTest()
    {
        _trail = GetService<IAuditTrailService>();
        _hosts = GetService<IHostsService>();
    }

    private static Host NewHost(int id) => new()
    {
        Id = id, HostName = $"host-{id}", Ip = $"10.0.0.{id}", Status = 42, Source = "manual",
        RegistrationDate = Registered, Criticality = 3, Environment = "Homolog", Owner = "Ana"
    };

    private int HostRowCount() => CountRows(nameof(Host));

    private int CountRows(string entityType)
    {
        using var db = OpenContext();
        return db.AuditLogs.Count(a => a.EntityType == entityType);
    }

    [Fact]
    public void TestHostsAndTheirServicesAreInTheAuditedScope()
    {
        Assert.Contains(nameof(Host), GovernanceAuditInterceptor.AuditedTypes);
        Assert.Contains(HostServiceType, GovernanceAuditInterceptor.AuditedTypes);

        // Still an allowlist: findings stay out, or every import writes a row per finding.
        Assert.DoesNotContain(nameof(Vulnerability), GovernanceAuditInterceptor.AuditedTypes);
    }

    [Fact]
    public async Task TestCreatingAHostWritesOneSummaryRow()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1)));

        var row = Assert.Single(await _trail.GetForRecordAsync(nameof(Host), 1));

        Assert.Equal(AuditLogAction.Create, row.Action);
        Assert.Contains("HostName=host-1", row.NewValue);
    }

    [Fact]
    public async Task TestEditingAHostRecordsEachChangedFieldInOneCorrelatedSave()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1)));

        await using (var db = OpenContext())
        {
            var host = db.Hosts.Single(h => h.Id == 1);
            host.Criticality = 5;
            host.Owner = "Bruno";
            host.Environment = "Produção";
            await db.SaveChangesAsync();
        }

        var updates = (await _trail.GetForRecordAsync(nameof(Host), 1))
            .Where(r => r.Action == AuditLogAction.Update)
            .ToList();

        Assert.Equal(3, updates.Count);

        var criticality = Assert.Single(updates, r => r.Field == nameof(Host.Criticality));
        Assert.Equal("3", criticality.OldValue);
        Assert.Equal("5", criticality.NewValue);

        var owner = Assert.Single(updates, r => r.Field == nameof(Host.Owner));
        Assert.Equal("Ana", owner.OldValue);
        Assert.Equal("Bruno", owner.NewValue);

        Assert.Single(updates, r => r.Field == nameof(Host.Environment) && r.NewValue == "Produção");
        Assert.Single(updates.Select(r => r.CorrelationId).Distinct());
    }

    /// <summary>The PUT /Hosts/{id} path: the service adapts the posted host onto the tracked row.</summary>
    [Fact]
    public async Task TestAnEditThroughTheHostsServiceIsRecorded()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1)));

        var edited = NewHost(1);
        edited.Owner = "Carla";
        _hosts.Update(edited);

        var rows = await _trail.GetForRecordAsync(nameof(Host), 1);

        var owner = Assert.Single(rows, r => r.Action == AuditLogAction.Update);
        Assert.Equal(nameof(Host.Owner), owner.Field);
        Assert.Equal("Carla", owner.NewValue);
    }

    /// <summary>Every scan stamps it; a row per host per scan would drown the trail.</summary>
    [Fact]
    public void TestALastVerificationDateOnlyChangeWritesNoRow()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1)));
        var before = HostRowCount();

        using (var db = OpenContext())
        {
            db.Hosts.Single(h => h.Id == 1).LastVerificationDate = DateTime.UtcNow;
            db.SaveChanges();
        }

        Assert.Equal(before, HostRowCount());
    }

    [Fact]
    public void TestARiskScoreTimestampOnlyChangeWritesNoRow()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1)));
        var before = HostRowCount();

        using (var db = OpenContext())
        {
            db.Hosts.Single(h => h.Id == 1).RiskScoreUpdatedAt = DateTime.UtcNow;
            db.SaveChanges();
        }

        Assert.Equal(before, HostRowCount());
    }

    /// <summary>The score itself is signal; only its timestamp is churn.</summary>
    [Fact]
    public async Task TestARiskScoreChangeIsRecordedWithoutItsTimestamp()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1)));

        await using (var db = OpenContext())
        {
            var host = db.Hosts.Single(h => h.Id == 1);
            host.RiskScore = 72;
            host.RiskScoreUpdatedAt = DateTime.UtcNow;
            host.LastVerificationDate = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }

        var update = Assert.Single(await _trail.GetForRecordAsync(nameof(Host), 1),
            r => r.Action == AuditLogAction.Update);
        Assert.Equal(nameof(Host.RiskScore), update.Field);
        Assert.Equal("72", update.NewValue);
    }

    [Fact]
    public async Task TestAHostServiceEditIsRecordedUnderItsOwnType()
    {
        Seed(ctx =>
        {
            ctx.Hosts.Add(NewHost(1));
            ctx.HostsServices.Add(new HostServiceEntity
            {
                Id = 7, HostId = 1, Name = "http", Protocol = "tcp", Port = 80
            });
        });

        await using (var db = OpenContext())
        {
            db.HostsServices.Single(s => s.Id == 7).Port = 8080;
            await db.SaveChangesAsync();
        }

        var rows = await _trail.GetForRecordAsync(HostServiceType, 7);

        Assert.Single(rows, r => r.Action == AuditLogAction.Create);
        var port = Assert.Single(rows, r => r.Action == AuditLogAction.Update);
        Assert.Equal(nameof(HostServiceEntity.Port), port.Field);
        Assert.Equal("80", port.OldValue);
        Assert.Equal("8080", port.NewValue);
    }

    [Fact]
    public async Task TestDeletingAHostWritesADeleteRow()
    {
        Seed(ctx => ctx.Hosts.Add(NewHost(1)));

        _hosts.Delete(1);

        Assert.Single(await _trail.GetForRecordAsync(nameof(Host), 1), r => r.Action == AuditLogAction.Delete);
    }
}

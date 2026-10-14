using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DAL.Auditing;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model.Entities;
using Model.Exceptions;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Tests.Mock;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Security;

/// <summary>
/// T241 (S39 D-17, S40 §7.5): the entity map is in the field-level audit trail — a change of parent (which moves
/// access, T292) and every property value, the risk-context inputs among them — and the trail of an entity is read
/// by a scoped caller only inside their own subtree.
/// </summary>
[TestSubject(typeof(GovernanceAuditInterceptor))]
public class EntityAuditTrailTest : InMemoryServiceTestBase
{
    private const int Org = 1, UnitX = 10, Process = 12, UnitY = 20;

    [ThreadStatic] private static PrincipalDalService? _building;

    private readonly PrincipalDalService _dal;

    public EntityAuditTrailTest() : base(name => _building = new PrincipalDalService(name, new FixedHttpContextAccessor()))
    {
        _dal = _building!;

        _dal.ActAs(new Claim("scope", "global"));
        using var db = _dal.GetContext();
        // The user the principal's Sid names, so the trail's Include of its author finds a row.
        db.Users.Add(new User
        {
            Value = 1, Enabled = true, Name = "scoped", Login = "scoped", Email = "s@x.test", Type = "local",
            Password = "secret"u8.ToArray(), RoleId = 1
        });
        db.Entities.AddRange(NewEntity(Org, null), NewEntity(UnitX, Org), NewEntity(Process, UnitX), NewEntity(UnitY, Org));
        db.EntitiesProperties.Add(new EntitiesProperty
        {
            Id = 500, Entity = Process, Type = RiskContextProperties.Criticality, Name = "criticality-12", Value = "3",
            OldValue = ""
        });
        db.SaveChanges();
    }

    private static Entity NewEntity(int id, int? parent) => new()
    {
        Id = id, DefinitionName = "organizationUnit", DefinitionVersion = "2.6", Status = "active", Parent = parent,
        Created = DateTime.UtcNow, Updated = DateTime.UtcNow
    };

    [Fact]
    public void TestTheEntityMapIsAudited()
    {
        Assert.Contains(nameof(Entity), GovernanceAuditInterceptor.AuditedTypes);
        Assert.Contains(nameof(EntitiesProperty), GovernanceAuditInterceptor.AuditedTypes);
    }

    [Fact]
    public void TestARe_parentAndAPropertyChangeEachWriteAnAttributedRow()
    {
        _dal.ActAs(new Claim("scope", "global"));
        using (var db = _dal.GetContext())
        {
            db.Entities.Single(e => e.Id == Process).Parent = UnitY;
            var criticality = db.EntitiesProperties.Single(p => p.Id == 500);
            criticality.OldValue = criticality.Value;
            criticality.Value = "5";
            db.SaveChanges();
        }

        using var read = _dal.GetContext(bypassEntityScope: true);
        var rows = read.AuditLogs.AsNoTracking().ToList();

        var parent = Assert.Single(rows, r => r.EntityType == nameof(Entity) && r.Field == nameof(Entity.Parent));
        Assert.Equal((Process, "10", "20", AuditLogAction.Update, "scoped"),
            (parent.EntityId, parent.OldValue, parent.NewValue, parent.Action, parent.Actor));

        var value = Assert.Single(rows, r => r.EntityType == nameof(EntitiesProperty) && r.Field == nameof(EntitiesProperty.Value));
        Assert.Equal((500, "3", "5"), (value.EntityId, value.OldValue, value.NewValue));

        // The churn stamp is not a row of its own.
        Assert.DoesNotContain(rows, r => r.EntityType == nameof(Entity) && r.Field == nameof(Entity.Updated));
    }

    [Fact]
    public void TestCreatingAndDeletingAPropertyAreRecorded()
    {
        _dal.ActAs(new Claim("scope", "global"));
        using (var db = _dal.GetContext())
        {
            db.EntitiesProperties.Add(new EntitiesProperty
            {
                Id = 501, Entity = Process, Type = RiskContextProperties.InternetFacing, Name = "internetFacing-12",
                Value = "true", OldValue = ""
            });
            db.EntitiesProperties.Remove(db.EntitiesProperties.Single(p => p.Id == 500));
            db.SaveChanges();
        }

        using var read = _dal.GetContext(bypassEntityScope: true);
        var rows = read.AuditLogs.AsNoTracking().Where(r => r.EntityType == nameof(EntitiesProperty)).ToList();

        Assert.Contains(rows, r => r.Action == AuditLogAction.Create && r.EntityId == 501 && r.NewValue!.Contains("internetFacing"));
        Assert.Contains(rows, r => r.Action == AuditLogAction.Delete && r.EntityId == 500 && r.OldValue!.Contains("Value=3"));
    }

    private async Task ChangeCriticality()
    {
        _dal.ActAs(new Claim("scope", "global"));
        await using var db = _dal.GetContext();
        db.EntitiesProperties.Single(p => p.Id == 500).Value = "4";
        db.Entities.Single(e => e.Id == Process).Status = "inactive";
        await db.SaveChangesAsync();
    }

    /// <summary>A scoped reader gets the trail of an entity in their subtree (T292), and of its properties.</summary>
    [Fact]
    public async Task TestAScopedReaderReadsTheTrailInsideTheirSubtree()
    {
        await ChangeCriticality();
        _dal.ActAs(new Claim("entity_id", UnitX.ToString()));
        var trail = GetService<IAuditTrailService>();

        Assert.NotEmpty(await trail.GetForRecordAsync(nameof(Entity), Process));
        Assert.NotEmpty(await trail.GetForRecordAsync(nameof(EntitiesProperty), 500));
    }

    /// <summary>...and not of one outside it, though the map itself is readable by every authenticated user.</summary>
    [Fact]
    public async Task TestAScopedReaderIsRefusedTheTrailOutsideTheirSubtree()
    {
        await ChangeCriticality();
        _dal.ActAs(new Claim("entity_id", UnitY.ToString()));
        var trail = GetService<IAuditTrailService>();

        await Assert.ThrowsAsync<DataNotFoundException>(() => trail.GetForRecordAsync(nameof(Entity), Process));
        await Assert.ThrowsAsync<DataNotFoundException>(() => trail.GetForRecordAsync(nameof(EntitiesProperty), 500));
        await Assert.ThrowsAsync<DataNotFoundException>(() => trail.GetForRecordAsync(nameof(Entity), UnitX));
    }

    private const int Person = 30, PersonEmail = 600, PersonPhone = 601, UnitDescription = 602, UnitName = 603;

    private void SeedPersonAndUnitDetails()
    {
        _dal.ActAs(new Claim("scope", "global"));
        using var db = _dal.GetContext();
        db.Entities.Add(new Entity
        {
            Id = Person, DefinitionName = "person", DefinitionVersion = "2.6", Status = "active", Parent = null,
            Created = DateTime.UtcNow, Updated = DateTime.UtcNow
        });
        db.EntitiesProperties.AddRange(
            new EntitiesProperty { Id = PersonEmail, Entity = Person, Type = "email", Name = "email-30", Value = "ana@old.test", OldValue = "" },
            new EntitiesProperty { Id = PersonPhone, Entity = Person, Type = "phoneNumber", Name = "phoneNumber-30", Value = "+55 21 5555-0101", OldValue = "" },
            new EntitiesProperty { Id = UnitDescription, Entity = UnitX, Type = "description", Name = "description-10", Value = "first", OldValue = "" },
            new EntitiesProperty { Id = UnitName, Entity = UnitX, Type = "name", Name = "name-10", Value = "Unit X", OldValue = "" });
        db.SaveChanges();
    }

    private List<AuditLog> PropertyRows(int propertyId)
    {
        using var read = _dal.GetContext(bypassEntityScope: true);
        return read.AuditLogs.AsNoTracking()
            .Where(r => r.EntityType == nameof(EntitiesProperty) && r.EntityId == propertyId)
            .ToList();
    }

    private List<AuditLog> AllRows()
    {
        using var read = _dal.GetContext(bypassEntityScope: true);
        return read.AuditLogs.AsNoTracking().ToList();
    }

    /// <summary>
    /// The review's LGPD finding: auditing every <see cref="EntitiesProperty"/> copied a person's email and phone into
    /// <c>audit_logs</c> — on creation, on every change and on deletion — where nobody may delete them. A person's
    /// property rows are never recorded now; the person's own entity row, which holds no personal value, still is.
    /// </summary>
    [Fact]
    public void TestAPersonsContactDetailsNeverReachTheTrail()
    {
        SeedPersonAndUnitDetails();

        _dal.ActAs(new Claim("scope", "global"));
        using (var db = _dal.GetContext())
        {
            var email = db.EntitiesProperties.Single(p => p.Id == PersonEmail);
            email.OldValue = email.Value;
            email.Value = "ana@new.test";
            db.EntitiesProperties.Remove(db.EntitiesProperties.Single(p => p.Id == PersonPhone));
            db.SaveChanges();
        }

        var rows = AllRows();
        Assert.Empty(PropertyRows(PersonEmail));
        Assert.Empty(PropertyRows(PersonPhone));
        Assert.DoesNotContain(rows, r => (r.OldValue ?? "").Contains("ana@") || (r.NewValue ?? "").Contains("ana@"));
        Assert.DoesNotContain(rows, r => (r.OldValue ?? "").Contains("5555-0101") || (r.NewValue ?? "").Contains("5555-0101"));

        // The person's entity row itself is still attributed: its creation names no personal value.
        Assert.Contains(rows, r => r.EntityType == nameof(Entity) && r.EntityId == Person && r.Action == AuditLogAction.Create);
    }

    /// <summary>...while a criticality change on the same save path is still recorded with both values.</summary>
    [Fact]
    public void TestACriticalityChangeIsStillRecordedWithItsValues()
    {
        _dal.ActAs(new Claim("scope", "global"));
        using (var db = _dal.GetContext())
        {
            db.EntitiesProperties.Single(p => p.Id == 500).Value = "1";
            db.SaveChanges();
        }

        var value = Assert.Single(PropertyRows(500), r => r.Field == nameof(EntitiesProperty.Value));
        Assert.Equal(("3", "1", AuditLogAction.Update), (value.OldValue, value.NewValue, value.Action));
    }

    /// <summary>
    /// Only the risk-context inputs and the name are recorded (S39 D-17, S40 §7.5): a free-text property of a unit is out,
    /// its name is in.
    /// </summary>
    [Fact]
    public void TestOnlyRiskContextAndNamePropertiesAreRecorded()
    {
        SeedPersonAndUnitDetails();

        _dal.ActAs(new Claim("scope", "global"));
        using (var db = _dal.GetContext())
        {
            db.EntitiesProperties.Single(p => p.Id == UnitDescription).Value = "second";
            db.EntitiesProperties.Single(p => p.Id == UnitName).Value = "Unit X (renamed)";
            db.SaveChanges();
        }

        Assert.Empty(PropertyRows(UnitDescription));
        var name = Assert.Single(PropertyRows(UnitName), r => r.Field == nameof(EntitiesProperty.Value));
        Assert.Equal(("Unit X", "Unit X (renamed)"), (name.OldValue, name.NewValue));
    }

    /// <summary>DAL cannot reference <see cref="RiskContextProperties"/>, so its copy of the names is pinned here.</summary>
    [Fact]
    public void TestTheAuditedPropertyKeysCoverEveryRiskContextProperty()
    {
        Assert.Subset(GovernanceAuditInterceptor.AuditedEntityPropertyKeys.ToHashSet(), RiskContextProperties.Names.ToHashSet());
    }

    [Fact]
    public async Task TestAnUnrestrictedReaderReadsEveryEntitysTrail()
    {
        await ChangeCriticality();
        _dal.ActAs(new Claim("scope", "global"));

        Assert.NotEmpty(await GetService<IAuditTrailService>().GetForRecordAsync(nameof(Entity), Process));
    }
}

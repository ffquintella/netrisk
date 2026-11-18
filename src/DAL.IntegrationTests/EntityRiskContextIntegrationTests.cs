using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Entities;
using MySqlConnector;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace DAL.IntegrationTests;

/// <summary>
/// M55, T264 (S40 §8.5) on a real MariaDB at the target schema: the batch's queries translate, its notes and audit rows
/// share the batch id, a set property's create row (a provider temporary key) still reaches the history, a failure inside
/// the save rolls everything back, and deleting an entity takes its notes. Needs Docker.
/// </summary>
[Collection("mariadb")]
[Trait("Category", "Integration")]
public class EntityRiskContextIntegrationTests(MariaDbContainerFixture fixture)
{
    private const int Org = 7100, Unit = 7101, Process = 7102, App = 7103, Restricted = 7104;
    private const string Reason = "Calibrated with the business owner in the BIA review";

    private sealed class ContainerDal(MariaDbContainerFixture f) : IDalService
    {
        public AuditableContext GetContext(bool withIdentity = true, bool bypassEntityScope = false) => f.NewContext();

        public EntityScope GetCurrentEntityScope() => EntityScope.Unrestricted;
    }

    private IEntitiesService NewService() => new EntitiesService(Substitute.For<Serilog.ILogger>(), new ContainerDal(fixture));

    private async Task<int> SeedAsync()
    {
        await fixture.InitializeNumberedSchemaAsync(MariaDbContainerFixture.TargetSchemaVersion);

        await using var conn = new MySqlConnection(fixture.ConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new MySqlCommand($@"
            INSERT INTO entities (Id, DefinitionName, DefinitionVersion, Created, Updated, CreatedBy, UpdatedBy, Status, Parent) VALUES
              ({Org},'organization','2.7',NOW(),NOW(),1,1,'active',NULL),
              ({Unit},'organizationUnit','2.7',NOW(),NOW(),1,1,'active',{Org}),
              ({Process},'businessProcess','2.7',NOW(),NOW(),1,1,'active',{Unit}),
              ({App},'application','2.7',NOW(),NOW(),1,1,'active',{Unit}),
              ({Restricted},'securityClassificationLevel','2.7',NOW(),NOW(),1,1,'active',NULL);
            INSERT INTO entities_properties (Type, Value, OldValue, Entity, Name) VALUES
              ('name','Process','',{Process},'name-{Process}'), ('name','App','',{App},'name-{App}'),
              ('criticality','3','',{Process},'criticality-{Process}'),
              ('internetFacing','false','',{App},'internetFacing-{App}');", conn))
            await cmd.ExecuteNonQueryAsync();

        await using var db = fixture.NewContext();
        var role = new Role { Name = "rc-" + Guid.NewGuid().ToString("N")[..8] };
        var user = new User
        {
            Name = "rc", Login = "rc" + Guid.NewGuid().ToString("N")[..8], Enabled = true, Type = "local", Salt = "s",
            Password = "p"u8.ToArray(), Email = "rc@example.invalid", Role = role
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user.Value;
    }

    [Fact]
    public async Task TestABatchCommitsItsValuesNotesAndAuditRowsUnderOneIdAndReadsBack()
    {
        var author = await SeedAsync();
        var service = NewService();

        var result = await service.SaveRiskContextAsync(new EntityRiskContextBatchRequest(Reason,
            [new EntityRiskContextChange(Process, 5, null, null), new EntityRiskContextChange(App, 4, true, Restricted)]),
            author, holdsRiskContextPermission: true);

        Assert.Equal(2, result.UpdatedEntities);
        await using (var db = fixture.NewContext())
        {
            var notes = await db.EntityRiskContextNotes.AsNoTracking().ToListAsync();
            Assert.Equal(new[] { Process, App }.Order(), notes.Select(n => n.EntityId).Order());
            Assert.All(notes, n => Assert.Equal((result.BatchId, Reason, (int?)author), (n.BatchId, n.Justification, n.CreatedById)));

            var trail = await db.AuditLogs.AsNoTracking().Where(a => a.EntityType == nameof(EntitiesProperty)).ToListAsync();
            Assert.NotEmpty(trail);
            Assert.All(trail, a => Assert.Equal(result.BatchId, a.CorrelationId));
        }

        var rows = await service.GetRiskContextAsync(["businessProcess", "application"]);
        Assert.Equal((int?)5, rows.Single(r => r.EntityId == Process).Criticality);
        var app = rows.Single(r => r.EntityId == App);
        Assert.Equal(((int?)4, (bool?)true, (int?)Restricted, "system"),
            (app.Criticality, app.InternetFacing, app.SecurityClassificationId, app.LastChangedBy));

        // The criticality row was created in the save, so its audit row was keyed by a temporary id: only the dump links it.
        var history = await service.GetHistoryAsync(App, 200, isAdmin: true, []);
        var set = Assert.Single(history, h => h.Property == RiskContextProperties.Criticality);
        Assert.Equal(("Create", "4", result.BatchId, Reason), (set.Action, set.NewValue, set.CorrelationId, set.Justification));
        Assert.Contains(history, h => h is { Property: RiskContextProperties.InternetFacing, OldValue: "false", NewValue: "true" });
    }

    [Fact]
    public async Task TestAFailureInsideTheSaveWritesNothing()
    {
        await SeedAsync();

        // A note author that is no user: the note's foreign key fails inside the one save that also writes the value.
        await Assert.ThrowsAsync<DbUpdateException>(() => NewService().SaveRiskContextAsync(
            new EntityRiskContextBatchRequest(Reason, [new EntityRiskContextChange(Process, 5, null, null)]),
            userId: 987654, holdsRiskContextPermission: true));

        await using var db = fixture.NewContext();
        Assert.Equal("3", await db.EntitiesProperties.Where(p => p.Entity == Process && p.Type == "criticality")
            .Select(p => p.Value).SingleAsync());
        Assert.Empty(await db.EntityRiskContextNotes.ToListAsync());
        Assert.False(await db.AuditLogs.AnyAsync(a => a.EntityType == nameof(EntitiesProperty) && a.Action == AuditLogAction.Update));
    }

    [Fact]
    public async Task TestDeletingAnEntityTakesItsNotes()
    {
        var author = await SeedAsync();
        await NewService().SaveRiskContextAsync(
            new EntityRiskContextBatchRequest(Reason, [new EntityRiskContextChange(App, null, true, null)]), author, true);

        await using (var conn = new MySqlConnection(fixture.ConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new MySqlCommand($"DELETE FROM entities_properties WHERE Entity = {App}; DELETE FROM entities WHERE Id = {App};", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        await using var db = fixture.NewContext();
        Assert.Empty(await db.EntityRiskContextNotes.ToListAsync());
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model.Entities;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.Mock;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

/// <summary>
/// M55, T264 (S40 §6.7, §8.5): the Risk context rows, the all-or-nothing batch with its notes and correlation id, and the
/// entity history — on the real <see cref="EntitiesService"/>, the real entities configuration and the production scope
/// derivation (<see cref="PrincipalDalService"/>), so the audit rows asserted are the interceptor's own.
/// </summary>
[TestSubject(typeof(EntitiesService))]
public class EntityRiskContextServiceTest : InMemoryServiceTestBase
{
    //   Org 1 ─┬─ Unit X 10 ─┬─ Process 12 (criticality 3) ── Activity 40
    //          │             └─ App 30 (internetFacing false, classification 51)
    //          └─ Unit Y 20
    //   Classification levels 50 (Restricted), 51 (Public); person 60.
    private const int Org = 1, UnitX = 10, Process = 12, UnitY = 20, App = 30, Activity = 40, Restricted = 50,
        Public = 51, Person = 60;

    private const int User = 1;
    private const string Reason = "  Calibrated with the business owner in the 2026 BIA review  ";
    private static readonly string[] AllPermissions = [EntityPermissions.RiskContext];

    [ThreadStatic] private static PrincipalDalService? _building;

    private readonly PrincipalDalService _dal;
    private readonly IEntitiesService _service;

    public EntityRiskContextServiceTest() : base(name => _building = new PrincipalDalService(name, new FixedHttpContextAccessor()))
    {
        _dal = _building!;
        _service = GetService<IEntitiesService>();

        _dal.ActAs(new Claim("scope", "global"));
        using var db = _dal.GetContext();
        db.Entities.AddRange(NewEntity(Org, "organization", null), NewEntity(UnitX, "organizationUnit", Org),
            NewEntity(Process, "businessProcess", UnitX), NewEntity(UnitY, "organizationUnit", Org),
            NewEntity(App, "application", UnitX), NewEntity(Activity, "activity", Process),
            NewEntity(Restricted, "securityClassificationLevel", null), NewEntity(Public, "securityClassificationLevel", null),
            NewEntity(Person, "person", null));
        foreach (var id in new[] { Org, UnitX, Process, UnitY, App, Activity, Restricted, Public, Person })
            db.EntitiesProperties.Add(Property(id, "name", $"entity {id}"));
        db.EntitiesProperties.Add(Property(Process, RiskContextProperties.Criticality, "3", 500));
        db.EntitiesProperties.Add(Property(App, RiskContextProperties.InternetFacing, "false", 501));
        db.EntitiesProperties.Add(Property(App, RiskContextProperties.SecurityClassification, Public.ToString(), 502));
        db.SaveChanges();

        // The seed is not under test: every test starts from an empty trail.
        db.AuditLogs.RemoveRange(db.AuditLogs.ToList());
        db.SaveChanges();
    }

    private static Entity NewEntity(int id, string definition, int? parent) => new()
    {
        Id = id, DefinitionName = definition, DefinitionVersion = "2.7", Status = "active", Parent = parent,
        Created = DateTime.UtcNow, Updated = DateTime.UtcNow, CreatedBy = 7, UpdatedBy = 7
    };

    private static EntitiesProperty Property(int entity, string type, string value, int id = 0) => new()
    {
        Id = id, Entity = entity, Type = type, Name = $"{type}-{entity}", Value = value, OldValue = ""
    };

    private static EntityRiskContextBatchRequest Batch(params EntityRiskContextChange[] changes) => new(Reason, changes);

    private Task<EntityRiskContextBatchResultDto> Save(EntityRiskContextBatchRequest request, bool permission = true) =>
        _service.SaveRiskContextAsync(request, User, permission);

    private List<EntitiesProperty> Stored(int entity)
    {
        using var db = _dal.GetContext(bypassEntityScope: true);
        return db.EntitiesProperties.AsNoTracking().Where(p => p.Entity == entity).ToList();
    }

    private string? ValueOf(int entity, string key) => Stored(entity).SingleOrDefault(p => p.Type == key)?.Value;

    private List<EntityRiskContextNote> Notes()
    {
        using var db = _dal.GetContext(bypassEntityScope: true);
        return db.EntityRiskContextNotes.AsNoTracking().ToList();
    }

    private async Task<EntityWriteRefusedException> Refused(EntityRiskContextBatchRequest request, bool permission = true) =>
        await Assert.ThrowsAsync<EntityWriteRefusedException>(() => Save(request, permission));

    private void AssertNothingWritten()
    {
        Assert.Equal("3", ValueOf(Process, RiskContextProperties.Criticality));
        Assert.Equal("false", ValueOf(App, RiskContextProperties.InternetFacing));
        Assert.Empty(Notes());
        using var db = _dal.GetContext(bypassEntityScope: true);
        Assert.DoesNotContain(db.AuditLogs.AsNoTracking(), a => a.EntityType == nameof(EntitiesProperty) && a.Action == AuditLogAction.Update);
    }

    // --- GET /Entities/RiskContext ----------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheDefaultRowsAreTheDefaultTypesWithWhatEachDefinitionSupports()
    {
        var rows = await _service.GetRiskContextAsync();

        Assert.Equal(new[] { Org, UnitX, Process, UnitY, App, Activity }.Order(), rows.Select(r => r.EntityId).Order());

        var process = rows.Single(r => r.EntityId == Process);
        Assert.Equal(("entity 12", "businessProcess", (int?)UnitX), (process.Name, process.DefinitionName, process.ParentId));
        Assert.Equal((true, (int?)3, false, (bool?)null, false, (int?)null),
            (process.SupportsCriticality, process.Criticality, process.SupportsInternetFacing, process.InternetFacing,
                process.SupportsSecurityClassification, process.SecurityClassificationId));

        var app = rows.Single(r => r.EntityId == App);
        Assert.Equal((true, (int?)null, true, (bool?)false, true, (int?)Public),
            (app.SupportsCriticality, app.Criticality, app.SupportsInternetFacing, app.InternetFacing,
                app.SupportsSecurityClassification, app.SecurityClassificationId));

        var activity = rows.Single(r => r.EntityId == Activity);
        Assert.False(activity.SupportsCriticality || activity.SupportsInternetFacing || activity.SupportsSecurityClassification);

        // Nothing changed through the trail yet.
        Assert.All(rows, r => Assert.Null(r.LastChangedAtUtc));
    }

    [Fact]
    public async Task TestTheTypesFilterKeepsOnlyDefinedTypes()
    {
        var rows = await _service.GetRiskContextAsync(["application", " bogus ", ""]);

        Assert.Equal(App, Assert.Single(rows).EntityId);
    }

    [Fact]
    public async Task TestOnlyUnknownTypesReadNothing()
    {
        Assert.Empty(await _service.GetRiskContextAsync(["bogus"]));
    }

    [Fact]
    public async Task TestTheLastChangeIsTheNewestAuditRowOfTheContext()
    {
        await Save(Batch(new EntityRiskContextChange(App, 4, true, Public)));

        var app = (await _service.GetRiskContextAsync(["application"])).Single();

        Assert.Equal(("scoped", DateTimeKind.Utc), (app.LastChangedBy, app.LastChangedAtUtc!.Value.Kind));
        Assert.Equal((4, true), (app.Criticality, app.InternetFacing));
    }

    [Fact]
    public async Task TestAClearedValueIsStillTheLastChange()
    {
        await Save(Batch(new EntityRiskContextChange(Process, null, null, null)));

        var process = (await _service.GetRiskContextAsync(["businessProcess"])).Single();

        Assert.Null(process.Criticality);
        Assert.Equal("scoped", process.LastChangedBy);
    }

    // --- PUT /Entities/RiskContext: the happy path ------------------------------------------------------------------

    [Fact]
    public async Task TestABatchAppliesEveryChangeInOneSaveWithOneBatchId()
    {
        var result = await Save(Batch(
            new EntityRiskContextChange(Process, 5, null, null),
            new EntityRiskContextChange(App, 2, true, Restricted)));

        Assert.Equal(2, result.UpdatedEntities);
        Assert.Equal(32, result.BatchId.Length);
        Assert.Equal("5", ValueOf(Process, RiskContextProperties.Criticality));
        Assert.Equal(("2", "true", Restricted.ToString()),
            (ValueOf(App, RiskContextProperties.Criticality), ValueOf(App, RiskContextProperties.InternetFacing),
                ValueOf(App, RiskContextProperties.SecurityClassification)));

        // The value changed in place: the row keeps its id and records what it held.
        var criticality = Stored(Process).Single(p => p.Type == RiskContextProperties.Criticality);
        Assert.Equal((500, "3"), (criticality.Id, criticality.OldValue));

        // One note per changed entity, the trimmed justification, the same batch id.
        var notes = Notes();
        Assert.Equal(new[] { Process, App }.Order(), notes.Select(n => n.EntityId).Order());
        Assert.All(notes, n => Assert.Equal((result.BatchId, Reason.Trim(), (int?)User),
            (n.BatchId, n.Justification, n.CreatedById)));

        // Every audit row of that save carries the batch id as its correlation id: the history's join to the note.
        using var db = _dal.GetContext(bypassEntityScope: true);
        var trail = db.AuditLogs.AsNoTracking().Where(a => a.EntityType == nameof(EntitiesProperty)).ToList();
        Assert.NotEmpty(trail);
        Assert.All(trail, a => Assert.Equal(result.BatchId, a.CorrelationId));
    }

    [Fact]
    public async Task TestAnUnchangedEntityIsNeitherWrittenNorCounted()
    {
        var result = await Save(Batch(
            new EntityRiskContextChange(Process, 3, null, null),
            new EntityRiskContextChange(App, null, true, Public)));

        Assert.Equal(1, result.UpdatedEntities);
        Assert.Equal(App, Assert.Single(Notes()).EntityId);
        Assert.Equal(500, Stored(Process).Single(p => p.Type == RiskContextProperties.Criticality).Id);
    }

    [Fact]
    public async Task TestABatchThatChangesNothingWritesNothing()
    {
        var result = await Save(Batch(new EntityRiskContextChange(Process, 3, null, null),
            new EntityRiskContextChange(Activity, null, null, null)));

        Assert.Equal(0, result.UpdatedEntities);
        AssertNothingWritten();
    }

    [Fact]
    public async Task TestNullClearsTheProperty()
    {
        var result = await Save(Batch(new EntityRiskContextChange(App, null, null, null)));

        Assert.Equal(1, result.UpdatedEntities);
        Assert.Null(ValueOf(App, RiskContextProperties.InternetFacing));
        Assert.Null(ValueOf(App, RiskContextProperties.SecurityClassification));
        Assert.Equal("entity 30", ValueOf(App, "name"));
    }

    // --- PUT /Entities/RiskContext: every refusal, and nothing written ---------------------------------------------

    [Fact]
    public async Task TestWithoutThePermissionTheBatchIsRefused()
    {
        var ex = await Refused(Batch(new EntityRiskContextChange(Process, 5, null, null)), permission: false);

        Assert.Equal((EntityWriteErrors.RiskContextRequiresPermission, 403), (ex.Error, ex.StatusCode));
        AssertNothingWritten();
    }

    [Fact]
    public async Task TestAScopedCallerIsRefusedEvenInsideTheirSubtree()
    {
        _dal.ActAs(new Claim("entity_id", UnitX.ToString()));

        var ex = await Refused(Batch(new EntityRiskContextChange(Process, 5, null, null)));

        Assert.Equal((EntityWriteErrors.RiskContextRequiresGlobalScope, 403), (ex.Error, ex.StatusCode));
        _dal.ActAs(new Claim("scope", "global"));
        AssertNothingWritten();
    }

    [Theory]
    [InlineData("too short, 19 chars", EntityRiskContextErrors.JustificationTooShort)]
    [InlineData("                                    short                                ", EntityRiskContextErrors.JustificationTooShort)]
    [InlineData("", EntityRiskContextErrors.JustificationTooShort)]
    public async Task TestAShortJustificationIsRefused(string justification, string error)
    {
        var ex = await Refused(new EntityRiskContextBatchRequest(justification, [new EntityRiskContextChange(Process, 5, null, null)]));

        Assert.Equal((error, 400), (ex.Error, ex.StatusCode));
        AssertNothingWritten();
    }

    [Fact]
    public async Task TestTheJustificationBoundsAreInclusive()
    {
        var twenty = new string('j', EntityRiskContextRules.MinimumJustificationLength);
        var max = new string('j', EntityRiskContextRules.MaximumJustificationLength);

        Assert.Equal(1, (await Save(new EntityRiskContextBatchRequest(twenty, [new EntityRiskContextChange(Process, 4, null, null)]))).UpdatedEntities);
        Assert.Equal(1, (await Save(new EntityRiskContextBatchRequest(max, [new EntityRiskContextChange(Process, 5, null, null)]))).UpdatedEntities);
    }

    [Fact]
    public async Task TestALongJustificationIsRefused()
    {
        var ex = await Refused(new EntityRiskContextBatchRequest(new string('j', EntityRiskContextRules.MaximumJustificationLength + 1),
            [new EntityRiskContextChange(Process, 5, null, null)]));

        Assert.Equal((EntityRiskContextErrors.JustificationTooLong, 400), (ex.Error, ex.StatusCode));
        AssertNothingWritten();
    }

    [Fact]
    public async Task TestAnEmptyBatchIsRefused()
    {
        var ex = await Refused(Batch());

        Assert.Equal((EntityRiskContextErrors.EmptyBatch, 400), (ex.Error, ex.StatusCode));
    }

    [Fact]
    public async Task TestABatchOverTheLimitIsRefused()
    {
        var changes = Enumerable.Range(1000, EntityRiskContextRules.MaximumBatchSize + 1)
            .Select(id => new EntityRiskContextChange(id, null, null, null)).ToArray();

        var ex = await Refused(Batch(changes));

        Assert.Equal((EntityRiskContextErrors.BatchTooLarge, 400), (ex.Error, ex.StatusCode));
    }

    [Fact]
    public async Task TestAnEntityTwiceInTheBatchIsRefused()
    {
        var ex = await Refused(Batch(new EntityRiskContextChange(Process, 5, null, null),
            new EntityRiskContextChange(Process, 4, null, null)));

        Assert.Equal((EntityRiskContextErrors.DuplicateEntity, 400, (int?)Process), (ex.Error, ex.StatusCode, ex.EntityId));
        AssertNothingWritten();
    }

    [Fact]
    public async Task TestAMissingEntityIsANotFoundNamingIt()
    {
        var ex = await Refused(Batch(new EntityRiskContextChange(Process, 5, null, null),
            new EntityRiskContextChange(999, 2, null, null)));

        Assert.Equal((EntityRiskContextErrors.EntityNotFound, 404, (int?)999), (ex.Error, ex.StatusCode, ex.EntityId));
        Assert.Contains("999", ex.Message);
        AssertNothingWritten();
    }

    [Theory]
    [InlineData(Activity, 3, null, null)]
    [InlineData(Process, null, true, null)]
    [InlineData(Process, null, null, Restricted)]
    [InlineData(Org, 2, null, null)]
    public async Task TestAFieldTheTypeDoesNotSupportIsRefused(int entity, int? criticality, bool? internet, int? classification)
    {
        var ex = await Refused(Batch(new EntityRiskContextChange(entity, criticality, internet, classification)));

        Assert.Equal((EntityRiskContextErrors.FieldNotSupportedForType, 400, (int?)entity), (ex.Error, ex.StatusCode, ex.EntityId));
        AssertNothingWritten();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    [InlineData(-1)]
    public async Task TestACriticalityOutsideOneToFiveIsRefused(int criticality)
    {
        var ex = await Refused(Batch(new EntityRiskContextChange(Process, criticality, null, null)));

        Assert.Equal((EntityRiskContextErrors.CriticalityOutOfRange, 400, (int?)Process), (ex.Error, ex.StatusCode, ex.EntityId));
        AssertNothingWritten();
    }

    [Theory]
    [InlineData(999)]
    [InlineData(UnitY)]
    public async Task TestAClassificationThatIsNotALevelIsRefused(int classification)
    {
        var ex = await Refused(Batch(new EntityRiskContextChange(App, null, null, classification)));

        Assert.Equal((EntityRiskContextErrors.ClassificationNotFound, 400, (int?)App), (ex.Error, ex.StatusCode, ex.EntityId));
        AssertNothingWritten();
    }

    /// <summary>All or nothing (S40 §8.5): a valid line before the refused one is not written either.</summary>
    [Fact]
    public async Task TestOneBadLineRefusesTheWholeBatch()
    {
        var ex = await Refused(Batch(
            new EntityRiskContextChange(Process, 5, null, null),
            new EntityRiskContextChange(App, 2, true, Restricted),
            new EntityRiskContextChange(Activity, 4, null, null)));

        Assert.Equal((EntityRiskContextErrors.FieldNotSupportedForType, (int?)Activity), (ex.Error, ex.EntityId));
        AssertNothingWritten();
        Assert.Null(ValueOf(App, RiskContextProperties.Criticality));
    }

    // --- GET /Entities/{id}/History ---------------------------------------------------------------------------------

    private Task<List<EntityHistoryEntryDto>> History(int entity, int limit = 200, string[]? permissions = null) =>
        _service.GetHistoryAsync(entity, limit, isAdmin: false, permissions ?? AllPermissions);

    [Fact]
    public async Task TestTheHistoryJoinsEachBatchToItsJustificationNewestFirst()
    {
        var first = await Save(Batch(new EntityRiskContextChange(App, 4, true, Public)));
        await Save(new EntityRiskContextBatchRequest("Second review by the risk committee, minute 2026/77",
            [new EntityRiskContextChange(App, 5, true, Public)]));

        // A change that is not a batch: a re-parent, written by the entity form path, has no justification.
        using (var db = _dal.GetContext())
        {
            db.Entities.Single(e => e.Id == App).Parent = UnitY;
            await db.SaveChangesAsync();
        }

        var history = await History(App);

        Assert.Equal(history.OrderByDescending(h => h.OccurredAtUtc).Select(h => h.OccurredAtUtc), history.Select(h => h.OccurredAtUtc));

        var parent = history.First();
        Assert.Equal(("Update", "Parent", "10", "20", (string?)null), (parent.Action, parent.Property, parent.OldValue, parent.NewValue, parent.Justification));

        var raised = Assert.Single(history, h => h.Property == RiskContextProperties.Criticality && h.Action == "Update");
        Assert.Equal(("4", "5", "scoped", "Second review by the risk committee, minute 2026/77"),
            (raised.OldValue, raised.NewValue, raised.Actor, raised.Justification));

        // The first batch created the criticality row and changed internetFacing in place.
        var set = Assert.Single(history, h => h.Property == RiskContextProperties.Criticality && h.Action == "Create");
        Assert.Equal(((string?)null, "4", first.BatchId, Reason.Trim()), (set.OldValue, set.NewValue, set.CorrelationId, set.Justification));
        var internet = Assert.Single(history, h => h.Property == RiskContextProperties.InternetFacing);
        Assert.Equal(("false", "true", first.BatchId), (internet.OldValue, internet.NewValue, internet.CorrelationId));

        // Never the property row's OldValue bookkeeping, nor who-saved churn.
        Assert.DoesNotContain(history, h => h.Property is "OldValue" or "UpdatedBy");
        Assert.All(history, h => Assert.Equal(DateTimeKind.Utc, h.OccurredAtUtc.Kind));
    }

    [Fact]
    public async Task TestAClearedPropertyIsInTheHistoryUnderItsKey()
    {
        var cleared = await Save(Batch(new EntityRiskContextChange(Process, null, null, null)));

        var entry = Assert.Single(await History(Process));

        Assert.Equal(("Delete", RiskContextProperties.Criticality, "3", (string?)null, cleared.BatchId, Reason.Trim()),
            (entry.Action, entry.Property, entry.OldValue, entry.NewValue, entry.CorrelationId, entry.Justification));
    }

    [Fact]
    public async Task TestAnotherEntitysTrailIsNotInTheHistory()
    {
        await Save(Batch(new EntityRiskContextChange(Process, 5, null, null)));

        Assert.Empty(await History(App));
    }

    [Fact]
    public async Task TestTheLimitCapsTheHistory()
    {
        await Save(Batch(new EntityRiskContextChange(App, 4, true, Restricted)));

        Assert.Equal(3, (await History(App)).Count);
        Assert.Single(await History(App, limit: 1));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    [InlineData(EntitiesService.MaxHistoryLimit + 1)]
    public async Task TestALimitOutsideTheRangeIsRefused(int limit)
    {
        var ex = await Assert.ThrowsAsync<EntityRequestRefusedException>(() => History(App, limit));

        Assert.Equal((EntityRiskContextErrors.InvalidLimit, 400), (ex.Error, ex.StatusCode));
    }

    [Fact]
    public async Task TestWithNeitherPermissionTheHistoryIsRefused()
    {
        var ex = await Assert.ThrowsAsync<EntityRequestRefusedException>(() => History(App, permissions: ["riskmanagement"]));

        Assert.Equal((EntityRiskContextErrors.HistoryRequiresPermission, 403), (ex.Error, ex.StatusCode));
    }

    [Fact]
    public async Task TestEitherPermissionOrAdminReadsTheHistory()
    {
        await Save(Batch(new EntityRiskContextChange(Process, 5, null, null)));

        Assert.NotEmpty(await History(Process, permissions: [EntityPermissions.Manage]));
        Assert.NotEmpty(await History(Process, permissions: [EntityPermissions.RiskContext]));
        Assert.NotEmpty(await _service.GetHistoryAsync(Process, 200, isAdmin: true, []));
    }

    [Fact]
    public async Task TestAScopedReaderReadsTheHistoryInsideTheirSubtreeOnly()
    {
        await Save(Batch(new EntityRiskContextChange(Process, 5, null, null)));

        _dal.ActAs(new Claim("entity_id", UnitX.ToString()));
        Assert.NotEmpty(await History(Process));

        _dal.ActAs(new Claim("entity_id", UnitY.ToString()));
        var ex = await Assert.ThrowsAsync<EntityRequestRefusedException>(() => History(Process));
        Assert.Equal((EntityRiskContextErrors.EntityNotFound, 404), (ex.Error, ex.StatusCode));
    }

    [Fact]
    public async Task TestAMissingEntityIsNotFoundLikeAnOutOfScopeOne()
    {
        var ex = await Assert.ThrowsAsync<EntityRequestRefusedException>(() => History(999));

        Assert.Equal((EntityRiskContextErrors.EntityNotFound, 404, "Entity 999 does not exist."), (ex.Error, ex.StatusCode, ex.Message));
    }

    // --- The audit dump reader --------------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheDumpReaderReadsWhatTheInterceptorWrites()
    {
        await Save(Batch(new EntityRiskContextChange(Process, null, null, null)));
        using var db = _dal.GetContext(bypassEntityScope: true);
        var dump = db.AuditLogs.AsNoTracking().Single(a => a.EntityType == nameof(EntitiesProperty) && a.Action == AuditLogAction.Delete).OldValue;

        Assert.Equal(new EntityPropertyAuditDump.Fields(Process, 500, RiskContextProperties.Criticality, "3"),
            EntityPropertyAuditDump.Parse(dump));
    }

    [Theory]
    [InlineData("Entity=7; Id=9; Name=n; OldValue=; Type=name; Value=a; Type=criticality; b", 7, 9, "name", "a; Type=criticality; b")]
    [InlineData("Entity=7; Id=-2147482647; Name=x; OldValue=; Type=internetFacing; Value=true", 7, -2147482647, "internetFacing", "true")]
    [InlineData("Entity=17; Id=3; Name=x; OldValue=; Type=securityClassification; Value=", 17, 3, "securityClassification", "")]
    public void TestTheDumpReaderParsesEachShape(string dump, int entity, int id, string key, string value)
    {
        Assert.Equal(new EntityPropertyAuditDump.Fields(entity, id, key, value), EntityPropertyAuditDump.Parse(dump));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Id=3; Type=criticality; Value=2")]
    [InlineData("Entity=7; Id=3; Type=description; Value=2")]
    public void TestADumpThatIsNotAnAuditedPropertyRowIsNotRead(string? dump)
    {
        Assert.Null(EntityPropertyAuditDump.Parse(dump));
    }
}

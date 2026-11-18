using System.Globalization;
using System.Text.RegularExpressions;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Entities;
using Model.Exceptions;
using ServerServices.Security;

namespace ServerServices.Services;

/// <summary>
/// The Risk context tab's backend (S40 §6.7, §8.5; M55, T264): the rows, the all-or-nothing batch with its justification,
/// and the entity history that joins the audit trail to each batch's note.
/// </summary>
public partial class EntitiesService
{
    /// <summary>The largest <c>limit</c> <see cref="GetHistoryAsync"/> accepts (S40 §8.5).</summary>
    public const int MaxHistoryLimit = 5000;

    private const string NameProperty = "name";
    private const string ClassificationLevelDefinition = "securityClassificationLevel";
    private const string EntityAuditType = nameof(Entity);
    private const string PropertyAuditType = nameof(EntitiesProperty);
    private const string ValueField = nameof(EntitiesProperty.Value);

    /// <summary>Who saved last is the audit row's actor; the column's own change is noise in the history.</summary>
    private const string UpdatedByField = nameof(Entity.UpdatedBy);

    private static readonly string[] ContextKeys =
        [RiskContextProperties.Criticality, RiskContextProperties.InternetFacing, RiskContextProperties.SecurityClassification];

    private sealed record AuditRow(int Id, int RecordId, AuditLogAction Action, string Field, string? OldValue,
        string? NewValue, string Actor, DateTime OccurredAt, string? CorrelationId);

    public async Task<List<EntityRiskContextRowDto>> GetRiskContextAsync(IReadOnlyCollection<string>? types = null,
        CancellationToken ct = default)
    {
        var config = await GetEntitiesConfigurationAsync();

        // Only names the configuration defines: that also bounds what a caller's query string can put in the SQL.
        var requested = (types ?? []).Where(t => !string.IsNullOrWhiteSpace(t)).Select(t => t.Trim()).ToList();
        var wanted = (requested.Count > 0 ? requested : EntityRiskContextRules.DefaultTypes)
            .Where(config.Definitions.ContainsKey).Distinct(StringComparer.Ordinal).ToList();
        if (wanted.Count == 0) return [];

        // The entity map carries no scope filter: every authenticated user reads it, as GET /Entities does.
        await using var db = DalService.GetContext();

        var entities = await db.Entities.AsNoTracking()
            .Where(e => wanted.Contains(e.DefinitionName))
            .Select(e => new { e.Id, e.DefinitionName, e.Parent })
            .ToListAsync(ct);

        string[] keys = [NameProperty, ..ContextKeys];
        var properties = await db.EntitiesProperties.AsNoTracking()
            .Where(p => keys.Contains(p.Type) && wanted.Contains(p.EntityNavigation.DefinitionName))
            .Select(p => new { p.Id, p.Entity, p.Type, p.Value })
            .ToListAsync(ct);

        var latest = await LatestContextChangesAsync(db, wanted, entities.Select(e => e.Id).ToHashSet(), ct);
        var byEntity = properties.ToLookup(p => p.Entity);

        string? ValueOf(int entityId, string key) => byEntity[entityId]
            .Where(p => p.Type == key && !string.IsNullOrWhiteSpace(p.Value))
            .OrderBy(p => p.Id)
            .Select(p => p.Value.Trim())
            .FirstOrDefault();

        var rows = new List<EntityRiskContextRowDto>(entities.Count);
        foreach (var e in entities)
        {
            config.Definitions.TryGetValue(e.DefinitionName, out var definition);
            bool Supports(string key) => definition?.Properties?.ContainsKey(key) == true;

            var criticality = Supports(RiskContextProperties.Criticality);
            var internet = Supports(RiskContextProperties.InternetFacing);
            var classification = Supports(RiskContextProperties.SecurityClassification);
            var changed = latest.TryGetValue(e.Id, out var last) ? last : default((DateTime, int, string)?);

            rows.Add(new EntityRiskContextRowDto(e.Id, ValueOf(e.Id, NameProperty) ?? "#" + e.Id.ToString(CultureInfo.InvariantCulture),
                e.DefinitionName, e.Parent,
                criticality, criticality ? ParseInt(ValueOf(e.Id, RiskContextProperties.Criticality)) : null,
                internet, internet ? ParseBool(ValueOf(e.Id, RiskContextProperties.InternetFacing)) : null,
                classification, classification ? ParseInt(ValueOf(e.Id, RiskContextProperties.SecurityClassification)) : null,
                changed is { } c ? Utc(c.Item1) : null, changed?.Item3));
        }

        return rows.OrderBy(r => r.Name, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.EntityId).ToList();
    }

    /// <summary>
    /// The newest audit row of the three risk-context properties per entity: value updates matched through the property
    /// rows that exist, sets and clears read back from the create/delete dump (a cleared row no longer exists).
    /// </summary>
    private static async Task<Dictionary<int, (DateTime, int, string)>> LatestContextChangesAsync(
        DAL.Context.AuditableContext db, List<string> wanted, HashSet<int> entityIds, CancellationToken ct)
    {
        var latest = new Dictionary<int, (DateTime At, int AuditId, string Actor)>();

        void Offer(int entityId, DateTime at, int auditId, string actor)
        {
            if (!entityIds.Contains(entityId)) return;
            if (latest.TryGetValue(entityId, out var current) && (at, auditId).CompareTo((current.At, current.AuditId)) <= 0)
                return;
            latest[entityId] = (at, auditId, actor);
        }

        var contextKeys = ContextKeys;
        var updates = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == PropertyAuditType && a.Action == AuditLogAction.Update && a.Field == ValueField)
            .Join(db.EntitiesProperties.Where(p => contextKeys.Contains(p.Type) &&
                                                   wanted.Contains(p.EntityNavigation.DefinitionName)),
                a => a.EntityId, p => p.Id, (a, p) => new { p.Entity, a.OccurredAt, a.Id, a.Actor })
            .ToListAsync(ct);
        foreach (var u in updates) Offer(u.Entity, u.OccurredAt, u.Id, u.Actor);

        string criticality = EntityPropertyAuditDump.KeyMarker(RiskContextProperties.Criticality),
            internet = EntityPropertyAuditDump.KeyMarker(RiskContextProperties.InternetFacing),
            classification = EntityPropertyAuditDump.KeyMarker(RiskContextProperties.SecurityClassification);

        var lifecycle = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == PropertyAuditType &&
                        (a.Action == AuditLogAction.Create || a.Action == AuditLogAction.Delete) &&
                        ((a.NewValue != null && (a.NewValue.Contains(criticality) || a.NewValue.Contains(internet) ||
                                                 a.NewValue.Contains(classification))) ||
                         (a.OldValue != null && (a.OldValue.Contains(criticality) || a.OldValue.Contains(internet) ||
                                                 a.OldValue.Contains(classification)))))
            .Select(a => new { a.Action, a.NewValue, a.OldValue, a.OccurredAt, a.Id, a.Actor })
            .ToListAsync(ct);

        foreach (var row in lifecycle)
        {
            var dump = EntityPropertyAuditDump.Parse(row.Action == AuditLogAction.Create ? row.NewValue : row.OldValue);
            if (dump is { } d && RiskContextProperties.IsRiskContext(d.Key)) Offer(d.EntityId, row.OccurredAt, row.Id, row.Actor);
        }

        return latest.ToDictionary(p => p.Key, p => (p.Value.At, p.Value.AuditId, p.Value.Actor));
    }

    public async Task<EntityRiskContextBatchResultDto> SaveRiskContextAsync(EntityRiskContextBatchRequest request,
        int userId, bool holdsRiskContextPermission, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        var changes = request.Changes ?? [];

        // Who may, before what: a caller without the permission or the scope learns nothing about the entities named.
        RiskContextWritePolicy.Enforce(holdsRiskContextPermission, DalService.GetCurrentEntityScope(),
            changes.Where(c => c is not null).Select(c => c.EntityId).Distinct().ToList(), "the risk context");

        var justification = (request.Justification ?? string.Empty).Trim();
        if (justification.Length < EntityRiskContextRules.MinimumJustificationLength)
            throw Refusal(EntityRiskContextErrors.JustificationTooShort,
                $"The justification needs at least {EntityRiskContextRules.MinimumJustificationLength} characters.");
        if (justification.Length > EntityRiskContextRules.MaximumJustificationLength)
            throw Refusal(EntityRiskContextErrors.JustificationTooLong,
                $"The justification takes at most {EntityRiskContextRules.MaximumJustificationLength} characters.");

        if (changes.Count == 0) throw Refusal(EntityRiskContextErrors.EmptyBatch, "The batch has no change.");
        if (changes.Any(c => c is null))
            throw Refusal(EntityRiskContextErrors.EmptyBatch, "The batch has an empty line.");
        if (changes.Count > EntityRiskContextRules.MaximumBatchSize)
            throw Refusal(EntityRiskContextErrors.BatchTooLarge,
                $"A batch takes at most {EntityRiskContextRules.MaximumBatchSize} entities; this one has {changes.Count}.");

        var seen = new HashSet<int>();
        foreach (var change in changes)
        {
            if (!seen.Add(change.EntityId))
                throw Refusal(EntityRiskContextErrors.DuplicateEntity,
                    $"Entity {change.EntityId} appears more than once in the batch.", entityId: change.EntityId);
        }

        var config = await GetEntitiesConfigurationAsync();
        await using var db = DalService.GetContext();

        var ids = seen.ToList();
        var entities = await db.Entities.Include(e => e.EntitiesProperties)
            .Where(e => ids.Contains(e.Id))
            .ToDictionaryAsync(e => e.Id, ct);

        var classificationIds = changes.Where(c => c.SecurityClassificationId is not null)
            .Select(c => c.SecurityClassificationId!.Value).Distinct().ToList();
        var classifications = classificationIds.Count == 0
            ? new Dictionary<int, string>()
            : await db.Entities.AsNoTracking().Where(e => classificationIds.Contains(e.Id))
                .ToDictionaryAsync(e => e.Id, e => e.DefinitionName, ct);

        // Every line before anything is written: one refusal and nothing changes (S40 §8.5).
        foreach (var change in changes)
        {
            if (!entities.TryGetValue(change.EntityId, out var entity))
                throw Refusal(EntityRiskContextErrors.EntityNotFound, $"Entity {change.EntityId} does not exist.", 404,
                    change.EntityId);

            config.Definitions.TryGetValue(entity.DefinitionName, out var definition);

            RequireSupported(change, entity, definition, change.Criticality is not null, RiskContextProperties.Criticality);
            RequireSupported(change, entity, definition, change.InternetFacing is not null,
                RiskContextProperties.InternetFacing);
            RequireSupported(change, entity, definition, change.SecurityClassificationId is not null,
                RiskContextProperties.SecurityClassification);

            if (change.Criticality is { } level && level is < EntityRiskContextRules.MinimumCriticality
                    or > EntityRiskContextRules.MaximumCriticality)
                throw Refusal(EntityRiskContextErrors.CriticalityOutOfRange,
                    $"Criticality {level} of entity {change.EntityId} is outside {EntityRiskContextRules.MinimumCriticality}…{EntityRiskContextRules.MaximumCriticality}.",
                    entityId: change.EntityId);

            if (change.SecurityClassificationId is { } classification &&
                (!classifications.TryGetValue(classification, out var levelDefinition) ||
                 !string.Equals(levelDefinition, ClassificationDefinitionOf(definition), StringComparison.Ordinal)))
                throw Refusal(EntityRiskContextErrors.ClassificationNotFound,
                    $"Security classification {classification} of entity {change.EntityId} is not a {ClassificationDefinitionOf(definition)}.",
                    entityId: change.EntityId);
        }

        var batchId = Guid.NewGuid().ToString("N");
        var now = DateTime.UtcNow;
        var updated = 0;

        foreach (var change in changes)
        {
            var entity = entities[change.EntityId];
            var definition = config.Definitions.GetValueOrDefault(entity.DefinitionName);
            bool Supports(string key) => definition?.Properties?.ContainsKey(key) == true;

            var touched = false;
            if (Supports(RiskContextProperties.Criticality))
                touched |= ApplyContextValue(db, entity, RiskContextProperties.Criticality,
                    change.Criticality?.ToString(CultureInfo.InvariantCulture));
            if (Supports(RiskContextProperties.InternetFacing))
                touched |= ApplyContextValue(db, entity, RiskContextProperties.InternetFacing,
                    change.InternetFacing is { } facing ? (facing ? "true" : "false") : null);
            if (Supports(RiskContextProperties.SecurityClassification))
                touched |= ApplyContextValue(db, entity, RiskContextProperties.SecurityClassification,
                    change.SecurityClassificationId?.ToString(CultureInfo.InvariantCulture));

            if (!touched) continue;

            updated++;
            entity.Updated = DateTime.Now;
            entity.UpdatedBy = userId;
            db.EntityRiskContextNotes.Add(new EntityRiskContextNote
            {
                BatchId = batchId, EntityId = entity.Id, Justification = justification, CreatedAt = now,
                CreatedById = userId > 0 ? userId : null
            });
        }

        if (updated == 0)
        {
            Logger.Information("Risk-context batch {Batch} changed nothing; no row written", batchId);
            return new EntityRiskContextBatchResultDto(0, batchId);
        }

        // One save: the property rows, the notes and the audit rows commit together or not at all, and every audit row of
        // this save carries the batch id as its correlation id — the history's join to the note.
        db.AuditCorrelationId = batchId;
        await db.SaveChangesAsync(ct);

        Logger.Information("Risk-context batch {Batch} by user {User} changed {Count} entities", batchId, userId, updated);
        return new EntityRiskContextBatchResultDto(updated, batchId);
    }

    /// <summary>
    /// Brings one risk-context property of a tracked entity to <paramref name="desired"/> (null = cleared), in place so the
    /// row keeps its id and its audit row names the old value. False, and nothing touched, when the value is already that.
    /// </summary>
    private static bool ApplyContextValue(DAL.Context.AuditableContext db, Entity entity, string key, string? desired)
    {
        var rows = entity.EntitiesProperties.Where(p => p.Type == key).OrderBy(p => p.Id).ToList();
        var current = rows.FirstOrDefault(p => !string.IsNullOrWhiteSpace(p.Value));

        if (desired is null)
        {
            if (current is null) return false;
            foreach (var row in rows) db.EntitiesProperties.Remove(row);
            return true;
        }

        if (current is not null && SameContextValue(key, current.Value, desired)) return false;

        var target = current ?? rows.FirstOrDefault();
        if (target is null)
        {
            db.EntitiesProperties.Add(new EntitiesProperty
            {
                Entity = entity.Id, Type = key, Name = $"{key}-{entity.Id.ToString(CultureInfo.InvariantCulture)}",
                Value = desired, OldValue = string.Empty
            });
            return true;
        }

        target.OldValue = target.Value;
        target.Value = desired;
        foreach (var extra in rows.Where(r => !ReferenceEquals(r, target))) db.EntitiesProperties.Remove(extra);
        return true;
    }

    /// <summary>A value for a property the entity's definition does not declare is refused (S39 §5.2/§5.3).</summary>
    private static void RequireSupported(EntityRiskContextChange change, Entity entity, EntityDefinition? definition,
        bool hasValue, string key)
    {
        if (hasValue && definition?.Properties?.ContainsKey(key) != true)
            throw Refusal(EntityRiskContextErrors.FieldNotSupportedForType,
                $"Entity {change.EntityId} is a {entity.DefinitionName}, which has no {key}.", entityId: change.EntityId);
    }

    private static bool SameContextValue(string key, string stored, string desired) => key == RiskContextProperties.InternetFacing
        ? ParseBool(stored) is { } a && ParseBool(desired) == a
        : ParseInt(stored) is { } n && ParseInt(desired) == n;

    /// <summary>The definition a <c>securityClassification</c> value must name: <c>Definition(‹name›)</c> in the configuration.</summary>
    private static string ClassificationDefinitionOf(EntityDefinition? definition)
    {
        var type = definition?.Properties?.GetValueOrDefault(RiskContextProperties.SecurityClassification)?.Type;
        var match = type is null ? null : Regex.Match(type, @"^Definition\(([^)]+)\)$");
        return match is { Success: true } ? match.Groups[1].Value : ClassificationLevelDefinition;
    }

    public async Task<List<EntityHistoryEntryDto>> GetHistoryAsync(int entityId, int limit, bool isAdmin,
        IReadOnlyCollection<string> permissions, CancellationToken ct = default)
    {
        if (limit is < 1 or > MaxHistoryLimit)
            throw new EntityRequestRefusedException(EntityRiskContextErrors.InvalidLimit,
                $"limit must be between 1 and {MaxHistoryLimit}.", 400);

        if (!MayReadHistory(isAdmin, permissions))
            throw new EntityRequestRefusedException(EntityRiskContextErrors.HistoryRequiresPermission,
                $"Reading an entity's history requires {EntityPermissions.RiskContext} or {EntityPermissions.Manage}.");

        var scope = DalService.GetCurrentEntityScope();
        await using var db = DalService.GetContext(withIdentity: false, bypassEntityScope: true);

        // Access before the trail (like HostsController.GetHistory): out of scope reads exactly like missing (T292).
        if (!scope.Allows(entityId) || !await db.Entities.AsNoTracking().AnyAsync(e => e.Id == entityId, ct))
            throw new EntityRequestRefusedException(EntityRiskContextErrors.EntityNotFound,
                $"Entity {entityId} does not exist.", 404, entityId);

        var entityRows = await db.AuditLogs.AsNoTracking()
            .Where(a => a.EntityType == EntityAuditType && a.EntityId == entityId && a.Field != UpdatedByField)
            .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
            .Take(limit)
            .Select(a => new AuditRow(a.Id, a.EntityId, a.Action, a.Field, a.OldValue, a.NewValue, a.Actor, a.OccurredAt,
                a.CorrelationId))
            .ToListAsync(ct);

        // Sets and clears: the row's create/delete dump names its entity (a cleared row no longer exists to look up).
        var marker = EntityPropertyAuditDump.EntityMarker(entityId);
        var lifecycle = (await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == PropertyAuditType &&
                            (a.Action == AuditLogAction.Create || a.Action == AuditLogAction.Delete) &&
                            ((a.NewValue != null && a.NewValue.Contains(marker)) ||
                             (a.OldValue != null && a.OldValue.Contains(marker))))
                .Select(a => new AuditRow(a.Id, a.EntityId, a.Action, a.Field, a.OldValue, a.NewValue, a.Actor,
                    a.OccurredAt, a.CorrelationId))
                .ToListAsync(ct))
            .Select(r => (Row: r, Dump: EntityPropertyAuditDump.Parse(r.Action == AuditLogAction.Create ? r.NewValue : r.OldValue)))
            .Where(x => x.Dump is { } d && d.EntityId == entityId)
            .Select(x => (x.Row, Dump: x.Dump!.Value))
            .ToList();

        // Value changes: through the property rows that exist and those a dump says were the entity's.
        var keyById = await db.EntitiesProperties.AsNoTracking().Where(p => p.Entity == entityId)
            .ToDictionaryAsync(p => p.Id, p => p.Type, ct);
        foreach (var (_, dump) in lifecycle.Where(x => x.Dump.PropertyId > 0)) keyById.TryAdd(dump.PropertyId, dump.Key);

        var propertyIds = keyById.Keys.ToList();
        var updates = propertyIds.Count == 0
            ? []
            : await db.AuditLogs.AsNoTracking()
                .Where(a => a.EntityType == PropertyAuditType && a.Action == AuditLogAction.Update && a.Field == ValueField &&
                            propertyIds.Contains(a.EntityId))
                .OrderByDescending(a => a.OccurredAt).ThenByDescending(a => a.Id)
                .Take(limit)
                .Select(a => new AuditRow(a.Id, a.EntityId, a.Action, a.Field, a.OldValue, a.NewValue, a.Actor,
                    a.OccurredAt, a.CorrelationId))
                .ToListAsync(ct);

        var page = entityRows.Select(r => (Row: r, Property: r.Field, Old: r.OldValue, New: r.NewValue))
            .Concat(updates.Select(r => (Row: r, Property: keyById[r.RecordId], Old: r.OldValue, New: r.NewValue)))
            .Concat(lifecycle.Select(x => (x.Row, Property: x.Dump.Key,
                Old: x.Row.Action == AuditLogAction.Delete ? x.Dump.Value : null,
                New: x.Row.Action == AuditLogAction.Create ? x.Dump.Value : null)))
            .OrderByDescending(e => e.Row.OccurredAt).ThenByDescending(e => e.Row.Id)
            .Take(limit)
            .ToList();

        var batches = page.Select(e => e.Row.CorrelationId).OfType<string>().Distinct().ToList();
        var reasons = batches.Count == 0
            ? new Dictionary<string, string>()
            : (await db.EntityRiskContextNotes.AsNoTracking()
                    .Where(n => n.EntityId == entityId && batches.Contains(n.BatchId))
                    .Select(n => new { n.BatchId, n.Justification })
                    .ToListAsync(ct))
                .GroupBy(n => n.BatchId, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.First().Justification, StringComparer.Ordinal);

        return page.Select(e => new EntityHistoryEntryDto(Utc(e.Row.OccurredAt), e.Row.Action.ToString(), e.Property,
                e.Old, e.New, e.Row.Actor, e.Row.CorrelationId,
                e.Row.CorrelationId is { } batch && reasons.TryGetValue(batch, out var reason) ? reason : null))
            .ToList();
    }

    /// <summary>The history's permission rule (S40 §8.5): either entity permission, or the Admin role.</summary>
    private static bool MayReadHistory(bool isAdmin, IReadOnlyCollection<string> permissions) =>
        isAdmin || permissions.Contains(EntityPermissions.RiskContext) || permissions.Contains(EntityPermissions.Manage);

    private static EntityWriteRefusedException Refusal(string error, string message, int status = 400, int? entityId = null) =>
        new(error, message, status, entityId);

    private static int? ParseInt(string? value) =>
        int.TryParse(value?.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var n) ? n : null;

    private static bool? ParseBool(string? value) => bool.TryParse(value?.Trim(), out var b) ? b : null;

    /// <summary><c>audit_logs.occurred_at</c> is written as UTC and read back unspecified.</summary>
    private static DateTime Utc(DateTime at) => DateTime.SpecifyKind(at, DateTimeKind.Utc);
}

/// <summary>
/// Reads an <c>entities_properties</c> row back from the create/delete dump <c>GovernanceAuditInterceptor</c> writes
/// (<c>Entity=12; Id=500; Name=…; OldValue=…; Type=criticality; Value=3</c>, EF's property order): which entity, which
/// row, which key, which value. The dump is the only record of a cleared row. Context values carry no <c>;</c>, so their
/// tail is anchored at the end; a name is free text, read after the first <c>; Type=name; Value=</c>.
/// </summary>
internal static class EntityPropertyAuditDump
{
    internal readonly record struct Fields(int EntityId, int PropertyId, string Key, string Value);

    private static readonly Regex EntityField = new(@"(?:^|; )Entity=(-?\d+)(?=;|$)", RegexOptions.CultureInvariant);
    private static readonly Regex IdField = new(@"(?:^|; )Id=(-?\d+)(?=;|$)", RegexOptions.CultureInvariant);

    private static readonly Regex ContextTail = new(@"; Type=(criticality|internetFacing|securityClassification); Value=([^;]*)$",
        RegexOptions.CultureInvariant);

    private static readonly Regex NameTail = new(@"; Type=(name); Value=(.*)$",
        RegexOptions.CultureInvariant | RegexOptions.Singleline);

    /// <summary>What a dump of entity <paramref name="entityId"/>'s row contains (the SQL pre-filter; <see cref="Parse"/> decides).</summary>
    public static string EntityMarker(int entityId) => "Entity=" + entityId.ToString(CultureInfo.InvariantCulture) + ";";

    /// <summary>What a dump of a row of property <paramref name="key"/> contains.</summary>
    public static string KeyMarker(string key) => "Type=" + key + ";";

    public static Fields? Parse(string? dump)
    {
        if (string.IsNullOrEmpty(dump)) return null;

        var entity = EntityField.Match(dump);
        if (!entity.Success ||
            !int.TryParse(entity.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var entityId))
            return null;

        var id = IdField.Match(dump);
        var propertyId = id.Success && int.TryParse(id.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture,
            out var parsed) ? parsed : 0;

        var tail = ContextTail.Match(dump);
        if (!tail.Success) tail = NameTail.Match(dump);
        if (!tail.Success) return null;

        return new Fields(entityId, propertyId, tail.Groups[1].Value, tail.Groups[2].Value);
    }
}

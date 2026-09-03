using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model;
using Model.Entities;
using Model.Integrations;
using ServerServices.Interfaces;

namespace ServerServices.Integrations.IssueTrackers.Jira;

/// <summary>
/// The Jira Assets register import (Track 4 milestone 4.6): read the CMDB objects an operator mapped,
/// project them through the attribute mapping, and land them as NetRisk hosts (servers and machines)
/// or <c>application</c> entities.
///
/// Three decisions shape this file.
///
/// **Matching reuses milestone 4.4.2's asset-identity chain** — external id, then MAC, then FQDN, then
/// hostname, then IP. An Assets server is very often a machine a scanner and Vision One already
/// found, and a name-only match would make it a third row for the same box.
///
/// **A dry run is the same code path.** <c>dryRun</c> skips the writes and the audit rows; everything
/// before that — the AQL, the projection, the matching, the decision about what would change — is
/// identical. A preview that runs different code is a preview of nothing.
///
/// **Every object read produces an audit row**, including the ones that resolved to nothing, with the
/// rule that matched and the reason it failed. Without it, "why is that server not in NetRisk" has no
/// answer except re-running the import and watching.
/// </summary>
public partial class JiraIntegrationService
{
    public async Task<AssetImportResult> ImportAssetsAsync(int connectionId, bool dryRun,
        int? userId = null)
    {
        var (connection, token, settings) = await ResolveAsync(connectionId);

        var result = new AssetImportResult { DryRun = dryRun };

        if (!settings.AssetsEnabled)
        {
            result.Messages.Add("Assets is not enabled on this connection.");
            return result;
        }

        var workspace = await RequireWorkspaceAsync(connection, token, settings);

        await using var db = DalService.GetContext();

        var mappings = await db.JiraObjectMappings
            .Include(m => m.AttributeMappings)
            .Where(m => m.ConnectionId == connectionId && m.Enabled)
            .ToListAsync();

        if (mappings.Count == 0)
        {
            result.Messages.Add("No object types are mapped, so there is nothing to import.");
            return result;
        }

        foreach (var mapping in mappings)
        {
            try
            {
                await ImportMappingAsync(db, connection, token, workspace, mapping, dryRun, userId,
                    result);
            }
            catch (Exception ex)
            {
                // Per mapping, so one object type with a broken AQL does not cost the operator the
                // other three that were configured correctly.
                result.Errors++;
                result.Messages.Add($"{mapping.ObjectTypeName}: {ex.Message}");
                Logger.Warning(ex, "Importing Assets object type {Type} of connection {Connection} failed",
                    mapping.ObjectTypeName, connectionId);
            }
        }

        if (!dryRun)
        {
            var settingsRow = await db.JiraConnectionSettings
                .FirstAsync(s => s.ConnectionId == connectionId);

            settingsRow.LastAssetsSyncAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            await RecordAssetImportAsync(connectionId, connection.Name, result);
        }

        Logger.Information(
            "Assets import of connection {Connection} by user {User} ({Mode}): {Examined} object(s), "
            + "{Created} created, {Updated} updated, {Deactivated} deactivated, {Errors} error(s)",
            connectionId, userId, dryRun ? "dry run" : "apply", result.Examined, result.Created,
            result.Updated, result.Deactivated, result.Errors);

        return result;
    }

    private async Task ImportMappingAsync(AuditableContext db, IssueTrackerConnection connection,
        string? token, string workspace, JiraObjectMapping mapping, bool dryRun, int? userId,
        AssetImportResult result)
    {
        // The attribute names come from the object type, not from the search payload: the AQL response
        // carries attribute *ids* reliably and their names only sometimes, so a projector fed from the
        // payload alone works on one site and maps nothing on the next.
        var attributes = await assets.GetAttributesAsync(connection, token, workspace,
            mapping.ObjectTypeId);

        var namesById = attributes.ToDictionary(a => a.Id, a => a.Name);

        var aql = BuildAql(mapping);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var startAt = 0;

        while (true)
        {
            var page = await assets.SearchAsync(connection, token, workspace, aql, startAt,
                JiraAssetsClient.PageSize);

            foreach (var payload in page.Objects)
            {
                result.Examined++;
                seen.Add(payload.ObjectId);

                // Names filled in from the object type where the payload did not carry them, so the
                // mapping's name-based fallback lookup works either way.
                foreach (var (id, values) in payload.Attributes)
                    if (namesById.TryGetValue(id, out var name))
                        payload.AttributesByName.TryAdd(name, values);

                var projected = AssetAttributeProjector.Project(payload,
                    mapping.AttributeMappings.ToList());

                var audit = await ApplyObjectAsync(db, connection, mapping, payload, projected,
                    dryRun, result);

                // A bounded sample, not every row: this is what the operator reads to decide whether
                // the mapping is right, and twenty rows answers that as well as ten thousand would.
                if (result.Sample.Count < 20) result.Sample.Add(audit);
            }

            if (page.IsLast || page.Objects.Count == 0) break;

            startAt += JiraAssetsClient.PageSize;

            // The same bound as the JSM pager, for the same reason: a runaway register must not turn
            // one import into an unbounded walk.
            if (startAt > JiraAssetsClient.PageSize * 200)
            {
                result.Messages.Add(
                    $"{mapping.ObjectTypeName}: stopped after {startAt} objects. Narrow the AQL filter.");
                break;
            }
        }

        if (mapping.DeactivateMissing && !dryRun)
            await DeactivateMissingAsync(db, connection.Id, mapping, seen, result);

        if (!dryRun)
        {
            var stored = await db.JiraObjectMappings.FirstOrDefaultAsync(m => m.Id == mapping.Id);

            if (stored != null)
            {
                stored.LastImportedAt = DateTime.UtcNow;
                await db.SaveChangesAsync();
            }
        }
    }

    /// <summary>
    /// The AQL for one mapping: the object type, and the operator's filter if they set one.
    ///
    /// The type name is quoted and its own quotes doubled, because an object type may legitimately be
    /// called <c>Server "Legacy"</c> and an unescaped name would produce an AQL syntax error that
    /// reads as "Assets refused the query" with no clue which type caused it.
    /// </summary>
    internal static string BuildAql(JiraObjectMapping mapping)
    {
        var quoted = mapping.ObjectTypeName.Replace("\"", "\"\"");
        var aql = $"objectType = \"{quoted}\"";

        return string.IsNullOrWhiteSpace(mapping.AqlFilter)
            ? aql
            : $"{aql} AND ({mapping.AqlFilter.Trim()})";
    }

    /// <summary>Resolves one object onto a NetRisk record and writes it, unless this is a dry run.</summary>
    private async Task<JiraAssetObjectView> ApplyObjectAsync(AuditableContext db,
        IssueTrackerConnection connection, JiraObjectMapping mapping, AssetObjectPayload payload,
        AssetAttributeProjector.ProjectedObject projected, bool dryRun, AssetImportResult result)
    {
        var connectionId = connection.Id;

        var view = new JiraAssetObjectView
        {
            ObjectId = payload.ObjectId,
            ObjectKey = payload.ObjectKey,
            // Set on the dry run's sample too, so the preview an operator reads offers the same link
            // out to Jira that the imported register will.
            ObjectUrl = AssetObjectUrl(connection.BaseUrl, payload.ObjectKey),
            ObjectTypeName = payload.ObjectTypeName ?? mapping.ObjectTypeName,
            Label = payload.Label,
            MappedName = projected.Name,
            MappedOwner = projected.Owner,
            MappedEnvironment = projected.Environment,
            MappedActive = projected.Active,
            TargetKind = mapping.TargetKind
        };

        if (string.IsNullOrWhiteSpace(projected.Name))
        {
            result.Errors++;
            view.ImportError = "The mapping produced no name for this object.";

            if (!dryRun) await WriteAuditAsync(db, connectionId, mapping, payload, view);

            return view;
        }

        try
        {
            if (mapping.TargetKind == JiraAssetTargetKind.Host)
                await ApplyHostAsync(db, connectionId, mapping, payload, projected, view, dryRun, result);
            else
                await ApplyApplicationAsync(db, mapping, projected, view, dryRun, result);
        }
        catch (Exception ex)
        {
            result.Errors++;
            view.ImportError = ex.Message;
            Logger.Warning(ex, "Applying Assets object {Object} failed", payload.ObjectId);
        }

        if (!dryRun) await WriteAuditAsync(db, connectionId, mapping, payload, view);

        return view;
    }

    // --- hosts (servers and machines) -------------------------------------------------------

    private async Task ApplyHostAsync(AuditableContext db, int connectionId, JiraObjectMapping mapping,
        AssetObjectPayload payload, AssetAttributeProjector.ProjectedObject projected,
        JiraAssetObjectView view, bool dryRun, AssetImportResult result)
    {
        var (host, reason) = await MatchHostAsync(db, mapping, payload, projected);

        view.MatchReason = reason;

        if (host == null)
        {
            if (!mapping.CreateMissing)
            {
                result.Unchanged++;
                view.MatchReason = "no-match (create is off)";
                return;
            }

            result.Created++;
            view.MatchReason = "created";

            if (dryRun) return;

            host = new Host
            {
                Source = ExternalProvider,
                RegistrationDate = DateTime.UtcNow,
                Status = (short)IntStatus.Active,
                EntityId = mapping.Connection?.EntityId
            };

            db.Hosts.Add(host);
        }
        else
        {
            if (!mapping.UpdateExisting)
            {
                result.Unchanged++;
                return;
            }

            result.Updated++;

            if (dryRun)
            {
                view.TargetHostId = host.Id;
                return;
            }
        }

        WriteHostFields(host, connectionId, payload, projected);

        await db.SaveChangesAsync();

        view.TargetHostId = host.Id;
    }

    /// <summary>
    /// The imported fields, written onto the host.
    ///
    /// Only fields the mapping actually produced are written. A blank projection leaves the stored
    /// value alone rather than clearing it: a CMDB that does not record MAC addresses must not erase
    /// the ones a scanner found, and "the register says nothing" is not the same statement as "the
    /// register says empty".
    /// </summary>
    private static void WriteHostFields(Host host, int connectionId, AssetObjectPayload payload,
        AssetAttributeProjector.ProjectedObject projected)
    {
        host.ExternalId = payload.ObjectId;
        host.ExternalProvider = ExternalProvider;

        host.HostName = projected.Get("HostName") ?? projected.Name ?? host.HostName;
        host.Fqdn = projected.Get("Fqdn") ?? host.Fqdn;
        host.Ip = projected.Get("Ip") ?? host.Ip;
        host.MacAddress = projected.Get("MacAddress") ?? host.MacAddress;
        host.Os = projected.Get("Os") ?? host.Os;
        host.OsVersion = projected.Get("OsVersion") ?? host.OsVersion;
        host.Comment = projected.Get("Comment") ?? host.Comment;
        host.Environment = projected.Environment ?? host.Environment;
        host.Owner = projected.Owner ?? host.Owner;

        // Clamped rather than refused: every CMDB has a different number of criticality bands, and
        // rejecting a 7 would mean the field imports for nobody whose scale is not 1-5.
        if (projected.GetInt("Criticality") is { } criticality)
            host.Criticality = Math.Clamp(criticality, 1, 5);

        // Only touched when the mapping has an active-state row at all. A mapping without one must
        // leave a host that somebody retired by hand retired.
        if (projected.Active is { } active)
            host.Status = (short)(active ? IntStatus.Active : IntStatus.Retired);

        host.LastVerificationDate = DateTime.UtcNow;
    }

    /// <summary>
    /// Milestone 4.4.2's asset-identity chain, plus the mapping's chosen strategy.
    ///
    /// The order is deliberate and is the whole point of reusing it: the external id is exact, a MAC
    /// survives a rename, an FQDN survives re-addressing, and an IP is the weakest because DHCP
    /// reassigns it — matching on IP first is how two unrelated machines get merged.
    /// </summary>
    private static async Task<(Host? Host, string? Reason)> MatchHostAsync(AuditableContext db,
        JiraObjectMapping mapping, AssetObjectPayload payload,
        AssetAttributeProjector.ProjectedObject projected)
    {
        var byExternal = await db.Hosts.FirstOrDefaultAsync(h =>
            h.ExternalId == payload.ObjectId && h.ExternalProvider == ExternalProvider);

        if (byExternal != null) return (byExternal, "external-id");

        if (mapping.MatchStrategy == AssetMatchStrategy.ExternalIdOnly) return (null, null);

        var name = projected.Get("HostName") ?? projected.Name;

        if (mapping.MatchStrategy == AssetMatchStrategy.NameOnly)
        {
            var byName = name == null
                ? null
                : await db.Hosts.FirstOrDefaultAsync(h => h.HostName == name);

            return (byName, byName == null ? null : "host-name");
        }

        if (projected.Get("MacAddress") is { } mac)
        {
            var normalised = NormaliseMac(mac);

            // Compared on the normalised form in memory rather than in SQL: the stored values arrive
            // from several importers with different separators, and a WHERE on the raw string matches
            // only the ones that happen to agree with this CMDB's formatting.
            var candidates = await db.Hosts
                .Where(h => h.MacAddress != null && h.MacAddress != "")
                .Select(h => new { h.Id, h.MacAddress })
                .ToListAsync();

            var match = candidates.FirstOrDefault(c => NormaliseMac(c.MacAddress!) == normalised);

            if (match != null)
                return (await db.Hosts.FirstAsync(h => h.Id == match.Id), "mac");
        }

        if (projected.Get("Fqdn") is { } fqdn)
        {
            var byFqdn = await db.Hosts.FirstOrDefaultAsync(h => h.Fqdn == fqdn);
            if (byFqdn != null) return (byFqdn, "fqdn");
        }

        if (name != null)
        {
            var byHostName = await db.Hosts.FirstOrDefaultAsync(h => h.HostName == name);
            if (byHostName != null) return (byHostName, "host-name");
        }

        if (projected.Get("Ip") is { } ip)
        {
            var byIp = await db.Hosts.FirstOrDefaultAsync(h => h.Ip == ip);
            if (byIp != null) return (byIp, "ip");
        }

        return (null, null);
    }

    /// <summary>Lower-case hex with every separator dropped, so <c>AA-BB</c> and <c>aa:bb</c> agree.</summary>
    internal static string NormaliseMac(string mac) =>
        new(mac.Where(char.IsAsciiLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

    // --- applications -----------------------------------------------------------------------

    /// <summary>
    /// Lands an Assets object as an <c>application</c> entity.
    ///
    /// Written through <see cref="IEntitiesService"/> rather than straight into <c>entities</c> so the
    /// definition's own validation runs — the property list, the types, and the single-versus-multiple
    /// rule are all declared in <c>EntitiesConfiguration.yaml</c>, and a direct insert would be a
    /// second, silently divergent copy of those rules.
    /// </summary>
    private async Task ApplyApplicationAsync(AuditableContext db, JiraObjectMapping mapping,
        AssetAttributeProjector.ProjectedObject projected, JiraAssetObjectView view, bool dryRun,
        AssetImportResult result)
    {
        var name = projected.Name!;

        var existing = await FindApplicationAsync(db, name);

        if (existing == null && !mapping.CreateMissing)
        {
            result.Unchanged++;
            view.MatchReason = "no-match (create is off)";
            return;
        }

        if (existing != null && !mapping.UpdateExisting)
        {
            result.Unchanged++;
            view.MatchReason = "name";
            view.TargetEntityId = existing.Id;
            return;
        }

        view.MatchReason = existing == null ? "created" : "name";

        if (existing == null) result.Created++;
        else result.Updated++;

        if (dryRun)
        {
            view.TargetEntityId = existing?.Id;
            return;
        }

        var entity = existing ?? entities.CreateInstance(SystemUserId, ApplicationDefinition);

        SetProperty(entity, "name", name);
        if (projected.Environment is { } environment) SetProperty(entity, "environment", environment);
        if (projected.Get("Technology") is { } technology) SetProperty(entity, "technology", technology);
        if (projected.Active is { } active) SetProperty(entity, "active", active ? "True" : "False");

        // The responsible is a Definition(person) reference, so a name only lands if a person entity
        // with that name already exists. An unmatched owner is *reported*, not invented: creating a
        // person row from a CMDB string is how a directory fills up with duplicates of real people.
        if (projected.Owner is { } owner)
        {
            var person = await FindPersonAsync(db, owner);

            if (person != null) SetProperty(entity, "responsible", person.Id.ToString());
            else
                result.Messages.Add(
                    $"{name}: no person entity matches the responsible '{owner}', so it was recorded "
                    + "on the import row but not linked.");
        }

        view.TargetEntityId = entity.Id;
    }

    /// <summary>
    /// Sets one property on an entity, creating it or updating it as needed.
    ///
    /// Idempotent by design: a re-import must not add a second <c>name</c> property, which is what a
    /// blind create would do on every run and which the definition's single-valued rule would then
    /// start refusing.
    /// </summary>
    private void SetProperty(Entity entity, string type, string value)
    {
        var existing = entity.EntitiesProperties.FirstOrDefault(p =>
            string.Equals(p.Type, type, StringComparison.OrdinalIgnoreCase));

        var dto = new EntitiesPropertyDto { Type = type, Name = type, Value = value };

        if (existing == null)
        {
            entities.CreateProperty(ApplicationDefinition, ref entity, dto);
            return;
        }

        if (string.Equals(existing.Value, value, StringComparison.Ordinal)) return;

        dto.Id = existing.Id;
        entities.UpdateProperty(ref entity, dto);
    }

    private static async Task<Entity?> FindApplicationAsync(AuditableContext db, string name)
    {
        var candidates = await db.Entities
            .Include(e => e.EntitiesProperties)
            .Where(e => e.DefinitionName == ApplicationDefinition && e.Status != "deleted")
            .ToListAsync();

        return candidates.FirstOrDefault(e => e.EntitiesProperties.Any(p =>
            p.Type == "name" && string.Equals(p.Value, name, StringComparison.OrdinalIgnoreCase)));
    }

    private static async Task<Entity?> FindPersonAsync(AuditableContext db, string owner)
    {
        var candidates = await db.Entities
            .Include(e => e.EntitiesProperties)
            .Where(e => e.DefinitionName == "person" && e.Status != "deleted")
            .ToListAsync();

        // Name first, then email: a CMDB owner attribute holds one or the other depending on whether
        // it references a user or is free text, and matching only on name would miss every site that
        // stores the address.
        return candidates.FirstOrDefault(e => e.EntitiesProperties.Any(p =>
                   p.Type == "name" && string.Equals(p.Value, owner, StringComparison.OrdinalIgnoreCase)))
               ?? candidates.FirstOrDefault(e => e.EntitiesProperties.Any(p =>
                   p.Type == "email" && string.Equals(p.Value, owner, StringComparison.OrdinalIgnoreCase)));
    }

    // --- deactivation, audit, log -----------------------------------------------------------

    /// <summary>
    /// Retires the previously imported objects the AQL no longer returns.
    ///
    /// Only ever reached when the mapping opted in, and only for objects this connection imported
    /// before — never for a host a scanner found. The opt-in is off by default because a typo in an
    /// AQL filter returns nothing, and an import that decommissions production on a typo is worse than
    /// one that leaves a stale row.
    /// </summary>
    private async Task DeactivateMissingAsync(AuditableContext db, int connectionId,
        JiraObjectMapping mapping, IReadOnlySet<string> seen, AssetImportResult result)
    {
        var stale = await db.JiraAssetObjects
            .Where(o => o.ConnectionId == connectionId
                        && o.ObjectTypeId == mapping.ObjectTypeId
                        && o.TargetHostId != null)
            .ToListAsync();

        foreach (var row in stale.Where(o => !seen.Contains(o.ObjectId)))
        {
            var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == row.TargetHostId);

            if (host == null || host.Status == (short)IntStatus.Retired) continue;

            host.Status = (short)IntStatus.Retired;
            host.LastVerificationDate = DateTime.UtcNow;

            row.MappedActive = false;
            row.LastSyncedAt = DateTime.UtcNow;

            result.Deactivated++;
            result.Messages.Add(
                $"{row.MappedName ?? row.ObjectKey ?? row.ObjectId}: retired — the register no longer "
                + "returns it.");
        }

        await db.SaveChangesAsync();
    }

    private static async Task WriteAuditAsync(AuditableContext db, int connectionId,
        JiraObjectMapping mapping, AssetObjectPayload payload, JiraAssetObjectView view)
    {
        var row = await db.JiraAssetObjects.FirstOrDefaultAsync(o =>
            o.ConnectionId == connectionId && o.ObjectId == payload.ObjectId);

        if (row == null)
        {
            row = new JiraAssetObject
            {
                ConnectionId = connectionId,
                ObjectId = payload.ObjectId,
                FirstSeenAt = DateTime.UtcNow
            };

            db.JiraAssetObjects.Add(row);
        }

        row.ObjectKey = Clip(payload.ObjectKey, 128);
        row.ObjectTypeId = mapping.ObjectTypeId;
        row.ObjectTypeName = Clip(payload.ObjectTypeName ?? mapping.ObjectTypeName, 255);
        row.Label = Clip(payload.Label, 512);
        row.MappedName = Clip(view.MappedName, 512);
        row.MappedOwner = Clip(view.MappedOwner, 255);
        row.MappedEnvironment = Clip(view.MappedEnvironment, 64);
        row.MappedActive = view.MappedActive;
        row.AttributesJson = payload.RawJson;
        row.TargetKind = mapping.TargetKind;
        row.TargetHostId = view.TargetHostId;
        row.TargetEntityId = view.TargetEntityId;
        row.MatchReason = Clip(view.MatchReason, 128);
        row.CreatedAtRemote = payload.CreatedAt;
        row.UpdatedAtRemote = payload.UpdatedAt;
        row.LastSyncedAt = DateTime.UtcNow;
        row.ImportError = view.ImportError;

        await db.SaveChangesAsync();

        view.Id = row.Id;
    }

    public async Task<List<JiraAssetObjectView>> GetAssetObjectsAsync(int connectionId, int limit = 500)
    {
        await EnsureConnectionExistsAsync(connectionId);

        await using var db = DalService.GetContext();

        // The base URL is read once and the links built from it, rather than a URL being stored per
        // row: a site that is renamed would otherwise leave every previously imported row pointing at
        // the old host.
        var baseUrl = await db.IssueTrackerConnections
            .Where(c => c.Id == connectionId)
            .Select(c => c.BaseUrl)
            .FirstOrDefaultAsync();

        return (await db.JiraAssetObjects
                .Where(o => o.ConnectionId == connectionId)
                .OrderByDescending(o => o.LastSyncedAt ?? o.FirstSeenAt)
                .Take(Math.Clamp(limit, 1, 5000))
                .ToListAsync())
            .Select(o => new JiraAssetObjectView
            {
                Id = o.Id,
                ObjectId = o.ObjectId,
                ObjectKey = o.ObjectKey,
                ObjectUrl = AssetObjectUrl(baseUrl, o.ObjectKey),
                ObjectTypeName = o.ObjectTypeName,
                Label = o.Label,
                MappedName = o.MappedName,
                MappedOwner = o.MappedOwner,
                MappedEnvironment = o.MappedEnvironment,
                MappedActive = o.MappedActive,
                TargetKind = o.TargetKind,
                TargetHostId = o.TargetHostId,
                TargetEntityId = o.TargetEntityId,
                MatchReason = o.MatchReason,
                LastSyncedAt = o.LastSyncedAt,
                ImportError = o.ImportError
            }).ToList();
    }

    private async Task RecordAssetImportAsync(int connectionId, string connectionName,
        AssetImportResult result)
    {
        await using var db = DalService.GetContext();

        db.IntegrationSyncLogs.Add(new IntegrationSyncLog
        {
            Integration = IntegrationKind.JiraAssets,
            ConnectionId = connectionId,
            ConnectionName = connectionName,
            StartedAt = DateTime.UtcNow,
            FinishedAt = DateTime.UtcNow,
            Status = result.Errors == 0
                ? IntegrationSyncStatus.Succeeded
                : result.Created + result.Updated > 0
                    ? IntegrationSyncStatus.PartiallySucceeded
                    : IntegrationSyncStatus.Failed,
            CreatedCount = result.Created,
            UpdatedCount = result.Updated,
            SkippedCount = result.Unchanged,
            FailedCount = result.Errors,
            Summary = Truncate(
                $"{result.Examined} object(s) examined, {result.Created} created, {result.Updated} "
                + $"updated, {result.Deactivated} retired."
                + (result.Messages.Count == 0
                    ? ""
                    : " " + string.Join(" | ", result.Messages.Take(20))), 2000)
        });

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// The object's page on the Jira site.
    ///
    /// Keyed on the object *key* (<c>ITSM-88</c>) and not the numeric id — Atlassian's own
    /// documentation for this route says so, and the id looks plausible enough that it is the natural
    /// wrong guess. Null when the object carries no key, rather than a URL that would 404.
    /// </summary>
    internal static string? AssetObjectUrl(string? baseUrl, string? objectKey) =>
        string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(objectKey)
            ? null
            : $"{baseUrl.TrimEnd('/')}/jira/servicedesk/assets/object/{Uri.EscapeDataString(objectKey)}";

    /// <summary>
    /// The value written to <c>hosts.external_provider</c>, alongside Vision One's and
    /// SecurityScorecard's. It is what keeps two integrations from claiming the same host's
    /// <c>external_id</c>.
    /// </summary>
    internal const string ExternalProvider = "JiraAssets";

    private const string ApplicationDefinition = "application";

    /// <summary>
    /// The author recorded on an entity the importer creates.
    ///
    /// Zero rather than the operator who pressed the button, because the scheduled import has no
    /// operator and an entity whose creator changes depending on whether a person or a job created it
    /// is a worse record than one that consistently says "the system".
    /// </summary>
    private const int SystemUserId = 0;
}

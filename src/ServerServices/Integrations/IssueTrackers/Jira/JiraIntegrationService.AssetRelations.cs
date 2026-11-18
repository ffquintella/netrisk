using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Integrations;

namespace ServerServices.Integrations.IssueTrackers.Jira;

/// <summary>
/// What a run's objects said about which host runs which application (M58, T291, S39 CMDB-REL), collected while the
/// mappings import and resolved once at the end, so either side of the relation may be imported first.
/// </summary>
internal sealed class AssetRelationBatch
{
    /// <summary>Host id → (its name, the applications it named).</summary>
    public Dictionary<int, (string Name, List<AssetObjectReference> References)> FromHosts { get; } = new();

    /// <summary>Application entity id → (its name, the hosts it named).</summary>
    public Dictionary<int, (string Name, List<AssetObjectReference> References)> FromApplications { get; } = new();

    public bool IsEmpty => FromHosts.Count == 0 && FromApplications.Count == 0;

    /// <summary>Records an object that resolved and named related objects; one that did neither is not an owner.</summary>
    public void Add(JiraAssetTargetKind kind, JiraAssetObjectView view, AssetAttributeProjector.ProjectedObject projected)
    {
        if (view.ImportError != null) return;

        var name = view.MappedName ?? view.Label ?? view.ObjectId;

        if (kind == JiraAssetTargetKind.Host && view.TargetHostId is { } hostId &&
            projected.ReferencesOf(MappableFields.Applications) is { Count: > 0 } applications)
            Merge(FromHosts, hostId, name, applications);
        else if (kind == JiraAssetTargetKind.ApplicationEntity && view.TargetEntityId is { } entityId &&
                 projected.ReferencesOf(MappableFields.Hosts) is { Count: > 0 } hosts)
            Merge(FromApplications, entityId, name, hosts);
    }

    private static void Merge(Dictionary<int, (string Name, List<AssetObjectReference> References)> map, int id, string name,
        IEnumerable<AssetObjectReference> references)
    {
        if (!map.TryGetValue(id, out var entry)) map[id] = entry = (name, []);
        entry.References.AddRange(references.Where(r => !entry.References.Contains(r)));
    }
}

public partial class JiraIntegrationService
{
    /// <summary>
    /// Turns the run's relations into <c>host_application_links</c> (T291). A reference resolves by the object this
    /// connection imported (id, then key), else by name. Each reported pair is created or refreshed; a <c>cmdb</c> link of
    /// this connection that an owner examined this run no longer names is removed. Another connection's <c>cmdb</c> link
    /// and a <c>manual</c> link are never refreshed, re-sourced or removed. The dry run counts alike and writes nothing.
    /// </summary>
    private async Task ApplyRelationsAsync(AuditableContext db, int connectionId, AssetRelationBatch batch, bool dryRun,
        AssetImportResult result)
    {
        if (batch.IsEmpty) return;

        var now = DateTime.UtcNow;

        var imported = await db.JiraAssetObjects.AsNoTracking()
            .Where(o => o.ConnectionId == connectionId && (o.TargetHostId != null || o.TargetEntityId != null))
            .Select(o => new { o.ObjectId, o.ObjectKey, o.TargetHostId, o.TargetEntityId })
            .ToListAsync();

        int? Imported(AssetObjectReference r, bool host) =>
            imported.Where(o => (r.ObjectId != null && o.ObjectId == r.ObjectId) ||
                                (r.ObjectKey != null && string.Equals(o.ObjectKey, r.ObjectKey, StringComparison.OrdinalIgnoreCase)))
                .Select(o => host ? o.TargetHostId : o.TargetEntityId).FirstOrDefault(id => id != null);

        // Name fallbacks, loaded only for the references the import rows did not resolve.
        var applicationNames = batch.FromHosts.Count == 0
            ? new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
            : await ApplicationIdsByNameAsync(db);

        var hostLabels = batch.FromApplications.Values.SelectMany(v => v.References)
            .Where(r => Imported(r, host: true) == null).Select(r => r.Label).Distinct().ToList();
        var hostsByLabel = hostLabels.Count == 0
            ? []
            : await db.Hosts.AsNoTracking()
                .Where(h => (h.HostName != null && hostLabels.Contains(h.HostName)) || (h.Fqdn != null && hostLabels.Contains(h.Fqdn)))
                .OrderBy(h => h.Id)
                .Select(h => new { h.Id, h.HostName, h.Fqdn })
                .ToListAsync();

        int? HostByLabel(string label) => hostsByLabel.FirstOrDefault(h =>
            string.Equals(h.HostName, label, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(h.Fqdn, label, StringComparison.OrdinalIgnoreCase))?.Id;

        var reported = new HashSet<(int HostId, int EntityId)>();

        foreach (var (hostId, (name, references)) in batch.FromHosts)
        {
            var missing = new List<string>();
            foreach (var r in references)
            {
                if ((Imported(r, host: false) ?? (applicationNames.TryGetValue(r.Label, out var byName) ? byName : null)) is { } entityId)
                    reported.Add((hostId, entityId));
                else missing.Add(r.Label);
            }
            ReportUnresolved(result, name, missing, "application");
        }

        foreach (var (entityId, (name, references)) in batch.FromApplications)
        {
            var missing = new List<string>();
            foreach (var r in references)
            {
                if ((Imported(r, host: true) ?? HostByLabel(r.Label)) is { } hostId) reported.Add((hostId, entityId));
                else missing.Add(r.Label);
            }
            ReportUnresolved(result, name, missing, "host");
        }

        var hostIds = batch.FromHosts.Keys.Concat(reported.Select(p => p.HostId)).Distinct().ToList();
        var entityIds = batch.FromApplications.Keys.Concat(reported.Select(p => p.EntityId)).Distinct().ToList();

        var existing = await db.HostApplicationLinks
            .Where(l => hostIds.Contains(l.HostId) || entityIds.Contains(l.EntityId))
            .ToListAsync();
        var byPair = existing.ToDictionary(l => (l.HostId, l.EntityId));

        foreach (var pair in reported.OrderBy(p => p.HostId).ThenBy(p => p.EntityId))
        {
            if (byPair.TryGetValue(pair, out var link))
            {
                // A person's link stays exactly as they left it; the import neither refreshes nor claims it.
                if (!AssetContextSources.MayOverwrite(link.Source, AssetContextSources.Cmdb) || dryRun) continue;

                // Another connection's cmdb link is that connection's to refresh and remove. Taking it over made two
                // connections reporting one relation hand the row back and forth every run, each time an audited update.
                if (link.Source == AssetContextSources.Cmdb && link.ConnectionId is { } owner && owner != connectionId) continue;

                link.Source = AssetContextSources.Cmdb;
                link.LastSeenAt = now;
                if (link.ConnectionId != connectionId)
                {
                    link.ConnectionId = connectionId;
                    link.UpdatedAt = now;
                }
                continue;
            }

            result.RelationsLinked++;
            if (!dryRun)
                db.HostApplicationLinks.Add(new HostApplicationLink
                {
                    HostId = pair.HostId, EntityId = pair.EntityId, Source = AssetContextSources.Cmdb,
                    ConnectionId = connectionId, CreatedAt = now, LastSeenAt = now
                });
        }

        // Only the owners the register was asked about this run, only this connection's cmdb rows.
        foreach (var stale in existing.Where(l => l.Source == AssetContextSources.Cmdb && l.ConnectionId == connectionId &&
                                                  !reported.Contains((l.HostId, l.EntityId)) &&
                                                  (batch.FromHosts.ContainsKey(l.HostId) ||
                                                   batch.FromApplications.ContainsKey(l.EntityId))))
        {
            result.RelationsRemoved++;
            if (!dryRun) db.HostApplicationLinks.Remove(stale);
        }

        if (!dryRun) await db.SaveChangesAsync();

        result.Messages.Add($"Relations: {result.RelationsLinked} linked, {result.RelationsRemoved} removed, "
                            + $"{result.RelationsUnresolved} not found.");
    }

    /// <summary>One message per object, naming at most five of what it could not find.</summary>
    private static void ReportUnresolved(AssetImportResult result, string name, List<string> missing, string kind)
    {
        if (missing.Count == 0) return;

        result.RelationsUnresolved += missing.Count;
        result.Messages.Add($"{Clip(name, 128)}: {missing.Count} related {kind}(s) not found in NetRisk — "
                            + string.Join(", ", missing.Take(5).Select(m => Clip(m, 64)))
                            + (missing.Count > 5 ? ", …" : "")
                            + ". Import their object type on this connection, or check the names.");
    }

    /// <summary>The live application entities by name; the lowest id wins a name two applications share.</summary>
    private static async Task<Dictionary<string, int>> ApplicationIdsByNameAsync(AuditableContext db)
    {
        var rows = await db.Entities.AsNoTracking()
            .Where(e => e.DefinitionName == ApplicationDefinition && e.Status != "deleted")
            .SelectMany(e => e.EntitiesProperties.Where(p => p.Type == "name").Select(p => new { e.Id, p.Value }))
            .ToListAsync();

        var names = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows.Where(r => !string.IsNullOrWhiteSpace(r.Value)).OrderBy(r => r.Id))
            names.TryAdd(row.Value!.Trim(), row.Id);
        return names;
    }
}

using System.Text.Json;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Integrations;
using ServerServices.Integrations.EntityMappings;

namespace ServerServices.Integrations.Tenable;

public partial class TenableService
{
    /// <summary>Assets applied per <c>SaveChanges</c>, the Vision One inventory's batch size for its reason.</summary>
    private const int AssetChunkSize = 500;

    /// <summary>
    /// T267 with T268: the asset export onto hosts by the identity chain, the tag rules of the connection (rule, then the
    /// default entity, then unassigned; the restamp policy for an attributed host), the asset's identity and tags in
    /// <c>host_external_ids</c>, and ACR and AES in <c>vendor_scores</c> when the connection collects them. Never writes
    /// <c>hosts.criticality</c> (S40 §6.8).
    /// </summary>
    private async Task SyncAssetsAsync(TenableConnection connection, TenableCredentials credentials, IntegrationSyncRun run,
        TenableSyncResult result, CancellationToken ct)
    {
        var requestedAt = DateTime.UtcNow;
        await run.StepAsync("assets", connection.AssetsCursor is { } since
            ? $"Requesting the asset export updated since {since:yyyy-MM-dd HH:mm} UTC."
            : "Requesting the full asset export.", ct: ct);

        var assets = new List<TenableAsset>();
        await client.ExportAssetsAsync(connection, credentials, connection.AssetsCursor,
            chunk =>
            {
                assets.AddRange(TenableNormalizer.ParseAssets(chunk));
                return Task.CompletedTask;
            },
            message => run.StepAsync("assets", Redact(message, credentials), ct: ct), ct);

        result.AssetsReceived += assets.Count;
        await run.StepAsync("assets", "Asset records received.", assets.Count, ct);

        var rules = await LoadRulesAsync(connection, run, ct);
        await ApplyAssetsAsync(connection, assets.Where(a => !a.Gone).ToList(), rules, result, ct);

        if ((connection.CollectAcr || connection.CollectAes) && assets.Count > 0 &&
            !assets.Any(a => (connection.CollectAcr && a.Acr is not null) || (connection.CollectAes && a.Aes is not null)))
        {
            const string missing = "ACR/AES missing: check the licence (Tenable One or Lumin).";
            result.Messages.Add(missing);
            await run.StepAsync("scores", missing, ct: ct);
        }

        await using (var db = DalService.GetContext())
        {
            var stored = await db.TenableConnections.FirstAsync(c => c.Id == connection.Id, ct);
            stored.AssetsCursor = requestedAt;
            await db.SaveChangesAsync(ct);
        }

        await run.StepAsync("assets",
            $"{result.HostsCreated} host(s) created, {result.HostsUpdated} updated; {result.HostsAssignedByRule} attributed and "
            + $"{result.HostsReassignedByRule} reattributed by rule; {result.ScoresWritten} reading(s) opened.", ct: ct);
    }

    /// <summary>The connection's enabled tag rules in the order they are tried (S40 §6.6), and a trail line naming them.</summary>
    private async Task<IReadOnlyList<MappingRule>> LoadRulesAsync(TenableConnection connection, IntegrationSyncRun run,
        CancellationToken ct)
    {
        await using var db = DalService.GetContext();
        var rules = await db.VendorEntityMappings.AsNoTracking()
            .Where(m => m.Provider == IntegrationKind.Tenable && m.ConnectionId == connection.Id && m.Enabled)
            .Select(m => new MappingRule(m.Id, m.MatchKind, m.MatchValue, m.EntityId, m.Priority, m.RestampPolicy))
            .ToListAsync(ct);

        var ordered = VendorEntityMappingRules.Order(rules);
        await run.StepAsync("entity-mapping", $"{ordered.Count} tag rule(s) will be applied.", ct: ct);
        return ordered;
    }

    private async Task ApplyAssetsAsync(TenableConnection connection, List<TenableAsset> assets,
        IReadOnlyList<MappingRule> rules, TenableSyncResult result, CancellationToken ct)
    {
        if (assets.Count == 0) return;

        await using var db = DalService.GetContext();
        db.AuditActor = ImportAuditActor;

        var index = await HostIdentityIndex.LoadAsync(db, ct);
        var identities = await db.HostExternalIds
            .Where(x => x.Provider == ProviderName)
            .ToDictionaryAsync(x => x.ExternalId, StringComparer.OrdinalIgnoreCase, ct);

        foreach (var chunk in assets.Chunk(AssetChunkSize))
        {
            var applied = new List<(TenableAsset Asset, Host Host, bool Created, MappingOutcome? Rule)>();

            foreach (var asset in chunk)
            {
                var host = index.Match(asset, identities);
                var created = host is null;
                MappingOutcome? outcome;

                if (host is null)
                {
                    host = NewHost(connection, asset);
                    var winner = VendorEntityMappingRules.Winner(rules, asset.Tags);
                    outcome = winner is null ? null : MappingOutcome.Assign;
                    if (winner is not null) host.EntityId = winner.EntityId;
                    db.Hosts.Add(host);
                    index.Add(host);
                }
                else
                {
                    Refresh(host, asset);
                    var winner = VendorEntityMappingRules.Winner(rules, asset.Tags);
                    outcome = winner is null ? null : VendorEntityMappingRules.Decide(host.EntityId, winner);
                    if (winner is not null) host.EntityId = VendorEntityMappingRules.Apply(host.EntityId, winner);
                }

                applied.Add((asset, host, created, outcome));
            }

            // Hosts first: a new host's id is what its identity row and its readings point at.
            await db.SaveChangesAsync(ct);

            var now = DateTime.UtcNow;
            var readings = new List<VendorScoreReading>();
            foreach (var (asset, host, created, rule) in applied)
            {
                if (created) index.Add(host); // now with its id, so a later chunk naming the same asset finds it
                RecordIdentity(db, identities, connection, asset, host, now);

                var details = asset.LastAssessedAt is { } at
                    ? JsonSerializer.Serialize(new { lastAssessedAt = at.ToString("O") })
                    : null;
                if (connection.CollectAes && asset.Aes is { } aes)
                    readings.Add(new VendorScoreReading(VendorScoreSource.TenableAes, connection.Id, host.Id, asset.Id,
                        host.EntityId, aes, 1000, details));
                if (connection.CollectAcr && asset.Acr is { } acr)
                    readings.Add(new VendorScoreReading(VendorScoreSource.TenableAcr, connection.Id, host.Id, asset.Id,
                        host.EntityId, acr, 10, details));

                if (created) result.HostsCreated++;
                else result.HostsUpdated++;
                if (rule == MappingOutcome.Assign) result.HostsAssignedByRule++;
                else if (rule == MappingOutcome.Reassign) result.HostsReassignedByRule++;
            }

            result.ScoresWritten += await VendorScoreStore.ApplyAsync(db, readings, now, ct);
            await db.SaveChangesAsync(ct);
        }
    }

    private static void RecordIdentity(NRDbContext db, Dictionary<string, HostExternalId> identities,
        TenableConnection connection, TenableAsset asset, Host host, DateTime now)
    {
        var tags = asset.Tags.Count == 0
            ? null
            : JsonSerializer.Serialize(asset.Tags.Order(StringComparer.OrdinalIgnoreCase).ToList());

        if (identities.TryGetValue(asset.Id, out var row))
        {
            row.HostId = host.Id;
            row.ConnectionId = connection.Id;
            row.Tags = tags;
            row.LastSeenAt = now;
            row.UpdatedAt = now;
            return;
        }

        row = new HostExternalId
        {
            HostId = host.Id, Provider = ProviderName, ConnectionId = connection.Id, ExternalId = asset.Id, Tags = tags,
            FirstSeenAt = now, LastSeenAt = now, CreatedAt = now
        };
        db.HostExternalIds.Add(row);
        identities[asset.Id] = row;
    }

    /// <summary>A host for an asset NetRisk has never seen, under the connection's default entity (a rule may override).</summary>
    private static Host NewHost(TenableConnection connection, TenableAsset asset) => new()
    {
        HostName = asset.DisplayName ?? asset.Id,
        Fqdn = asset.Fqdns.FirstOrDefault(),
        Ip = asset.Ipv4s.FirstOrDefault(),
        MacAddress = asset.MacAddresses.FirstOrDefault(),
        Os = asset.OperatingSystem,
        Source = ProviderName,
        Status = 1,
        RegistrationDate = DateTime.UtcNow,
        LastVerificationDate = DateTime.UtcNow,
        EntityId = connection.EntityId,
        ExternalId = asset.Id,
        ExternalProvider = ProviderName
    };

    /// <summary>
    /// Fills only what the host lacks — a name a person typed beats one a scanner guessed — and never touches the external
    /// id another provider owns: Tenable's identity lives in <c>host_external_ids</c>. Criticality is never written.
    /// </summary>
    private static void Refresh(Host host, TenableAsset asset)
    {
        if (string.IsNullOrWhiteSpace(host.ExternalProvider))
        {
            host.ExternalProvider = ProviderName;
            host.ExternalId = asset.Id;
        }

        host.HostName ??= asset.DisplayName;
        host.Fqdn ??= asset.Fqdns.FirstOrDefault();
        host.Ip ??= asset.Ipv4s.FirstOrDefault();
        host.MacAddress ??= asset.MacAddresses.FirstOrDefault();
        host.Os ??= asset.OperatingSystem;
        host.LastVerificationDate = DateTime.UtcNow;
    }

    /// <summary>
    /// The identity chain of S40 §9.4 over one read of <c>hosts</c>: the Tenable asset id in <c>host_external_ids</c>,
    /// then any MAC, FQDN, hostname and IPv4 the asset reports, in that order (external id before IP, D-14). Keys compare
    /// case-insensitively, as the database collation does, and each keeps its lowest-id host.
    /// </summary>
    private sealed class HostIdentityIndex
    {
        private readonly Dictionary<int, Host> _byId = new();
        private readonly Dictionary<string, Host> _byMac = New(), _byFqdn = New(), _byName = New(), _byIp = New();

        private static Dictionary<string, Host> New() => new(StringComparer.OrdinalIgnoreCase);

        public static async Task<HostIdentityIndex> LoadAsync(NRDbContext db, CancellationToken ct)
        {
            var index = new HostIdentityIndex();
            foreach (var host in await db.Hosts.OrderBy(h => h.Id).ToListAsync(ct)) index.Add(host);
            return index;
        }

        public void Add(Host host)
        {
            if (host.Id != 0) _byId.TryAdd(host.Id, host);
            if (!string.IsNullOrWhiteSpace(host.MacAddress)) _byMac.TryAdd(host.MacAddress, host);
            if (!string.IsNullOrWhiteSpace(host.Fqdn)) _byFqdn.TryAdd(host.Fqdn, host);
            if (!string.IsNullOrWhiteSpace(host.HostName)) _byName.TryAdd(host.HostName, host);
            if (!string.IsNullOrWhiteSpace(host.Ip)) _byIp.TryAdd(host.Ip, host);
        }

        public Host? Match(TenableAsset asset, IReadOnlyDictionary<string, HostExternalId> identities)
        {
            if (identities.TryGetValue(asset.Id, out var identity) && _byId.TryGetValue(identity.HostId, out var known))
                return known;
            return First(_byMac, asset.MacAddresses) ?? First(_byFqdn, asset.Fqdns) ?? First(_byName, asset.HostNames)
                ?? First(_byIp, asset.Ipv4s);
        }

        private static Host? First(Dictionary<string, Host> map, IEnumerable<string> keys)
        {
            foreach (var key in keys)
                if (map.TryGetValue(key, out var host)) return host;
            return null;
        }
    }
}

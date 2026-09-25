using Contracts.Importers;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Integrations;
using Model.Secrets;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Integrations.TrendMicro;

/// <summary>
/// Trend Micro Vision One synchronization (Track 4 milestone 4.4).
///
/// Vulnerability ingestion deliberately goes through <see cref="IFindingIngestionService"/> rather than
/// writing <c>vulnerabilities</c> rows directly. That is what gives Vision One findings the same
/// deduplication, sticky triage and SLA due dates as a Nessus import — an integration that inserted
/// its own rows would reactivate every false positive on every sync.
/// </summary>
public class TrendMicroService(
    ILogger logger,
    IDalService dalService,
    ISecretProtector protector,
    ISecretResolver resolver,
    ITrendMicroClient client,
    IFindingIngestionService ingestion,
    IFindingLifecycleService lifecycle,
    IIntegrationSyncTracker tracker)
    : ServiceBase(logger, dalService), ITrendMicroService
{
    /// <summary>The importer name Vision One findings are recorded under, and their dedup identity.</summary>
    public const string ImporterName = "trendmicro-visionone";

    /// <summary>Value written to <c>hosts.external_provider</c> and <c>hosts.risk_score_source</c>.</summary>
    public const string ProviderName = "TrendMicroVisionOne";

    // --- connections ------------------------------------------------------------------------

    public async Task<List<TrendMicroConnectionView>> GetConnectionsAsync(bool includeDisabled = true)
    {
        await using var db = DalService.GetContext();

        var connections = await db.TrendMicroConnections
            .Where(c => includeDisabled || c.Enabled)
            .OrderBy(c => c.Name)
            .ToListAsync();

        return connections.Select(ToView).ToList();
    }

    public async Task<TrendMicroConnectionView> GetConnectionAsync(int id)
    {
        await using var db = DalService.GetContext();
        return ToView(await LoadAsync(db, id));
    }

    public async Task<TrendMicroConnectionView> CreateConnectionAsync(TrendMicroConnection connection,
        string? apiKey)
    {
        Validate(connection);

        await using var db = DalService.GetContext();

        if (await db.TrendMicroConnections.AnyAsync(c => c.Name == connection.Name))
            throw new InvalidParameterException(nameof(connection.Name),
                $"A Vision One connection named '{connection.Name}' already exists.");

        var stored = Copy(connection, new TrendMicroConnection { CreatedAt = DateTime.UtcNow });
        stored.EncryptedApiKey = protector.Protect(apiKey);

        db.TrendMicroConnections.Add(stored);
        await db.SaveChangesAsync();

        Logger.Information("Vision One connection {Name} created for region {Region}",
            stored.Name, stored.Region);

        return ToView(stored);
    }

    public async Task<TrendMicroConnectionView> UpdateConnectionAsync(TrendMicroConnection connection,
        string? apiKey)
    {
        Validate(connection);

        await using var db = DalService.GetContext();

        var stored = await LoadAsync(db, connection.Id);

        if (await db.TrendMicroConnections.AnyAsync(c => c.Name == connection.Name && c.Id != connection.Id))
            throw new InvalidParameterException(nameof(connection.Name),
                $"A Vision One connection named '{connection.Name}' already exists.");

        Copy(connection, stored);
        stored.UpdatedAt = DateTime.UtcNow;

        if (apiKey != null) stored.EncryptedApiKey = protector.Protect(apiKey);

        await db.SaveChangesAsync();

        return ToView(stored);
    }

    public async Task DeleteConnectionAsync(int id)
    {
        await using var db = DalService.GetContext();

        var stored = await LoadAsync(db, id);

        db.TrendMicroConnections.Remove(stored);
        await db.SaveChangesAsync();

        Logger.Information("Vision One connection {Id} ({Name}) deleted", id, stored.Name);
    }

    public async Task<ConnectionTestResult> TestConnectionAsync(int id)
    {
        await using var db = DalService.GetContext();

        var connection = await LoadAsync(db, id);

        try
        {
            return await client.TestAsync(connection,
                await resolver.ResolveAsync(connection.EncryptedApiKey));
        }
        catch (Exception ex) when (ex is SecretProtectionException or SecretVaultResolutionException)
        {
            // A vault reference that will not resolve is reported the same way a credential that
            // will not decrypt is: as a failed test with the reason, not as an exception. The
            // operator is looking at the connection form either way.
            return ConnectionTestResult.Fail(ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Warning("Testing Vision One connection {Id} threw: {Message}", id, ex.Message);
            return ConnectionTestResult.Fail($"The test failed: {ex.Message}");
        }
    }

    public IReadOnlyDictionary<string, string> GetRegions() => TrendMicroRegions.BaseUrls;

    // --- synchronization --------------------------------------------------------------------

    public async Task<PostureSyncResult> SyncAsync(int connectionId, CancellationToken ct = default)
    {
        var result = new PostureSyncResult();

        TrendMicroConnection connection;

        await using (var db = DalService.GetContext())
        {
            connection = await LoadAsync(db, connectionId);
        }

        // await using: a run that escapes without recording an outcome is settled by disposal
        // rather than left Running until the ledger's reaper horizon — see IntegrationSyncRun.
        await using var run = await BeginLogAsync(connection, ct);

        try
        {
            // Inside the try, not above it. BeginLogAsync has already written a Running row, so a
            // credential that cannot be decrypted has to reach CompleteLogAsync as well — when this
            // threw past the try, every such attempt left a sync-log row Running forever and logged
            // nothing at all, which read exactly like a sync that had succeeded silently.
            var apiKey = await resolver.ResolveAsync(connection.EncryptedApiKey, ct);

            // 4.4.2 — inventory. Runs first because the CVE pass and the risk-score pass both look
            // hosts up by external id, and a device NetRisk has never seen would otherwise be skipped.
            await run.StepAsync("inventory", $"Requesting the device inventory from {connection.BaseUrl}.",
                ct: ct);

            var devices = await client.GetDevicesAsync(connection, apiKey,
                message => run.StepAsync("inventory", message, ct: ct), ct);

            await run.StepAsync("inventory", "Device inventory received.", devices.Count, ct);

            var hostsByExternalId = await SyncInventoryAsync(connection, devices, result, ct);

            await run.StepAsync("inventory",
                $"{result.HostsCreated} host(s) created, {result.HostsUpdated} updated.", ct: ct);

            // 4.4.4 — risk scores. They ride on the inventory rows as latestRiskScore, so the second
            // crawl this used to make (/v3.0/asrm/highRiskDevices) is gone. That endpoint does exist,
            // but it returns only the devices Vision One already considers at risk — reading the
            // inventory scores every device the tenant has, which is what the Cyber Risk Index needs.
            if (connection.SyncRiskScores)
            {
                await run.StepAsync("risk-scores", "Rolling device risk scores into the Cyber Risk Index.",
                    ct: ct);

                await SyncRiskScoresAsync(connection, devices, result, ct);

                await run.StepAsync("risk-scores",
                    $"{result.PostureRowsWritten} posture row(s) written"
                    + (result.CyberRiskIndex == null ? "" : $", index {result.CyberRiskIndex}") + ".",
                    ct: ct);
            }
            else
            {
                await run.StepAsync("risk-scores", "Skipped: the connection does not sync risk scores.",
                    ct: ct);
            }

            // 4.4.3 — CVEs, including virtual-patch state.
            if (connection.SyncVulnerabilities)
            {
                await run.StepAsync("cves", "Requesting vulnerable devices.", ct: ct);

                var vulnerabilities = await client.GetVulnerableDevicesAsync(connection, apiKey,
                    message => run.StepAsync("cves", message, ct: ct), ct);

                await run.StepAsync("cves", "Vulnerability records received.", vulnerabilities.Count, ct);

                await IngestVulnerabilitiesAsync(connection, vulnerabilities, devices, result, ct);

                await run.StepAsync("cves",
                    $"{result.FindingsCreated} finding(s) created, {result.FindingsUpdated} updated, "
                    + $"{result.VirtualPatchesApplied} closed by virtual patch.", ct: ct);
            }
            else
            {
                await run.StepAsync("cves", "Skipped: the connection does not sync vulnerabilities.",
                    ct: ct);
            }

            await CompleteLogAsync(run, connection, result, null);
        }
        catch (SecretProtectionException ex)
        {
            result.Errors++;
            result.Messages.Add(ex.Message);

            Logger.Error(ex, "Vision One sync for connection {Connection} could not read its API key",
                connection.Name);

            await CompleteLogAsync(run, connection, result, ex.Message);

            // Rethrown, unlike every other failure: an undecryptable credential is a state the operator
            // has to fix by re-entering the key, which the controller reports as 409. Swallowing it here
            // would answer 200 with an error count, and "the sync ran" is the one thing it did not do.
            throw;
        }
        catch (Exception ex)
        {
            result.Errors++;
            result.Messages.Add(ex.Message);

            Logger.Error(ex, "Vision One sync for connection {Connection} failed", connection.Name);

            await CompleteLogAsync(run, connection, result, ex.Message);
        }

        return result;
    }

    public async Task<PostureSyncResult> SyncDueConnectionsAsync(DateTime nowUtc, CancellationToken ct = default)
    {
        var combined = new PostureSyncResult();

        List<int> due;

        await using (var db = DalService.GetContext())
        {
            // Before the due list is computed, not after: a row left Running by a process that died
            // mid-sync would otherwise refuse every scheduled run for this connection from here on.
            await IntegrationSyncLedger.ReapAbandonedAsync(db, IntegrationKind.TrendMicroVisionOne,
                nowUtc, ct: ct);

            var connections = await db.TrendMicroConnections
                .Where(c => c.Enabled)
                .Select(c => new { c.Id, c.LastSyncAt, c.SyncIntervalHours })
                .ToListAsync(ct);

            due = connections
                .Where(c => c.LastSyncAt == null
                            || c.LastSyncAt.Value.AddHours(Math.Max(1, c.SyncIntervalHours)) <= nowUtc)
                .Select(c => c.Id)
                .ToList();
        }

        foreach (var connectionId in due)
        {
            PostureSyncResult result;

            try
            {
                result = await SyncAsync(connectionId, ct);
            }
            catch (IntegrationSyncBusyException ex)
            {
                // One busy connection is not a failed pass: the run that holds it is doing the work
                // this one would have done, and the remaining connections still have to be synced.
                Logger.Information("Vision One connection {Connection} was skipped: {Message}",
                    connectionId, ex.Message);

                combined.Messages.Add(ex.Message);
                continue;
            }

            combined.HostsCreated += result.HostsCreated;
            combined.HostsUpdated += result.HostsUpdated;
            combined.FindingsCreated += result.FindingsCreated;
            combined.FindingsUpdated += result.FindingsUpdated;
            combined.VirtualPatchesApplied += result.VirtualPatchesApplied;
            combined.PostureRowsWritten += result.PostureRowsWritten;
            combined.Errors += result.Errors;
            combined.Messages.AddRange(result.Messages);
            combined.CyberRiskIndex ??= result.CyberRiskIndex;
        }

        return combined;
    }

    /// <summary>
    /// How many devices are applied before the context is flushed.
    ///
    /// Not one, which is what this used to do. A <c>SaveChanges</c> per device on a tenant of 17,900
    /// devices is 17,900 round trips *and* 17,900 change-tracker sweeps over an ever-growing set of
    /// tracked hosts, which is quadratic and was the larger half of a 52-minute inventory pass. 500 is
    /// large enough that the round trips stop mattering and small enough that one failed batch costs a
    /// bounded amount of re-work.
    /// </summary>
    private const int InventoryChunkSize = 500;

    /// <summary>
    /// Upserts the device inventory onto NetRisk hosts (4.4.2).
    ///
    /// The match order is the deduplication the milestone calls for, strongest identity first: the
    /// provider's own external id, then MAC, then hostname/FQDN, then IP. IP is last on purpose — DHCP
    /// makes it the weakest of the four, and matching on it first merges two machines that happened to
    /// share a lease.
    ///
    /// The matching is done against one in-memory read of <c>hosts</c> rather than a query per
    /// candidate. Four of those five columns — <c>external_id</c>, <c>mac_address</c>, <c>fqdn</c>,
    /// <c>host_name</c>, <c>ip</c> — carry no index, so the old shape was up to five full scans per
    /// device: on the tenant this was fixed for, 17,934 devices against an 18,000-row table, which is
    /// on the order of a billion row comparisons and took 52 minutes for a pass that created 32 hosts.
    /// One read of the table is a single scan, and the dictionaries below answer the same five
    /// questions in constant time.
    /// </summary>
    private async Task<Dictionary<string, int>> SyncInventoryAsync(TrendMicroConnection connection,
        List<TrendMicroDevice> devices, PostureSyncResult result, CancellationToken ct)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var db = DalService.GetContext();

        try
        {
            var index = await HostMatchIndex.LoadAsync(db, ct);

            var remaining = 0;

            foreach (var chunk in devices.Chunk(InventoryChunkSize))
            {
                // Counted per chunk and only added to the result once the chunk is committed: a batch
                // that fails is re-applied one device at a time below, and counting it here as well
                // would report every host in it twice.
                var applied = new List<(TrendMicroDevice Device, Host Host, bool Created)>();

                foreach (var device in chunk)
                {
                    try
                    {
                        var host = index.Match(device);
                        var created = host == null;

                        if (host == null)
                        {
                            host = NewHost(connection, device);
                            db.Hosts.Add(host);

                            // Into the index before the chunk is saved, so a second device carrying the
                            // same MAC in the same batch claims this host instead of creating a twin —
                            // which is what the per-device save used to give for free.
                            index.Add(host);
                        }
                        else
                        {
                            ApplyToExistingHost(host, device);
                        }

                        applied.Add((device, host, created));
                    }
                    catch (Exception ex)
                    {
                        RecordDeviceError(result, device, ex);
                    }
                }

                try
                {
                    await db.SaveChangesAsync(ct);
                }
                catch (Exception ex)
                {
                    // The context now holds a batch that will not commit, so every later chunk would
                    // fail on the same rows. It is abandoned, and the rest of the inventory — this
                    // chunk included, since nothing in it was written — falls back to the one-device-
                    // at-a-time path, which is slow but isolates the row that is actually bad.
                    Logger.Warning(ex,
                        "A batch of {Count} Vision One device(s) for connection {Connection} could not "
                        + "be saved; falling back to one device at a time", applied.Count, connection.Name);

                    result.Messages.Add(
                        $"A batch of {applied.Count} device(s) failed to save ({ex.Message}); "
                        + "the remaining devices were applied one at a time.");

                    await db.DisposeAsync();
                    db = null!;

                    await SyncInventoryDeviceByDeviceAsync(connection,
                        devices.Skip(remaining).ToList(), map, result, ct);

                    return map;
                }

                foreach (var (device, host, created) in applied)
                {
                    map[device.Id] = host.Id;

                    if (created) result.HostsCreated++;
                    else result.HostsUpdated++;
                }

                remaining += chunk.Length;
            }
        }
        finally
        {
            if (db != null) await db.DisposeAsync();
        }

        return map;
    }

    /// <summary>
    /// The original one-device-at-a-time upsert, kept as the recovery path for a batch that would not
    /// commit. Every lookup is a query and every device is its own transaction, which is exactly what
    /// makes it able to report the single device that is at fault and carry on.
    /// </summary>
    private async Task SyncInventoryDeviceByDeviceAsync(TrendMicroConnection connection,
        List<TrendMicroDevice> devices, Dictionary<string, int> map, PostureSyncResult result,
        CancellationToken ct)
    {
        await using var db = DalService.GetContext();

        foreach (var device in devices)
        {
            try
            {
                var host = await MatchHostAsync(db, connection, device, ct);

                if (host == null)
                {
                    host = NewHost(connection, device);
                    db.Hosts.Add(host);
                    result.HostsCreated++;
                }
                else
                {
                    ApplyToExistingHost(host, device);
                    result.HostsUpdated++;
                }

                await db.SaveChangesAsync(ct);

                map[device.Id] = host.Id;
            }
            catch (Exception ex)
            {
                RecordDeviceError(result, device, ex);
            }
        }
    }

    /// <summary>
    /// The per-device form of the match, one query per identity. Only the recovery path uses it now;
    /// the normal pass answers the same five questions from <see cref="HostMatchIndex"/>.
    /// </summary>
    private static async Task<Host?> MatchHostAsync(AuditableContext db, TrendMicroConnection connection,
        TrendMicroDevice device, CancellationToken ct)
    {
        var host = await db.Hosts.FirstOrDefaultAsync(
            h => h.ExternalProvider == ProviderName && h.ExternalId == device.Id, ct);

        if (host != null) return host;

        if (!string.IsNullOrWhiteSpace(device.PrimaryMac))
        {
            host = await db.Hosts.FirstOrDefaultAsync(h => h.MacAddress == device.PrimaryMac, ct);
            if (host != null) return host;
        }

        if (!string.IsNullOrWhiteSpace(device.Fqdn))
        {
            host = await db.Hosts.FirstOrDefaultAsync(h => h.Fqdn == device.Fqdn, ct);
            if (host != null) return host;
        }

        if (!string.IsNullOrWhiteSpace(device.Name))
        {
            host = await db.Hosts.FirstOrDefaultAsync(h => h.HostName == device.Name, ct);
            if (host != null) return host;
        }

        if (!string.IsNullOrWhiteSpace(device.PrimaryIp))
            host = await db.Hosts.FirstOrDefaultAsync(h => h.Ip == device.PrimaryIp, ct);

        return host;
    }

    private static Host NewHost(TrendMicroConnection connection, TrendMicroDevice device) => new()
    {
        HostName = device.Name ?? device.Fqdn ?? device.PrimaryIp ?? device.Id,
        Fqdn = device.Fqdn,
        Ip = device.PrimaryIp,
        MacAddress = device.PrimaryMac,
        Os = device.OperatingSystem,
        OsVersion = device.OsVersion,
        Source = ProviderName,
        Status = 1,
        RegistrationDate = DateTime.UtcNow,
        EntityId = connection.EntityId,
        ExternalId = device.Id,
        ExternalProvider = ProviderName,
        Criticality = device.Criticality
    };

    private static void ApplyToExistingHost(Host host, TrendMicroDevice device)
    {
        // Claiming an existing host: the external id is written so the next sync matches on it directly
        // rather than re-deriving the match from MAC or hostname.
        host.ExternalId = device.Id;
        host.ExternalProvider = ProviderName;

        // Only filled in where NetRisk has nothing. A hostname a person typed is better data than one
        // an agent guessed, and overwriting it every night is how an integration becomes something
        // people turn off.
        host.HostName ??= device.Name;
        host.Fqdn ??= device.Fqdn;
        host.Ip ??= device.PrimaryIp;
        host.MacAddress ??= device.PrimaryMac;
        host.Os ??= device.OperatingSystem;
        host.OsVersion = device.OsVersion ?? host.OsVersion;

        // Criticality is the provider's to own: it is the asset classification the customer configured
        // in Vision One, which is more current than a NetRisk value nobody maintains.
        if (device.Criticality != null) host.Criticality = device.Criticality;

        host.LastVerificationDate = DateTime.UtcNow;
    }

    private void RecordDeviceError(PostureSyncResult result, TrendMicroDevice device, Exception ex)
    {
        result.Errors++;
        result.Messages.Add($"Device {device.Id}: {ex.Message}");
        Logger.Warning("Could not sync Vision One device {Device}: {Message}", device.Id, ex.Message);
    }

    /// <summary>
    /// One read of <c>hosts</c>, indexed the five ways <see cref="MatchHostAsync"/> queries it.
    ///
    /// Case-insensitive on purpose: the database compares these columns under a case-insensitive
    /// collation, and a plain .NET dictionary would not — so matching in memory with ordinal keys would
    /// quietly create a second host for a device whose hostname came back in a different case.
    /// </summary>
    private sealed class HostMatchIndex
    {
        private readonly Dictionary<string, Host> _byExternalId = New();
        private readonly Dictionary<string, Host> _byMac = New();
        private readonly Dictionary<string, Host> _byFqdn = New();
        private readonly Dictionary<string, Host> _byName = New();
        private readonly Dictionary<string, Host> _byIp = New();

        private static Dictionary<string, Host> New() => new(StringComparer.OrdinalIgnoreCase);

        public static async Task<HostMatchIndex> LoadAsync(AuditableContext db, CancellationToken ct)
        {
            var index = new HostMatchIndex();

            // Ordered by id, and every key kept at its first holder: the queries this replaces were
            // FirstOrDefault against an unordered table, which MariaDB answers in primary-key order for
            // a scan. Two hosts sharing a MAC therefore resolve to the same one as before.
            foreach (var host in await db.Hosts.OrderBy(h => h.Id).ToListAsync(ct))
                index.Add(host);

            return index;
        }

        public void Add(Host host)
        {
            if (host.ExternalProvider == ProviderName && !string.IsNullOrWhiteSpace(host.ExternalId))
                _byExternalId.TryAdd(host.ExternalId, host);

            if (!string.IsNullOrWhiteSpace(host.MacAddress)) _byMac.TryAdd(host.MacAddress, host);
            if (!string.IsNullOrWhiteSpace(host.Fqdn)) _byFqdn.TryAdd(host.Fqdn, host);
            if (!string.IsNullOrWhiteSpace(host.HostName)) _byName.TryAdd(host.HostName, host);
            if (!string.IsNullOrWhiteSpace(host.Ip)) _byIp.TryAdd(host.Ip, host);
        }

        /// <summary>Strongest identity first, in the same order <see cref="MatchHostAsync"/> asks.</summary>
        public Host? Match(TrendMicroDevice device)
        {
            if (_byExternalId.TryGetValue(device.Id, out var host)) return host;

            if (!string.IsNullOrWhiteSpace(device.PrimaryMac)
                && _byMac.TryGetValue(device.PrimaryMac, out host)) return host;

            if (!string.IsNullOrWhiteSpace(device.Fqdn)
                && _byFqdn.TryGetValue(device.Fqdn, out host)) return host;

            if (!string.IsNullOrWhiteSpace(device.Name)
                && _byName.TryGetValue(device.Name, out host)) return host;

            if (!string.IsNullOrWhiteSpace(device.PrimaryIp)
                && _byIp.TryGetValue(device.PrimaryIp, out host)) return host;

            return null;
        }
    }

    /// <summary>
    /// Writes device risk scores and rolls them into the entity's Cyber Risk Index (4.4.4).
    ///
    /// The index is a criticality-weighted mean rather than a plain average: a critical server at 90 and
    /// twenty test machines at 10 should not average out to "fine", which is exactly what an unweighted
    /// mean would say.
    /// </summary>
    private async Task SyncRiskScoresAsync(TrendMicroConnection connection, List<TrendMicroDevice> inventory,
        PostureSyncResult result, CancellationToken ct)
    {
        var scores = new Dictionary<string, TrendMicroDevice>(StringComparer.OrdinalIgnoreCase);

        foreach (var device in inventory)
        {
            if (device.RiskScore == null) continue;
            scores[device.Id] = device;
        }

        if (scores.Count == 0) return;

        await using var db = DalService.GetContext();

        // One read of this provider's hosts rather than a query per scored device. external_id carries
        // no index, so each of those queries was a full scan of hosts — 9,340 of them on the tenant
        // this was measured against, for a pass that writes one column on each row it finds.
        var byExternalId = new Dictionary<string, Host>(StringComparer.OrdinalIgnoreCase);

        foreach (var known in await db.Hosts
                     .Where(h => h.ExternalProvider == ProviderName && h.ExternalId != null)
                     .OrderBy(h => h.Id)
                     .ToListAsync(ct))
            byExternalId.TryAdd(known.ExternalId!, known);

        foreach (var (externalId, device) in scores)
        {
            if (!byExternalId.TryGetValue(externalId, out var host)) continue;

            host.RiskScore = device.RiskScore;
            host.RiskScoreSource = ProviderName;
            host.RiskScoreUpdatedAt = DateTime.UtcNow;

            if (device.Criticality != null) host.Criticality = device.Criticality;

            result.PostureRowsWritten++;
        }

        await db.SaveChangesAsync(ct);

        if (connection.EntityId == null) return;

        var hosts = await db.Hosts
            .Where(h => h.EntityId == connection.EntityId && h.RiskScore != null)
            .Select(h => new { h.RiskScore, h.Criticality })
            .ToListAsync(ct);

        if (hosts.Count == 0) return;

        var weighted = hosts.Sum(h => (double)h.RiskScore! * (h.Criticality ?? 3));
        var weights = hosts.Sum(h => (double)(h.Criticality ?? 3));

        var index = Math.Round(weighted / weights, 2);

        var entity = await db.Entities.FirstOrDefaultAsync(e => e.Id == connection.EntityId, ct);

        if (entity != null)
        {
            entity.CyberRiskIndex = index;
            entity.PostureSource = ProviderName;
            entity.PostureUpdatedAt = DateTime.UtcNow;

            await db.SaveChangesAsync(ct);
        }

        result.CyberRiskIndex = index;

        Logger.Information(
            "Vision One risk sync set the Cyber Risk Index for entity {Entity} to {Index} over {Hosts} host(s)",
            connection.EntityId, index, hosts.Count);
    }

    /// <summary>
    /// Turns per-device CVEs into NetRisk findings through the shared ingestion pipeline (4.4.3), then
    /// applies the virtual-patch policy.
    /// </summary>
    private async Task IngestVulnerabilitiesAsync(TrendMicroConnection connection,
        List<TrendMicroDeviceVulnerability> vulnerabilities, List<TrendMicroDevice> devices,
        PostureSyncResult result, CancellationToken ct)
    {
        if (vulnerabilities.Count == 0) return;

        var byId = devices.ToDictionary(d => d.Id, d => d, StringComparer.OrdinalIgnoreCase);

        var parsed = new ImportResult
        {
            DetectedTool = ImporterName,
            // Not a full scan: Vision One reports the devices it knows about, and treating that as
            // exhaustive would auto-close every finding from every other scanner.
            IsFullScan = false,
            ScanDate = DateTime.UtcNow
        };

        foreach (var vulnerability in vulnerabilities)
        {
            var device = byId.GetValueOrDefault(vulnerability.DeviceId);

            var finding = new NormalizedFinding
            {
                Tool = ImporterName,
                // The CVE on the device is the identity: the same CVE on the same machine is the same
                // finding, whatever Vision One renames the title to.
                ToolUniqueId = $"{vulnerability.DeviceId}:{vulnerability.CveId}",
                RuleId = vulnerability.CveId,
                Title = vulnerability.Title ?? vulnerability.CveId,
                Description = vulnerability.Description,
                Severity = MapSeverity(vulnerability),
                RawSeverity = vulnerability.Severity,
                Cvss3BaseScore = vulnerability.CvssScore,
                ExploitAvailable = vulnerability.ExploitAvailable,
                FirstSeen = vulnerability.FirstDetected,
                LastSeen = vulnerability.LastDetected,
                RawStatus = vulnerability.VirtualPatchApplied ? "virtually-patched" : "open",
                Host = device == null
                    ? new NormalizedHost { HostName = vulnerability.DeviceName }
                    : new NormalizedHost
                    {
                        Ip = device.PrimaryIp,
                        HostName = device.Name,
                        Fqdn = device.Fqdn,
                        MacAddress = device.PrimaryMac,
                        OperatingSystem = device.OperatingSystem
                    }
            };

            finding.Cves.Add(vulnerability.CveId);

            if (vulnerability.EpssScore != null)
                finding.ToolFields["epss"] = vulnerability.EpssScore.Value.ToString("0.0000");

            if (vulnerability.MitigationStatus != null)
                finding.ToolFields["mitigationStatus"] = vulnerability.MitigationStatus;

            if (vulnerability.VirtualPatchApplied)
            {
                finding.ToolFields["virtualPatch"] = "applied";

                if (vulnerability.VirtualPatchRuleId != null)
                    finding.ToolFields["virtualPatchRuleId"] = vulnerability.VirtualPatchRuleId;

                // Stated in the evidence as well as in ToolFields: the triager reading the finding needs
                // to know a compensating control is in place without going to Vision One to find out.
                finding.Evidence =
                    $"Trend Micro Vision One reports a virtual patch covering this CVE on this device"
                    + (vulnerability.VirtualPatchRuleId == null
                        ? "."
                        : $" (IPS rule {vulnerability.VirtualPatchRuleId}).");
            }

            parsed.Findings.Add(finding);
        }

        var import = await ingestion.IngestAsync(parsed, new ImportIngestionRequest
        {
            Importer = ImporterName,
            FileName = $"Vision One {connection.Name} {DateTime.UtcNow:yyyy-MM-dd HH:mm}",
            EntityId = connection.EntityId
        }, ct);

        result.ImportId = import.Id;
        result.FindingsCreated += import.NewCount;
        result.FindingsUpdated += import.UpdatedCount;

        if (connection.VirtualPatchClosesFinding)
            await ApplyVirtualPatchPolicyAsync(vulnerabilities, result, ct);
        else
            result.VirtualPatchesApplied = 0;
    }

    /// <summary>
    /// Moves findings covered by a virtual patch to <c>Mitigated</c>, recording the IPS rule in the
    /// transition's justification so the audit trail says why (4.4.3).
    ///
    /// Only when the connection opted in. A virtual patch is a compensating control, not a fix — the
    /// underlying software is still vulnerable — so whether it closes the finding is the customer's
    /// policy call, and defaulting to closing it would quietly hide unpatched software.
    /// </summary>
    private async Task ApplyVirtualPatchPolicyAsync(List<TrendMicroDeviceVulnerability> vulnerabilities,
        PostureSyncResult result, CancellationToken ct)
    {
        var patched = vulnerabilities.Where(v => v.VirtualPatchApplied).ToList();

        if (patched.Count == 0) return;

        await using var db = DalService.GetContext();

        foreach (var vulnerability in patched)
        {
            var toolUniqueId = $"{vulnerability.DeviceId}:{vulnerability.CveId}";

            var finding = await db.Vulnerabilities.FirstOrDefaultAsync(
                v => v.ToolUniqueId == toolUniqueId && v.ImportSource == ImporterName, ct);

            if (finding == null) continue;
            if (finding.LifecycleStatus == FindingStatus.Mitigated) continue;

            // Suppressed findings are left alone: a finding somebody marked false-positive must not be
            // reopened and re-closed by an integration.
            if (finding.LifecycleStatus.IsSuppressed()) continue;

            try
            {
                await lifecycle.TransitionAsync(finding.Id, FindingStatus.Mitigated, null,
                    FindingStatusChangeSource.Job,
                    "Covered by a Trend Micro virtual patch"
                    + (vulnerability.VirtualPatchRuleId == null
                        ? "."
                        : $" (IPS rule {vulnerability.VirtualPatchRuleId})."));

                result.VirtualPatchesApplied++;
            }
            catch (Exception ex)
            {
                result.Errors++;
                result.Messages.Add($"{vulnerability.CveId} on {vulnerability.DeviceId}: {ex.Message}");
            }
        }
    }

    public async Task<bool> PushExemptionAsync(int findingId, string reason, CancellationToken ct = default)
    {
        await using var db = DalService.GetContext();

        var finding = await db.Vulnerabilities.FirstOrDefaultAsync(v => v.Id == findingId, ct)
                      ?? throw new DataNotFoundException("vulnerabilities", findingId.ToString(),
                          new Exception($"Finding {findingId} was not found."));

        if (finding.HostId == null) return false;

        var host = await db.Hosts.FirstOrDefaultAsync(h => h.Id == finding.HostId, ct);

        if (host?.ExternalId == null || host.ExternalProvider != ProviderName) return false;

        var connection = await db.TrendMicroConnections
            .FirstOrDefaultAsync(c => c.Enabled && c.PushExemptions
                                                && (c.EntityId == null || c.EntityId == finding.EntityId), ct);

        // Not an error: the overwhelmingly common configuration is not to write back into the EDR
        // console, and the caller reports "no connection is configured to push exemptions".
        if (connection == null) return false;

        var note = $"NetRisk accepted the risk for {finding.Title}: {reason}";

        return await client.UpdateDeviceAsync(connection,
            await resolver.ResolveAsync(connection.EncryptedApiKey, ct),
            host.ExternalId, host.Criticality, note, ct);
    }

    public async Task<List<IntegrationSyncLog>> GetSyncLogAsync(int limit = 50)
    {
        await using var db = DalService.GetContext();

        return await db.IntegrationSyncLogs
            .Where(l => l.Integration == IntegrationKind.TrendMicroVisionOne)
            .OrderByDescending(l => l.Id)
            .Take(Math.Clamp(limit, 1, 500))
            .ToListAsync();
    }

    // --- helpers ----------------------------------------------------------------------------

    /// <summary>
    /// Maps Vision One severity onto NetRisk's scale, preferring the CVSS score when the word is
    /// missing — Vision One omits the severity word on some ASRM payloads but rarely the score.
    /// </summary>
    internal static NormalizedSeverity MapSeverity(TrendMicroDeviceVulnerability vulnerability)
    {
        var word = (vulnerability.Severity ?? string.Empty).Trim().ToLowerInvariant();

        var mapped = word switch
        {
            "critical" => NormalizedSeverity.Critical,
            "high" => NormalizedSeverity.High,
            "medium" or "moderate" => NormalizedSeverity.Medium,
            "low" => NormalizedSeverity.Low,
            "info" or "informational" or "none" => NormalizedSeverity.None,
            _ => (NormalizedSeverity?)null
        } ?? FromCvss(vulnerability.CvssScore);

        return mapped;
    }

    /// <summary>CVSS v3 qualitative bands.</summary>
    private static NormalizedSeverity FromCvss(double? score) => score switch
    {
        null => NormalizedSeverity.Medium,
        >= 9.0 => NormalizedSeverity.Critical,
        >= 7.0 => NormalizedSeverity.High,
        >= 4.0 => NormalizedSeverity.Medium,
        > 0 => NormalizedSeverity.Low,
        _ => NormalizedSeverity.None
    };

    private void Validate(TrendMicroConnection connection)
    {
        if (connection == null) throw new InvalidParameterException(nameof(connection), "A connection is required.");

        if (string.IsNullOrWhiteSpace(connection.Name))
            throw new InvalidParameterException(nameof(connection.Name), "A connection requires a name.");

        if (string.IsNullOrWhiteSpace(connection.Region))
            throw new InvalidParameterException(nameof(connection.Region),
                "A region is required. Available: " + string.Join(", ", TrendMicroRegions.BaseUrls.Keys));

        var regional = TrendMicroRegions.BaseUrlFor(connection.Region);

        if (regional == null && string.IsNullOrWhiteSpace(connection.BaseUrl))
            throw new InvalidParameterException(nameof(connection.Region),
                $"'{connection.Region}' is not a Vision One region. Available: "
                + string.Join(", ", TrendMicroRegions.BaseUrls.Keys)
                + ". Supply an explicit base URL to use a region NetRisk does not list.");

        // Derived from the region unless the operator overrode it, which is what makes a mistyped URL
        // impossible for the common case.
        if (string.IsNullOrWhiteSpace(connection.BaseUrl)) connection.BaseUrl = regional!;

        if (!Uri.TryCreate(connection.BaseUrl, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidParameterException(nameof(connection.BaseUrl),
                "The Vision One base URL must be an absolute https URL.");

        if (connection.SyncIntervalHours is < 1 or > 168)
            throw new InvalidParameterException(nameof(connection.SyncIntervalHours),
                "The sync interval must be between 1 and 168 hours.");
    }

    private static TrendMicroConnection Copy(TrendMicroConnection source, TrendMicroConnection target)
    {
        target.Name = source.Name.Trim();
        target.Region = source.Region.Trim().ToLowerInvariant();
        target.BaseUrl = source.BaseUrl.TrimEnd('/');
        target.EntityId = source.EntityId;
        target.Enabled = source.Enabled;
        target.SyncIntervalHours = source.SyncIntervalHours;
        target.SyncVulnerabilities = source.SyncVulnerabilities;
        target.SyncRiskScores = source.SyncRiskScores;
        target.VirtualPatchClosesFinding = source.VirtualPatchClosesFinding;
        target.PushExemptions = source.PushExemptions;
        return target;
    }

    /// <summary>
    /// Claims the connection, writes its Running row and announces the start, refusing when a run is
    /// already in flight — see <see cref="IntegrationSyncLedger"/> for why that guard lives here rather
    /// than in the caller.
    /// </summary>
    private Task<IntegrationSyncRun> BeginLogAsync(TrendMicroConnection connection,
        CancellationToken ct = default) =>
        tracker.BeginAsync(IntegrationKind.TrendMicroVisionOne, connection.Id, connection.Name,
            ProviderName, singleFlight: true, ct);

    private async Task CompleteLogAsync(IntegrationSyncRun run, TrendMicroConnection connection,
        PostureSyncResult result, string? error)
    {
        await using var db = DalService.GetContext();

        var stored = await db.IntegrationSyncLogs.FirstOrDefaultAsync(l => l.Id == run.LogId);

        var status = error != null
            ? IntegrationSyncStatus.Failed
            : result.Errors > 0
                ? IntegrationSyncStatus.PartiallySucceeded
                : IntegrationSyncStatus.Succeeded;

        if (stored != null)
        {
            stored.FinishedAt = DateTime.UtcNow;
            stored.Status = status;
            stored.CreatedCount = result.HostsCreated + result.FindingsCreated;
            stored.UpdatedCount = result.HostsUpdated + result.FindingsUpdated;
            stored.FailedCount = result.Errors;
            stored.Summary = Truncate(
                $"{result.HostsCreated} host(s) created, {result.HostsUpdated} updated, "
                + $"{result.FindingsCreated} finding(s) created, {result.FindingsUpdated} updated, "
                + $"{result.VirtualPatchesApplied} closed by virtual patch"
                + (result.CyberRiskIndex == null ? "" : $", index {result.CyberRiskIndex}") + ".", 2000);
            stored.ErrorMessage = Truncate(error, 2000);

            // Noted rather than stepped, then drained into the row this method is about to save: the
            // settle below writes the log row and the connection row in one SaveChanges, and a step
            // that flushed on its own would be a second write against the row already loaded here.
            run.Note("finished", $"Run ended {status}." + (error == null ? "" : " " + error));

            stored.ProgressLog = run.DrainInto(stored.ProgressLog);
        }

        var storedConnection = await db.TrendMicroConnections.FirstOrDefaultAsync(c => c.Id == connection.Id);

        if (storedConnection != null)
        {
            storedConnection.LastSyncAt = DateTime.UtcNow;
            storedConnection.LastSyncStatus = status;
            storedConnection.LastSyncError = Truncate(error, 2000);
        }

        try
        {
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Logged and swallowed, which is the lesser of two bad outcomes. This method is called
            // from inside SyncAsync's catch, so a throw here replaced the real failure with a 500 —
            // and a 500 is what the desktop client retries immediately, so the next attempt started
            // another sync that failed the same way, each one leaving another Running row behind. The
            // sync itself is already over; the ledger row is now inconsistent and the reaper in
            // IntegrationSyncLedger is what settles it.
            Logger.Error(ex, "The Vision One sync for connection {Connection} finished {Status} but its "
                             + "sync-log row {Log} could not be updated", connection.Name, status,
                run.LogId);
        }

        // After the settle, so a GUI woken by the notification reads the finished row and not the
        // Running one. Idempotent, because both the success branch and each catch reach this method.
        await run.NotifyFinishedAsync(status, stored?.Summary, error);
    }

    private static async Task<TrendMicroConnection> LoadAsync(AuditableContext db, int id) =>
        await db.TrendMicroConnections.FirstOrDefaultAsync(c => c.Id == id)
        ?? throw new DataNotFoundException("trendmicro_connections", id.ToString(),
            new Exception($"Vision One connection {id} was not found."));

    private static TrendMicroConnectionView ToView(TrendMicroConnection connection) => new()
    {
        Id = connection.Id,
        Name = connection.Name,
        Region = connection.Region,
        BaseUrl = connection.BaseUrl,
        HasApiKey = !string.IsNullOrEmpty(connection.EncryptedApiKey),
        ApiKeyVaultReference = SecretReference.StoredReferenceOrNull(connection.EncryptedApiKey),
        EntityId = connection.EntityId,
        Enabled = connection.Enabled,
        SyncIntervalHours = connection.SyncIntervalHours,
        SyncVulnerabilities = connection.SyncVulnerabilities,
        SyncRiskScores = connection.SyncRiskScores,
        VirtualPatchClosesFinding = connection.VirtualPatchClosesFinding,
        PushExemptions = connection.PushExemptions,
        LastSyncAt = connection.LastSyncAt,
        LastSyncStatus = connection.LastSyncStatus,
        LastSyncError = connection.LastSyncError
    };

    private static string? Truncate(string? text, int max) =>
        text == null || text.Length <= max ? text : text[..(max - 1)] + "…";
}

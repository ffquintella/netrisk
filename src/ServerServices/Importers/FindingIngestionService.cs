using System.Globalization;
using System.Text.Json;
using Contracts.Importers;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model;
using Model.Exceptions;
using Serilog;
using ServerServices.Findings;
using ServerServices.Importers.Dedup;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.Extensions;

namespace ServerServices.Importers;

/// <summary>
/// The import persistence pipeline (Track 3 milestones 3.1–3.4).
///
/// Per finding: resolve its asset, compute its dedup key, look for an existing finding with that
/// key, then either update the existing one (last-seen, occurrence count, changed severity) or
/// create a new one with a due date and a first history event. Findings a full scan no longer
/// reports are candidates for auto-close, off by default.
///
/// The rule the whole design turns on: dedup <em>groups</em>, it never discards. A second sighting
/// of a known finding raises its occurrence count and moves its last-seen date; it never silently
/// vanishes, and it never resurrects a triage verdict a human already made.
/// </summary>
public class FindingIngestionService(
    ILogger logger,
    IDalService dalService,
    IDeduplicationService dedupService,
    ISlaService slaService,
    INotificationEventPublisher notifications)
    : ServiceBase(logger, dalService), IFindingIngestionService
{
    /// <summary>
    /// The team new findings are assigned to when the caller names none. Matches what the previous
    /// Nessus importer did, so the register's existing triage queue keeps working.
    /// </summary>
    public const int DefaultFixTeamId = 1;

    /// <summary>Team assigned to hosts the importer creates, as the previous importer did.</summary>
    public const int DefaultHostTeamId = 2;

    public async Task<ImportReservation> BeginImportAsync(ImportIngestionRequest request)
    {
        // An already-used key short-circuits before the insert, which keeps the common retry case
        // off the unique-index-violation path entirely.
        if (request.IdempotencyKey != null)
        {
            var replayed = await FindByIdempotencyKeyAsync(request.IdempotencyKey);
            if (replayed != null) return new ImportReservation(replayed, IsReplay: true);
        }

        await using var db = DalService.GetContext();

        var import = new ScanImport
        {
            Importer = request.Importer,
            FileName = ImporterHelpers.Clip(request.FileName, 512),
            FileId = request.FileId,
            UserId = request.UserId,
            EntityId = request.EntityId,
            JobId = request.JobId,
            IdempotencyKey = request.IdempotencyKey,
            StartedAt = request.ImportedAt,
            Status = (int)ScanImportStatus.Running
        };

        db.ScanImports.Add(import);

        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException) when (request.IdempotencyKey != null)
        {
            // The unique index on idempotency_key rejected this: a concurrent retry claimed the key
            // first, and that request's import is the honest answer rather than a second one.
            var existing = await FindByIdempotencyKeyAsync(request.IdempotencyKey);
            if (existing != null) return new ImportReservation(existing, IsReplay: true);
            throw;
        }

        request.ExistingImportId = import.Id;
        return new ImportReservation(import, IsReplay: false);
    }

    public async Task FailImportAsync(int importId, string errorMessage)
    {
        await using var db = DalService.GetContext();

        var import = await db.ScanImports.FirstOrDefaultAsync(i => i.Id == importId);
        if (import == null) return;

        import.Status = (int)ScanImportStatus.Failed;
        import.FinishedAt = DateTime.UtcNow;
        import.ErrorMessage = ImporterHelpers.Clip(errorMessage, 65000);

        await db.SaveChangesAsync();
    }

    /// <summary>
    /// How many findings share one <c>DbContext</c> and one round of batched lookups.
    ///
    /// The number exists because the loop used to run every finding through a single long-lived
    /// context. Each save then walked a change tracker holding everything ingested so far, which is
    /// quadratic: fine for a 5000-finding Nessus file, and the reason a 563,310-finding Vision One
    /// sync ran for three days without finishing. A fresh context per batch bounds the tracker.
    /// </summary>
    public const int FindingBatchSize = 500;

    /// <summary>Largest <c>IN (…)</c> list sent in one lookup, so a batch cannot overrun the packet.</summary>
    private const int LookupBatchSize = 500;

    public async Task<ScanImport> IngestAsync(ImportResult parsed, ImportIngestionRequest request,
        Func<ImportProgress, Task>? onProgress = null, CancellationToken ct = default)
    {
        var configuration = await dedupService.GetConfigurationAsync(request.Importer);

        // Resolved once per import, not once per finding. Resolving the chain enumerates the plugin
        // directory and loads every enabled plugin, and the SLA policy table was queried per finding
        // from two different places; both are small, unchanging tables read half a million times.
        var keys = await dedupService.GetKeyCalculatorAsync(configuration);
        var sla = await LoadSlaPoliciesAsync(ct);

        int importId;

        await using (var setup = DalService.GetContext())
        {
            importId = (await ResolveImportRowAsync(setup, request)).Id;
        }

        var counts = new IngestCounts();
        var newBySeverity = new Dictionary<NormalizedSeverity, int>();
        var warnings = parsed.Warnings.Select(w => w.ToString()).ToList();

        // Every finding this import matched or created, so the auto-close pass can tell "still
        // present" from "gone" without re-deriving keys.
        var seenFindingIds = new HashSet<int>();

        // Asset resolution keeps its own context, because a host is shared by many findings while a
        // finding belongs to one batch. Its tracker is cleared after each write, so the cache holds
        // ids rather than entities and nothing accumulates across a long import.
        await using var assetDb = DalService.GetContext();

        var hostIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var serviceIds = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        // Candidate key → the finding it resolves to, for keys already looked up or created during
        // this import. Without it, a batch lookup would miss a duplicate created by an earlier batch
        // — which the old per-finding lookup caught only because it saved after every insert.
        var knownIdsByKey = new Dictionary<string, int>(StringComparer.Ordinal);

        var total = parsed.Findings.Count;
        var processed = 0;

        await ReportAsync(onProgress, "findings", processed, total, counts);

        foreach (var batch in Batch(parsed.Findings, FindingBatchSize))
        {
            ct.ThrowIfCancellationRequested();

            await IngestBatchAsync(batch, request, keys, sla, assetDb, hostIds, serviceIds,
                knownIdsByKey, seenFindingIds, counts, newBySeverity, warnings, importId, ct);

            processed += batch.Count;

            await ReportAsync(onProgress, "findings", processed, total, counts);
        }

        await using var db = DalService.GetContext();

        var import = await db.ScanImports.FirstAsync(i => i.Id == importId, ct);

        // Auto-close is opt-in per scanner and only ever runs for a report the importer itself
        // declared exhaustive. A partial scan treated as full closes every finding outside its
        // slice, which is far worse than a stale open one.
        if (configuration.AutoCloseMissing && parsed.IsFullScan)
        {
            await ReportAsync(onProgress, "auto-close", processed, total, counts);
            counts.Closed = await AutoCloseMissingAsync(db, request, seenFindingIds, importId, ct);
        }
        else if (configuration.AutoCloseMissing && !parsed.IsFullScan)
            warnings.Add("[warning] Auto-close is enabled for this scanner but the report is not a full scan; " +
                         "no findings were closed.");

        import.NewCount = counts.New;
        import.UpdatedCount = counts.Updated;
        import.DuplicateCount = counts.Duplicates;
        import.ClosedCount = counts.Closed;
        import.SkippedCount = counts.Skipped + parsed.SkippedCount;
        import.WarningCount = warnings.Count;
        import.NewBySeverity = SerializeSeverities(newBySeverity);
        import.Warnings = warnings.Count == 0 ? null : string.Join("\n", warnings);
        import.Status = (int)ScanImportStatus.Succeeded;
        import.FinishedAt = DateTime.UtcNow;

        await db.SaveChangesAsync(ct);

        Logger.Information(
            "Import {Import} ({Importer}) finished: {New} new, {Updated} updated, {Duplicate} suppressed, {Closed} closed, {Skipped} skipped",
            import.Id, import.Importer, counts.New, counts.Updated, counts.Duplicates, counts.Closed,
            import.SkippedCount);

        // Track 4.1.3 — vulnerability.imported. One notification per import rather than per finding,
        // which is why this event is the one the catalog marks digest-recommended: a nightly scan of a
        // large estate is thousands of findings and exactly one thing worth telling a channel about.
        await notifications.VulnerabilityImportedAsync(import);

        return import;
    }

    /// <summary>
    /// Persists one batch of findings on a context of its own.
    ///
    /// Two saves rather than one: a created finding's first history event needs its id, and the id
    /// only exists after the insert. The alternative the old code used — saving per finding so the
    /// id was there — is the quadratic behaviour this replaced.
    /// </summary>
    /// <summary>
    /// Persists one batch, falling back to one finding at a time if the batch insert is rejected.
    ///
    /// The fallback is what keeps the old per-finding guarantee that "one malformed finding must not
    /// lose the other 4999". With a single insert per batch, a row the database refuses now takes its
    /// 499 neighbours with it, and only a retry can tell which one was at fault.
    /// </summary>
    private async Task IngestBatchAsync(List<NormalizedFinding> batch, ImportIngestionRequest request,
        IDedupKeyCalculator keys, SlaPolicySet sla, NRDbContext assetDb,
        Dictionary<string, int> hostIds, Dictionary<string, int> serviceIds,
        Dictionary<string, int> knownIdsByKey, HashSet<int> seenFindingIds, IngestCounts counts,
        Dictionary<NormalizedSeverity, int> newBySeverity, List<string> warnings, int importId,
        CancellationToken ct)
    {
        var outcome = await TryBatchAsync(batch, request, keys, sla, assetDb, hostIds, serviceIds,
            knownIdsByKey, importId, ct);

        if (outcome != null)
        {
            outcome.MergeInto(counts, newBySeverity, warnings, seenFindingIds, knownIdsByKey);
            return;
        }

        Logger.Warning(
            "A batch of {Count} finding(s) from import {Import} was rejected as a whole; retrying "
            + "one at a time to isolate the row at fault", batch.Count, importId);

        foreach (var finding in batch)
        {
            // A one-finding batch never reports itself as un-isolatable — there is nothing left to
            // isolate — so this always has an outcome, and a rejected row comes back as its own skip.
            var single = await TryBatchAsync([finding], request, keys, sla, assetDb, hostIds,
                serviceIds, knownIdsByKey, importId, ct);

            single?.MergeInto(counts, newBySeverity, warnings, seenFindingIds, knownIdsByKey);
        }
    }

    /// <summary>
    /// Persists one batch on a context of its own, or returns null if the write was rejected.
    ///
    /// Everything it decides is accumulated into the returned outcome rather than written into the
    /// caller's running totals, so a batch that fails can be retried without having already counted
    /// the findings the retry will count again.
    /// </summary>
    private async Task<BatchOutcome?> TryBatchAsync(List<NormalizedFinding> batch,
        ImportIngestionRequest request, IDedupKeyCalculator keys, SlaPolicySet sla,
        NRDbContext assetDb, Dictionary<string, int> hostIds, Dictionary<string, int> serviceIds,
        Dictionary<string, int> knownIdsByKey, int importId, CancellationToken ct)
    {
        var outcome = new BatchOutcome();

        await using var db = DalService.GetContext();

        // Asset resolution first and for the whole batch, because the dedup key depends on the host
        // and service ids it produces. It commits as it goes on its own context, so a rejected batch
        // does not undo it — and must not, since the retry reuses the same ids.
        var resolved = new List<(NormalizedFinding Finding, int? HostId, int? ServiceId, DedupKeyResult Keys)>(batch.Count);

        foreach (var finding in batch)
        {
            try
            {
                var hostId = await ResolveHostIdAsync(assetDb, finding, request, hostIds, ct);
                var serviceId = hostId == null
                    ? null
                    : await ResolveServiceIdAsync(assetDb, hostId.Value, finding, serviceIds, ct);

                resolved.Add((finding, hostId, serviceId, keys.ComputeKey(new DedupContext
                {
                    Finding = finding,
                    HostId = hostId,
                    HostServiceId = serviceId,
                    EntityId = request.EntityId
                })));
            }
            catch (Exception ex)
            {
                outcome.Skip(finding, ex, Logger, importId);
            }
        }

        var existingById = await LoadExistingAsync(db, resolved, knownIdsByKey, request.EntityId, ct);

        // Findings created earlier in this same batch, by every key they answer to. Without it three
        // copies of one finding in one batch all miss the database lookup and all get created — which
        // the old code avoided only by saving after each insert.
        var pendingByKey = new Dictionary<string, Vulnerability>(StringComparer.Ordinal);

        var created = new List<(Vulnerability Finding, FindingStatusHistory History)>();

        foreach (var (finding, hostId, serviceId, findingKeys) in resolved)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var existing = MatchExisting(findingKeys, knownIdsByKey, existingById)
                               ?? MatchPending(findingKeys, pendingByKey);

                if (existing != null)
                {
                    var result = UpdateExisting(db, existing, finding, request, findingKeys, importId, sla);

                    // Id is 0 for a match against something created earlier in this batch; the
                    // second pass below picks those up once the insert has assigned one.
                    if (existing.Id != 0) outcome.Seen.Add(existing.Id);

                    outcome.Remember(findingKeys, existing);

                    if (result == ExistingOutcome.Suppressed) outcome.Duplicates++;
                    else outcome.Updated++;
                }
                else
                {
                    var (row, history) = CreateFinding(db, finding, hostId, serviceId, request,
                        findingKeys, importId, sla);

                    created.Add((row, history));

                    foreach (var candidate in findingKeys.Candidates) pendingByKey[candidate.Key] = row;

                    outcome.Remember(findingKeys, row);

                    outcome.New++;
                    outcome.NewBySeverity[finding.Severity] =
                        outcome.NewBySeverity.GetValueOrDefault(finding.Severity) + 1;
                }
            }
            catch (Exception ex)
            {
                outcome.Skip(finding, ex, Logger, importId);
            }
        }

        try
        {
            await db.SaveChangesAsync(ct);

            foreach (var (row, history) in created)
            {
                history.VulnerabilityId = row.Id;
                db.FindingStatusHistories.Add(history);

                outcome.Seen.Add(row.Id);
            }

            if (created.Count > 0) await db.SaveChangesAsync(ct);
        }
        catch (Exception ex) when (batch.Count > 1)
        {
            // Only worth isolating when there is more than one candidate for the blame. A single
            // finding that fails is simply a skip, which the caller records.
            Logger.Warning("A batch of {Count} finding(s) for import {Import} could not be saved: "
                           + "{Message}", batch.Count, importId, ex.Message);
            return null;
        }
        catch (Exception ex)
        {
            outcome.Skip(batch[0], ex, Logger, importId);
            outcome.Undo();

            return outcome;
        }

        return outcome;
    }

    /// <summary>A finding created earlier in this batch that one of these keys already names.</summary>
    private static Vulnerability? MatchPending(DedupKeyResult keys,
        Dictionary<string, Vulnerability> pending)
    {
        foreach (var candidate in keys.Candidates)
            if (pending.TryGetValue(candidate.Key, out var match))
                return match;

        return null;
    }

    private void Skip(IngestCounts counts, List<string> warnings, NormalizedFinding finding,
        int importId, Exception ex)
    {
        counts.Skipped++;
        warnings.Add($"[skipped] {finding.Title}: {ex.Message}");
        Logger.Warning("Could not ingest finding {Title} from import {Import}: {Message}",
            finding.Title, importId, ex.Message);
    }

    /// <summary>
    /// Splits a list into consecutive batches without copying the whole thing first.
    /// </summary>
    private static IEnumerable<List<T>> Batch<T>(List<T> source, int size)
    {
        for (var offset = 0; offset < source.Count; offset += size)
            yield return source.GetRange(offset, Math.Min(size, source.Count - offset));
    }

    private static async Task ReportAsync(Func<ImportProgress, Task>? onProgress, string phase,
        int processed, int total, IngestCounts counts)
    {
        if (onProgress == null) return;

        await onProgress(new ImportProgress(phase, processed, total, counts.New, counts.Updated,
            counts.Skipped));
    }

    public async Task<ScanImport> GetImportAsync(int importId)
    {
        await using var db = DalService.GetContext();

        var import = await db.ScanImports.AsNoTracking().FirstOrDefaultAsync(i => i.Id == importId);
        if (import == null)
            throw new DataNotFoundException("scan_imports", importId.ToString(),
                new Exception("Import not found"));

        return import;
    }

    public async Task<ScanImport?> FindByIdempotencyKeyAsync(string idempotencyKey)
    {
        await using var db = DalService.GetContext();
        return await db.ScanImports.AsNoTracking().FirstOrDefaultAsync(i => i.IdempotencyKey == idempotencyKey);
    }

    public async Task<List<ScanImport>> GetRecentImportsAsync(int take = 50)
    {
        await using var db = DalService.GetContext();

        return await db.ScanImports
            .AsNoTracking()
            .OrderByDescending(i => i.StartedAt)
            .ThenByDescending(i => i.Id)
            .Take(Math.Clamp(take, 1, 500))
            .ToListAsync();
    }

    // --- pipeline steps --------------------------------------------------------------------

    private async Task<ScanImport> ResolveImportRowAsync(NRDbContext db, ImportIngestionRequest request)
    {
        if (request.ExistingImportId != null)
        {
            var reserved = await db.ScanImports.FirstOrDefaultAsync(i => i.Id == request.ExistingImportId.Value);
            if (reserved != null) return reserved;
        }

        // A caller that skipped BeginImportAsync but supplied an idempotency key must still land on
        // the row that key already claimed. Inserting a second one would violate the unique index
        // and surface as an opaque DbUpdateException at the end of an otherwise successful import.
        if (request.IdempotencyKey != null)
        {
            var claimed = await db.ScanImports
                .FirstOrDefaultAsync(i => i.IdempotencyKey == request.IdempotencyKey);
            if (claimed != null) return claimed;
        }

        var import = new ScanImport
        {
            Importer = request.Importer,
            FileName = ImporterHelpers.Clip(request.FileName, 512),
            FileId = request.FileId,
            UserId = request.UserId,
            EntityId = request.EntityId,
            JobId = request.JobId,
            IdempotencyKey = request.IdempotencyKey,
            StartedAt = request.ImportedAt,
            Status = (int)ScanImportStatus.Running
        };

        db.ScanImports.Add(import);
        await db.SaveChangesAsync();

        return import;
    }

    /// <summary>
    /// Finds or creates the asset a finding sits on, and returns its id. Null for findings that have
    /// none — code and dependency scanners report a file path, not a host, and inventing an asset for
    /// them would fill the inventory with fictional machines.
    ///
    /// Ids rather than entities, so the cache survives the tracker being cleared below.
    /// </summary>
    private async Task<int?> ResolveHostIdAsync(NRDbContext db, NormalizedFinding finding,
        ImportIngestionRequest request, Dictionary<string, int> cache, CancellationToken ct)
    {
        var normalized = finding.Host;
        if (normalized == null || normalized.IsEmpty) return null;

        var key = normalized.Ip ?? normalized.Fqdn ?? normalized.HostName!;

        if (cache.TryGetValue(key, out var cached)) return cached;

        var existing = normalized.Ip != null
            ? await db.Hosts.FirstOrDefaultAsync(h => h.Ip == normalized.Ip, ct)
            : await db.Hosts.FirstOrDefaultAsync(h => h.HostName == key || h.Fqdn == key, ct);

        if (existing != null)
        {
            // A host the scan touched is a host that exists. Marking it verified now is what keeps
            // the inventory's "last seen" honest.
            existing.LastVerificationDate = request.ImportedAt;
            existing.Status = (short)IntStatus.Active;

            // Filled in only where empty: a scanner that reports no FQDN this time must not erase
            // the one a previous scan found.
            existing.Fqdn ??= normalized.Fqdn;
            existing.Os ??= normalized.OperatingSystem;
            existing.MacAddress ??= normalized.MacAddress;
            if (string.IsNullOrWhiteSpace(existing.Properties)) existing.Properties = normalized.Properties;

            await SaveAndForgetAsync(db, ct);

            cache[key] = existing.Id;
            return existing.Id;
        }

        var host = new Host
        {
            Ip = normalized.Ip,
            HostName = normalized.HostName ?? key,
            Fqdn = normalized.Fqdn,
            MacAddress = ImporterHelpers.Clip(normalized.MacAddress, 254),
            Os = normalized.OperatingSystem,
            Properties = ImporterHelpers.Clip(normalized.Properties, 65000),
            LastVerificationDate = request.ImportedAt,
            RegistrationDate = request.ImportedAt,
            Source = request.Importer,
            Status = (short)IntStatus.Active,
            TeamId = DefaultHostTeamId,
            EntityId = request.EntityId,
            Comment = $"Created by the {request.Importer} importer"
        };

        db.Hosts.Add(host);
        await SaveAndForgetAsync(db, ct);

        cache[key] = host.Id;
        return host.Id;
    }

    private async Task<int?> ResolveServiceIdAsync(NRDbContext db, int hostId,
        NormalizedFinding finding, Dictionary<string, int> cache, CancellationToken ct)
    {
        var normalized = finding.Host;
        if (normalized == null) return null;
        if (string.IsNullOrWhiteSpace(normalized.ServiceName) && string.IsNullOrWhiteSpace(normalized.Port))
            return null;

        var name = normalized.ServiceName ?? "unknown";
        var protocol = normalized.Protocol ?? "tcp";
        int? port = int.TryParse(normalized.Port, out var parsedPort) ? parsedPort : null;

        var key = $"{hostId}|{name}|{port}|{protocol}";
        if (cache.TryGetValue(key, out var cached)) return cached;

        var existing = await db.HostsServices
            .FirstOrDefaultAsync(s => s.HostId == hostId && s.Name == name && s.Port == port
                                      && s.Protocol == protocol, ct);

        if (existing != null)
        {
            cache[key] = existing.Id;
            return existing.Id;
        }

        var service = new DAL.Entities.HostsService
        {
            HostId = hostId,
            Name = name,
            Port = port,
            Protocol = protocol
        };

        db.HostsServices.Add(service);
        await SaveAndForgetAsync(db, ct);

        cache[key] = service.Id;
        return service.Id;
    }

    /// <summary>
    /// Saves, then forgets everything the context was tracking.
    ///
    /// The asset context lives for the whole import, and without the clear it would accumulate every
    /// host it touched — putting the same growing-tracker cost on host resolution that batching
    /// removed from findings. Nothing downstream holds these entities; the caches keep ids.
    /// </summary>
    private static async Task SaveAndForgetAsync(NRDbContext db, CancellationToken ct)
    {
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    /// <summary>
    /// Loads, in one pass over the batch, every existing finding any of its candidate keys matches.
    ///
    /// This replaced a pair of <c>FirstOrDefault</c> queries per finding. The behaviour that had to
    /// survive is that every candidate is tried, not just the primary one: a finding imported before
    /// a configuration change was keyed by whatever strategy led the chain then, and matching only
    /// the current primary key would duplicate the whole register on the next scan.
    /// </summary>
    private static async Task<Dictionary<int, Vulnerability>> LoadExistingAsync(NRDbContext db,
        List<(NormalizedFinding Finding, int? HostId, int? ServiceId, DedupKeyResult Keys)> resolved,
        Dictionary<string, int> knownIdsByKey, int? entityId, CancellationToken ct)
    {
        var unresolved = new HashSet<string>(StringComparer.Ordinal);
        var legacy = new HashSet<string>(StringComparer.Ordinal);

        foreach (var (_, _, _, keys) in resolved)
        {
            if (!keys.HasKey) continue;

            foreach (var candidate in keys.Candidates)
            {
                if (knownIdsByKey.ContainsKey(candidate.Key)) continue;

                unresolved.Add(candidate.Key);

                // The legacy candidate is additionally compared against import_hash, which is where
                // the pre-Track-3 code stored it.
                if (candidate.MatchesLegacyImportHash) legacy.Add(candidate.Key);
            }
        }

        if (unresolved.Count > 0)
            await MapKeysAsync(db, unresolved, legacy: false, entityId, knownIdsByKey, ct);

        if (legacy.Count > 0)
        {
            // Only the keys the dedup_key pass did not already place: an import_hash match is the
            // fallback, never an override.
            var stillUnresolved = legacy.Where(k => !knownIdsByKey.ContainsKey(k)).ToHashSet(StringComparer.Ordinal);

            if (stillUnresolved.Count > 0)
                await MapKeysAsync(db, stillUnresolved, legacy: true, entityId, knownIdsByKey, ct);
        }

        var wanted = new HashSet<int>();

        foreach (var (_, _, _, keys) in resolved)
        foreach (var candidate in keys.Candidates)
            if (knownIdsByKey.TryGetValue(candidate.Key, out var id))
                wanted.Add(id);

        var loaded = new Dictionary<int, Vulnerability>();

        foreach (var slice in Slice(wanted, LookupBatchSize))
        {
            var rows = await db.Vulnerabilities.Where(v => slice.Contains(v.Id)).ToListAsync(ct);
            foreach (var row in rows) loaded[row.Id] = row;
        }

        return loaded;
    }

    /// <summary>
    /// Records which finding each of <paramref name="candidateKeys"/> belongs to, if any.
    /// </summary>
    private static async Task MapKeysAsync(NRDbContext db, IReadOnlyCollection<string> candidateKeys,
        bool legacy, int? entityId, Dictionary<string, int> knownIdsByKey, CancellationToken ct)
    {
        foreach (var slice in Slice(candidateKeys, LookupBatchSize))
        {
            var matches = legacy
                ? await db.Vulnerabilities.AsNoTracking()
                    .Where(v => v.ImportHash != null && slice.Contains(v.ImportHash)
                                && (entityId == null || v.EntityId == entityId))
                    .Select(v => new { v.Id, Key = v.ImportHash! })
                    .ToListAsync(ct)
                : await db.Vulnerabilities.AsNoTracking()
                    .Where(v => v.DedupKey != null && slice.Contains(v.DedupKey)
                                && (entityId == null || v.EntityId == entityId))
                    .Select(v => new { v.Id, Key = v.DedupKey! })
                    .ToListAsync(ct);

            foreach (var match in matches)
            {
                // Lowest id wins a key two findings somehow share, so a re-import is deterministic
                // rather than dependent on the order the database happened to return rows in.
                if (knownIdsByKey.TryGetValue(match.Key, out var already) && already <= match.Id) continue;

                knownIdsByKey[match.Key] = match.Id;
            }
        }
    }

    /// <summary>
    /// The finding this one's candidate keys resolve to, trying them in chain order.
    /// </summary>
    private static Vulnerability? MatchExisting(DedupKeyResult keys,
        Dictionary<string, int> knownIdsByKey, Dictionary<int, Vulnerability> loaded)
    {
        if (!keys.HasKey) return null;

        foreach (var candidate in keys.Candidates)
        {
            if (!knownIdsByKey.TryGetValue(candidate.Key, out var id)) continue;
            if (loaded.TryGetValue(id, out var match)) return match;
        }

        return null;
    }

    private static IEnumerable<List<T>> Slice<T>(IEnumerable<T> source, int size)
    {
        var batch = new List<T>(size);

        foreach (var item in source)
        {
            batch.Add(item);

            if (batch.Count < size) continue;

            yield return batch;
            batch = new List<T>(size);
        }

        if (batch.Count > 0) yield return batch;
    }

    /// <summary>
    /// Records a fresh sighting of a known finding, honouring the sticky-triage rules.
    ///
    /// Nothing here reopens a suppressed finding, and a mitigated one that comes back is reopened as
    /// a regression with a history event — those two behaviours are the whole point of the
    /// lifecycle.
    /// </summary>
    private ExistingOutcome UpdateExisting(NRDbContext db, Vulnerability existing, NormalizedFinding finding,
        ImportIngestionRequest request, DedupKeyResult keys, int importId, SlaPolicySet sla)
    {
        existing.LastDetection = request.ImportedAt;
        existing.DetectionCount++;
        existing.LastImportId = importId;

        // Backfills the key on a finding imported before Track 3, so the next scan matches on
        // dedup_key directly rather than through the legacy hash.
        existing.DedupKey ??= keys.PrimaryKey;
        existing.DedupStrategy ??= keys.PrimaryStrategy;

        var previousSeverity = existing.Severity;
        var severityChanged = !string.Equals(previousSeverity, SeverityString(finding), StringComparison.Ordinal);

        // Scanner-derived facts are refreshed; human-entered ones (comments, assignment, technology)
        // are never touched by an import.
        existing.Severity = SeverityString(finding);
        existing.RawSeverity = ImporterHelpers.Clip(finding.RawSeverity, 64);
        existing.Score = finding.Cvss3BaseScore ?? finding.CvssBaseScore ?? existing.Score;
        ApplyScannerFields(existing, finding);

        var outcome = FindingStatusMachine.OnSeenAgain(existing.LifecycleStatus);

        if (outcome == ReimportOutcome.Reactivate)
        {
            var from = existing.LifecycleStatus;
            existing.LifecycleStatus = FindingStatus.Active;

            db.FindingStatusHistories.Add(new FindingStatusHistory
            {
                // The navigation, not just the id: `existing` may be a finding created earlier in
                // this same batch and not yet inserted, whose id is still 0. EF fills the foreign
                // key in from the reference once the principal has one.
                Vulnerability = existing,
                FromStatus = from,
                ToStatus = FindingStatus.Active,
                UserId = request.UserId,
                Source = FindingStatusChangeSource.Import,
                ChangedAt = request.ImportedAt,
                Justification = $"Reported again by {request.Importer} after being marked {from} — " +
                                "treated as a regression."
            });
        }

        if (severityChanged)
        {
            // A severity change moves the SLA deadline, so both facts go on the timeline together:
            // "why is this due sooner than it was" has one answer and it is here.
            var recomputed = RecomputeDueDate(sla, existing, finding, request);

            db.FindingStatusHistories.Add(new FindingStatusHistory
            {
                Vulnerability = existing,
                FromStatus = existing.LifecycleStatus,
                ToStatus = existing.LifecycleStatus,
                UserId = request.UserId,
                Source = FindingStatusChangeSource.Import,
                ChangedAt = request.ImportedAt,
                Justification = $"Severity changed from {previousSeverity ?? "none"} to " +
                                $"{existing.Severity ?? "none"} by {request.Importer}" +
                                (recomputed == null
                                    ? "."
                                    : $"; SLA due date recomputed to {recomputed:yyyy-MM-dd}.")
            });
        }

        return outcome == ReimportOutcome.KeepSuppressed ? ExistingOutcome.Suppressed : ExistingOutcome.Updated;
    }

    /// <summary>
    /// Builds a new finding and its first history event, without saving either.
    ///
    /// The history is handed back rather than added, because its foreign key needs an id the insert
    /// has not produced yet. Saving here to get one is what the old code did, and it is what made a
    /// large import quadratic.
    /// </summary>
    private (Vulnerability Finding, FindingStatusHistory History) CreateFinding(NRDbContext db,
        NormalizedFinding finding, int? hostId, int? serviceId, ImportIngestionRequest request,
        DedupKeyResult keys, int importId, SlaPolicySet sla)
    {
        var firstSeen = finding.FirstSeen ?? request.ImportedAt;

        var vulnerability = new Vulnerability
        {
            Title = ImporterHelpers.Clip(finding.Title, 250)!,
            Description = finding.Description.Truncate(65500),
            Solution = finding.Solution,
            Details = finding.Evidence,
            Severity = SeverityString(finding),
            RawSeverity = ImporterHelpers.Clip(finding.RawSeverity, 64),
            Score = finding.Cvss3BaseScore ?? finding.CvssBaseScore,
            FirstDetection = firstSeen,
            LastDetection = finding.LastSeen ?? request.ImportedAt,
            DetectionCount = 1,
            // The legacy workflow column stays on its existing "New" default so the register's
            // existing triage buttons keep behaving; the ASPM lifecycle is status_id.
            Status = (ushort)IntStatus.New,
            LifecycleStatus = FindingStatus.Active,
            HostId = hostId,
            HostServiceId = serviceId,
            EntityId = request.EntityId,
            AnalystId = request.UserId,
            FixTeamId = request.FixTeamId ?? DefaultFixTeamId,
            Technology = "Not Specified",
            ImportSource = request.Importer,
            // Kept alongside dedup_key so a re-import by the legacy strategy still matches findings
            // this pipeline created.
            ImportHash = keys.Candidates.FirstOrDefault(c => c.MatchesLegacyImportHash)?.Key ?? keys.PrimaryKey,
            DedupKey = keys.PrimaryKey,
            DedupStrategy = keys.PrimaryStrategy,
            LastImportId = importId
        };

        ApplyScannerFields(vulnerability, finding);

        vulnerability.SlaDueDate = sla.DueDate(finding.Severity, request.EntityId, firstSeen);

        db.Vulnerabilities.Add(vulnerability);

        var history = new FindingStatusHistory
        {
            FromStatus = null,
            ToStatus = FindingStatus.Active,
            UserId = request.UserId,
            Source = FindingStatusChangeSource.Import,
            ChangedAt = request.ImportedAt,
            Justification = $"Imported from {request.Importer}" +
                            (request.FileName == null ? "." : $" ({request.FileName}).")
        };

        return (vulnerability, history);
    }

    /// <summary>
    /// Closes open findings this scanner previously reported for this scope but did not report now
    /// (3.3.2). Only reached when the scanner is configured for it and the report is a full scan.
    /// </summary>
    private async Task<int> AutoCloseMissingAsync(NRDbContext db, ImportIngestionRequest request,
        HashSet<int> seenFindingIds, int importId, CancellationToken ct)
    {
        // "Not seen" is applied in memory, not in the query. As a predicate it becomes an IN list
        // holding every finding the import touched, which for a large scan is a statement the server
        // rejects rather than a filter.
        var candidates = await db.Vulnerabilities
            .Where(v => v.ImportSource == request.Importer
                        && (v.LifecycleStatus == FindingStatus.Active || v.LifecycleStatus == FindingStatus.Verified)
                        && (request.EntityId == null || v.EntityId == request.EntityId))
            .ToListAsync(ct);

        var stale = candidates.Where(v => !seenFindingIds.Contains(v.Id)).ToList();

        foreach (var finding in stale)
        {
            var from = finding.LifecycleStatus;
            finding.LifecycleStatus = FindingStatus.Mitigated;
            finding.LastImportId = importId;

            db.FindingStatusHistories.Add(new FindingStatusHistory
            {
                VulnerabilityId = finding.Id,
                FromStatus = from,
                ToStatus = FindingStatus.Mitigated,
                UserId = request.UserId,
                Source = FindingStatusChangeSource.Import,
                ChangedAt = request.ImportedAt,
                Justification = $"Not reported by the latest full {request.Importer} scan; " +
                                "closed automatically because auto-close is enabled for this scanner."
            });
        }

        return stale.Count;
    }

    // --- helpers ---------------------------------------------------------------------------

    /// <summary>
    /// Reads the SLA policy table once, for the whole import.
    /// </summary>
    private async Task<SlaPolicySet> LoadSlaPoliciesAsync(CancellationToken ct)
    {
        await using var db = DalService.GetContext();

        return new SlaPolicySet(await db.SlaConfigurations.AsNoTracking().ToListAsync(ct));
    }

    /// <summary>
    /// Copies the scanner-derived fields. Split out because create and update need exactly the same
    /// set, and a field that gets refreshed on create but not on update is a bug nobody notices for
    /// months.
    /// </summary>
    private static void ApplyScannerFields(Vulnerability target, NormalizedFinding finding)
    {
        target.RuleId = ImporterHelpers.Clip(finding.RuleId, 255);
        target.ToolUniqueId = ImporterHelpers.Clip(finding.ToolUniqueId, 255);
        target.Location = ImporterHelpers.Clip(finding.Location, 65000);
        target.Component = ImporterHelpers.Clip(finding.Component, 255);
        target.ComponentVersion = ImporterHelpers.Clip(finding.ComponentVersion, 255);
        target.FixedInVersion = ImporterHelpers.Clip(finding.FixedInVersion, 255);

        if (finding.Cves.Count > 0) target.Cves = string.Join(",", finding.Cves.Distinct());
        if (finding.Cwes.Count > 0) target.Cwes = string.Join(",", finding.Cwes.Distinct());
        if (finding.References.Count > 0)
            target.Xref = ImporterHelpers.Clip(string.Join(",", finding.References.Distinct()), 65000);

        target.CvssVector = ImporterHelpers.Clip(finding.CvssVector, 255);
        target.CvssBaseScore = ToFloat(finding.CvssBaseScore) ?? target.CvssBaseScore;
        target.Cvss3Vector = ImporterHelpers.Clip(finding.Cvss3Vector, 255);
        target.Cvss3BaseScore = ToFloat(finding.Cvss3BaseScore) ?? target.Cvss3BaseScore;
        target.Cvss3TemporalScore = ToFloat(finding.Cvss3TemporalScore) ?? target.Cvss3TemporalScore;
        target.Cvss3ImpactScore = ToFloat(finding.Cvss3ImpactScore) ?? target.Cvss3ImpactScore;
        target.VprScore = ToFloat(finding.VprScore) ?? target.VprScore;

        target.ExploitAvaliable = finding.ExploitAvailable ?? target.ExploitAvaliable;
        target.ExploitCodeMaturity = ImporterHelpers.Clip(finding.ExploitCodeMaturity, 255) ??
                                    target.ExploitCodeMaturity;
        target.ExploitabilityEasy = ImporterHelpers.Clip(finding.ExploitabilityEasy, 255) ??
                                    target.ExploitabilityEasy;
        target.ExploitedByScanner = finding.ExploitedByScanner ?? target.ExploitedByScanner;
        target.ThreatIntensity = ImporterHelpers.Clip(finding.ThreatIntensity, 255) ?? target.ThreatIntensity;
        target.ThreatRecency = ImporterHelpers.Clip(finding.ThreatRecency, 255) ?? target.ThreatRecency;
        target.ThreatSources = ImporterHelpers.Clip(finding.ThreatSources, 255) ?? target.ThreatSources;
        target.VulnerabilityPublicationDate = finding.VulnerabilityPublicationDate ??
                                              target.VulnerabilityPublicationDate;
        target.PatchPublicationDate = finding.PatchPublicationDate ?? target.PatchPublicationDate;
    }

    /// <summary>
    /// Recomputes the due date inside an already-open context, so the change lands in the same
    /// transaction as the severity that caused it. <see cref="ISlaService"/>'s own recompute opens
    /// its own context, which would split the two.
    /// </summary>
    private static DateTime? RecomputeDueDate(SlaPolicySet sla, Vulnerability existing,
        NormalizedFinding finding, ImportIngestionRequest request)
    {
        existing.SlaDueDate = sla.DueDate(finding.Severity, request.EntityId, existing.FirstDetection);

        return existing.SlaDueDate;
    }

    /// <summary>
    /// The register's <c>severity</c> column is free text and has always held the numeric scale for
    /// Nessus findings. Writing the normalized band's number keeps every importer's findings sortable
    /// against each other, which the mixed strings never were.
    /// </summary>
    private static string SeverityString(NormalizedFinding finding) =>
        ((int)finding.Severity).ToString(CultureInfo.InvariantCulture);

    private static float? ToFloat(double? value) => value == null ? null : (float)value.Value;

    private static string? SerializeSeverities(Dictionary<NormalizedSeverity, int> counts)
    {
        if (counts.Count == 0) return null;

        // Keyed by name rather than number so the stored JSON is readable, and so a CI gate policy
        // can be written as "critical" instead of "4".
        var byName = counts.ToDictionary(c => c.Key.ToString().ToLowerInvariant(), c => c.Value);
        return JsonSerializer.Serialize(byName);
    }

    /// <summary>
    /// What one batch decided, held apart from the import's running totals until it commits.
    ///
    /// A batch the database rejects is retried a finding at a time, and the retry decides the same
    /// findings again. Counting into the totals as it went would count them twice.
    /// </summary>
    private sealed class BatchOutcome
    {
        public int New;
        public int Updated;
        public int Duplicates;
        public int Skipped;

        public readonly Dictionary<NormalizedSeverity, int> NewBySeverity = new();
        public readonly List<string> Warnings = [];
        public readonly HashSet<int> Seen = [];

        /// <summary>Findings this batch resolved, by every key that names them.</summary>
        private readonly List<(DedupKeyResult Keys, Vulnerability Finding)> _resolved = [];

        public void Remember(DedupKeyResult keys, Vulnerability finding) =>
            _resolved.Add((keys, finding));

        public void Skip(NormalizedFinding finding, Exception ex, ILogger logger, int importId)
        {
            Skipped++;
            Warnings.Add($"[skipped] {finding.Title}: {ex.Message}");
            logger.Warning("Could not ingest finding {Title} from import {Import}: {Message}",
                finding.Title, importId, ex.Message);
        }

        /// <summary>Forgets everything but the skips, for a batch whose write did not land.</summary>
        public void Undo()
        {
            New = Updated = Duplicates = 0;
            NewBySeverity.Clear();
            Seen.Clear();
            _resolved.Clear();
        }

        public void MergeInto(IngestCounts counts, Dictionary<NormalizedSeverity, int> newBySeverity,
            List<string> warnings, HashSet<int> seenFindingIds, Dictionary<string, int> knownIdsByKey)
        {
            counts.New += New;
            counts.Updated += Updated;
            counts.Duplicates += Duplicates;
            counts.Skipped += Skipped;

            foreach (var (severity, count) in NewBySeverity)
                newBySeverity[severity] = newBySeverity.GetValueOrDefault(severity) + count;

            warnings.AddRange(Warnings);
            seenFindingIds.UnionWith(Seen);

            // Merged only now, and only for rows that actually have an id: a key pointing at a
            // finding the retry is about to create again would send the next batch to a row that
            // does not exist.
            foreach (var (keys, finding) in _resolved)
            {
                if (finding.Id == 0) continue;

                foreach (var candidate in keys.Candidates) knownIdsByKey[candidate.Key] = finding.Id;
            }
        }
    }

    private class IngestCounts
    {
        public int New;
        public int Updated;
        public int Duplicates;
        public int Closed;
        public int Skipped;
    }

    private enum ExistingOutcome
    {
        Updated,
        Suppressed
    }
}

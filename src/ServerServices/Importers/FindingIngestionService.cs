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
    ISlaService slaService)
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

    public async Task<ScanImport> IngestAsync(ImportResult parsed, ImportIngestionRequest request,
        CancellationToken ct = default)
    {
        var configuration = await dedupService.GetConfigurationAsync(request.Importer);

        await using var db = DalService.GetContext();

        var import = await ResolveImportRowAsync(db, request);

        var counts = new IngestCounts();
        var newBySeverity = new Dictionary<NormalizedSeverity, int>();
        var warnings = parsed.Warnings.Select(w => w.ToString()).ToList();

        // Every finding this import matched or created, so the auto-close pass can tell "still
        // present" from "gone" without re-deriving keys.
        var seenFindingIds = new HashSet<int>();

        // Cached per import: a 5000-finding Nessus report touches a handful of hosts, and resolving
        // each one from the database per finding is what made the previous importer slow.
        var hostCache = new Dictionary<string, Host>(StringComparer.OrdinalIgnoreCase);
        var serviceCache = new Dictionary<string, DAL.Entities.HostsService>(StringComparer.OrdinalIgnoreCase);

        foreach (var finding in parsed.Findings)
        {
            ct.ThrowIfCancellationRequested();

            try
            {
                var host = await ResolveHostAsync(db, finding, request, hostCache);
                var service = host == null
                    ? null
                    : await ResolveServiceAsync(db, host, finding, serviceCache);

                var context = new DedupContext
                {
                    Finding = finding,
                    HostId = host?.Id,
                    HostServiceId = service?.Id,
                    EntityId = request.EntityId
                };

                var keys = await dedupService.ComputeKeyAsync(context, configuration);
                var existing = await FindExistingAsync(db, keys, request.EntityId);

                if (existing != null)
                {
                    var outcome = UpdateExisting(db, existing, finding, request, keys, import.Id);
                    seenFindingIds.Add(existing.Id);

                    if (outcome == ExistingOutcome.Suppressed) counts.Duplicates++;
                    else counts.Updated++;
                }
                else
                {
                    var created = await CreateFindingAsync(db, finding, host, service, request, keys, import.Id);
                    seenFindingIds.Add(created.Id);

                    counts.New++;
                    newBySeverity[finding.Severity] = newBySeverity.GetValueOrDefault(finding.Severity) + 1;
                }
            }
            catch (Exception ex)
            {
                // One malformed finding must not lose the other 4999. The failure is recorded as a
                // skip so the summary's counts still add up to what was in the file.
                counts.Skipped++;
                warnings.Add($"[skipped] {finding.Title}: {ex.Message}");
                Logger.Warning("Could not ingest finding {Title} from import {Import}: {Message}",
                    finding.Title, import.Id, ex.Message);
            }
        }

        // Auto-close is opt-in per scanner and only ever runs for a report the importer itself
        // declared exhaustive. A partial scan treated as full closes every finding outside its
        // slice, which is far worse than a stale open one.
        if (configuration.AutoCloseMissing && parsed.IsFullScan)
            counts.Closed = await AutoCloseMissingAsync(db, request, seenFindingIds, import.Id);
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

        await db.SaveChangesAsync();

        Logger.Information(
            "Import {Import} ({Importer}) finished: {New} new, {Updated} updated, {Duplicate} suppressed, {Closed} closed, {Skipped} skipped",
            import.Id, import.Importer, counts.New, counts.Updated, counts.Duplicates, counts.Closed,
            import.SkippedCount);

        return import;
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
    /// Finds or creates the asset a finding sits on. Returns null for findings that have none —
    /// code and dependency scanners report a file path, not a host, and inventing an asset for them
    /// would fill the inventory with fictional machines.
    /// </summary>
    private async Task<Host?> ResolveHostAsync(NRDbContext db, NormalizedFinding finding,
        ImportIngestionRequest request, Dictionary<string, Host> cache)
    {
        var normalized = finding.Host;
        if (normalized == null || normalized.IsEmpty) return null;

        var key = normalized.Ip ?? normalized.Fqdn ?? normalized.HostName!;

        if (cache.TryGetValue(key, out var cached)) return cached;

        var existing = normalized.Ip != null
            ? await db.Hosts.FirstOrDefaultAsync(h => h.Ip == normalized.Ip)
            : await db.Hosts.FirstOrDefaultAsync(h => h.HostName == key || h.Fqdn == key);

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

            cache[key] = existing;
            return existing;
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
        await db.SaveChangesAsync();

        cache[key] = host;
        return host;
    }

    private async Task<DAL.Entities.HostsService?> ResolveServiceAsync(NRDbContext db, Host host,
        NormalizedFinding finding, Dictionary<string, DAL.Entities.HostsService> cache)
    {
        var normalized = finding.Host;
        if (normalized == null) return null;
        if (string.IsNullOrWhiteSpace(normalized.ServiceName) && string.IsNullOrWhiteSpace(normalized.Port))
            return null;

        var name = normalized.ServiceName ?? "unknown";
        var protocol = normalized.Protocol ?? "tcp";
        int? port = int.TryParse(normalized.Port, out var parsedPort) ? parsedPort : null;

        var key = $"{host.Id}|{name}|{port}|{protocol}";
        if (cache.TryGetValue(key, out var cached)) return cached;

        var existing = await db.HostsServices
            .FirstOrDefaultAsync(s => s.HostId == host.Id && s.Name == name && s.Port == port
                                      && s.Protocol == protocol);

        if (existing != null)
        {
            cache[key] = existing;
            return existing;
        }

        var service = new DAL.Entities.HostsService
        {
            HostId = host.Id,
            Name = name,
            Port = port,
            Protocol = protocol
        };

        db.HostsServices.Add(service);
        await db.SaveChangesAsync();

        cache[key] = service;
        return service;
    }

    /// <summary>
    /// Looks for a finding matching any of the chain's candidate keys.
    ///
    /// Every candidate is tried, not just the primary one, because a finding imported before a
    /// configuration change was keyed by whatever strategy led the chain then; matching only the
    /// current primary key would duplicate the whole register on the next scan. The legacy
    /// candidate is additionally compared against <c>import_hash</c>, which is where the
    /// pre-Track-3 code stored it.
    /// </summary>
    private static async Task<Vulnerability?> FindExistingAsync(NRDbContext db, DedupKeyResult keys, int? entityId)
    {
        if (!keys.HasKey) return null;

        foreach (var candidate in keys.Candidates)
        {
            var match = await db.Vulnerabilities
                .FirstOrDefaultAsync(v => v.DedupKey == candidate.Key
                                          && (entityId == null || v.EntityId == entityId));
            if (match != null) return match;

            if (!candidate.MatchesLegacyImportHash) continue;

            match = await db.Vulnerabilities
                .FirstOrDefaultAsync(v => v.ImportHash == candidate.Key
                                          && (entityId == null || v.EntityId == entityId));
            if (match != null) return match;
        }

        return null;
    }

    /// <summary>
    /// Records a fresh sighting of a known finding, honouring the sticky-triage rules.
    ///
    /// Nothing here reopens a suppressed finding, and a mitigated one that comes back is reopened as
    /// a regression with a history event — those two behaviours are the whole point of the
    /// lifecycle.
    /// </summary>
    private ExistingOutcome UpdateExisting(NRDbContext db, Vulnerability existing, NormalizedFinding finding,
        ImportIngestionRequest request, DedupKeyResult keys, int importId)
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
                VulnerabilityId = existing.Id,
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
            var recomputed = RecomputeDueDate(db, existing, finding, request);

            db.FindingStatusHistories.Add(new FindingStatusHistory
            {
                VulnerabilityId = existing.Id,
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

    private async Task<Vulnerability> CreateFindingAsync(NRDbContext db, NormalizedFinding finding, Host? host,
        DAL.Entities.HostsService? service, ImportIngestionRequest request, DedupKeyResult keys, int importId)
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
            HostId = host?.Id,
            HostServiceId = service?.Id,
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

        vulnerability.SlaDueDate = await slaService.ComputeDueDateAsync(finding.Severity, request.EntityId, firstSeen);

        db.Vulnerabilities.Add(vulnerability);
        await db.SaveChangesAsync();

        db.FindingStatusHistories.Add(new FindingStatusHistory
        {
            VulnerabilityId = vulnerability.Id,
            FromStatus = null,
            ToStatus = FindingStatus.Active,
            UserId = request.UserId,
            Source = FindingStatusChangeSource.Import,
            ChangedAt = request.ImportedAt,
            Justification = $"Imported from {request.Importer}" +
                            (request.FileName == null ? "." : $" ({request.FileName}).")
        });

        return vulnerability;
    }

    /// <summary>
    /// Closes open findings this scanner previously reported for this scope but did not report now
    /// (3.3.2). Only reached when the scanner is configured for it and the report is a full scan.
    /// </summary>
    private async Task<int> AutoCloseMissingAsync(NRDbContext db, ImportIngestionRequest request,
        HashSet<int> seenFindingIds, int importId)
    {
        var stale = await db.Vulnerabilities
            .Where(v => v.ImportSource == request.Importer
                        && (v.LifecycleStatus == FindingStatus.Active || v.LifecycleStatus == FindingStatus.Verified)
                        && !seenFindingIds.Contains(v.Id)
                        && (request.EntityId == null || v.EntityId == request.EntityId))
            .ToListAsync();

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
    private DateTime? RecomputeDueDate(NRDbContext db, Vulnerability existing, NormalizedFinding finding,
        ImportIngestionRequest request)
    {
        var policy = db.SlaConfigurations
            .AsNoTracking()
            .Where(c => c.Severity == (int)finding.Severity
                        && c.EffectiveFrom <= existing.FirstDetection
                        && (c.EffectiveTo == null || c.EffectiveTo > existing.FirstDetection)
                        && (c.EntityId == null || c.EntityId == request.EntityId))
            .OrderByDescending(c => c.EntityId != null)
            .ThenByDescending(c => c.EffectiveFrom)
            .ThenByDescending(c => c.Id)
            .FirstOrDefault();

        existing.SlaDueDate = policy == null
            ? null
            : existing.FirstDetection.AddDays(policy.MaxRemediationDays);

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

using Contracts.Importers;
using DAL.Entities;

namespace ServerServices.Interfaces;

/// <summary>
/// Persists parsed findings: host/service resolution, deduplication, sticky triage, SLA due dates,
/// and the scan-import log (Track 3 milestones 3.1–3.4).
///
/// This is the only writer of findings on the import path. Importers parse and return records; this
/// service decides what each one means for the register. Keeping the two apart is what makes a
/// third-party importer safe to load — it cannot touch the database — and what lets dedup and SLA
/// behaviour change without touching ten importers.
/// </summary>
public interface IFindingIngestionService
{
    /// <summary>
    /// Ingests one parse result. Creates, updates or suppresses each finding per the importer's
    /// dedup configuration, writes the <c>scan_imports</c> row, and returns its counts.
    /// </summary>
    Task<ScanImport> IngestAsync(ImportResult parsed, ImportIngestionRequest request,
        CancellationToken ct = default);

    /// <summary>The log row for an import, for <c>GET /vulnerabilities/import-jobs/{id}</c>.</summary>
    Task<ScanImport> GetImportAsync(int importId);

    /// <summary>
    /// The import a caller's <c>Idempotency-Key</c> already produced, or null. Lets a retried CI
    /// upload return the original result rather than importing again.
    /// </summary>
    Task<ScanImport?> FindByIdempotencyKeyAsync(string idempotencyKey);

    /// <summary>Recent imports, newest first — the import history view.</summary>
    Task<List<ScanImport>> GetRecentImportsAsync(int take = 50);

    /// <summary>
    /// Reserves an import row before parsing starts, so a caller polling by id sees
    /// <c>Queued</c>/<c>Running</c> rather than a 404 while a large file is still being read. Also
    /// where the idempotency key is claimed, under the unique index.
    /// </summary>
    Task<ImportReservation> BeginImportAsync(ImportIngestionRequest request);

    /// <summary>Marks a reserved import as failed, recording why.</summary>
    Task FailImportAsync(int importId, string errorMessage);
}

/// <summary>
/// The outcome of reserving an import row.
/// </summary>
/// <param name="Import">The row to fill in, or the earlier row this key already produced.</param>
/// <param name="IsReplay">
/// True when the caller's <c>Idempotency-Key</c> had already been used and <paramref name="Import"/>
/// is that original import. Reported explicitly rather than inferred from ids, because "did this
/// actually start a new import" is the one thing the caller must not guess wrong.
/// </param>
public record ImportReservation(ScanImport Import, bool IsReplay);

/// <summary>Everything the ingestion pipeline needs to know about one import beyond the findings.</summary>
public class ImportIngestionRequest
{
    /// <summary>The importer's name, as it appears in <c>scan_imports.importer</c>.</summary>
    public required string Importer { get; init; }

    public string? FileName { get; init; }

    public string? FileId { get; init; }

    public int? UserId { get; init; }

    /// <summary>The tenant new findings are attributed to (Track 2.3).</summary>
    public int? EntityId { get; init; }

    /// <summary>The <c>jobs</c> row driving this, when the import runs as a background job.</summary>
    public int? JobId { get; init; }

    public string? IdempotencyKey { get; init; }

    /// <summary>
    /// A reserved <c>scan_imports</c> row to complete rather than insert. Set when
    /// <see cref="IFindingIngestionService.BeginImportAsync"/> ran first.
    /// </summary>
    public int? ExistingImportId { get; set; }

    /// <summary>
    /// The import time, in UTC. Passed in so every finding from one import shares a timestamp and
    /// so tests are deterministic.
    /// </summary>
    public DateTime ImportedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// Team assigned to new findings, matching the register's existing default. Kept configurable
    /// because a CI pipeline importing into a specific team is a reasonable thing to want.
    /// </summary>
    public int? FixTeamId { get; init; }
}

namespace DAL.Entities;

/// <summary>
/// The log of one scanner import (Track 3 milestone 3.3.2). Every import is reconstructible from
/// this: which importer ran, on what file, for whom, and what it did to the register.
///
/// This is also what <c>GET /vulnerabilities/import-jobs/{id}</c> reads, so an import's outcome
/// survives a server restart instead of living only in the in-memory job runner.
/// </summary>
public class ScanImport
{
    public int Id { get; set; }

    /// <summary>The importer's <c>Name</c> — "nessus", "trivy", a plugin's identifier.</summary>
    public string Importer { get; set; } = null!;

    public string? FileName { get; set; }

    /// <summary>The uploaded file's id, when the import came from the file-upload path.</summary>
    public string? FileId { get; set; }

    public int? UserId { get; set; }

    public int? EntityId { get; set; }

    /// <summary>The <c>jobs</c> row driving this import, so progress and cancellation stay unified
    /// with every other long-running operation.</summary>
    public int? JobId { get; set; }

    public DateTime StartedAt { get; set; }

    public DateTime? FinishedAt { get; set; }

    /// <summary>Queued / Running / Succeeded / Failed — see <c>Model.Findings.ScanImportStatus</c>.</summary>
    public int Status { get; set; }

    public int NewCount { get; set; }

    public int UpdatedCount { get; set; }

    public int DuplicateCount { get; set; }

    public int ClosedCount { get; set; }

    public int SkippedCount { get; set; }

    public int WarningCount { get; set; }

    /// <summary>
    /// Findings by severity, as JSON. Denormalized deliberately: CI gating asks "how many new
    /// criticals did this import add", and answering it from the findings table means a query
    /// against a moving target — a finding's severity can change after the import that created it.
    /// </summary>
    public string? NewBySeverity { get; set; }

    /// <summary>
    /// The importer's warning list, newline-delimited. Stored so the per-import summary the GUI
    /// offers for download is still available after the job's memory is gone.
    /// </summary>
    public string? Warnings { get; set; }

    public string? ErrorMessage { get; set; }

    /// <summary>
    /// The caller's <c>Idempotency-Key</c> header, when it sent one. A repeated key returns this
    /// row instead of importing again, which is what makes a CI retry storm harmless.
    /// </summary>
    public string? IdempotencyKey { get; set; }

    public virtual User? User { get; set; }

    public virtual Entity? Entity { get; set; }
}

namespace Model.Findings;

/// <summary>
/// What the import endpoints return: the job to watch and the import row to read.
///
/// A client-side mirror of the API's response rather than a shared type, because the API's version
/// also carries the completed <c>ScanImport</c> for a synchronous call and the desktop client always
/// polls.
/// </summary>
public class ImportJobStatus
{
    /// <summary>The background job, for progress. Zero when nothing was started (a replayed key).</summary>
    public int JobId { get; set; }

    /// <summary>The <c>scan_imports</c> row id — what the status endpoint takes.</summary>
    public int ImportId { get; set; }

    /// <summary>True when an idempotency key had already been used and this is the original import.</summary>
    public bool IsReplay { get; set; }

    public bool Success { get; set; }

    public string? Message { get; set; }
}

/// <summary>
/// SLA compliance for one severity band, as the dashboard widget consumes it. Mirrors the API's
/// <c>SlaComplianceBucket</c>.
/// </summary>
public class SlaComplianceView
{
    public int Severity { get; set; }

    public int Total { get; set; }

    public int WithinSla { get; set; }

    public int Breached { get; set; }

    /// <summary>Null when the band holds no findings — an empty band has no compliance figure, and
    /// showing 100% for it reads as a result rather than an absence of data.</summary>
    public double? CompliancePercent { get; set; }
}

namespace Model.Reports;

public class ReportParameters
{
    /// <summary>
    /// Report.Type value used for reports rendered from a user-defined report template
    /// (as opposed to the built-in hardcoded report types 0 and 1).
    /// </summary>
    public const int TemplateReportType = 2;

    public int ReportType { get; set; } = -1;

    /// <summary>
    /// When <see cref="ReportType"/> is <see cref="TemplateReportType"/>, identifies the
    /// <c>ReportTemplate</c> the report should be rendered from (latest version is used).
    /// </summary>
    public int? TemplateId { get; set; }

    /// <summary>
    /// Report.Type value for the auditor governance evidence pack (Track 8 milestone 8.4.2, with the
    /// campaign evidence of 8.6.5). Rendered by <c>GovernanceEvidencePdfReport</c> from the same pack
    /// the CSV export uses, so the two cannot describe different evidence.
    /// </summary>
    public const int GovernanceEvidenceReportType = 3;

    /// <summary>
    /// The business entity the evidence pack covers. Null means the whole register, which is an
    /// admin-only disclosure — <c>ReportsController</c> enforces that, not this DTO.
    /// </summary>
    public int? EntityId { get; set; }

    /// <summary>Start of the evidence period, UTC. Defaults to a year back when omitted.</summary>
    public DateTime? PeriodStart { get; set; }

    /// <summary>End of the evidence period, UTC. Defaults to now when omitted.</summary>
    public DateTime? PeriodEnd { get; set; }
}

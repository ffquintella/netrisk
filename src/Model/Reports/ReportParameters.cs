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
}

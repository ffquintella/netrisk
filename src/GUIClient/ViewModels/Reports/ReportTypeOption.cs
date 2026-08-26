namespace GUIClient.ViewModels.Reports;

/// <summary>
/// One selectable entry in the Create Report dialog. Represents either a built-in
/// report (identified by <see cref="ReportType"/> 0/1) or a template-based report
/// (<see cref="ReportType"/> = <see cref="Model.Reports.ReportParameters.TemplateReportType"/>
/// with <see cref="TemplateId"/> set).
/// </summary>
public class ReportTypeOption
{
    public string Name { get; init; } = "";

    public int ReportType { get; init; }

    public int? TemplateId { get; init; }

    /// <summary>
    /// True for the governance evidence pack, which is the only report that needs an entity and a
    /// period chosen before it can be produced. The dialog reveals those fields on this flag rather
    /// than on the report type number, so a future scoped report does not need the dialog changed.
    /// </summary>
    public bool NeedsEvidenceScope { get; init; }

    public override string ToString() => Name;
}

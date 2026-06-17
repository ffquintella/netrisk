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

    public override string ToString() => Name;
}

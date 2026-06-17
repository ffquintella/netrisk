namespace GUIClient.ViewModels.Dialogs.Results;

public class ReportDialogResult:DialogResultBase
{
    public int ReportType { get; set; }

    /// <summary>
    /// Set when the chosen report is a template-based report; identifies the report template.
    /// </summary>
    public int? TemplateId { get; set; }

    /// <summary>
    /// Display name of the chosen report (built-in label or template name).
    /// </summary>
    public string? ReportName { get; set; }
}

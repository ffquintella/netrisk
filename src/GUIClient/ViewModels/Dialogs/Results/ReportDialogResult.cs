using System;

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

    /// <summary>
    /// Set when the chosen report is the governance evidence pack (Track 8 milestone 8.4.2):
    /// the business entity it covers, or null for the whole register.
    /// </summary>
    public int? EntityId { get; set; }

    /// <summary>Start of the evidence period, UTC. Null lets the server default to a year back.</summary>
    public DateTime? PeriodStart { get; set; }

    /// <summary>End of the evidence period, UTC. Null lets the server default to now.</summary>
    public DateTime? PeriodEnd { get; set; }
}

namespace WebSite.Models;

public class IRTEReportViewModel
{
    /// <summary>
    /// The e-mailed report key (NR-2026-037). It is the only thing the report form posts back that the
    /// server acts on: the execution is re-derived from it, so there is deliberately no task-execution
    /// id on this model for a visitor to forge.
    /// </summary>
    public string Key { get; set; } = string.Empty;
    public string IncidentName { get; set; } = string.Empty;
    public string IncidentDescription { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public string TaskDescription { get; set; } = string.Empty;
    public string TaskNotes { get; set; } = string.Empty;
    public string TaskConditionToProceed { get; set; } = string.Empty;
    public string TaskConditionToSkip { get; set; } = string.Empty;
    public string TaskCriteriaToSucceed { get; set; } = string.Empty;
    public string TaskCriteriaToFail { get; set; } = string.Empty;
    public string TaskCriteriaToComplete { get; set; } = string.Empty;
    public string TaskVerificationCriteria  { get; set; } = string.Empty;

    public string Result { get; set; } = string.Empty;

}

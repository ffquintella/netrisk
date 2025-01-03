namespace WebSite.Models;

public class IRTEReportViewModel
{
    public int TaskExecutionId { get; set; } = -1;
    public string IncidentName { get; set; } = string.Empty;
    public string IncidentDescription { get; set; } = string.Empty;
    public string TaskName { get; set; } = string.Empty;
    public string TaskDescription { get; set; } = string.Empty;
    public string TaskNotes { get; set; } = string.Empty;
    public int TaskId { get; set; } = -1;
    public string TaskConditionToProceed { get; set; } = string.Empty;
    public string TaskConditionToSkip { get; set; } = string.Empty;
    public string TaskCriteriaToSucceed { get; set; } = string.Empty;
    public string TaskCriteriaToFail { get; set; } = string.Empty;
    public string TaskCriteriaToComplete { get; set; } = string.Empty;
    public string TaskVerificationCriteria  { get; set; } = string.Empty;
    
    public string Result { get; set; } = string.Empty;
    
}
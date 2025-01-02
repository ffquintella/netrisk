using Microsoft.AspNetCore.Mvc.RazorPages;

namespace ServerServices.EmailTemplates;

public class TaskExecution : PageModel
{
    
    public string IncidentName { get; set; } = String.Empty;
    public string IncidentDescription { get; set; } = String.Empty;
    
    public string TaskName { get; set; } = String.Empty;
    
    public string TaskSuccessCriteria { get; set; } = String.Empty;

    public string TaskFailureCriteria { get; set; } = String.Empty;

    public string TaskCompletionCriteria { get; set; } = String.Empty;

    public string TaskVerificationCriteria { get; set; } = String.Empty;

    public string TaskConditionToProceed { get; set; } = String.Empty;

    public string TaskConditionToSkip { get; set; } = String.Empty;
    
    public string ReportLink { get; set; } = String.Empty;
    
    public void OnGet()
    {
        
    }
}
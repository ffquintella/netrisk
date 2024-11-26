using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DAL.Entities;

public partial class IncidentResponsePlanExecution
{
    public int Id { get; set; }
    
    [ValidateNever]
    public IncidentResponsePlan? Plan { get; set; } = null!;
    
    public int PlanId { get; set; }
    
    public DateTime ExecutionDate { get; set; }
    
    public TimeSpan Duration { get; set; }
    
    public Entity? ExecutedBy { get; set; }
    
    public int? ExecutedById { get; set; }
    
    
    public User? CreatedBy { get; set; } = null!;
    
    public int CreatedById { get; set; }
    
    public string? Notes { get; set; }
    
    public int Status { get; set; }
    
    public bool IsTest { get; set; } = false;
    
    public bool IsExercise { get; set; } = false;
    
    public virtual ICollection<NrFile> Attachments { get; set; } = new List<NrFile>();
    
    public string ExecutionTrigger { get; set; } = null!;
    
    public string ExecutionResult { get; set; } = null!;
    
    public virtual ICollection<IncidentResponsePlanTaskExecution> TasksExecuted { get; set; } = new List<IncidentResponsePlanTaskExecution>();
    
}
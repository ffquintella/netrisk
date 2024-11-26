using Microsoft.AspNetCore.Mvc.ModelBinding.Validation;

namespace DAL.Entities;

public partial class IncidentResponsePlanTaskExecution
{
    public int Id { get; set; }
    
    public int PlanExecutionId { get; set; }
    
    [ValidateNever]
    public IncidentResponsePlanExecution? PlanExecution { get; set; } = null;
    
    public IncidentResponsePlanTask? Task { get; set; } = null;
    
    public int TaskId { get; set; }
    
    public DateTime ExecutionDate { get; set; }
    
    public TimeSpan Duration { get; set; }
    
    public Entity? ExecutedBy { get; set; }
    public int? ExecutedById { get; set; }
    
    public User? CreatedBy { get; set; } = null!;
    public int? CreatedById { get; set; }
    
    public User? LastUpdatedBy { get; set; } = null!;
    public int? LastUpdatedById { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    
    public DateTime? LastUpdatedAt { get; set; } = DateTime.Now;
    
    public string? Notes { get; set; }
    
    public int Status { get; set; }
    
    public bool? IsTest { get; set; } = false;
    
    public bool? IsExercise { get; set; } = false;
    
    public ICollection<NrFile> Attachments { get; set; } = new List<NrFile>();
    
}
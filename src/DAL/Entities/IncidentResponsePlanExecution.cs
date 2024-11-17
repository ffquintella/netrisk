namespace DAL.Entities;

public partial class IncidentResponsePlanExecution
{
    public int Id { get; set; }
    
    public IncidentResponsePlan Plan { get; set; } = null!;
    
    public int PlanId { get; set; }
    
    public DateTime ExecutionDate { get; set; }
    
    public TimeSpan Duration { get; set; }
    
    public Entity? ExecutedBy { get; set; }
    
    public int? ExecutedById { get; set; }
    
    public string? Notes { get; set; }
    
    public int Status { get; set; }
    
    public bool IsTest { get; set; } = false;
    
    public bool IsExercise { get; set; } = false;
    
    public virtual ICollection<NrFile> Attachments { get; set; } = new List<NrFile>();
    
    public string ExecutionTrigger { get; set; } = null!;
    
    public string ExecutionResult { get; set; } = null!;
    
    public IncidentResponsePlanTask? Task { get; set; } = null!;
    public int? TaskId { get; set; }
    
}
namespace DAL.Entities;

public abstract class IncidentResponsePlanTaskExecutions
{
    int Id { get; set; }
    
    public int PlanId { get; set; }
    
    public IncidentResponsePlan? Plan { get; set; } = null!;
    
    public IncidentResponsePlanTask? Task { get; set; } = null!;
    
    public int TaskId { get; set; }
    
    public DateTime ExecutionDate { get; set; }
    
    public TimeSpan Duration { get; set; }
    
    public Entity? ExecutedBy { get; set; }
    
    public string? Notes { get; set; }
    
    public int Status { get; set; }
    
    public bool? IsTest { get; set; } = false;
    
    public bool? IsExercise { get; set; } = false;
    
    public ICollection<NrFile> Attachments { get; set; } = new List<NrFile>();
    
}
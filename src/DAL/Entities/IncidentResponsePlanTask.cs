namespace DAL.Entities;


// TODO: REVIEW Nullable and default values. 
public partial class IncidentResponsePlanTask
{
    public int Id { get; set; }
    
    public string? Description { get; set; } = null!;
    
    public DateTime CreationDate { get; set; } = DateTime.Now;
    
    public User? CreatedBy { get; set; } = null!;
    
    public int? CreatedById { get; set; }
    
    public DateTime? LastUpdate { get; set; }
    
    public User? UpdatedBy { get; set; } = null!;
    public int? UpdatedById { get; set; }
    
    public int Status { get; set; } = 0;
    
    public string? Notes { get; set; }
    
    public bool? HasBeenTested { get; set; } = false;
    
    public IncidentResponsePlan? Plan { get; set; } = null!;
    
    public int PlanId { get; set; }

    public int ExecutionOrder { get; set; } = 1;
    
    public DateTime? LastTestDate { get; set; }
    
    public Entity? LastTestedBy { get; set; }
    
    public int? LastTestedById { get; set; }
    
    public TimeSpan? EstimatedDuration { get; set; }
    
    public TimeSpan? LastActualDuration { get; set; }
    
    public Entity? AssignedTo { get; set; }
    public int AssignedToId { get; set; }
    
    public int Priority { get; set; } = 0;
    
    public virtual ICollection<IncidentResponsePlanExecution> Executions { get; set; } = new List<IncidentResponsePlanExecution>();
    
    public bool? IsParallel { get; set; } = false;
    
    public bool? IsSequential { get; set; } = false;
    
    public bool? IsOptional { get; set; } = false;
    
    public string? SuccessCriteria { get; set; } = null!;
    
    public string? FailureCriteria { get; set; }
    
    public string? CompletionCriteria { get; set; }
    
    public string? VerificationCriteria { get; set; }
    
    public string? TaskType { get; set; } = null!;
    
    public string? ConditionToProceed { get; set; } = null!;
    
    public string? ConditionToSkip { get; set; }
    
    public ICollection<NrFile> Attachments { get; set; } = new List<NrFile>();
    
}
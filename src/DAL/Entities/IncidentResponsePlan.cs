namespace DAL.Entities;

public partial class IncidentResponsePlan
{
    public int Id { get; set; }

    public virtual ICollection<Risk> RelatedRisks { get; set; } = new List<Risk>();

    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    
    public DateTime CreationDate { get; set; }
    
    public User CreatedBy { get; set; } = null!;
    
    public int CreatedById { get; set; } 
    
    public DateTime LastUpdate { get; set; }
    
    public User? UpdatedBy { get; set; } = null!;
    
    public int? UpdatedById { get; set; }
    
    public int Status { get; set; } = 0;
    
    public string? Notes { get; set; }
    
    public bool HasBeenTested { get; set; } = false;
    
    public bool HasBeenUpdated { get; set; } = false;
    
    public bool HasBeenExercised { get; set; } = false;
    
    public bool HasBeenReviewed { get; set; } = false;
    
    public bool HasBeenApproved { get; set; } = false;
    
    public DateTime? LastTestDate { get; set; }
   
    public DateTime? LastExerciseDate { get; set; }
    
    public DateTime? LastReviewDate { get; set; }
    
    public DateTime? ApprovalDate { get; set; }
    
    public Entity? LastTestedBy { get; set; }
    
    public int LastTestedById { get; set; }
    
    public Entity? LastExercisedBy { get; set; }
    
    public int LastExercisedById { get; set; }
    
    public Entity? LastReviewedBy { get; set; }
    
    public int LastReviewedById { get; set; }
    
    public virtual ICollection<IncidentResponsePlanTask> Tasks { get; set; } = new List<IncidentResponsePlanTask>();
    
    public virtual ICollection<IncidentResponsePlanExecution> Executions { get; set; } = new List<IncidentResponsePlanExecution>();
    
    public virtual ICollection<NrFile> Attachments { get; set; } = new List<NrFile>();
    
}
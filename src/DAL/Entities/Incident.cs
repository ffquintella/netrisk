using System.Runtime.InteropServices.JavaScript;

namespace DAL.Entities;

public class Incident
{
    public int Id  { get; set; }
    public int Year { get; set; } = DateTime.Now.Year;
    public int Sequence { get; set; } = 1;
    public string Name { get; set; } = $"{DateTime.Now.Year}-";
    public string Description { get; set; } = null!;
    public DateTime CreationDate { get; set; } = DateTime.Now;
    public User? CreatedBy { get; set; } = null!;
    public int CreatedById { get; set; }
    public DateTime LastUpdate { get; set; } = DateTime.Now;
    public User? UpdatedBy { get; set; } = null!;
    public int? UpdatedById { get; set; }
    public int Status { get; set; } = 0;
    public string? Report { get; set; }
    public bool? ReportedByEntity { get; set; }
    public Entity? ReportEntity { get; set; }
    public int? ReportEntityId { get; set; }
    public string? ReportedBy { get; set; }
    public Entity? ImpactedEntity { get; set; }
    public int? ImpactedEntityId { get; set; }
    public string Category { get; set; } = "not_specified";    
    public DateTime ReportDate { get; set; } = DateTime.Now;
    public User? AssignedTo { get; set; }
    public int? AssignedToId { get; set; }
    public string? Notes { get; set; }
    public string? Impact { get; set; } 
    public string? Cause { get; set; }
    public string? Solution { get; set; }
    public string? Recommendations { get; set; }
    public TimeSpan? Duration { get; set; }
    public DateTime? StartDate { get; set; } 

    
    public virtual ICollection<NrFile> Attachments { get; set; } = new List<NrFile>();
    
    public virtual ICollection<NrAction> Actions { get; set; } = new List<NrAction>();
    
    public virtual ICollection<IncidentResponsePlan> IncidentResponsePlansActivated { get; set; } = new List<IncidentResponsePlan>();
    
}
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
    public string? Notes { get; set; }
    public string? Impact { get; set; } 
    public string? Cause { get; set; }
    public string? Resolution { get; set; }
    public TimeSpan? Duration { get; set; }
    public DateTime? StartDate { get; set; } 

    
    public virtual ICollection<NrFile> Attachments { get; set; } = new List<NrFile>();
    
    public virtual ICollection<NrAction> Actions { get; set; } = new List<NrAction>();
    
    public virtual ICollection<IncidentResponsePlan> IncidentResponsePlansActivated { get; set; } = new List<IncidentResponsePlan>();
    
}
using System.Runtime.InteropServices.JavaScript;

namespace DAL.Entities;

public class Incident : DAL.Interfaces.IEntityScoped
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
    // Track 6 Phase 3: optional FK to a system user; the free-text ReportedBy is kept for external reporters.
    public int? ReportedById { get; set; }
    public User? ReportedByUser { get; set; }
    public Entity? ImpactedEntity { get; set; }
    public int? ImpactedEntityId { get; set; }

    public int? EntityId { get; set; }
    public virtual Entity? Entity { get; set; }

    public string Category { get; set; } = "not_specified";

    /// <summary>
    /// Incident or near miss (Stage 9.2, S42 §4.3) — orthogonal to <see cref="Category"/>, which is the
    /// threat type. Defaults to <c>Incident</c>, so a client that never sends it keeps today's meaning.
    /// </summary>
    public DAL.Enums.IncidentKind Kind { get; set; } = DAL.Enums.IncidentKind.Incident;

    /// <summary>
    /// How severe the incident is (M58, T279; S39 D-15) — <c>incidents.severity</c>. Null when nobody assessed it: the
    /// risk index then falls back to the category anchor, which is what "unknown" has always meant.
    /// </summary>
    public DAL.Enums.IncidentSeverity? Severity { get; set; }

    /// <summary>
    /// UTC: when the incident last moved from an open status into a closed one (<c>Model.Status.ClosedStatuses</c>) —
    /// <c>incidents.resolved_at</c> (M58, T279; S39 §9.2, D-15). The server owns it: <c>IncidentsService</c> stamps it on
    /// that transition, keeps it while the incident stays closed, clears it on reopening, and ignores whatever a client
    /// sends. Null for an incident closed before the column existed, whose closure time nobody recorded.
    /// </summary>
    public DateTime? ResolvedAt { get; set; }

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
using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class NrFile
{
    public int Id { get; set; }

    public int? RiskId { get; set; }

    public int? ViewType { get; set; }

    public string Name { get; set; } = null!;

    public string UniqueName { get; set; } = null!;

    public string? Type { get; set; }

    public int Size { get; set; }

    public DateTime Timestamp { get; set; }

    public int User { get; set; }

    public byte[] Content { get; set; } = null!;

    public int? MitigationId { get; set; }
    
    public IncidentResponsePlan? IncidentResponsePlan { get; set; }
    public int? IncidentResponsePlanId { get; set; }
    
    public IncidentResponsePlanExecution? IncidentResponsePlanExecution { get; set; }
    
    public int? IncidentResponsePlanExecutionId { get; set; }
    
    public IncidentResponsePlanTask? IncidentResponsePlanTask { get; set; }
    
    public int? IncidentResponsePlanTaskId { get; set; }
    
    public IncidentResponsePlanTaskExecution? IncidentResponsePlanTaskExecution { get; set; }
    
    public int? IncidentResponsePlanTaskExecutionId { get; set; }
    
    public Incident? Incident { get; set; }
    
    public int? IncidentId { get; set; }

    /// <summary>
    /// Evidence attached to a formal risk acceptance (Track 3 milestone 3.2.3) — the approval
    /// email, the signed exception form, the compensating-control design. Follows the same
    /// one-nullable-FK-per-attachment-target pattern as the columns above.
    /// </summary>
    public RiskAcceptance? RiskAcceptance { get; set; }

    public int? RiskAcceptanceId { get; set; }

    /// <summary>
    /// The business entity the attachment belongs to (security finding NR-2026-017). Brings files
    /// under the Track 2.3 global query filter, which is what closes the cross-tenant read; the
    /// per-parent permission check in <c>FilesService</c> closes the rest.
    /// </summary>
    public int? EntityId { get; set; }

    public virtual Entity? Entity { get; set; }

    public virtual ICollection<Report> Reports { get; set; } = new List<Report>();
}

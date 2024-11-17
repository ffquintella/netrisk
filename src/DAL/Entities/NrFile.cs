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

    public virtual ICollection<Report> Reports { get; set; } = new List<Report>();
}

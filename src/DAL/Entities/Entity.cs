using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Entity
{
    public int Id { get; set; }

    public string DefinitionName { get; set; } = null!;

    public string DefinitionVersion { get; set; } = null!;

    public DateTime Created { get; set; }

    public DateTime Updated { get; set; }

    public int CreatedBy { get; set; }

    public int UpdatedBy { get; set; }

    public string Status { get; set; } = null!;

    public int? Parent { get; set; }

    public virtual ICollection<AssessmentRun> AssessmentRuns { get; set; } = new List<AssessmentRun>();

    public virtual ICollection<EntitiesProperty> EntitiesProperties { get; set; } = new List<EntitiesProperty>();

    public virtual ICollection<Entity> InverseParentNavigation { get; set; } = new List<Entity>();

    public virtual Entity? ParentNavigation { get; set; }

    public virtual ICollection<Vulnerability> Vulnerabilities { get; set; } = new List<Vulnerability>();

    public virtual ICollection<Risk> Risks { get; set; } = new List<Risk>();
    
    public virtual ICollection<IncidentResponsePlan> IncidentResponsePlansLastExercised { get; set; } = new List<IncidentResponsePlan>();
    
    public virtual ICollection<IncidentResponsePlan> IncidentResponsePlansLastTested { get; set; } = new List<IncidentResponsePlan>();
    
    public virtual ICollection<IncidentResponsePlanTask> IncidentResponsePlanTasksLastTested { get; set; } = new List<IncidentResponsePlanTask>();
    
    public virtual ICollection<IncidentResponsePlan> IncidentResponsePlansLastReviewed { get; set; } = new List<IncidentResponsePlan>();
    
    public virtual ICollection<IncidentResponsePlan> IncidentResponsePlansApproved { get; set; } = new List<IncidentResponsePlan>();
    
    public virtual ICollection<IncidentResponsePlanExecution> IncidentResponsePlanExecutions { get; set; } = new List<IncidentResponsePlanExecution>();
    
    public virtual ICollection<IncidentResponsePlanTask> IncidentResponsePlanTasks { get; set; } = new List<IncidentResponsePlanTask>();
    
    public virtual ICollection<IncidentResponsePlanTaskExecution> IncidentResponsePlanTaskExecutions { get; set; } = new List<IncidentResponsePlanTaskExecution>();
}

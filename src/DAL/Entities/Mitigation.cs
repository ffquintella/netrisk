using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Mitigation
{
    public int Id { get; set; }

    public int RiskId { get; set; }

    public DateTime SubmissionDate { get; set; }

    public DateTime LastUpdate { get; set; }

    public int PlanningStrategy { get; set; }

    public int MitigationEffort { get; set; }

    public int MitigationCost { get; set; }

    public int MitigationOwner { get; set; }

    public string CurrentSolution { get; set; } = null!;

    public string SecurityRequirements { get; set; } = null!;

    public string SecurityRecommendations { get; set; } = null!;

    public int SubmittedBy { get; set; }

    public DateOnly PlanningDate { get; set; }

    public int MitigationPercent { get; set; }

    public virtual MitigationCost MitigationCostNavigation { get; set; } = null!;

    public virtual MitigationEffort MitigationEffortNavigation { get; set; } = null!;

    public virtual User MitigationOwnerNavigation { get; set; } = null!;

    public virtual PlanningStrategy PlanningStrategyNavigation { get; set; } = null!;

    public virtual Risk Risk { get; set; } = null!;

    public virtual ICollection<Risk> Risks { get; set; } = new List<Risk>();

    public virtual User SubmittedByNavigation { get; set; } = null!;
}

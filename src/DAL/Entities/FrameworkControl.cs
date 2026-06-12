using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class FrameworkControl
{
    public int Id { get; set; }

    public string? ShortName { get; set; }

    public string? LongName { get; set; }

    public string? Description { get; set; }

    public string? SupplementalGuidance { get; set; }

    public int? ControlOwner { get; set; }

    public int? ControlClass { get; set; }

    public int? ControlPhase { get; set; }

    public string? ControlNumber { get; set; }

    public int ControlMaturity { get; set; }

    public int DesiredMaturity { get; set; }

    public int? ControlPriority { get; set; }

    public bool? ControlStatus { get; set; }

    public int? Family { get; set; }

    public DateTime SubmissionDate { get; set; }

    public DateOnly? LastAuditDate { get; set; }

    public DateOnly? NextAuditDate { get; set; }

    public int? DesiredFrequency { get; set; }

    public int MitigationPercent { get; set; }

    public int Status { get; set; }

    public bool Deleted { get; set; }

    // Track 6 Phase 3: ControlOwner promoted to navigable FK (fk_framework_controls_control_owner).
    public virtual User? ControlOwnerUser { get; set; }
}

using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Risk
{
    public int Id { get; set; }

    public string Status { get; set; } = null!;

    public string Subject { get; set; } = null!;

    public string ReferenceId { get; set; } = null!;

    public int? Regulation { get; set; }

    public string? ControlNumber { get; set; }

    public int Source { get; set; }

    public int Category { get; set; }

    public int Owner { get; set; }

    public int Manager { get; set; }

    public string Assessment { get; set; } = null!;

    public string Notes { get; set; } = null!;

    public DateTime SubmissionDate { get; set; }

    public DateTime LastUpdate { get; set; }

    public int? MitigationId { get; set; }

    public int ProjectId { get; set; }

    public int? CloseId { get; set; }

    public int SubmittedBy { get; set; }

    public string RiskCatalogMapping { get; set; } = null!;

    public string ThreatCatalogMapping { get; set; } = null!;

    public int TemplateGroupId { get; set; }

    public virtual ICollection<MgmtReview> MgmtReviews { get; set; } = new List<MgmtReview>();

    public virtual Mitigation? Mitigation { get; set; }

    public virtual ICollection<Mitigation> Mitigations { get; set; } = new List<Mitigation>();

    public virtual ICollection<Entity> Entities { get; set; } = new List<Entity>();
}

using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class Risk : DAL.Interfaces.IEntityScoped
{
    public int Id { get; set; }

    public string Status { get; set; } = null!;

    // Track 6 Phase 5: type-safe replacement for the free-text `status` column (create-copy-coexist).
    // Nullable: legacy rows whose `status` text is outside the known set stay NULL rather than being
    // misrepresented as New. The old `status` column is retained until a later release drops it.
    public Enums.RiskStatus? StatusId { get; set; }

    public string Subject { get; set; } = null!;

    public string ReferenceId { get; set; } = null!;

    // Track 6 Phase 6a: `regulation` and `project_id` unmapped (orphan columns, no live `regulation`/`projects`
    // table or code reference). Columns physically dropped in Phase 6b (73.sql).

    public string? ControlNumber { get; set; }

    public int? Source { get; set; }

    public int? Category { get; set; }

    public int? Owner { get; set; }

    public int? Manager { get; set; }

    public string Assessment { get; set; } = null!;

    public string Notes { get; set; } = null!;

    public DateTime SubmissionDate { get; set; }

    public DateTime LastUpdate { get; set; }

    public int? MitigationId { get; set; }

    public int? CloseId { get; set; }

    public int? SubmittedBy { get; set; }

    public string RiskCatalogMapping { get; set; } = null!;

    public string ThreatCatalogMapping { get; set; } = null!;

    public int TemplateGroupId { get; set; }

    public virtual Category? CategoryNavigation { get; set; }

    public virtual ICollection<Comment> Comments { get; set; } = new List<Comment>();

    public virtual ICollection<MgmtReview> MgmtReviews { get; set; } = new List<MgmtReview>();

    public virtual Mitigation? Mitigation { get; set; }

    public virtual ICollection<Mitigation> Mitigations { get; set; } = new List<Mitigation>();

    public virtual Source? SourceNavigation { get; set; }

    // Track 6 Phase 3: correlation columns promoted to navigable FKs (fk_risks_owner/manager/submitted_by).
    public virtual User? OwnerUser { get; set; }
    public virtual User? ManagerUser { get; set; }
    public virtual User? SubmittedByUser { get; set; }

    public int? IncidentResponsePlanId { get; set; }
    public virtual IncidentResponsePlan? IncidentResponsePlan { get; set; }

    public int? EntityId { get; set; }
    public virtual Entity? Entity { get; set; }
    
    public virtual ICollection<Entity> Entities { get; set; } = new List<Entity>();
    public virtual ICollection<RiskCatalog> RiskCatalogs { get; set; } = new List<RiskCatalog>();
    public virtual ICollection<Vulnerability> Vulnerabilities { get; set; } = new List<Vulnerability>();
}

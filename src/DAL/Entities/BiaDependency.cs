using System;

namespace DAL.Entities;

/// <summary>
/// One continuity dependency (Stage 9.3, S43 §4.2): <see cref="DependentEntityId"/> depends on
/// <see cref="ProviderEntityId"/> — if the provider stops, the dependent is affected. Both ends are a
/// business process or an IT service, in any combination.
///
/// Cycles are allowed (directory ↔ DNS is real) and reported, never refused; a self-dependency is
/// refused by <c>ck_bia_dependencies_not_self</c>. The row is immutable: it is corrected by deleting and
/// adding, which is why it has no <c>updated_at</c>.
/// </summary>
public class BiaDependency
{
    public int Id { get; set; }

    public int DependentEntityId { get; set; }

    public int ProviderEntityId { get; set; }

    public string? Description { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual Entity Dependent { get; set; } = null!;

    public virtual Entity Provider { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}

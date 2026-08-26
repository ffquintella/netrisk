namespace DAL.Entities;

/// <summary>
/// The organization's declared appetite for residual risk (Track 8 milestone 8.3.3).
///
/// COSO ERM (2017) asks for an appetite aligned to strategy and DORA Art. 6(8) asks for the
/// tolerance level to be an explicit, documented artifact. Before this entity NetRisk had
/// <c>risk_levels</c>, which are display bands: they colour the grid and pick a review cadence and
/// gate nothing. These two numbers are what turn a score into behaviour.
/// </summary>
public class RiskAppetite : DAL.Interfaces.IEntityScoped
{
    public int Id { get; set; }

    /// <summary>
    /// The business entity this appetite governs, or <c>null</c> for the organization-wide default.
    /// Exactly one global row is permitted; the service enforces it, because MySQL's unique index
    /// treats every NULL as distinct and would happily accept a second.
    /// </summary>
    public int? EntityId { get; set; }

    /// <summary>
    /// Residual score above which an acceptance is refused outright. Raising this number is itself
    /// an audited act — that is the point of storing it rather than hard-coding a constant.
    /// </summary>
    public double MaxAcceptableResidual { get; set; }

    /// <summary>
    /// Residual score above which a decision needs a second, distinct approver holding the top
    /// review band. Must not exceed <see cref="MaxAcceptableResidual"/>: a threshold above the
    /// ceiling can never fire, and a configuration that can never fire reads as a control.
    /// </summary>
    public double DualApprovalThreshold { get; set; }

    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual Entity? Entity { get; set; }

    public virtual User? CreatedBy { get; set; }
}

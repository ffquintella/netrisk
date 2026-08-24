namespace DAL.Entities;

/// <summary>
/// Links one acceptance to one finding it covers. An explicit join entity rather than an implicit
/// many-to-many so the row can say <em>when</em> the finding was brought under the acceptance —
/// findings get added to an existing acceptance as later scans surface them, and the timeline needs
/// that date.
/// </summary>
public class RiskAcceptanceFinding
{
    public int Id { get; set; }

    public int RiskAcceptanceId { get; set; }

    public int VulnerabilityId { get; set; }

    public DateTime CreatedAt { get; set; }

    public virtual RiskAcceptance? RiskAcceptance { get; set; }

    public virtual Vulnerability? Vulnerability { get; set; }
}

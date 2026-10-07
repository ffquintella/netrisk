namespace DAL.Entities;

/// <summary>
/// A MITRE ATT&amp;CK technique associated with a finding (Stage 9.4, T164, S45 §4.5), table
/// <c>vulnerability_attack_techniques</c>. Immutable: corrected by deleting and adding. Scoped through
/// the finding.
/// </summary>
public class VulnerabilityAttackTechnique
{
    public int Id { get; set; }

    public int VulnerabilityId { get; set; }

    /// <summary><c>T1234</c> or <c>T1234.567</c>, upper case.</summary>
    public string TechniqueId { get; set; } = null!;

    public string? TechniqueName { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual Vulnerability Vulnerability { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}

/// <summary>
/// A MITRE ATT&amp;CK technique associated with a risk scenario (Stage 9.4, T164, S45 §4.5), table
/// <c>risk_attack_techniques</c>. Immutable, scoped through the risk, and in the governance audit trail.
/// </summary>
public class RiskAttackTechnique
{
    public int Id { get; set; }

    public int RiskId { get; set; }

    /// <summary><c>T1234</c> or <c>T1234.567</c>, upper case.</summary>
    public string TechniqueId { get; set; } = null!;

    public string? TechniqueName { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    public virtual Risk Risk { get; set; } = null!;

    public virtual User? CreatedBy { get; set; }
}

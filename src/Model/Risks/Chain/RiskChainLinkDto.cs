using DAL.Enums;

namespace Model.Risks.Chain;

/// <summary>One link from a risk to one node of the linkage chain (Stage 9.1, S41 §6).</summary>
public class RiskChainLinkDto
{
    public int Id { get; set; }

    public int RiskId { get; set; }

    public RiskChainLevel Level { get; set; }

    public int? EntityId { get; set; }

    /// <summary>Null on a redacted host link (<see cref="IsRedacted"/>).</summary>
    public int? HostId { get; set; }

    /// <summary>
    /// The target's <c>name</c> property; for a host, <c>HostName ?? Fqdn ?? Ip</c>. Null on a
    /// redacted host link.
    /// </summary>
    public string? TargetName { get; set; }

    /// <summary>The entity definition name (<c>businessProcess</c>, <c>itService</c>, …) or <c>host</c>.</summary>
    public string TargetType { get; set; } = string.Empty;

    public RiskChainLinkOrigin Origin { get; set; }

    /// <summary>UTC.</summary>
    public DateTime CreatedAt { get; set; }

    public int? CreatedById { get; set; }

    /// <summary>UTC; set when the link was promoted to Declared or demoted back to Legacy.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>
    /// True for a host link shown to a caller without the <c>hosts</c> permission: the level is
    /// reported as present, and nothing about the host — id or name — is.
    /// </summary>
    public bool IsRedacted { get; set; }
}

/// <summary>
/// The body of <c>POST /RiskChain/Risks/{riskId}/Links</c>: exactly one of the two targets.
///
/// There is deliberately no level. The level is derived from the target's type, so a request can
/// never name one that contradicts it (S41 §11, D3).
/// </summary>
public class RiskChainLinkCreateDto
{
    public int? EntityId { get; set; }

    public int? HostId { get; set; }
}

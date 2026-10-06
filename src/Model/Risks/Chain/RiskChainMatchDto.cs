using DAL.Enums;

namespace Model.Risks.Chain;

/// <summary>
/// A risk found by a node query (Stage 9.1, S41 §6): <c>GET /RiskChain/Entities/{id}/Risks</c> or
/// <c>GET /RiskChain/Hosts/{id}/Risks</c>.
///
/// Any status — a closed risk is returned with its status rather than filtered out, so no risk goes
/// invisible through an implicit filter. A risk appears once: a direct link wins over an inferred one,
/// and between inferred ones the shallowest node wins, then the lowest node id.
/// </summary>
public class RiskChainMatchDto
{
    public int RiskId { get; set; }

    public string Subject { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// True when the risk is not linked to the queried node itself but to a node below it — the
    /// service that serves the process, the application the service runs — and was found by walking
    /// the typed edges upward.
    /// </summary>
    public bool Inferred { get; set; }

    /// <summary>For an inferred match, the level of the node the risk is actually linked to.</summary>
    public RiskChainLevel? ViaLevel { get; set; }

    public int? ViaEntityId { get; set; }

    public string? ViaEntityName { get; set; }
}

using DAL.Enums;

namespace Model.Risks.Chain;

/// <summary>
/// The projection of one risk onto the linkage chain (Stage 9.1, S41 §6):
/// <c>GET /RiskChain/Risks/{riskId}</c>.
///
/// <see cref="Levels"/> always carries all five levels, in order from <see cref="RiskChainLevel.Objective"/>
/// to <see cref="RiskChainLevel.Asset"/>, including the empty ones — a missing middle link is a fact
/// to show ("IT service: not informed"), not a reason to hide the risk or the rest of its chain.
/// </summary>
public class RiskChainDto
{
    public int RiskId { get; set; }

    /// <summary>
    /// The risk's <c>risks.entity_id</c> — the scope column (who may see the risk, which appetite
    /// applies). Read-only here: the chain never writes it.
    /// </summary>
    public int? ScopeEntityId { get; set; }

    public string? ScopeEntityName { get; set; }

    /// <summary>Always five, Objective through Asset.</summary>
    public List<RiskChainLevelDto> Levels { get; set; } = new();

    /// <summary>The levels with no link. A redacted host link still counts as present.</summary>
    public List<RiskChainLevel> MissingLevels { get; set; } = new();
}

/// <summary>One level of a <see cref="RiskChainDto"/> and the links the risk has at it.</summary>
public class RiskChainLevelDto
{
    public RiskChainLevel Level { get; set; }

    public List<RiskChainLinkDto> Links { get; set; } = new();
}

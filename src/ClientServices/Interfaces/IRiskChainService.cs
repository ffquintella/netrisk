using Model.Risks.Chain;

namespace ClientServices.Interfaces;

/// <summary>
/// The desktop client's view of the risk linkage chain (Stage 9.1, S41 §7): <c>/RiskChain</c>.
///
/// Refusals keep the server's explanation. A 404 is a <see cref="Model.Exceptions.DataNotFoundException"/>;
/// a 400, 403, 409 or 422 is an <see cref="Model.Exceptions.InvalidHttpRequestException"/> whose message
/// is the server's body — "This link comes from the risk's Entity field. Remove it there…" is something
/// a person can act on, a generic failure is not.
/// </summary>
public interface IRiskChainService
{
    /// <summary>The risk's chain: five levels, empty ones included; host links redacted without
    /// <c>hosts</c>.</summary>
    Task<RiskChainDto> GetRiskChainAsync(int riskId);

    /// <summary>Links the risk to an entity or a host. Returns the new link (201) or the existing Legacy
    /// link promoted to Declared (200).</summary>
    Task<RiskChainLinkDto> AddLinkAsync(int riskId, RiskChainLinkCreateDto request);

    /// <summary>
    /// Removes a Declared link. Null when it was deleted (204); the link, now Legacy, when it was
    /// demoted because the same entity is still on the risk's "Entity" field (200).
    /// </summary>
    Task<RiskChainLinkDto?> DeleteLinkAsync(int riskId, int linkId);

    /// <summary>The risks linked to a chain entity; with <paramref name="inferred"/>, also through any
    /// node below it.</summary>
    Task<List<RiskChainMatchDto>> GetRisksByEntityAsync(int entityId, bool inferred = false);

    /// <summary>The risks linked directly to a host. Needs <c>hosts</c>.</summary>
    Task<List<RiskChainMatchDto>> GetRisksByHostAsync(int hostId);

    /// <summary>The critical-process coverage metric.</summary>
    Task<CriticalProcessCoverageDto> GetCriticalProcessCoverageAsync();
}

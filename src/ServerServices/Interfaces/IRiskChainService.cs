using System.Security.Claims;
using Model.Risks.Chain;

namespace ServerServices.Interfaces;

/// <summary>
/// The risk linkage chain (Stage 9.1, S41 §6): objective → process → IT service → data → asset.
///
/// Every read and write goes through the caller's scoped context, so a risk or a host outside the
/// caller's scope is indistinguishable from one that does not exist. Host targets additionally need the
/// <c>hosts</c> permission, which is checked from <paramref name="user"/> <b>before</b> any query runs;
/// a null principal has no <c>hosts</c>.
/// </summary>
public interface IRiskChainService
{
    /// <summary>The risk's chain: five levels, empty ones included. Host links are redacted for a
    /// principal without <c>hosts</c>.</summary>
    /// <exception cref="Model.Exceptions.DataNotFoundException">Risk missing or out of scope.</exception>
    Task<RiskChainDto> GetRiskChainAsync(int riskId, ClaimsPrincipal? user);

    /// <summary>
    /// Links the risk to an entity of a chain type or to a host. A new link is created Declared; an
    /// existing Legacy link to the same target is promoted to Declared instead.
    /// </summary>
    /// <exception cref="Model.Exceptions.InvalidParameterException">Neither or both targets.</exception>
    /// <exception cref="Model.Exceptions.PermissionInvalidException">Host target without <c>hosts</c>.</exception>
    /// <exception cref="Model.Exceptions.DataNotFoundException">Risk, entity or host missing or out of scope.</exception>
    /// <exception cref="Model.Exceptions.RuleBrokenException"><c>entity_not_in_chain</c>.</exception>
    /// <exception cref="Model.Exceptions.DataAlreadyExistsException">Already linked as Declared.</exception>
    Task<RiskChainLinkWriteResult> AddLinkAsync(int riskId, RiskChainLinkCreateDto request, int? actingUserId,
        ClaimsPrincipal? user);

    /// <summary>
    /// Removes a Declared link — or, when the same (risk, entity) pair is still in
    /// <c>risk_to_entity</c>, demotes it back to Legacy, because deleting it would break the coexistence
    /// invariant and the next legacy save would recreate it.
    /// </summary>
    /// <exception cref="Model.Exceptions.DataNotFoundException">No such link on this risk, or hidden by scope.</exception>
    /// <exception cref="Model.Exceptions.PermissionInvalidException">Host link without <c>hosts</c>.</exception>
    /// <exception cref="Model.Exceptions.RuleBrokenException"><c>legacy_link</c>.</exception>
    Task<RiskChainLinkDeleteResult> DeleteLinkAsync(int riskId, int linkId, ClaimsPrincipal? user);

    /// <summary>
    /// The risks linked to a chain entity: directly, or — with <paramref name="inferred"/> — also to any
    /// node below it. Any status.
    /// </summary>
    /// <exception cref="Model.Exceptions.DataNotFoundException">No such entity.</exception>
    /// <exception cref="Model.Exceptions.RuleBrokenException"><c>entity_not_in_chain</c>.</exception>
    Task<List<RiskChainMatchDto>> GetRisksByEntityAsync(int entityId, bool inferred);

    /// <summary>The risks linked directly to a host. Any status.</summary>
    /// <exception cref="Model.Exceptions.PermissionInvalidException">Without <c>hosts</c>, before any query.</exception>
    /// <exception cref="Model.Exceptions.DataNotFoundException">Host missing or out of scope — the same answer.</exception>
    Task<List<RiskChainMatchDto>> GetRisksByHostAsync(int hostId, ClaimsPrincipal? user);

    /// <summary>The critical-process coverage metric, over what the caller can see.</summary>
    Task<CriticalProcessCoverageDto> GetCriticalProcessCoverageAsync();
}

/// <summary>The outcome of <see cref="IRiskChainService.AddLinkAsync"/>: 201 when
/// <paramref name="Created"/>, 200 when an existing Legacy link was promoted.</summary>
public sealed record RiskChainLinkWriteResult(RiskChainLinkDto Link, bool Created);

/// <summary>The outcome of <see cref="IRiskChainService.DeleteLinkAsync"/>: 204 when deleted; 200 with
/// <paramref name="Demoted"/> when the link went back to Legacy.</summary>
public sealed record RiskChainLinkDeleteResult(RiskChainLinkDto? Demoted)
{
    public bool Deleted => Demoted is null;
}

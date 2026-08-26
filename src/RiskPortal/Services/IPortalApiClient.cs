using DAL.Entities;
using DAL.Enums;
using Model.Governance;
using RiskPortal.Models;

namespace RiskPortal.Services;

/// <summary>
/// Everything the portal asks of the NetRisk API.
///
/// An interface with one HTTP implementation and one test double, deliberately narrow: the portal
/// reaches exactly these endpoints and nothing else, which is what makes "what can this outward-facing
/// app do" answerable by reading one file. That mattered enough to state, because the portal is the
/// first internet-facing surface in this product with write access to the register.
/// </summary>
public interface IPortalApiClient
{
    /// <summary>
    /// Whether this portal's client registration exists and has been approved. Never throws: the
    /// sign-in page has to be able to explain an unreachable API rather than fail to render.
    /// </summary>
    Task<PortalRegistrationState> GetRegistrationStateAsync(CancellationToken ct = default);

    /// <summary>
    /// Exchanges credentials for a session token, or null when the credentials are refused.
    ///
    /// The password reaches this method and no further: it is used for one Basic-authenticated call
    /// and is never stored, logged or returned.
    /// </summary>
    Task<string?> SignInAsync(string login, string password, CancellationToken ct = default);

    /// <summary>Revokes the session token server-side (finding NR-2026-028).</summary>
    Task SignOutAsync(string token, CancellationToken ct = default);

    /// <summary>The campaigns the signed-in reviewer is appointed to.</summary>
    Task<List<CampaignSummary>> GetMyCampaignsAsync(string token, bool openOnly,
        CancellationToken ct = default);

    /// <summary>One campaign with its risks, scores, appetite verdicts and existing decisions.</summary>
    Task<CampaignDetail?> GetCampaignAsync(string token, int campaignId, CancellationToken ct = default);

    /// <summary>Persists a drag-to-rank ordering.</summary>
    Task<PortalResult> SaveRankingAsync(string token, int campaignId, List<int> orderedItemIds,
        CancellationToken ct = default);

    /// <summary>Records one decision. The result carries the server's explanation when it refuses.</summary>
    Task<PortalResult> DecideAsync(string token, int campaignId, int itemId,
        CampaignDecisionRequest request, CancellationToken ct = default);
}

/// <summary>
/// The outcome of a write.
///
/// A result rather than an exception because the interesting case is not "it failed" but "the server
/// refused, and here is the sentence to show the reviewer" — the appetite ceiling, the
/// segregation-of-duties rule, a missing justification. Turning those into exceptions would push the
/// page models into catching and unwrapping, and the message is the point.
/// </summary>
public class PortalResult
{
    public bool Succeeded { get; init; }

    /// <summary>A sentence written to be shown verbatim. Empty on success.</summary>
    public string Message { get; init; } = string.Empty;

    public static PortalResult Ok() => new() { Succeeded = true };

    public static PortalResult Fail(string message) => new() { Succeeded = false, Message = message };
}

using DAL.Enums;
using Model.Governance;
using RiskPortal.Models;
using RiskPortal.Services;

namespace RiskPortal.Tests.Mock;

/// <summary>
/// A scriptable stand-in for the NetRisk API.
///
/// Hand-written rather than a mocking-framework double because what these tests assert is mostly
/// *what the portal sent* — the decision payload it assembled from a form post — and a recorded call
/// list reads better for that than a chain of argument matchers.
/// </summary>
public class FakePortalApiClient : IPortalApiClient
{
    public PortalRegistrationState Registration { get; set; } = new()
    {
        ClientId = "portal-test", Approved = true
    };

    /// <summary>The token handed back on a successful sign-in. Null means "credentials refused".</summary>
    public string? TokenToIssue { get; set; } = "test-token";

    public List<CampaignSummary> Campaigns { get; set; } = [];

    public CampaignDetail? Detail { get; set; }

    /// <summary>What every write answers. Set to a failure to exercise the refusal paths.</summary>
    public PortalResult NextWriteResult { get; set; } = PortalResult.Ok();

    public List<(string Login, string Password)> SignInAttempts { get; } = [];

    public List<string> RevokedTokens { get; } = [];

    public List<(int CampaignId, List<int> Order)> Rankings { get; } = [];

    public List<(int CampaignId, int ItemId, CampaignDecisionRequest Request)> Decisions { get; } = [];

    public Task<PortalRegistrationState> GetRegistrationStateAsync(CancellationToken ct = default) =>
        Task.FromResult(Registration);

    public Task<string?> SignInAsync(string login, string password, CancellationToken ct = default)
    {
        SignInAttempts.Add((login, password));
        return Task.FromResult(TokenToIssue);
    }

    public Task SignOutAsync(string token, CancellationToken ct = default)
    {
        RevokedTokens.Add(token);
        return Task.CompletedTask;
    }

    public Task<List<CampaignSummary>> GetMyCampaignsAsync(string token, bool openOnly,
        CancellationToken ct = default) => Task.FromResult(Campaigns);

    public Task<CampaignDetail?> GetCampaignAsync(string token, int campaignId,
        CancellationToken ct = default) => Task.FromResult(Detail);

    public Task<PortalResult> SaveRankingAsync(string token, int campaignId, List<int> orderedItemIds,
        CancellationToken ct = default)
    {
        Rankings.Add((campaignId, orderedItemIds));
        return Task.FromResult(NextWriteResult);
    }

    public Task<PortalResult> DecideAsync(string token, int campaignId, int itemId,
        CampaignDecisionRequest request, CancellationToken ct = default)
    {
        Decisions.Add((campaignId, itemId, request));
        return Task.FromResult(NextWriteResult);
    }
}

/// <summary>A session with a token, or without one.</summary>
public class FakePortalSession(string? token = "test-token", string? login = "breviewer") : IPortalSession
{
    public string? Token { get; } = token;

    public string? Login { get; } = login;
}

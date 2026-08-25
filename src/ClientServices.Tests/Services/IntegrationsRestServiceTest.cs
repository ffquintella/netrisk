using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Authentication.Federation;
using Model.Authentication.Scim;
using Model.Exceptions;
using Model.Integrations;
using Model.Notifications;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Drives <see cref="IntegrationsRestService"/> over <see cref="StubRestBackend"/>, so every URL it
/// builds and every status branch runs for real.
///
/// The property worth holding across this whole client: credentials travel outward only. A create or
/// update puts the secret in a separate field, and every read returns a view type that has nowhere to
/// put one — so there is no state in which the desktop client holds a token it read back from the
/// server.
/// </summary>
[TestSubject(typeof(IntegrationsRestService))]
public class IntegrationsRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IIntegrationsService _service;

    public IntegrationsRestServiceTest()
    {
        _service = ResolveWith<IIntegrationsService>(_backend);
    }

    // --- 4.1 notification channels -----------------------------------------------------------

    [Fact]
    public async Task ChannelsAreReadFromTheChannelsEndpoint()
    {
        _backend.OnGet("/NotificationChannels", new List<NotificationChannel>
        {
            new() { Id = 1, Name = "SOC Slack", Kind = NotificationChannelKind.Slack, Enabled = true }
        });

        var channels = await _service.GetChannelsAsync();

        Assert.Equal("SOC Slack", Assert.Single(channels).Name);
        Assert.True(_backend.Sent(Method.Get, "/NotificationChannels"));
        Assert.Contains("includeDisabled=true", _backend.LastRequest!.Query);
    }

    [Fact]
    public async Task AnEmptyChannelListIsNotAnError()
    {
        _backend.OnStatus(Method.Get, "/NotificationChannels", HttpStatusCode.NoContent);

        Assert.Empty(await _service.GetChannelsAsync());
    }

    [Fact]
    public async Task AFailedChannelReadIsReported()
    {
        _backend.OnStatus(Method.Get, "/NotificationChannels", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetChannelsAsync());
    }

    [Fact]
    public async Task ATransportFailureBecomesACommunicationException()
    {
        _backend.OnTransportFailure(Method.Get, "/NotificationChannels");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetChannelsAsync());
    }

    [Fact]
    public async Task CreatingAChannelPostsIt()
    {
        _backend.OnPost("/NotificationChannels",
            new NotificationChannel { Id = 9, Name = "SOC Slack", Kind = NotificationChannelKind.Slack },
            HttpStatusCode.Created);

        var created = await _service.CreateChannelAsync(new NotificationChannel
        {
            Name = "SOC Slack", Kind = NotificationChannelKind.Slack
        });

        Assert.Equal(9, created.Id);
        Assert.Contains("SOC Slack", _backend.LastRequest!.Body);
    }

    [Fact]
    public async Task ARejectedChannelSurfacesTheServersExplanation()
    {
        _backend.On(Method.Post, "/NotificationChannels",
            """{"error":"invalid_parameter","parameterName":"ConfigurationJson"}""",
            HttpStatusCode.BadRequest);

        var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.CreateChannelAsync(new NotificationChannel { Name = "x" }));

        // Which parameter was refused is the whole diagnosis; replacing it with a generic message
        // leaves the operator guessing.
        Assert.Contains("ConfigurationJson", thrown.Message);
    }

    [Fact]
    public async Task UpdatingAChannelPutsToItsOwnRoute()
    {
        _backend.OnPut("/NotificationChannels/3", new NotificationChannel { Id = 3, Name = "renamed" });

        await _service.UpdateChannelAsync(new NotificationChannel { Id = 3, Name = "renamed" });

        Assert.True(_backend.Sent(Method.Put, "/NotificationChannels/3"));
    }

    [Fact]
    public async Task DeletingAChannelAcceptsA204()
    {
        _backend.OnStatus(Method.Delete, "/NotificationChannels/3", HttpStatusCode.NoContent);

        await _service.DeleteChannelAsync(3);

        Assert.True(_backend.Sent(Method.Delete, "/NotificationChannels/3"));
    }

    [Fact]
    public async Task ARefusedDeleteSurfacesItsReason()
    {
        _backend.On(Method.Delete, "/NotificationChannels/3",
            """{"error":"invalid_parameter","message":"1 channel(s) fall back to this one."}""",
            HttpStatusCode.BadRequest);

        var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.DeleteChannelAsync(3));

        Assert.Contains("fall back", thrown.Message);
    }

    [Fact]
    public async Task DeletingAnUnknownChannelIsNotFound()
    {
        _backend.OnStatus(Method.Delete, "/NotificationChannels/404", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.DeleteChannelAsync(404));
    }

    [Fact]
    public async Task TestingAChannelPostsToItsTestRoute()
    {
        _backend.OnPost("/NotificationChannels/1/test",
            ChannelTestResult.Ok("Test message delivered.", 42));

        var result = await _service.TestChannelAsync(1);

        Assert.True(result.Success);
        Assert.Equal(42, result.ElapsedMilliseconds);
    }

    [Fact]
    public async Task TheProviderAndEventCatalogsAreRead()
    {
        _backend.OnGet("/NotificationChannels/providers", new List<NotificationChannelProvider>
        {
            new() { Kind = NotificationChannelKind.Slack, Name = "Slack" }
        });

        _backend.OnGet("/NotificationChannels/events", new List<NotificationEventDescriptor>
        {
            new() { EventType = NotificationEventType.SlaBreached, Name = "sla.breached" }
        });

        Assert.Single(await _service.GetChannelProvidersAsync());
        Assert.Equal("sla.breached", (await _service.GetNotificationEventsAsync())[0].Name);
    }

    // --- 4.1.3 subscriptions and deliveries ---------------------------------------------------

    [Fact]
    public async Task SubscriptionsRoundTrip()
    {
        _backend.OnGet("/NotificationSubscriptions", new List<NotificationSubscription>
        {
            new() { Id = 1, EventType = NotificationEventType.RiskCreated, ChannelId = 1 }
        });

        _backend.OnPost("/NotificationSubscriptions", new NotificationSubscription { Id = 2, ChannelId = 1 },
            HttpStatusCode.Created);

        _backend.OnStatus(Method.Delete, "/NotificationSubscriptions/1", HttpStatusCode.NoContent);

        Assert.Single(await _service.GetSubscriptionsAsync());
        Assert.Equal(2, (await _service.CreateSubscriptionAsync(new NotificationSubscription())).Id);

        await _service.DeleteSubscriptionAsync(1);
        Assert.True(_backend.Sent(Method.Delete, "/NotificationSubscriptions/1"));
    }

    [Fact]
    public async Task TheDeliveryLogIsReadWithItsLimit()
    {
        _backend.OnGet("/NotificationSubscriptions/deliveries", new List<NotificationDelivery>
        {
            new() { Id = 1, Status = NotificationDeliveryStatus.Failed, LastError = "HTTP 404" }
        });

        var deliveries = await _service.GetDeliveriesAsync(50);

        Assert.Equal("HTTP 404", Assert.Single(deliveries).LastError);
        Assert.Contains("limit=50", _backend.LastRequest!.Query);
    }

    [Fact]
    public async Task RequeuingADeliveryPostsToItsRoute()
    {
        _backend.OnPost("/NotificationSubscriptions/deliveries/5/requeue",
            new NotificationDelivery { Id = 5, Status = NotificationDeliveryStatus.Pending });

        var delivery = await _service.RequeueDeliveryAsync(5);

        Assert.Equal(NotificationDeliveryStatus.Pending, delivery.Status);
    }

    // --- 4.2 issue trackers -------------------------------------------------------------------

    [Fact]
    public async Task IssueTrackerConnectionsCarryFlagsRatherThanCredentials()
    {
        _backend.OnGet("/IssueTrackers", new List<IssueTrackerConnectionView>
        {
            new()
            {
                Id = 1, Name = "Security Jira", Provider = IssueTrackerProviderKind.Jira,
                HasToken = true, HasWebhookSecret = true
            }
        });

        var connection = Assert.Single(await _service.GetIssueTrackersAsync());

        Assert.True(connection.HasToken);
        Assert.Null(typeof(IssueTrackerConnectionView).GetProperty("Token"));
    }

    [Fact]
    public async Task CreatingAConnectionSendsTheCredentialsInSeparateFields()
    {
        _backend.OnPost("/IssueTrackers", new IssueTrackerConnectionView { Id = 9, Name = "Security Jira" },
            HttpStatusCode.Created);

        await _service.CreateIssueTrackerAsync(new IssueTrackerConnection
        {
            Name = "Security Jira", Provider = IssueTrackerProviderKind.Jira,
            BaseUrl = "https://acme.atlassian.net", ProjectKey = "SEC"
        }, "api-token", "webhook-secret");

        var body = _backend.LastRequest!.Body;

        Assert.Contains("api-token", body);
        Assert.Contains("webhookSecret", body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task UpdatingWithoutCredentialsSendsNullsMeaningUnchanged()
    {
        _backend.OnPut("/IssueTrackers/1", new IssueTrackerConnectionView { Id = 1 });

        await _service.UpdateIssueTrackerAsync(new IssueTrackerConnection { Id = 1, Name = "x" }, null, null);

        var body = _backend.LastRequest!.Body;

        // Null means "leave the stored one alone" all the way down; the client never received it.
        Assert.Contains("\"token\":null", body.Replace(" ", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task StatusMappingsArePutWholesale()
    {
        _backend.OnPut("/IssueTrackers/1/status-mappings", new List<IssueStatusMappingView>
        {
            new() { Id = 1, ExternalStatus = "Done", Action = IssueSyncAction.MarkMitigated }
        });

        var mappings = await _service.SetStatusMappingsAsync(1,
        [
            new IssueStatusMapping { ExternalStatus = "Done", Action = IssueSyncAction.MarkMitigated }
        ]);

        Assert.Single(mappings);
        Assert.True(_backend.Sent(Method.Put, "/IssueTrackers/1/status-mappings"));
    }

    [Fact]
    public async Task SyncingAConnectionReturnsItsResult()
    {
        _backend.OnPost("/IssueTrackers/1/sync", new IssueSyncResult { Examined = 3, Applied = 1 });

        var result = await _service.SyncIssueTrackerAsync(1);

        Assert.Equal(3, result.Examined);
    }

    [Fact]
    public async Task TheConflictQueueIsReadAndResolvable()
    {
        _backend.OnGet("/IssueTrackers/conflicts", new List<FindingIssueLinkView>
        {
            new() { Id = 7, IssueKey = "SEC-1", HasConflict = true }
        });

        _backend.OnPost("/IssueTrackers/conflicts/7/resolve",
            new FindingIssueLinkView { Id = 7, HasConflict = false });

        Assert.True(Assert.Single(await _service.GetIssueSyncConflictsAsync()).HasConflict);
        Assert.False((await _service.ResolveIssueSyncConflictAsync(7)).HasConflict);
    }

    // --- 4.2.2 links --------------------------------------------------------------------------

    [Fact]
    public async Task AFindingsLinksAreReadFromItsOwnRoute()
    {
        _backend.OnGet("/FindingIssues/finding/42", new List<FindingIssueLinkView>
        {
            new() { Id = 7, FindingId = 42, IssueKey = "SEC-1" }
        });

        Assert.Equal("SEC-1", Assert.Single(await _service.GetLinksForFindingAsync(42)).IssueKey);
    }

    [Fact]
    public async Task ThePreviewSendsBothIdsAsQueryParameters()
    {
        _backend.OnGet("/FindingIssues/preview",
            new IssueDraft { Title = "[Critical] SQL injection", FindingId = 42 });

        var draft = await _service.PreviewIssueAsync(1, 42);

        Assert.Contains("Critical", draft.Title);
        Assert.Contains("connectionId=1", _backend.LastRequest!.Query);
        Assert.Contains("findingId=42", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task AnUnknownPreviewIsNotFound()
    {
        _backend.OnStatus(Method.Get, "/FindingIssues/preview", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<DataNotFoundException>(() => _service.PreviewIssueAsync(1, 404));
    }

    [Fact]
    public async Task CreatingAnIssuePostsBothIds()
    {
        _backend.OnPost("/FindingIssues", new FindingIssueLinkView { Id = 7, IssueKey = "SEC-1" },
            HttpStatusCode.Created);

        var link = await _service.CreateIssueAsync(1, 42);

        Assert.Equal("SEC-1", link.IssueKey);
        Assert.Contains("42", _backend.LastRequest!.Body);
    }

    [Fact]
    public async Task ATrackerRefusalIsSurfacedRatherThanSwallowed()
    {
        _backend.On(Method.Post, "/FindingIssues",
            """{"error":"upstream_failure","provider":"Jira","message":"Jira refused."}""",
            HttpStatusCode.BadGateway);

        var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.CreateIssueAsync(1, 42));

        // "The tracker refused it" is not a NetRisk error, and the operator needs to see whose it is.
        Assert.Contains("Jira", thrown.Message);
    }

    [Fact]
    public async Task BulkCreationPostsTheSelection()
    {
        _backend.OnPost("/FindingIssues/bulk", new List<FindingIssueLinkView>
        {
            new() { Id = 7 }, new() { Id = 8 }
        });

        Assert.Equal(2, (await _service.CreateIssuesAsync(1, [42, 43])).Count);
    }

    [Fact]
    public async Task LinkingAnExistingIssueSendsTheKey()
    {
        _backend.OnPost("/FindingIssues/link", new FindingIssueLinkView { Id = 7, IssueKey = "SEC-9" },
            HttpStatusCode.Created);

        await _service.LinkExistingIssueAsync(1, 42, "https://acme.atlassian.net/browse/SEC-9");

        Assert.Contains("SEC-9", _backend.LastRequest!.Body);
    }

    [Fact]
    public async Task UnlinkingDeletes()
    {
        _backend.OnStatus(Method.Delete, "/FindingIssues/7", HttpStatusCode.NoContent);

        await _service.UnlinkIssueAsync(7);

        Assert.True(_backend.Sent(Method.Delete, "/FindingIssues/7"));
    }

    // --- 4.3 enterprise authentication --------------------------------------------------------

    [Fact]
    public async Task IdentityProvidersCarryAFlagRatherThanTheClientSecret()
    {
        _backend.OnGet("/IdentityProviders", new List<IdentityProviderView>
        {
            new()
            {
                Id = 1, Name = "Acme Entra ID", Protocol = IdentityProviderProtocol.Oidc,
                HasClientSecret = true
            }
        });

        var provider = Assert.Single(await _service.GetIdentityProvidersAsync());

        Assert.True(provider.HasClientSecret);
        Assert.Null(typeof(IdentityProviderView).GetProperty("ClientSecret"));
    }

    [Fact]
    public async Task CreatingAnIdentityProviderSendsTheSecretSeparately()
    {
        _backend.OnPost("/IdentityProviders", new IdentityProviderView { Id = 9, Name = "Okta" },
            HttpStatusCode.Created);

        await _service.CreateIdentityProviderAsync(new IdentityProvider { Name = "Okta" }, "top-secret");

        Assert.Contains("top-secret", _backend.LastRequest!.Body);
        Assert.Contains("clientSecret", _backend.LastRequest.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TestingAnIdentityProviderPostsToItsTestRoute()
    {
        _backend.OnPost("/IdentityProviders/1/test", ConnectionTestResult.Ok("Read the document."));

        Assert.True((await _service.TestIdentityProviderAsync(1)).Success);
    }

    [Fact]
    public async Task IssuingAScimTokenReturnsTheSecretOnce()
    {
        _backend.OnPost("/ScimTokens", new ScimTokenView
        {
            Id = 2, Name = "Entra", KeyId = "abcd", Secret = "scim_abcd_secret"
        }, HttpStatusCode.Created);

        _backend.OnGet("/ScimTokens", new List<ScimTokenView>
        {
            new() { Id = 2, Name = "Entra", KeyId = "abcd" }
        });

        var issued = await _service.IssueScimTokenAsync("Entra", null);
        Assert.NotNull(issued.Secret);

        // Every read path leaves it null.
        Assert.Null(Assert.Single(await _service.GetScimTokensAsync()).Secret);
    }

    [Fact]
    public async Task RevokingAScimTokenPostsToItsRoute()
    {
        _backend.OnPost("/ScimTokens/2/revoke", new ScimTokenView
        {
            Id = 2, RevokedAt = DateTime.UtcNow
        });

        Assert.NotNull((await _service.RevokeScimTokenAsync(2)).RevokedAt);
    }

    [Fact]
    public async Task TheScimAuditIsRead()
    {
        _backend.OnGet("/ScimTokens/log", new List<ScimRequestLog>
        {
            new() { Id = 1, Method = "PATCH", Path = "/scim/v2/Users/1", StatusCode = 200 }
        });

        Assert.Equal("PATCH", Assert.Single(await _service.GetScimLogAsync()).Method);
    }

    // --- 4.4 / 4.5 posture providers ----------------------------------------------------------

    [Fact]
    public async Task VisionOneRegionsAreRead()
    {
        _backend.OnGet("/TrendMicro/regions", new Dictionary<string, string>
        {
            ["eu"] = "https://api.eu.xdr.trendmicro.com"
        });

        var regions = await _service.GetTrendMicroRegionsAsync();

        Assert.Equal("https://api.eu.xdr.trendmicro.com", regions["eu"]);
    }

    [Fact]
    public async Task VisionOneConnectionsCarryAFlagRatherThanTheKey()
    {
        _backend.OnGet("/TrendMicro", new List<TrendMicroConnectionView>
        {
            new() { Id = 1, Name = "Acme", Region = "eu", HasApiKey = true }
        });

        Assert.True(Assert.Single(await _service.GetTrendMicroConnectionsAsync()).HasApiKey);
        Assert.Null(typeof(TrendMicroConnectionView).GetProperty("ApiKey"));
    }

    [Fact]
    public async Task SyncingVisionOneReturnsThePostureResult()
    {
        _backend.OnPost("/TrendMicro/1/sync", new PostureSyncResult
        {
            HostsCreated = 3, FindingsCreated = 12, CyberRiskIndex = 67.1
        });

        var result = await _service.SyncTrendMicroConnectionAsync(1);

        Assert.Equal(3, result.HostsCreated);
        Assert.Equal(67.1, result.CyberRiskIndex);
    }

    [Fact]
    public async Task AVisionOneOutageIsSurfaced()
    {
        _backend.On(Method.Post, "/TrendMicro/1/sync",
            """{"error":"upstream_failure","provider":"Trend Micro Vision One"}""",
            HttpStatusCode.BadGateway);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.SyncTrendMicroConnectionAsync(1));
    }

    [Fact]
    public async Task ScorecardConnectionsAndHistoryAreRead()
    {
        _backend.OnGet("/SecurityScorecard", new List<SecurityScorecardConnectionView>
        {
            new() { Id = 1, Name = "Acme", Domain = "acme.com", HasApiToken = true }
        });

        _backend.OnGet("/SecurityScorecard/1/history", new List<SecurityScorecardFactor>
        {
            new() { Id = 1, FactorName = "patching_cadence", Score = 54, Grade = "F" }
        });

        Assert.Equal("acme.com",
            Assert.Single(await _service.GetSecurityScorecardConnectionsAsync()).Domain);

        var history = Assert.Single(await _service.GetSecurityScorecardHistoryAsync(1));
        Assert.Equal(54, history.Score);
    }

    [Fact]
    public async Task CreatingAScorecardConnectionSendsTheTokenSeparately()
    {
        _backend.OnPost("/SecurityScorecard",
            new SecurityScorecardConnectionView { Id = 9, Domain = "acme.com" }, HttpStatusCode.Created);

        await _service.CreateSecurityScorecardConnectionAsync(
            new SecurityScorecardConnection { Name = "Acme", Domain = "acme.com" }, "api-token");

        Assert.Contains("api-token", _backend.LastRequest!.Body);
    }

    [Fact]
    public async Task TheSyncLogsAreRead()
    {
        _backend.OnGet("/TrendMicro/log", new List<IntegrationSyncLog>
        {
            new() { Id = 1, Integration = IntegrationKind.TrendMicroVisionOne }
        });

        _backend.OnGet("/SecurityScorecard/log", new List<IntegrationSyncLog>
        {
            new() { Id = 2, Integration = IntegrationKind.SecurityScorecard }
        });

        Assert.Single(await _service.GetTrendMicroLogAsync());
        Assert.Single(await _service.GetSecurityScorecardLogAsync());
    }
}

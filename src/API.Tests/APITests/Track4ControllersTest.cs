using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Model.Authentication.Federation;
using Model.Authentication.Scim;
using Model.Authentication.WebAuthn;
using Model.Integrations;
using Model.Notifications;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// The Track 4 endpoints: notification channels and subscriptions, issue-tracker connections and
/// links, the inbound webhook receiver, identity providers, SCIM, WebAuthn, and the two posture
/// providers.
///
/// These tests are about the HTTP contract rather than the domain logic — which exception becomes
/// which status code, and what does and does not reach the wire. Three of those would actually hurt if
/// they regressed: a stored credential appearing in a response, a 502 for an upstream failure being
/// reported as a NetRisk 500, and an unauthenticated webhook being answered with anything other than
/// 401.
/// </summary>
[TestSubject(typeof(NotificationChannelsController))]
public class Track4ControllersTest : BaseControllerTest
{
    private static T Controller<T>() where T : notnull => ResolveController<T>(_ => { });

    private static TValue Ok<TValue>(ActionResult<TValue> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsType<TValue>(ok.Value);
    }

    /// <summary>
    /// For actions whose declared type is an interface (<c>IReadOnlyList&lt;T&gt;</c>): the boxed value
    /// is the concrete list, so an exact-type assertion would fail on the shape rather than the content.
    /// </summary>
    private static TValue OkAs<TValue>(ActionResult<TValue> result)
    {
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        return Assert.IsAssignableFrom<TValue>(ok.Value);
    }

    private static TValue Created<TValue>(ActionResult<TValue> result)
    {
        var created = Assert.IsType<CreatedResult>(result.Result);
        return Assert.IsType<TValue>(created.Value);
    }

    // --- 4.1 notification channels -----------------------------------------------------------

    [Fact]
    public void TheProviderListNamesWhatThisBuildCanDeliverThrough()
    {
        var providers = Ok(Controller<NotificationChannelsController>().GetProviders());

        Assert.Equal(2, providers.Count);
        Assert.Contains(providers, p => p.Kind == NotificationChannelKind.Slack);
    }

    [Fact]
    public void TheEventCatalogIsServedFromOnePlace()
    {
        var events = OkAs(Controller<NotificationChannelsController>().GetEvents());

        // A client that hard-coded its own copy would offer a checkbox for an event nothing raises.
        Assert.Equal(NotificationCatalog.Events.Count, events.Count);
        Assert.Contains(events, e => e.Name == "sla.breached");
    }

    [Fact]
    public async Task ChannelsAreListedWithTheirSecretsRedacted()
    {
        var channels = Ok(await Controller<NotificationChannelsController>().GetAll());

        var slack = channels.Single(c => c.Kind == NotificationChannelKind.Slack);
        var configuration = ChannelConfiguration.Parse(slack.ConfigurationJson);

        // There is deliberately no endpoint that returns a webhook URL in clear.
        Assert.Equal(ChannelConfiguration.RedactedPlaceholder, configuration.WebhookUrl);
    }

    [Fact]
    public async Task AnUnknownChannelIsNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(
            (await Controller<NotificationChannelsController>().Get(404)).Result);
    }

    [Fact]
    public async Task CreatingAChannelAnswers201WithALocation()
    {
        var created = Created(await Controller<NotificationChannelsController>().Create(
            new NotificationChannel { Name = "SOC Slack", Kind = NotificationChannelKind.Slack }));

        Assert.Equal(99, created.Id);
    }

    [Fact]
    public async Task AnInvalidChannelIsABadRequestThatNamesTheParameter()
    {
        var result = await Controller<NotificationChannelsController>().Create(new NotificationChannel());

        var bad = Assert.IsType<BadRequestObjectResult>(result.Result);

        Assert.Contains("invalid_parameter", bad.Value!.ToString());
    }

    [Fact]
    public async Task UpdatingUsesTheRouteIdRatherThanTheBodyId()
    {
        var controller = Controller<NotificationChannelsController>();

        // A mismatched id in a PUT body is a client bug; honouring it would let a request update a
        // channel other than the one it addressed.
        var updated = Ok(await controller.Update(MockedNotificationSubscriptionsService.KnownChannelId,
            new NotificationChannel { Id = 999, Name = "renamed", Kind = NotificationChannelKind.Slack }));

        Assert.Equal(MockedNotificationSubscriptionsService.KnownChannelId, updated.Id);
    }

    [Fact]
    public async Task DeletingAChannelAnswers204()
    {
        Assert.IsType<NoContentResult>(await Controller<NotificationChannelsController>()
            .Delete(MockedNotificationSubscriptionsService.KnownChannelId));
    }

    [Fact]
    public async Task DeletingAChannelOtherChannelsFallBackToIsRefused()
    {
        Assert.IsType<BadRequestObjectResult>(
            await Controller<NotificationChannelsController>().Delete(2));
    }

    [Fact]
    public async Task TestingAChannelReturnsTheProvidersMessage()
    {
        var result = Ok(await Controller<NotificationChannelsController>()
            .Test(MockedNotificationSubscriptionsService.KnownChannelId));

        Assert.True(result.Success);
        Assert.Contains("delivered", result.Message);
    }

    [Fact]
    public async Task TestingAnUnknownChannelIsNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(
            (await Controller<NotificationChannelsController>().Test(404)).Result);
    }

    // --- 4.1.3 subscriptions and the delivery log --------------------------------------------

    [Fact]
    public async Task SubscriptionsAreListed()
    {
        Assert.Single(Ok(await Controller<NotificationSubscriptionsController>().GetAll()));
    }

    [Fact]
    public async Task CreatingASubscriptionAnswers201()
    {
        var created = Created(await Controller<NotificationSubscriptionsController>().Create(
            new NotificationSubscription
            {
                EventType = NotificationEventType.SlaBreached,
                ChannelId = MockedNotificationSubscriptionsService.KnownChannelId
            }));

        Assert.Equal(77, created.Id);
    }

    [Fact]
    public async Task ASubscriptionWithoutAChannelIsABadRequest()
    {
        Assert.IsType<BadRequestObjectResult>(
            (await Controller<NotificationSubscriptionsController>()
                .Create(new NotificationSubscription())).Result);
    }

    [Fact]
    public async Task TheDeliveryLogIsReadableAndCarriesTheLastError()
    {
        var deliveries = Ok(await Controller<NotificationSubscriptionsController>().GetDeliveries());

        var delivery = Assert.Single(deliveries);

        // "Did it go out?" cannot be answered from the absence of a Slack message.
        Assert.Equal(NotificationDeliveryStatus.Failed, delivery.Status);
        Assert.Equal("HTTP 404", delivery.LastError);
    }

    [Fact]
    public async Task RequeuingAFailedDeliveryResetsIt()
    {
        var delivery = Ok(await Controller<NotificationSubscriptionsController>().Requeue(5));

        Assert.Equal(NotificationDeliveryStatus.Pending, delivery.Status);
        Assert.Equal(0, delivery.Attempts);
    }

    [Fact]
    public async Task RequeuingADeliveredNotificationIsRefused()
    {
        // Re-sending a delivered notification would duplicate the alert.
        Assert.IsType<BadRequestObjectResult>(
            (await Controller<NotificationSubscriptionsController>().Requeue(6)).Result);
    }

    // --- 4.2 issue trackers ------------------------------------------------------------------

    [Fact]
    public void TrackerProvidersCarryTheirCapabilityFlags()
    {
        var providers = Ok(Controller<IssueTrackersController>().GetProviders());

        var github = providers.Single(p => p.Provider == IssueTrackerProviderKind.GitHub);

        // So the UI does not offer a priority field for a tracker that has none.
        Assert.False(github.Capabilities!.SupportsPriority);
    }

    [Fact]
    public async Task ConnectionsAreListedWithoutTheirCredentials()
    {
        var connection = Assert.Single(Ok(await Controller<IssueTrackersController>().GetAll()));

        Assert.True(connection.HasToken);
        Assert.True(connection.HasWebhookSecret);
        // The view type has no field a token could travel in.
        Assert.Null(typeof(IssueTrackerConnectionView).GetProperty("Token"));
    }

    [Fact]
    public async Task CreatingAConnectionCarriesTheCredentialsInASeparateField()
    {
        var created = Created(await Controller<IssueTrackersController>().Create(
            new IssueTrackerConnectionRequest
            {
                Connection = new IssueTrackerConnection
                {
                    Name = "Security Jira", Provider = IssueTrackerProviderKind.Jira,
                    BaseUrl = "https://acme.atlassian.net", ProjectKey = "SEC"
                },
                Token = "api-token",
                WebhookSecret = "webhook-secret"
            }));

        Assert.Equal(99, created.Id);
        Assert.True(created.HasToken);
    }

    [Fact]
    public async Task AConnectionWithNoBaseUrlIsABadRequest()
    {
        Assert.IsType<BadRequestObjectResult>(
            (await Controller<IssueTrackersController>().Create(new IssueTrackerConnectionRequest
            {
                Connection = new IssueTrackerConnection { Name = "x" }
            })).Result);
    }

    [Fact]
    public async Task TestingAConnectionReturnsTheProvidersVerdict()
    {
        var result = Ok(await Controller<IssueTrackersController>()
            .Test(MockedIssueTrackerService.KnownConnectionId));

        Assert.True(result.Success);
    }

    [Fact]
    public async Task StatusMappingsAreReplacedWholesale()
    {
        var mappings = Ok(await Controller<IssueTrackersController>().SetStatusMappings(
            MockedIssueTrackerService.KnownConnectionId,
            [new IssueStatusMapping { ExternalStatus = "Done", Action = IssueSyncAction.MarkMitigated }]));

        Assert.Single(mappings);
    }

    [Fact]
    public async Task AMappingWithNoStatusIsABadRequest()
    {
        Assert.IsType<BadRequestObjectResult>(
            (await Controller<IssueTrackersController>().SetStatusMappings(
                MockedIssueTrackerService.KnownConnectionId,
                [new IssueStatusMapping { ExternalStatus = "  " }])).Result);
    }

    [Fact]
    public async Task PollingAConnectionReturnsWhatItDid()
    {
        var result = Ok(await Controller<IssueTrackersController>()
            .Sync(MockedIssueTrackerService.KnownConnectionId));

        Assert.Equal(3, result.Examined);
        Assert.Equal(1, result.Applied);
    }

    [Fact]
    public async Task TheConflictQueueIsReadableAndResolvable()
    {
        var conflicts = Ok(await Controller<IssueTrackersController>().GetConflicts());

        var conflict = Assert.Single(conflicts);
        Assert.True(conflict.HasConflict);

        Assert.NotNull(Ok(await Controller<IssueTrackersController>()
            .ResolveConflict(MockedIssueTrackerService.KnownLinkId)));
    }

    // --- 4.2.2 finding ↔ issue links ---------------------------------------------------------

    [Fact]
    public async Task AFindingsLinksAreListed()
    {
        Assert.Single(Ok(await Controller<FindingIssuesController>()
            .GetForFinding(MockedIssueTrackerService.KnownFindingId)));
    }

    [Fact]
    public async Task ThePreviewRendersWithoutCreatingAnything()
    {
        var draft = Ok(await Controller<FindingIssuesController>().Preview(
            MockedIssueTrackerService.KnownConnectionId, MockedIssueTrackerService.KnownFindingId));

        Assert.Contains("Critical", draft.Title);
    }

    [Fact]
    public async Task CreatingAnIssueAnswers201()
    {
        var link = Created(await Controller<FindingIssuesController>().Create(new CreateIssueRequest
        {
            ConnectionId = MockedIssueTrackerService.KnownConnectionId,
            FindingId = MockedIssueTrackerService.KnownFindingId
        }));

        Assert.Equal("SEC-1", link.IssueKey);
    }

    [Fact]
    public async Task ATrackerRefusalIsA502NotA500()
    {
        var result = await Controller<FindingIssuesController>().Create(new CreateIssueRequest
        {
            ConnectionId = MockedIssueTrackerService.KnownConnectionId, FindingId = 500
        });

        // The failure is upstream; a 500 would point the operator at NetRisk.
        var status = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(502, status.StatusCode);
        Assert.Contains("Jira", status.Value!.ToString());
    }

    [Fact]
    public async Task CreatingAnIssueForAnUnknownFindingIsNotFound()
    {
        Assert.IsType<NotFoundObjectResult>((await Controller<FindingIssuesController>()
            .Create(new CreateIssueRequest
            {
                ConnectionId = MockedIssueTrackerService.KnownConnectionId, FindingId = 404
            })).Result);
    }

    [Fact]
    public async Task LinkingWithNoKeyIsABadRequest()
    {
        Assert.IsType<BadRequestObjectResult>((await Controller<FindingIssuesController>()
            .LinkExisting(new LinkIssueRequest
            {
                ConnectionId = MockedIssueTrackerService.KnownConnectionId,
                FindingId = MockedIssueTrackerService.KnownFindingId
            })).Result);
    }

    [Fact]
    public async Task UnlinkingAnswers204AndAnUnknownLinkIsNotFound()
    {
        Assert.IsType<NoContentResult>(await Controller<FindingIssuesController>()
            .Unlink(MockedIssueTrackerService.KnownLinkId));

        Assert.IsType<NotFoundObjectResult>(await Controller<FindingIssuesController>().Unlink(404));
    }

    [Fact]
    public async Task ATriageDecisionIsPushedOntoTheLinkedIssues()
    {
        var trackers = MockedIssueTrackerService.Create();

        var controller = ResolveController<VulnerabilitiesController>(
            services => services.AddSingleton(trackers));

        // Finding 1 is the one the shared lifecycle mock knows how to transition.
        var result = await controller.UpdateLifecycleStatus(1, new FindingStatusChangeRequest
        {
            Status = FindingStatus.Mitigated, Justification = "Verified by re-scan."
        });

        Assert.IsType<OkObjectResult>(result.Result);

        // The outbound half of 4.2.3 fires from the transition endpoint rather than from the lifecycle
        // service, because the issue-tracker service already depends on that service to apply an
        // inbound transition and the reverse dependency would be a constructor cycle.
        await trackers.Received(1).PushFindingTransitionAsync(1, FindingStatus.Mitigated,
            "Verified by re-scan.");
    }

    // --- 4.2.3 the webhook receiver ----------------------------------------------------------

    [Fact]
    public async Task AWebhookWithTheWrongSecretIsUnauthorized()
    {
        var controller = WebhookController();

        // An unauthenticated caller must not be able to close findings.
        Assert.IsType<UnauthorizedResult>(await controller.Receive(
            MockedIssueTrackerService.KnownConnectionId, "wrong"));
    }

    [Fact]
    public async Task AWebhookWithTheRightSecretAnswers204()
    {
        var controller = WebhookController();

        Assert.IsType<NoContentResult>(await controller.Receive(
            MockedIssueTrackerService.KnownConnectionId, "correct-secret"));
    }

    [Fact]
    public async Task AWebhookForAnUnknownConnectionIsNotFound()
    {
        Assert.IsType<NotFoundResult>(await WebhookController().Receive(404, "correct-secret"));
    }

    /// <summary>
    /// The receiver reads the raw request body, so it needs an HTTP context with one — the shared mock
    /// accessor has no body stream.
    /// </summary>
    private static IssueSyncWebhooksController WebhookController()
    {
        var controller = Controller<IssueSyncWebhooksController>();

        var context = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        context.Request.Body = new System.IO.MemoryStream(
            System.Text.Encoding.UTF8.GetBytes("""{"issue":{"key":"SEC-1"}}"""));

        controller.ControllerContext = new ControllerContext { HttpContext = context };

        return controller;
    }

    // --- 4.3.1 identity providers ------------------------------------------------------------

    [Fact]
    public async Task ProvidersAreListedWithoutTheirClientSecret()
    {
        var provider = Assert.Single(Ok(await Controller<IdentityProvidersController>().GetAll()));

        Assert.True(provider.HasClientSecret);
        Assert.Null(typeof(IdentityProviderView).GetProperty("ClientSecret"));
    }

    [Fact]
    public async Task TheAnonymousSignInListCarriesOnlyANameAndAProtocol()
    {
        var providers = Ok(await Controller<IdentityProvidersController>().GetAvailable());

        var provider = Assert.Single(providers);

        Assert.Equal("Acme Entra ID", provider.Name);
        // Configuration is not something an anonymous caller enumerates.
        Assert.Null(provider.Authority);
    }

    [Fact]
    public async Task CreatingAProviderAnswers201()
    {
        var created = Created(await Controller<IdentityProvidersController>().Create(
            new IdentityProviderRequest
            {
                Provider = new IdentityProvider
                {
                    Name = "Okta", Protocol = IdentityProviderProtocol.Oidc,
                    Authority = "https://acme.okta.com", ClientId = "netrisk"
                },
                ClientSecret = "s"
            }));

        Assert.Equal(42, created.Id);
    }

    [Fact]
    public async Task AProviderWithNoNameIsABadRequest()
    {
        Assert.IsType<BadRequestObjectResult>((await Controller<IdentityProvidersController>()
            .Create(new IdentityProviderRequest { Provider = new IdentityProvider() })).Result);
    }

    [Fact]
    public async Task BeginningASignInReturnsTheAuthorizationUrl()
    {
        var request = Ok(await Controller<IdentityProvidersController>().BeginOidc(
            MockedIdentityProvidersService.KnownProviderId,
            new BeginSignInRequest { RedirectUri = "http://127.0.0.1:51789/callback" }));

        Assert.Contains("response_type=code", request.AuthorizationUrl);
        Assert.NotEmpty(request.State);
    }

    [Fact]
    public async Task ANonLoopbackRedirectIsABadRequest()
    {
        // Without this the endpoint is an open redirector for collecting authorization codes.
        Assert.IsType<BadRequestObjectResult>((await Controller<IdentityProvidersController>()
            .BeginOidc(MockedIdentityProvidersService.KnownProviderId,
                new BeginSignInRequest { RedirectUri = "https://attacker.example/collect" })).Result);
    }

    [Fact]
    public async Task CompletingASignInReturnsTheResolvedAccount()
    {
        var result = Ok(await Controller<IdentityProvidersController>().CompleteOidc(
            new OidcCallbackRequest { State = "state-1", Code = "auth-code" }));

        Assert.True(result.Success);
        Assert.Equal(1, result.UserId);
    }

    [Fact]
    public async Task AReplayedStateIsAnUnsuccessfulResultRatherThanAnError()
    {
        // The refusal is a value, not an exception: the client shows a different message for each
        // distinguishable reason, and a 500 carries none of them.
        var result = Ok(await Controller<IdentityProvidersController>().CompleteOidc(
            new OidcCallbackRequest { State = "replayed", Code = "auth-code" }));

        Assert.False(result.Success);
        Assert.Contains("no longer valid", result.Error!);
    }

    [Fact]
    public async Task TheAssertionConsumerAcceptsAFormPost()
    {
        var result = Ok(await Controller<IdentityProvidersController>().AssertionConsumer(
            MockedIdentityProvidersService.KnownProviderId, "base64-response", "relay-1"));

        Assert.True(result.Success);
    }

    [Fact]
    public async Task TheSpMetadataIsServedAsXml()
    {
        var result = await Controller<IdentityProvidersController>()
            .GetServiceProviderMetadata(MockedIdentityProvidersService.KnownProviderId);

        var content = Assert.IsType<ContentResult>(result);

        Assert.Equal("application/xml", content.ContentType);
        Assert.Contains("EntityDescriptor", content.Content!);
    }

    // --- 4.3.2 SCIM --------------------------------------------------------------------------

    /// <summary>
    /// The SCIM controller reads <c>Request.Method</c> and <c>Request.Path</c> for its audit row, so it
    /// needs a real HTTP context — the shared mock accessor is only what <c>ApiBaseController</c> uses,
    /// and this controller does not derive from it.
    /// </summary>
    private static ScimController Scim()
    {
        var controller = Controller<ScimController>();

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext()
        };

        return controller;
    }

    [Fact]
    public async Task ScimListsUsersAsAListResponse()
    {
        var result = await Scim().ListUsers(null);

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<ScimListResponse<ScimUser>>(ok.Value);

        Assert.Equal(1, list.TotalResults);
        Assert.Contains(ScimSchemas.ListResponse, list.Schemas);
    }

    [Fact]
    public async Task AnUnsupportedScimFilterIsA400WithScimTypeInvalidFilter()
    {
        var result = await Scim().ListUsers("""title eq "CISO" """);

        var status = Assert.IsType<ObjectResult>(result);
        var error = Assert.IsType<ScimError>(status.Value);

        // A SCIM client parses the error body, not just the status.
        Assert.Equal(400, status.StatusCode);
        Assert.Equal("invalidFilter", error.ScimType);
    }

    [Fact]
    public async Task ADuplicateUserNameIsA409WithScimTypeUniqueness()
    {
        var result = await Scim()
            .CreateUser(new ScimUser { UserName = "alice@acme.com" });

        var status = Assert.IsType<ObjectResult>(result);
        var error = Assert.IsType<ScimError>(status.Value);

        // A conflict is how an IdP learns to switch from create to PATCH; a generic 400 makes it retry
        // the create forever.
        Assert.Equal(409, status.StatusCode);
        Assert.Equal("uniqueness", error.ScimType);
    }

    [Fact]
    public async Task CreatingAUserAnswers201WithALocationHeader()
    {
        var controller = Scim();

        await controller.CreateUser(new ScimUser { UserName = "carol@acme.com" });

        Assert.Equal("/scim/v2/Users/99", controller.Response.Headers.Location);
    }

    [Fact]
    public async Task PatchingActiveToFalseIsTheDeprovisionPath()
    {
        var patch = new ScimPatchRequest
        {
            Operations =
            [
                new ScimPatchOperation
                {
                    Op = "replace", Path = "active",
                    Value = System.Text.Json.JsonSerializer.Deserialize<System.Text.Json.JsonElement>("false")
                }
            ]
        };

        var result = await Scim().PatchUser(MockedScimService.KnownUserId, patch);

        var ok = Assert.IsType<OkObjectResult>(result);
        var user = Assert.IsType<ScimUser>(ok.Value);

        Assert.False(user.Active);
    }

    [Fact]
    public async Task AnUnknownScimUserIsA404WithAScimErrorBody()
    {
        var result = await Scim().GetUser("404");

        var notFound = Assert.IsType<NotFoundObjectResult>(result);

        Assert.IsType<ScimError>(notFound.Value);
    }

    [Fact]
    public async Task ScimDeleteAnswers204()
    {
        Assert.IsType<NoContentResult>(
            await Scim().DeleteUser(MockedScimService.KnownUserId));
    }

    [Fact]
    public void TheServiceProviderConfigDeclaresPatchSupport()
    {
        var result = Scim().GetServiceProviderConfig();

        var ok = Assert.IsType<OkObjectResult>(result);

        // An IdP that cannot read this sometimes refuses to configure at all.
        Assert.Contains("patch", ok.Value!.ToString(), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GroupsAreListedAndPatchable()
    {
        Assert.IsType<OkObjectResult>(await Scim().ListGroups(null));
        Assert.IsType<OkObjectResult>(await Scim().GetGroup("2"));
    }

    // --- 4.3.2 provisioning tokens -----------------------------------------------------------

    [Fact]
    public async Task IssuingAProvisioningTokenReturnsTheSecretExactlyOnce()
    {
        var issued = Created(await Controller<ScimTokensController>()
            .Issue(new IssueScimTokenRequest { Name = "Entra ID provisioning" }));

        Assert.NotNull(issued.Secret);

        // Every read path leaves it null, which is what makes the credential write-only.
        var listed = Assert.Single(Ok(await Controller<ScimTokensController>().GetAll()));
        Assert.Null(listed.Secret);
    }

    [Fact]
    public async Task IssuingWithoutANameIsABadRequest()
    {
        Assert.IsType<BadRequestObjectResult>(
            (await Controller<ScimTokensController>().Issue(new IssueScimTokenRequest())).Result);
    }

    [Fact]
    public async Task RevokingATokenReturnsItWithARevocationDate()
    {
        var revoked = Ok(await Controller<ScimTokensController>()
            .Revoke(MockedScimService.KnownTokenId));

        Assert.NotNull(revoked.RevokedAt);
    }

    [Fact]
    public async Task TheScimAuditIsReadable()
    {
        var log = Assert.Single(Ok(await Controller<ScimTokensController>().GetLog()));

        Assert.Equal("PATCH", log.Method);
    }

    // --- 4.3.3 WebAuthn ----------------------------------------------------------------------

    [Fact]
    public async Task RegistrationOptionsAreReturnedForTheCallingUser()
    {
        var options = Ok(await Controller<WebAuthnController>()
            .BeginRegistration(new BeginRegistrationRequest { Name = "YubiKey 5C" }));

        Assert.NotEmpty(options.CeremonyId);
        Assert.Contains("challenge", options.OptionsJson);
    }

    [Fact]
    public async Task CompletingARegistrationReturnsTheCredential()
    {
        var result = Ok(await Controller<WebAuthnController>().CompleteRegistration(
            new CompleteCeremonyRequest { CeremonyId = "ceremony-1", Response = "{}" }));

        Assert.True(result.Success);
        Assert.Equal("YubiKey 5C", result.Credential!.Name);
    }

    [Fact]
    public async Task AnExpiredCeremonyIsAnUnsuccessfulResult()
    {
        var result = Ok(await Controller<WebAuthnController>().CompleteRegistration(
            new CompleteCeremonyRequest { CeremonyId = "stale", Response = "{}" }));

        Assert.False(result.Success);
    }

    [Fact]
    public async Task AnAssertionCeremonyForAnAccountWithNoAuthenticatorIsABadRequest()
    {
        Assert.IsType<BadRequestObjectResult>((await Controller<WebAuthnController>()
            .BeginAssertion(new BeginAssertionRequest { UserId = 404 })).Result);
    }

    [Fact]
    public async Task CompletingAnAssertionReturnsTheUser()
    {
        var result = Ok(await Controller<WebAuthnController>().CompleteAssertion(
            new CompleteCeremonyRequest { CeremonyId = "assert-1", Response = "{}" }));

        Assert.True(result.Success);
        Assert.Equal(1, result.UserId);
    }

    [Fact]
    public async Task RecoveryCodesAreReturnedOnceAndRedeemedOnce()
    {
        var batch = Ok(await Controller<WebAuthnController>().GenerateRecoveryCodes(1));

        Assert.Equal(2, batch.Codes.Count);

        Assert.True(Ok(await Controller<WebAuthnController>()
            .RedeemRecoveryCode(new RedeemRecoveryCodeRequest { UserId = 1, Code = "ABCDE-12345" })));

        Assert.False(Ok(await Controller<WebAuthnController>()
            .RedeemRecoveryCode(new RedeemRecoveryCodeRequest { UserId = 1, Code = "WRONG-00000" })));
    }

    [Fact]
    public async Task RecoveryCodesForAnUnknownUserAreNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(
            (await Controller<WebAuthnController>().GenerateRecoveryCodes(404)).Result);
    }

    [Fact]
    public async Task TheHardwareFactorStatusIsReadable()
    {
        var status = Ok(await Controller<WebAuthnController>().GetStatus());

        Assert.True(status.Required);
        Assert.True(status.Satisfied);
    }

    [Fact]
    public async Task RevokingAnUnknownCredentialIsNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(
            (await Controller<WebAuthnController>().Revoke(404)).Result);
    }

    // --- 4.4 Trend Micro ---------------------------------------------------------------------

    [Fact]
    public void TheRegionListIsOfferedRatherThanAFreeTextHost()
    {
        var regions = OkAs(Controller<TrendMicroController>().GetRegions());

        // A key issued in one region is rejected by every other.
        Assert.Contains("eu", regions.Keys);
        Assert.Equal("https://api.eu.xdr.trendmicro.com", regions["eu"]);
    }

    [Fact]
    public async Task VisionOneConnectionsAreListedWithoutTheirKey()
    {
        var connection = Assert.Single(Ok(await Controller<TrendMicroController>().GetAll()));

        Assert.True(connection.HasApiKey);
        Assert.Null(typeof(TrendMicroConnectionView).GetProperty("ApiKey"));
    }

    [Fact]
    public async Task AnUnknownVisionOneRegionIsABadRequest()
    {
        Assert.IsType<BadRequestObjectResult>((await Controller<TrendMicroController>()
            .Create(new TrendMicroConnectionRequest
            {
                Connection = new TrendMicroConnection { Name = "x", Region = "mars", BaseUrl = "" },
                ApiKey = "k"
            })).Result);
    }

    [Fact]
    public async Task SyncingReturnsWhatItDid()
    {
        var result = Ok(await Controller<TrendMicroController>()
            .Sync(MockedTrendMicroService.KnownConnectionId));

        Assert.Equal(3, result.HostsCreated);
        Assert.Equal(67.1, result.CyberRiskIndex);
    }

    [Fact]
    public async Task AVisionOneOutageIsA502()
    {
        var status = Assert.IsType<ObjectResult>((await Controller<TrendMicroController>().Sync(502)).Result);

        Assert.Equal(502, status.StatusCode);
    }

    [Fact]
    public async Task TheVisionOneSyncLogIsReadable()
    {
        var log = Assert.Single(Ok(await Controller<TrendMicroController>().GetLog()));

        Assert.Equal(IntegrationSyncStatus.Succeeded, log.Status);
    }

    // --- 4.5 SecurityScorecard ---------------------------------------------------------------

    [Fact]
    public void TheTenFactorNamesAreOffered()
    {
        var factors = OkAs(Controller<SecurityScorecardController>().GetFactorNames());

        // The trend chart renders a row per factor before any sync has produced one.
        Assert.Equal(10, factors.Count);
    }

    [Fact]
    public async Task ScorecardConnectionsAreListedWithoutTheirToken()
    {
        var connection = Assert.Single(Ok(await Controller<SecurityScorecardController>().GetAll()));

        Assert.True(connection.HasApiToken);
        Assert.Null(typeof(SecurityScorecardConnectionView).GetProperty("ApiToken"));
    }

    [Fact]
    public async Task ADomainThatIsAUrlIsABadRequest()
    {
        Assert.IsType<BadRequestObjectResult>((await Controller<SecurityScorecardController>()
            .Create(new SecurityScorecardConnectionRequest
            {
                Connection = new SecurityScorecardConnection { Name = "x", Domain = "https://acme.com" },
                ApiToken = "t"
            })).Result);
    }

    [Fact]
    public async Task SyncingAScorecardReturnsThePostureCounts()
    {
        var result = Ok(await Controller<SecurityScorecardController>()
            .Sync(MockedSecurityScorecardService.KnownConnectionId));

        Assert.Equal(11, result.PostureRowsWritten);
        // 88 out of 100 where higher is better becomes an index of 12 where higher is worse.
        Assert.Equal(12, result.CyberRiskIndex);
    }

    [Fact]
    public async Task TheFactorHistoryIsReadable()
    {
        var history = Assert.Single(Ok(await Controller<SecurityScorecardController>()
            .GetHistory(MockedSecurityScorecardService.KnownConnectionId)));

        Assert.Equal("patching_cadence", history.FactorName);
    }

    [Fact]
    public async Task AnUnknownScorecardConnectionIsNotFound()
    {
        Assert.IsType<NotFoundObjectResult>(
            (await Controller<SecurityScorecardController>().Get(404)).Result);
    }
}

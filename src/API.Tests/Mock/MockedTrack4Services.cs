using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using Model.Authentication.Federation;
using Model.Authentication.Scim;
using Model.Authentication.WebAuthn;
using Model.Exceptions;
using Model.Integrations;
using Model.Notifications;
using NSubstitute;
using ServerServices.Interfaces;

namespace API.Tests.Mock;

/// <summary>
/// Deterministic doubles for the Track 4 services (Integrations &amp; Notification Channels).
///
/// They return fixtures and throw the domain exceptions the controllers map onto status codes —
/// <see cref="DataNotFoundException"/>, <see cref="InvalidParameterException"/>,
/// <see cref="IntegrationRequestException"/>, <see cref="WebhookAuthenticationException"/> — because
/// what a controller test is for is the HTTP contract, and that contract is mostly which exception
/// becomes which code.
/// </summary>
public static class MockedNotificationSubscriptionsService
{
    public const int KnownChannelId = 1;

    public const int KnownSubscriptionId = 10;

    public static INotificationSubscriptionsService Create()
    {
        var service = Substitute.For<INotificationSubscriptionsService>();

        service.GetChannelsAsync(Arg.Any<bool>()).Returns(Task.FromResult(new List<NotificationChannel>
        {
            Channel(KnownChannelId, "SOC Slack", NotificationChannelKind.Slack),
            Channel(2, "Fallback mail", NotificationChannelKind.Email)
        }));

        service.GetChannelAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);
            if (id != KnownChannelId)
                throw new DataNotFoundException("notification_channels", id.ToString(),
                    new Exception($"Notification channel {id} was not found."));

            return Task.FromResult(Channel(id, "SOC Slack", NotificationChannelKind.Slack));
        });

        service.CreateChannelAsync(Arg.Any<NotificationChannel>(), Arg.Any<int?>()).Returns(call =>
        {
            var channel = call.ArgAt<NotificationChannel>(0);

            if (string.IsNullOrWhiteSpace(channel.Name))
                throw new InvalidParameterException(nameof(channel.Name), "A channel requires a name.");

            channel.Id = 99;
            return Task.FromResult(channel);
        });

        service.UpdateChannelAsync(Arg.Any<NotificationChannel>(), Arg.Any<int?>()).Returns(call =>
        {
            var channel = call.ArgAt<NotificationChannel>(0);

            if (channel.Id != KnownChannelId)
                throw new DataNotFoundException("notification_channels", channel.Id.ToString(),
                    new Exception("Not found."));

            return Task.FromResult(channel);
        });

        service.DeleteChannelAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);

            if (id == 2)
                throw new InvalidParameterException("id", "1 channel(s) fall back to this one.");

            if (id != KnownChannelId)
                throw new DataNotFoundException("notification_channels", id.ToString(),
                    new Exception("Not found."));

            return Task.CompletedTask;
        });

        service.GetSubscriptionsAsync().Returns(Task.FromResult(new List<NotificationSubscription>
        {
            new()
            {
                Id = KnownSubscriptionId, EventType = NotificationEventType.RiskCreated,
                ChannelId = KnownChannelId, MinSeverity = 4, Enabled = true
            }
        }));

        service.CreateSubscriptionAsync(Arg.Any<NotificationSubscription>()).Returns(call =>
        {
            var subscription = call.ArgAt<NotificationSubscription>(0);

            if (subscription.ChannelId == 0)
                throw new InvalidParameterException(nameof(subscription.ChannelId),
                    "Notification channel 0 was not found.");

            subscription.Id = 77;
            return Task.FromResult(subscription);
        });

        service.UpdateSubscriptionAsync(Arg.Any<NotificationSubscription>()).Returns(call =>
        {
            var subscription = call.ArgAt<NotificationSubscription>(0);

            if (subscription.Id != KnownSubscriptionId)
                throw new DataNotFoundException("notification_subscriptions", subscription.Id.ToString(),
                    new Exception("Not found."));

            return Task.FromResult(subscription);
        });

        service.DeleteSubscriptionAsync(Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == KnownSubscriptionId
                ? Task.CompletedTask
                : throw new DataNotFoundException("notification_subscriptions",
                    call.ArgAt<int>(0).ToString(), new Exception("Not found.")));

        service.GetDeliveriesAsync(Arg.Any<int>(), Arg.Any<NotificationDeliveryStatus?>(), Arg.Any<int?>())
            .Returns(Task.FromResult(new List<NotificationDelivery>
            {
                new()
                {
                    Id = 5, EventType = NotificationEventType.SlaBreached,
                    Status = NotificationDeliveryStatus.Failed, Attempts = 3,
                    Title = "SLA breached: SQL injection", LastError = "HTTP 404",
                    CreatedAt = DateTime.UtcNow
                }
            }));

        service.RequeueDeliveryAsync(Arg.Any<int>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);

            if (id == 6)
                throw new InvalidParameterException("id",
                    "This notification was already delivered; re-sending it would duplicate the alert.");

            if (id != 5)
                throw new DataNotFoundException("notification_deliveries", id.ToString(),
                    new Exception("Not found."));

            return Task.FromResult(new NotificationDelivery
            {
                Id = 5, Status = NotificationDeliveryStatus.Pending, Attempts = 0
            });
        });

        return service;
    }

    private static NotificationChannel Channel(int id, string name, NotificationChannelKind kind) => new()
    {
        Id = id,
        Name = name,
        Kind = kind,
        Enabled = true,
        // Redacted, exactly as the real service returns it: a client is never handed a stored secret.
        ConfigurationJson = new ChannelConfiguration
        {
            WebhookUrl = ChannelConfiguration.RedactedPlaceholder
        }.ToJson(),
        CreatedAt = DateTime.UtcNow
    };
}

/// <summary>Dispatcher double: the channel test button and the sweep.</summary>
public static class MockedNotificationDispatcher
{
    public static INotificationDispatcher Create()
    {
        var dispatcher = Substitute.For<INotificationDispatcher>();

        dispatcher.TestChannelAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);

            if (id != MockedNotificationSubscriptionsService.KnownChannelId)
                throw new DataNotFoundException("notification_channels", id.ToString(),
                    new Exception("Not found."));

            return Task.FromResult(ChannelTestResult.Ok("Test message delivered.", 42));
        });

        dispatcher.DispatchAsync(Arg.Any<NotificationMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<NotificationDelivery>()));

        dispatcher.ProcessPendingAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DispatchSweepResult { Delivered = 1 }));

        return dispatcher;
    }
}

/// <summary>Registry double: two providers, so the picker endpoint has something to return.</summary>
public static class MockedNotificationChannelRegistry
{
    public static INotificationChannelRegistry Create()
    {
        var registry = Substitute.For<INotificationChannelRegistry>();

        var slack = Substitute.For<INotificationChannel>();
        slack.Kind.Returns(NotificationChannelKind.Slack);
        slack.Name.Returns("Slack");

        var email = Substitute.For<INotificationChannel>();
        email.Kind.Returns(NotificationChannelKind.Email);
        email.Name.Returns("Email");

        registry.All.Returns(new List<INotificationChannel> { email, slack });
        registry.For(Arg.Any<NotificationChannelKind>()).Returns(slack);

        return registry;
    }
}

/// <summary>Issue-tracker double covering connections, links and the sync surface.</summary>
public static class MockedIssueTrackerService
{
    public const int KnownConnectionId = 1;

    public const int KnownFindingId = 42;

    public const int KnownLinkId = 7;

    public static IIssueTrackerService Create()
    {
        var service = Substitute.For<IIssueTrackerService>();

        service.GetProviders().Returns(new List<(IssueTrackerProviderKind, string, IssueTrackerCapabilities)>
        {
            (IssueTrackerProviderKind.Jira, "Jira", new IssueTrackerCapabilities
            {
                SupportsWebhooks = true, SupportsComments = true, SupportsTransitions = true,
                SupportsLabels = true, SupportsPriority = true
            }),
            (IssueTrackerProviderKind.GitHub, "GitHub Issues", new IssueTrackerCapabilities
            {
                SupportsWebhooks = true, SupportsComments = true, SupportsTransitions = true,
                SupportsLabels = true, SupportsPriority = false
            })
        });

        service.GetConnectionsAsync(Arg.Any<bool>())
            .Returns(Task.FromResult(new List<IssueTrackerConnectionView> { Connection() }));

        service.GetConnectionAsync(Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == KnownConnectionId
                ? Task.FromResult(Connection())
                : throw new DataNotFoundException("issue_tracker_connections",
                    call.ArgAt<int>(0).ToString(), new Exception("Not found.")));

        service.CreateConnectionAsync(Arg.Any<IssueTrackerConnection>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<int?>())
            .Returns(call =>
            {
                var connection = call.ArgAt<IssueTrackerConnection>(0);

                if (string.IsNullOrWhiteSpace(connection.BaseUrl))
                    throw new InvalidParameterException(nameof(connection.BaseUrl),
                        "The base URL must be an absolute http or https URL.");

                return Task.FromResult(Connection(id: 99, name: connection.Name));
            });

        service.UpdateConnectionAsync(Arg.Any<IssueTrackerConnection>(), Arg.Any<string?>(),
                Arg.Any<string?>(), Arg.Any<int?>())
            .Returns(call =>
            {
                var connection = call.ArgAt<IssueTrackerConnection>(0);

                if (connection.Id != KnownConnectionId)
                    throw new DataNotFoundException("issue_tracker_connections",
                        connection.Id.ToString(), new Exception("Not found."));

                return Task.FromResult(Connection(name: connection.Name));
            });

        service.DeleteConnectionAsync(Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == KnownConnectionId
                ? Task.CompletedTask
                : throw new DataNotFoundException("issue_tracker_connections",
                    call.ArgAt<int>(0).ToString(), new Exception("Not found.")));

        service.TestConnectionAsync(Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == KnownConnectionId
                ? Task.FromResult(ConnectionTestResult.Ok("Connected to Jira."))
                : throw new DataNotFoundException("issue_tracker_connections",
                    call.ArgAt<int>(0).ToString(), new Exception("Not found.")));

        service.GetStatusMappingsAsync(Arg.Any<int>())
            .Returns(Task.FromResult(new List<IssueStatusMappingView>
            {
                new() { Id = 1, ExternalStatus = "Done", Action = IssueSyncAction.MarkMitigated }
            }));

        service.SetStatusMappingsAsync(Arg.Any<int>(), Arg.Any<IReadOnlyList<IssueStatusMapping>>())
            .Returns(call =>
            {
                var mappings = call.ArgAt<IReadOnlyList<IssueStatusMapping>>(1);

                if (mappings.Any(m => string.IsNullOrWhiteSpace(m.ExternalStatus)))
                    throw new InvalidParameterException("mappings",
                        "A status mapping needs the tracker's status name.");

                return Task.FromResult(mappings.Select(m => new IssueStatusMappingView
                {
                    ExternalStatus = m.ExternalStatus, Action = m.Action
                }).ToList());
            });

        service.GetLinksForFindingAsync(Arg.Any<int>()).Returns(call =>
            Task.FromResult(call.ArgAt<int>(0) == KnownFindingId
                ? new List<FindingIssueLinkView> { Link() }
                : new List<FindingIssueLinkView>()));

        service.PreviewAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(1) == KnownFindingId
                ? Task.FromResult(new IssueDraft
                {
                    Title = "[Critical] SQL injection", Description = "…", Priority = "Highest",
                    FindingId = KnownFindingId
                })
                : throw new DataNotFoundException("vulnerabilities", call.ArgAt<int>(1).ToString(),
                    new Exception("Not found.")));

        service.CreateIssueAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<int?>()).Returns(call =>
        {
            var findingId = call.ArgAt<int>(1);

            if (findingId == 500)
                throw new IntegrationRequestException("Jira", "Jira refused to create the issue (HTTP 400).");

            if (findingId != KnownFindingId)
                throw new DataNotFoundException("vulnerabilities", findingId.ToString(),
                    new Exception("Not found."));

            return Task.FromResult(Link());
        });

        service.CreateIssuesAsync(Arg.Any<int>(), Arg.Any<IReadOnlyList<int>>(), Arg.Any<int?>())
            .Returns(Task.FromResult(new List<FindingIssueLinkView> { Link() }));

        service.LinkExistingAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<string>(), Arg.Any<int?>())
            .Returns(call =>
            {
                var key = call.ArgAt<string>(2);

                if (string.IsNullOrWhiteSpace(key))
                    throw new InvalidParameterException("issueKeyOrUrl", "An issue key or URL is required.");

                return Task.FromResult(Link(key));
            });

        service.UnlinkAsync(Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == KnownLinkId
                ? Task.CompletedTask
                : throw new DataNotFoundException("finding_issue_links", call.ArgAt<int>(0).ToString(),
                    new Exception("Not found.")));

        service.PollConnectionAsync(Arg.Any<int>(), Arg.Any<int?>()).Returns(call =>
            call.ArgAt<int>(0) == KnownConnectionId
                ? Task.FromResult(new IssueSyncResult { Examined = 3, Changed = 1, Applied = 1 })
                : throw new DataNotFoundException("issue_tracker_connections",
                    call.ArgAt<int>(0).ToString(), new Exception("Not found.")));

        service.PushFindingTransitionAsync(Arg.Any<int>(), Arg.Any<FindingStatus>(), Arg.Any<string?>())
            .Returns(Task.FromResult(1));

        service.GetConflictsAsync().Returns(Task.FromResult(new List<FindingIssueLinkView>
        {
            new()
            {
                Id = KnownLinkId, FindingId = KnownFindingId, ConnectionId = KnownConnectionId,
                ConnectionName = "Security Jira", IssueKey = "SEC-1", HasConflict = true,
                ConflictDetail = "NetRisk had the finding as FalsePositive."
            }
        }));

        service.ResolveConflictAsync(Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == KnownLinkId
                ? Task.FromResult(Link())
                : throw new DataNotFoundException("finding_issue_links", call.ArgAt<int>(0).ToString(),
                    new Exception("Not found.")));

        service.ApplyWebhookAsync(Arg.Any<int>(), Arg.Any<string>(),
                Arg.Any<IReadOnlyDictionary<string, string>>(), Arg.Any<string?>())
            .Returns(call =>
            {
                var connectionId = call.ArgAt<int>(0);
                var secret = call.ArgAt<string?>(3);

                if (connectionId != KnownConnectionId)
                    throw new DataNotFoundException("issue_tracker_connections", connectionId.ToString(),
                        new Exception("Not found."));

                if (secret != "correct-secret") throw new WebhookAuthenticationException("Jira");

                return Task.FromResult(new IssueSyncResult { Examined = 1, Applied = 1 });
            });

        return service;
    }

    private static IssueTrackerConnectionView Connection(int id = KnownConnectionId,
        string name = "Security Jira") => new()
    {
        Id = id,
        Name = name,
        Provider = IssueTrackerProviderKind.Jira,
        BaseUrl = "https://acme.atlassian.net",
        ProjectKey = "SEC",
        // Flags, never the credentials themselves.
        HasToken = true,
        HasWebhookSecret = true,
        Enabled = true,
        PushFindingUpdates = true,
        PollIntervalMinutes = 15
    };

    private static FindingIssueLinkView Link(string issueKey = "SEC-1") => new()
    {
        Id = KnownLinkId,
        FindingId = KnownFindingId,
        ConnectionId = KnownConnectionId,
        ConnectionName = "Security Jira",
        Provider = IssueTrackerProviderKind.Jira,
        IssueKey = issueKey,
        IssueUrl = $"https://acme.atlassian.net/browse/{issueKey}"
    };
}

/// <summary>Identity-provider double, including the OIDC and SAML sign-in flows.</summary>
public static class MockedIdentityProvidersService
{
    public const int KnownProviderId = 1;

    public static IIdentityProvidersService Create()
    {
        var service = Substitute.For<IIdentityProvidersService>();

        service.GetProvidersAsync(Arg.Any<bool>())
            .Returns(Task.FromResult(new List<IdentityProviderView> { Provider() }));

        service.GetEnabledForSignInAsync().Returns(Task.FromResult(new List<IdentityProviderView>
        {
            new() { Id = KnownProviderId, Name = "Acme Entra ID", Protocol = IdentityProviderProtocol.Oidc,
                Enabled = true }
        }));

        service.GetProviderAsync(Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == KnownProviderId
                ? Task.FromResult(Provider())
                : throw new DataNotFoundException("identity_providers", call.ArgAt<int>(0).ToString(),
                    new Exception("Not found.")));

        service.CreateProviderAsync(Arg.Any<IdentityProvider>(), Arg.Any<string?>()).Returns(call =>
        {
            var provider = call.ArgAt<IdentityProvider>(0);

            if (string.IsNullOrWhiteSpace(provider.Name))
                throw new InvalidParameterException(nameof(provider.Name), "A provider requires a name.");

            return Task.FromResult(Provider(id: 42, name: provider.Name));
        });

        service.UpdateProviderAsync(Arg.Any<IdentityProvider>(), Arg.Any<string?>()).Returns(call =>
        {
            var provider = call.ArgAt<IdentityProvider>(0);

            if (provider.Id != KnownProviderId)
                throw new DataNotFoundException("identity_providers", provider.Id.ToString(),
                    new Exception("Not found."));

            return Task.FromResult(Provider(name: provider.Name));
        });

        service.DeleteProviderAsync(Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == KnownProviderId
                ? Task.CompletedTask
                : throw new DataNotFoundException("identity_providers", call.ArgAt<int>(0).ToString(),
                    new Exception("Not found.")));

        service.TestProviderAsync(Arg.Any<int>())
            .Returns(Task.FromResult(ConnectionTestResult.Ok("Read the discovery document.")));

        service.BeginOidcSignInAsync(Arg.Any<int>(), Arg.Any<string>()).Returns(call =>
        {
            var redirect = call.ArgAt<string>(1);

            if (!redirect.StartsWith("http://127.0.0.1", StringComparison.Ordinal))
                throw new InvalidParameterException("redirectUri",
                    "The redirect URI must be a loopback address.");

            return Task.FromResult(new FederatedSignInRequest
            {
                ProviderId = KnownProviderId,
                AuthorizationUrl = "https://login.acme.com/authorize?response_type=code",
                State = "state-1",
                RedirectUri = redirect,
                ExpiresInSeconds = 600
            });
        });

        service.CompleteOidcSignInAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(call =>
            call.ArgAt<string>(0) == "state-1"
                ? Task.FromResult(FederatedSignInResult.Ok(1, "alice@acme.com",
                    new FederatedIdentity { Subject = "alice@acme.com", Email = "alice@acme.com" }))
                : Task.FromResult(FederatedSignInResult.Fail("This sign-in is no longer valid.")));

        service.BeginSamlSignInAsync(Arg.Any<int>(), Arg.Any<string?>())
            .Returns(Task.FromResult(new FederatedSignInRequest
            {
                ProviderId = KnownProviderId,
                AuthorizationUrl = "https://idp.acme.com/sso?SAMLRequest=x",
                State = "relay-1",
                ExpiresInSeconds = 600
            }));

        service.CompleteSamlSignInAsync(Arg.Any<string>(), Arg.Any<string?>()).Returns(call =>
            string.IsNullOrWhiteSpace(call.ArgAt<string>(0))
                ? Task.FromResult(FederatedSignInResult.Fail("The SAML response was empty."))
                : Task.FromResult(FederatedSignInResult.Ok(1, "alice@acme.com",
                    new FederatedIdentity { Subject = "alice@acme.com" })));

        service.GetServiceProviderMetadataAsync(Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == KnownProviderId
                ? Task.FromResult("<EntityDescriptor entityID=\"https://netrisk.acme.com/saml/metadata\"/>")
                : throw new DataNotFoundException("identity_providers", call.ArgAt<int>(0).ToString(),
                    new Exception("Not found.")));

        return service;
    }

    private static IdentityProviderView Provider(int id = KnownProviderId, string name = "Acme Entra ID")
        => new()
        {
            Id = id,
            Name = name,
            Protocol = IdentityProviderProtocol.Oidc,
            Enabled = true,
            Authority = "https://login.acme.com",
            ClientId = "netrisk-desktop",
            // A flag, never the secret.
            HasClientSecret = true,
            ClockSkewSeconds = 120
        };
}

/// <summary>SCIM double covering the resource operations, the tokens and the audit.</summary>
public static class MockedScimService
{
    public const string KnownUserId = "1";

    public const int KnownTokenId = 1;

    public static IScimService Create()
    {
        var service = Substitute.For<IScimService>();

        service.ListUsersAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
        {
            var filter = call.ArgAt<string?>(0);

            if (filter != null && filter.Contains("title", StringComparison.OrdinalIgnoreCase))
                throw new InvalidParameterException("filter", "Filtering on 'title' is not supported.");

            return Task.FromResult(new ScimListResponse<ScimUser>
            {
                TotalResults = 1, StartIndex = 1, ItemsPerPage = 1, Resources = [User()]
            });
        });

        service.GetUserAsync(Arg.Any<string>()).Returns(call =>
            call.ArgAt<string>(0) == KnownUserId
                ? Task.FromResult(User())
                : throw new DataNotFoundException("user", call.ArgAt<string>(0),
                    new Exception("Not found.")));

        service.CreateUserAsync(Arg.Any<ScimUser>()).Returns(call =>
        {
            var user = call.ArgAt<ScimUser>(0);

            if (user.UserName == "alice@acme.com")
                throw new InvalidParameterException(nameof(user.UserName),
                    $"A user with userName '{user.UserName}' already exists.");

            if (string.IsNullOrWhiteSpace(user.UserName))
                throw new InvalidParameterException(nameof(user.UserName), "userName is required.");

            user.Id = "99";
            return Task.FromResult(user);
        });

        service.ReplaceUserAsync(Arg.Any<string>(), Arg.Any<ScimUser>()).Returns(call =>
        {
            if (call.ArgAt<string>(0) != KnownUserId)
                throw new DataNotFoundException("user", call.ArgAt<string>(0), new Exception("Not found."));

            var user = call.ArgAt<ScimUser>(1);
            user.Id = KnownUserId;
            return Task.FromResult(user);
        });

        service.PatchUserAsync(Arg.Any<string>(), Arg.Any<ScimPatchRequest>()).Returns(call =>
        {
            if (call.ArgAt<string>(0) != KnownUserId)
                throw new DataNotFoundException("user", call.ArgAt<string>(0), new Exception("Not found."));

            var patch = call.ArgAt<ScimPatchRequest>(1);

            if (patch.Operations.Any(o => o.Path == "title"))
                throw new InvalidParameterException("path", "Patching 'title' is not supported.");

            var user = User();
            user.Active = false;
            return Task.FromResult(user);
        });

        service.DeactivateUserAsync(Arg.Any<string>()).Returns(call =>
            call.ArgAt<string>(0) == KnownUserId
                ? Task.CompletedTask
                : throw new DataNotFoundException("user", call.ArgAt<string>(0), new Exception("Not found.")));

        service.ListGroupsAsync(Arg.Any<string?>(), Arg.Any<int>(), Arg.Any<int>())
            .Returns(Task.FromResult(new ScimListResponse<ScimGroup>
            {
                TotalResults = 1, ItemsPerPage = 1, Resources = [Group()]
            }));

        service.GetGroupAsync(Arg.Any<string>()).Returns(call =>
            call.ArgAt<string>(0) == "2"
                ? Task.FromResult(Group())
                : throw new DataNotFoundException("role", call.ArgAt<string>(0), new Exception("Not found.")));

        service.CreateGroupAsync(Arg.Any<ScimGroup>()).Returns(call =>
        {
            var group = call.ArgAt<ScimGroup>(0);
            group.Id = "3";
            return Task.FromResult(group);
        });

        service.ReplaceGroupAsync(Arg.Any<string>(), Arg.Any<ScimGroup>())
            .Returns(call => Task.FromResult(call.ArgAt<ScimGroup>(1)));

        service.PatchGroupAsync(Arg.Any<string>(), Arg.Any<ScimPatchRequest>())
            .Returns(Task.FromResult(Group()));

        service.DeleteGroupAsync(Arg.Any<string>()).Returns(Task.CompletedTask);

        service.GetTokensAsync(Arg.Any<bool>()).Returns(Task.FromResult(new List<ScimTokenView>
        {
            new() { Id = KnownTokenId, Name = "Entra ID provisioning", KeyId = "abcd1234",
                CreatedAt = DateTime.UtcNow }
        }));

        service.IssueTokenAsync(Arg.Any<string>(), Arg.Any<int?>(), Arg.Any<int?>()).Returns(call =>
        {
            var name = call.ArgAt<string>(0);

            if (string.IsNullOrWhiteSpace(name))
                throw new InvalidParameterException(nameof(name), "A provisioning token requires a name.");

            return Task.FromResult(new ScimTokenView
            {
                Id = 2, Name = name, KeyId = "ffff0000", CreatedAt = DateTime.UtcNow,
                Secret = "scim_ffff0000_the-secret-half"
            });
        });

        service.RevokeTokenAsync(Arg.Any<int>(), Arg.Any<int?>()).Returns(call =>
            call.ArgAt<int>(0) == KnownTokenId
                ? Task.FromResult(new ScimTokenView
                {
                    Id = KnownTokenId, Name = "Entra ID provisioning", KeyId = "abcd1234",
                    RevokedAt = DateTime.UtcNow
                })
                : throw new DataNotFoundException("scim_tokens", call.ArgAt<int>(0).ToString(),
                    new Exception("Not found.")));

        service.LogRequestAsync(Arg.Any<int?>(), Arg.Any<string>(), Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<string?>(), Arg.Any<string?>()).Returns(Task.CompletedTask);

        service.GetRequestLogAsync(Arg.Any<int>()).Returns(Task.FromResult(new List<ScimRequestLog>
        {
            new()
            {
                Id = 1, Method = "PATCH", Path = "/scim/v2/Users/1", StatusCode = 200,
                Target = "alice@acme.com", Outcome = "patched user: replace active",
                OccurredAt = DateTime.UtcNow
            }
        }));

        service.AuthenticateAsync(Arg.Any<string>()).Returns(Task.FromResult<ScimToken?>(null));

        return service;
    }

    private static ScimUser User() => new()
    {
        Id = KnownUserId, UserName = "alice@acme.com", DisplayName = "Alice Adams", Active = true,
        Emails = [new ScimEmail { Value = "alice@acme.com", Primary = true }]
    };

    private static ScimGroup Group() => new()
    {
        Id = "2", DisplayName = "Security Admins",
        Members = [new ScimMember { Value = "1", Display = "Alice Adams" }]
    };
}

/// <summary>WebAuthn double covering both ceremonies, recovery codes and the policy.</summary>
public static class MockedWebAuthnService
{
    public const int KnownCredentialId = 1;

    public static IWebAuthnService Create()
    {
        var service = Substitute.For<IWebAuthnService>();

        service.GetCredentialsAsync(Arg.Any<int>(), Arg.Any<bool>())
            .Returns(Task.FromResult(new List<WebAuthnCredentialView>
            {
                new()
                {
                    Id = KnownCredentialId, UserId = 1, Name = "YubiKey 5C",
                    AttestationFormat = "none", CreatedAt = DateTime.UtcNow
                }
            }));

        service.BeginRegistrationAsync(Arg.Any<int>(), Arg.Any<string?>())
            .Returns(Task.FromResult(new WebAuthnCeremonyOptions
            {
                CeremonyId = "ceremony-1", OptionsJson = """{"challenge":"abc"}""", ExpiresInSeconds = 300
            }));

        service.CompleteRegistrationAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(call =>
            call.ArgAt<string>(0) == "ceremony-1"
                ? Task.FromResult(WebAuthnRegistrationResult.Ok(new WebAuthnCredentialView
                {
                    Id = KnownCredentialId, UserId = 1, Name = "YubiKey 5C"
                }))
                : Task.FromResult(WebAuthnRegistrationResult.Fail(
                    "This registration has expired or was already completed.")));

        service.BeginAssertionAsync(Arg.Any<int?>()).Returns(call =>
            call.ArgAt<int?>(0) == 404
                ? throw new InvalidParameterException("userId", "This account has no registered authenticator.")
                : Task.FromResult(new WebAuthnCeremonyOptions
                {
                    CeremonyId = "assert-1", OptionsJson = """{"challenge":"def"}""", ExpiresInSeconds = 300
                }));

        service.CompleteAssertionAsync(Arg.Any<string>(), Arg.Any<string>()).Returns(call =>
            call.ArgAt<string>(0) == "assert-1"
                ? Task.FromResult(WebAuthnAssertionResult.Ok(1))
                : Task.FromResult(WebAuthnAssertionResult.Fail("This sign-in has expired.")));

        service.RevokeCredentialAsync(Arg.Any<int>(), Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == KnownCredentialId
                ? Task.FromResult(new WebAuthnCredentialView
                {
                    Id = KnownCredentialId, UserId = 1, Name = "YubiKey 5C",
                    RevokedAt = DateTime.UtcNow
                })
                : throw new DataNotFoundException("webauthn_credentials", call.ArgAt<int>(0).ToString(),
                    new Exception("Not found.")));

        service.GenerateRecoveryCodesAsync(Arg.Any<int>(), Arg.Any<int?>(), Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == 1
                ? Task.FromResult(new RecoveryCodeBatch
                {
                    UserId = 1, GeneratedAt = DateTime.UtcNow,
                    Codes = ["ABCDE-12345", "FGHIJ-67890"]
                })
                : throw new DataNotFoundException("user", call.ArgAt<int>(0).ToString(),
                    new Exception("Not found.")));

        service.RedeemRecoveryCodeAsync(Arg.Any<int>(), Arg.Any<string>())
            .Returns(call => Task.FromResult(call.ArgAt<string>(1) == "ABCDE-12345"));

        service.GetHardwareFactorStatusAsync(Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == 404
                ? throw new DataNotFoundException("user", "404", new Exception("Not found."))
                : Task.FromResult(new HardwareFactorStatus
                {
                    UserId = call.ArgAt<int>(0), Required = true, RegisteredAuthenticators = 1,
                    UnusedRecoveryCodes = 2, Satisfied = true
                }));

        return service;
    }
}

/// <summary>Trend Micro double covering connections, the test utility and a sync.</summary>
public static class MockedTrendMicroService
{
    public const int KnownConnectionId = 1;

    public static ITrendMicroService Create()
    {
        var service = Substitute.For<ITrendMicroService>();

        service.GetRegions().Returns(TrendMicroRegions.BaseUrls);

        service.GetConnectionsAsync(Arg.Any<bool>())
            .Returns(Task.FromResult(new List<TrendMicroConnectionView> { Connection() }));

        service.GetConnectionAsync(Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == KnownConnectionId
                ? Task.FromResult(Connection())
                : throw new DataNotFoundException("trendmicro_connections", call.ArgAt<int>(0).ToString(),
                    new Exception("Not found.")));

        service.CreateConnectionAsync(Arg.Any<TrendMicroConnection>(), Arg.Any<string?>()).Returns(call =>
        {
            var connection = call.ArgAt<TrendMicroConnection>(0);

            if (TrendMicroRegions.BaseUrlFor(connection.Region) == null
                && string.IsNullOrWhiteSpace(connection.BaseUrl))
                throw new InvalidParameterException(nameof(connection.Region),
                    $"'{connection.Region}' is not a Vision One region.");

            return Task.FromResult(Connection(id: 99, name: connection.Name));
        });

        service.UpdateConnectionAsync(Arg.Any<TrendMicroConnection>(), Arg.Any<string?>()).Returns(call =>
        {
            var connection = call.ArgAt<TrendMicroConnection>(0);

            if (connection.Id != KnownConnectionId)
                throw new DataNotFoundException("trendmicro_connections", connection.Id.ToString(),
                    new Exception("Not found."));

            return Task.FromResult(Connection(name: connection.Name));
        });

        service.DeleteConnectionAsync(Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == KnownConnectionId
                ? Task.CompletedTask
                : throw new DataNotFoundException("trendmicro_connections", call.ArgAt<int>(0).ToString(),
                    new Exception("Not found.")));

        service.TestConnectionAsync(Arg.Any<int>())
            .Returns(Task.FromResult(ConnectionTestResult.Ok("Connected to Vision One in region 'eu'.")));

        service.SyncAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);

            if (id == 502) throw new IntegrationRequestException("Trend Micro Vision One", "HTTP 500.");

            if (id != KnownConnectionId)
                throw new DataNotFoundException("trendmicro_connections", id.ToString(),
                    new Exception("Not found."));

            return Task.FromResult(new PostureSyncResult
            {
                HostsCreated = 3, FindingsCreated = 12, CyberRiskIndex = 67.1
            });
        });

        service.SyncDueConnectionsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PostureSyncResult { HostsUpdated = 3 }));

        service.PushExemptionAsync(Arg.Any<int>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(false));

        service.GetSyncLogAsync(Arg.Any<int>()).Returns(Task.FromResult(new List<IntegrationSyncLog>
        {
            new()
            {
                Id = 1, Integration = IntegrationKind.TrendMicroVisionOne, ConnectionId = KnownConnectionId,
                ConnectionName = "Acme Vision One", StartedAt = DateTime.UtcNow,
                Status = IntegrationSyncStatus.Succeeded
            }
        }));

        return service;
    }

    private static TrendMicroConnectionView Connection(int id = KnownConnectionId,
        string name = "Acme Vision One") => new()
    {
        Id = id, Name = name, Region = "eu", BaseUrl = "https://api.eu.xdr.trendmicro.com",
        HasApiKey = true, Enabled = true, SyncIntervalHours = 24, SyncVulnerabilities = true,
        SyncRiskScores = true
    };
}

/// <summary>SecurityScorecard double covering connections, sync and the factor history.</summary>
public static class MockedSecurityScorecardService
{
    public const int KnownConnectionId = 1;

    public static ISecurityScorecardService Create()
    {
        var service = Substitute.For<ISecurityScorecardService>();

        service.GetConnectionsAsync(Arg.Any<bool>())
            .Returns(Task.FromResult(new List<SecurityScorecardConnectionView> { Connection() }));

        service.GetConnectionAsync(Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == KnownConnectionId
                ? Task.FromResult(Connection())
                : throw new DataNotFoundException("securityscorecard_connections",
                    call.ArgAt<int>(0).ToString(), new Exception("Not found.")));

        service.CreateConnectionAsync(Arg.Any<SecurityScorecardConnection>(), Arg.Any<string?>())
            .Returns(call =>
            {
                var connection = call.ArgAt<SecurityScorecardConnection>(0);

                if (connection.Domain.Contains("://") || !connection.Domain.Contains('.'))
                    throw new InvalidParameterException(nameof(connection.Domain),
                        "The domain must be a bare registered domain such as acme.com.");

                return Task.FromResult(Connection(id: 99, name: connection.Name));
            });

        service.UpdateConnectionAsync(Arg.Any<SecurityScorecardConnection>(), Arg.Any<string?>())
            .Returns(call =>
            {
                var connection = call.ArgAt<SecurityScorecardConnection>(0);

                if (connection.Id != KnownConnectionId)
                    throw new DataNotFoundException("securityscorecard_connections",
                        connection.Id.ToString(), new Exception("Not found."));

                return Task.FromResult(Connection(name: connection.Name));
            });

        service.DeleteConnectionAsync(Arg.Any<int>()).Returns(call =>
            call.ArgAt<int>(0) == KnownConnectionId
                ? Task.CompletedTask
                : throw new DataNotFoundException("securityscorecard_connections",
                    call.ArgAt<int>(0).ToString(), new Exception("Not found.")));

        service.TestConnectionAsync(Arg.Any<int>())
            .Returns(Task.FromResult(ConnectionTestResult.Ok("Read the scorecard for 'acme.com'.")));

        service.SyncAsync(Arg.Any<int>(), Arg.Any<CancellationToken>()).Returns(call =>
        {
            var id = call.ArgAt<int>(0);

            if (id == 502) throw new IntegrationRequestException("SecurityScorecard", "HTTP 500.");

            if (id != KnownConnectionId)
                throw new DataNotFoundException("securityscorecard_connections", id.ToString(),
                    new Exception("Not found."));

            return Task.FromResult(new PostureSyncResult
            {
                PostureRowsWritten = 11, FindingsCreated = 4, CyberRiskIndex = 12
            });
        });

        service.SyncDueConnectionsAsync(Arg.Any<DateTime>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PostureSyncResult { PostureRowsWritten = 11 }));

        service.GetFactorHistoryAsync(Arg.Any<int>(), Arg.Any<int>())
            .Returns(Task.FromResult(new List<SecurityScorecardFactor>
            {
                new()
                {
                    Id = 1, ConnectionId = KnownConnectionId, FactorName = "patching_cadence",
                    Score = 54, Grade = "F", CapturedAt = DateTime.UtcNow
                }
            }));

        service.GetSyncLogAsync(Arg.Any<int>()).Returns(Task.FromResult(new List<IntegrationSyncLog>
        {
            new()
            {
                Id = 2, Integration = IntegrationKind.SecurityScorecard,
                ConnectionId = KnownConnectionId, ConnectionName = "Acme scorecard",
                StartedAt = DateTime.UtcNow, Status = IntegrationSyncStatus.Succeeded
            }
        }));

        return service;
    }

    private static SecurityScorecardConnectionView Connection(int id = KnownConnectionId,
        string name = "Acme scorecard") => new()
    {
        Id = id, Name = name, Domain = "acme.com", BaseUrl = "https://api.securityscorecard.io",
        HasApiToken = true, Enabled = true, SyncIntervalHours = 24, SyncVulnerabilities = true,
        SyncIssues = true
    };
}

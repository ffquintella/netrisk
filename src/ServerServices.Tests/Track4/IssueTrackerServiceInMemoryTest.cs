using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Integrations;
using ServerServices.Integrations.IssueTrackers;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// Issue-tracker connections, links and bi-directional synchronization
/// (Track 4 milestone 4.2).
///
/// The milestone's acceptance criterion is here: closing a linked ticket flips the finding to
/// <c>Mitigated</c> and the timeline shows it. So are the two safety properties that make the feature
/// usable rather than dangerous — loop protection, so an inbound change does not echo back out, and
/// conflict flagging, so last-writer-wins is visible rather than a finding that reopened itself.
/// </summary>
[TestSubject(typeof(IssueTrackerService))]
public class IssueTrackerServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IIssueTrackerService _svc;
    private readonly IFindingLifecycleService _lifecycle;

    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    public IssueTrackerServiceInMemoryTest()
    {
        _svc = GetService<IIssueTrackerService>();
        _lifecycle = GetService<IFindingLifecycleService>();

        Seed(ctx =>
        {
            ctx.Users.Add(new User
            {
                Value = 1, Name = "analyst", Login = "analyst", Enabled = true, Type = "local",
                Salt = "s", Password = Encoding.UTF8.GetBytes("p"), Email = "analyst@acme.com"
            });

            ctx.Hosts.Add(new Host
            {
                Id = 1, HostName = "db-prod-01", Ip = "10.0.0.5", Source = "manual",
                RegistrationDate = Now, Status = 1
            });

            ctx.Vulnerabilities.Add(NewFinding(42, "critical"));
            ctx.Vulnerabilities.Add(NewFinding(43, "low"));
        });
    }

    private static Vulnerability NewFinding(int id, string severity) => new()
    {
        Id = id,
        Title = $"Finding {id}",
        Description = "Details.",
        Severity = severity,
        Solution = "Patch it.",
        HostId = 1,
        FirstDetection = Now.AddDays(-10),
        LastDetection = Now,
        LifecycleStatus = FindingStatus.Active,
        Cves = "CVE-2026-1234,CVE-2026-5678",
        ImportSource = "nessus"
    };

    private async Task<IssueTrackerConnectionView> ConnectionAsync(
        IssueTrackerProviderKind provider = IssueTrackerProviderKind.Jira,
        int? autoCreateMinSeverity = null, bool pushUpdates = true, string name = "Security Jira")
    {
        return await _svc.CreateConnectionAsync(new IssueTrackerConnection
        {
            Name = name,
            Provider = provider,
            BaseUrl = "https://acme.atlassian.net",
            ProjectKey = "SEC",
            IssueType = "Bug",
            AuthUser = "ci@acme.com",
            Enabled = true,
            PushFindingUpdates = pushUpdates,
            AutoCreateMinSeverity = autoCreateMinSeverity,
            PollIntervalMinutes = 15,
            DefaultLabels = "netrisk"
        }, "api-token", "webhook-secret", userId: 1);
    }

    /// <summary>
    /// Answers only the create call. Matched on the method and the exact path rather than on a URL
    /// fragment: "/rest/api/3/issue" is a prefix of the read and comment paths too, and a fragment rule
    /// would shadow them and make every later read return the create response.
    /// </summary>
    private void JiraCreateReturns(string key = "SEC-1")
    {
        FakeOutboundHttpClient.Rules.Add((
            request => request.Method == "POST" && request.Url.EndsWith("/rest/api/3/issue"),
            new OutboundHttpResponse
            {
                StatusCode = 201,
                Body = $$"""{"id":"10001","key":"{{key}}"}"""
            }));
    }

    // --- connections ------------------------------------------------------------------------

    [Fact]
    public async Task CreatingAConnectionStoresTheCredentialsEncryptedAndNeverReturnsThem()
    {
        var view = await ConnectionAsync();

        Assert.True(view.HasToken);
        Assert.True(view.HasWebhookSecret);

        await using var db = OpenContext();
        var stored = db.IssueTrackerConnections.Single();

        Assert.NotEqual("api-token", stored.EncryptedToken);
        Assert.NotEqual("webhook-secret", stored.EncryptedWebhookSecret);

        // The view type has no field a token could travel in, which is the point of the shape.
        Assert.Null(typeof(IssueTrackerConnectionView).GetProperty("Token"));
        Assert.Null(typeof(IssueTrackerConnectionView).GetProperty("EncryptedToken"));
    }

    [Fact]
    public async Task UpdatingWithoutACredentialKeepsTheStoredOne()
    {
        var view = await ConnectionAsync();

        await _svc.UpdateConnectionAsync(new IssueTrackerConnection
        {
            Id = view.Id, Name = "Security Jira", Provider = view.Provider, BaseUrl = view.BaseUrl,
            ProjectKey = view.ProjectKey, Enabled = true, PollIntervalMinutes = 15
        }, token: null, webhookSecret: null, userId: 1);

        await using var db = OpenContext();
        Assert.NotNull(db.IssueTrackerConnections.Single().EncryptedToken);
    }

    [Theory]
    [InlineData("", "SEC")]
    [InlineData("not-a-url", "SEC")]
    [InlineData("ftp://acme.atlassian.net", "SEC")]
    [InlineData("https://acme.atlassian.net", "")]
    public async Task AnInvalidConnectionIsRefused(string baseUrl, string project)
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.CreateConnectionAsync(new IssueTrackerConnection
            {
                Name = "x", Provider = IssueTrackerProviderKind.Jira,
                BaseUrl = baseUrl, ProjectKey = project
            }, null, null, 1));
    }

    [Fact]
    public async Task DuplicateConnectionNamesAreRefused()
    {
        await ConnectionAsync();

        await Assert.ThrowsAsync<InvalidParameterException>(() => ConnectionAsync());
    }

    // --- templates and mapping --------------------------------------------------------------

    [Fact]
    public async Task ThePreviewRendersTheFindingWithoutCreatingAnything()
    {
        var connection = await ConnectionAsync();

        var draft = await _svc.PreviewAsync(connection.Id, 42);

        Assert.Contains("Critical", draft.Title);
        Assert.Contains("Finding 42", draft.Title);

        // The CVE links are what make the ticket useful to a developer with no NetRisk login.
        Assert.Contains("nvd.nist.gov/vuln/detail/CVE-2026-1234", draft.Description);
        Assert.Contains("db-prod-01", draft.Description);

        Assert.Empty(FakeOutboundHttpClient.Requests);
    }

    [Fact]
    public async Task ThePriorityUsesTheProvidersDefaultMappingWhenNoneIsConfigured()
    {
        var connection = await ConnectionAsync();

        var draft = await _svc.PreviewAsync(connection.Id, 42);

        Assert.Equal("Highest", draft.Priority);
    }

    [Fact]
    public void AzureDevOpsDefaultPriorityIsInvertedBecauseItsScaleIs()
    {
        var map = IssueTrackerService.DefaultPriorities[IssueTrackerProviderKind.AzureDevOps];

        // ADO's Priority is 1 (highest) to 4 (lowest) — the inverse of NetRisk's scale.
        Assert.Equal("1", map[4]);
        Assert.Equal("4", map[1]);
    }

    [Fact]
    public async Task AConfiguredPriorityMappingWins()
    {
        var connection = await ConnectionAsync();

        await _svc.UpdateConnectionAsync(new IssueTrackerConnection
        {
            Id = connection.Id, Name = connection.Name, Provider = connection.Provider,
            BaseUrl = connection.BaseUrl, ProjectKey = connection.ProjectKey, Enabled = true,
            PollIntervalMinutes = 15,
            PriorityMappingJson = """{"4":"P0","3":"P1"}"""
        }, null, null, 1);

        Assert.Equal("P0", (await _svc.PreviewAsync(connection.Id, 42)).Priority);
    }

    [Fact]
    public async Task AMalformedPriorityMappingFallsBackRatherThanFailingTheCreate()
    {
        var connection = await ConnectionAsync();

        await _svc.UpdateConnectionAsync(new IssueTrackerConnection
        {
            Id = connection.Id, Name = connection.Name, Provider = connection.Provider,
            BaseUrl = connection.BaseUrl, ProjectKey = connection.ProjectKey, Enabled = true,
            PollIntervalMinutes = 15, PriorityMappingJson = "{not json"
        }, null, null, 1);

        Assert.Equal("Highest", (await _svc.PreviewAsync(connection.Id, 42)).Priority);
    }

    [Fact]
    public async Task ACustomTemplateIsHonouredAndAnUnknownPlaceholderIsLeftVisible()
    {
        var connection = await ConnectionAsync();

        await _svc.UpdateConnectionAsync(new IssueTrackerConnection
        {
            Id = connection.Id, Name = connection.Name, Provider = connection.Provider,
            BaseUrl = connection.BaseUrl, ProjectKey = connection.ProjectKey, Enabled = true,
            PollIntervalMinutes = 15,
            TitleTemplate = "[{{Severity}}] {{Title}} on {{Asset}} ({{Nonexistent}})"
        }, null, null, 1);

        var draft = await _svc.PreviewAsync(connection.Id, 42);

        Assert.Equal("[Critical] Finding 42 on db-prod-01 ({{Nonexistent}})", draft.Title);
    }

    [Theory]
    [InlineData(IssueTrackerProviderKind.Jira, "https://acme.atlassian.net/browse/SEC-1421", "SEC-1421")]
    [InlineData(IssueTrackerProviderKind.Jira, "sec-1421", "SEC-1421")]
    [InlineData(IssueTrackerProviderKind.GitHub, "https://github.com/a/b/issues/88", "88")]
    [InlineData(IssueTrackerProviderKind.GitLab, "https://gitlab.com/g/p/-/issues/12", "12")]
    [InlineData(IssueTrackerProviderKind.AzureDevOps, "https://dev.azure.com/a/p/_workitems/edit/4712", "4712")]
    public void AnIssueKeyIsExtractedFromWhateverThePersonPasted(IssueTrackerProviderKind provider,
        string input, string expected)
    {
        Assert.Equal(expected, IssueTrackerService.ExtractKey(provider, input));
    }

    // --- creating and linking ---------------------------------------------------------------

    [Fact]
    public async Task CreatingAnIssueLinksItToTheFinding()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        var link = await _svc.CreateIssueAsync(connection.Id, 42, 1);

        Assert.Equal("SEC-1", link.IssueKey);
        Assert.Equal(42, link.FindingId);
        Assert.Equal("https://acme.atlassian.net/browse/SEC-1", link.IssueUrl);

        Assert.Single(await _svc.GetLinksForFindingAsync(42));
    }

    [Fact]
    public async Task CreatingTwiceReturnsTheExistingLinkRatherThanFilingADuplicate()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        var first = await _svc.CreateIssueAsync(connection.Id, 42, 1);
        var callsAfterFirst = FakeOutboundHttpClient.Requests.Count;

        var second = await _svc.CreateIssueAsync(connection.Id, 42, 1);

        Assert.Equal(first.Id, second.Id);
        // A retried request must not produce a second ticket.
        Assert.Equal(callsAfterFirst, FakeOutboundHttpClient.Requests.Count);
    }

    [Fact]
    public async Task ADisabledConnectionRefusesToCreate()
    {
        var connection = await ConnectionAsync();

        await _svc.UpdateConnectionAsync(new IssueTrackerConnection
        {
            Id = connection.Id, Name = connection.Name, Provider = connection.Provider,
            BaseUrl = connection.BaseUrl, ProjectKey = connection.ProjectKey, Enabled = false,
            PollIntervalMinutes = 15
        }, null, null, 1);

        await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.CreateIssueAsync(connection.Id, 42, 1));
    }

    [Fact]
    public async Task ABulkCreateFilesTheOnesItCanAndSkipsTheOneItCannot()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        // 44 does not exist; 42 and 43 do.
        var created = await _svc.CreateIssuesAsync(connection.Id, [42, 43, 44], 1);

        // Filing thirty-nine of forty tickets is a better outcome than filing none.
        Assert.Equal(2, created.Count);
    }

    [Fact]
    public async Task LinkingAnExistingIssueChecksItExistsFirst()
    {
        var connection = await ConnectionAsync();

        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SEC-9", "not found", 404);

        // A link to an issue that does not exist is a link that fails silently on every later sync.
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            _svc.LinkExistingAsync(connection.Id, 42, "SEC-9", 1));
    }

    [Fact]
    public async Task AnIssueAlreadyLinkedToAnotherFindingIsRefused()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        await _svc.CreateIssueAsync(connection.Id, 42, 1);

        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SEC-1?",
            """{"key":"SEC-1","fields":{"status":{"name":"To Do"}}}""");

        var thrown = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.LinkExistingAsync(connection.Id, 43, "SEC-1", 1));

        Assert.Contains("#42", thrown.Message);
    }

    [Fact]
    public async Task UnlinkingLeavesTheExternalIssueAlone()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        var link = await _svc.CreateIssueAsync(connection.Id, 42, 1);
        var callsBefore = FakeOutboundHttpClient.Requests.Count;

        await _svc.UnlinkAsync(link.Id);

        Assert.Empty(await _svc.GetLinksForFindingAsync(42));
        // NetRisk does not delete other people's tickets.
        Assert.Equal(callsBefore, FakeOutboundHttpClient.Requests.Count);
    }

    // --- inbound synchronization ------------------------------------------------------------

    [Fact]
    public async Task ClosingALinkedTicketFlipsTheFindingToMitigatedAndTheTimelineShowsIt()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        await _svc.CreateIssueAsync(connection.Id, 42, 1);

        await _svc.SetStatusMappingsAsync(connection.Id,
        [
            new IssueStatusMapping { ExternalStatus = "Done", Action = IssueSyncAction.MarkMitigated }
        ]);

        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SEC-1?", """
            {"key":"SEC-1","fields":{"status":{"name":"Done","statusCategory":{"key":"done"}}}}
            """);

        var result = await _svc.PollConnectionAsync(connection.Id);

        Assert.Equal(1, result.Applied);

        await using var db = OpenContext();
        Assert.Equal(FindingStatus.Mitigated, db.Vulnerabilities.Single(v => v.Id == 42).LifecycleStatus);

        // The audit trail names the tracker as the source, which is what tells a human decision from an
        // automated one.
        var history = await _lifecycle.GetHistoryAsync(42);
        var transition = history.First(h => h.ToStatus == FindingStatus.Mitigated);

        Assert.Equal(FindingStatusChangeSource.IssueSync, transition.Source);
        Assert.Contains("SEC-1", transition.Justification!);
    }

    [Fact]
    public async Task ScheduleReverifyDoesNotTransitionTheFinding()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        await _svc.CreateIssueAsync(connection.Id, 42, 1);

        await _svc.SetStatusMappingsAsync(connection.Id,
        [
            new IssueStatusMapping { ExternalStatus = "Done", Action = IssueSyncAction.ScheduleReverify }
        ]);

        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SEC-1?", """
            {"key":"SEC-1","fields":{"status":{"name":"Done","statusCategory":{"key":"done"}}}}
            """);

        var result = await _svc.PollConnectionAsync(connection.Id);

        Assert.Equal(1, result.Applied);

        await using var db = OpenContext();
        // "Re-verify" means a human or a scanner has to confirm the fix; moving it to Mitigated first is
        // exactly the assumption this option exists to avoid.
        Assert.Equal(FindingStatus.Active, db.Vulnerabilities.Single(v => v.Id == 42).LifecycleStatus);
    }

    [Fact]
    public async Task AClosedIssueWithNoMappingStillCountsAsMitigated()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        await _svc.CreateIssueAsync(connection.Id, 42, 1);

        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SEC-1?", """
            {"key":"SEC-1","fields":{"status":{"name":"Done","statusCategory":{"key":"done"}}}}
            """);

        // A connection nobody configured mappings for should still close findings when the ticket closes;
        // an operator who disagrees maps that status to None explicitly.
        Assert.Equal(1, (await _svc.PollConnectionAsync(connection.Id)).Applied);
    }

    [Fact]
    public async Task AStatusMappedToNoneChangesNothing()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        await _svc.CreateIssueAsync(connection.Id, 42, 1);

        await _svc.SetStatusMappingsAsync(connection.Id,
        [
            new IssueStatusMapping { ExternalStatus = "Done", Action = IssueSyncAction.None }
        ]);

        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SEC-1?", """
            {"key":"SEC-1","fields":{"status":{"name":"Done","statusCategory":{"key":"done"}}}}
            """);

        var result = await _svc.PollConnectionAsync(connection.Id);

        Assert.Equal(1, result.Changed);
        Assert.Equal(0, result.Applied);
    }

    [Fact]
    public async Task ASecondPollWithNoChangeDoesNothing()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        await _svc.CreateIssueAsync(connection.Id, 42, 1);

        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SEC-1?",
            """{"key":"SEC-1","fields":{"status":{"name":"To Do"}}}""");

        await _svc.PollConnectionAsync(connection.Id);

        var second = await _svc.PollConnectionAsync(connection.Id);

        Assert.Equal(1, second.Examined);
        Assert.Equal(0, second.Changed);
    }

    [Fact]
    public async Task ATrackerChangeThatContradictsASuppressedFindingIsFlaggedAsAConflict()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        await _svc.CreateIssueAsync(connection.Id, 42, 1);

        // NetRisk decided this was a false positive.
        await _lifecycle.TransitionAsync(42, FindingStatus.FalsePositive, 1,
            FindingStatusChangeSource.Manual, "Not exploitable in this configuration.");

        await _svc.SetStatusMappingsAsync(connection.Id,
        [
            new IssueStatusMapping { ExternalStatus = "Done", Action = IssueSyncAction.MarkMitigated }
        ]);

        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SEC-1?", """
            {"key":"SEC-1","fields":{"status":{"name":"Done","statusCategory":{"key":"done"}}}}
            """);

        var result = await _svc.PollConnectionAsync(connection.Id);

        Assert.Equal(1, result.Conflicts);

        var conflicts = await _svc.GetConflictsAsync();
        var conflict = Assert.Single(conflicts);

        Assert.True(conflict.HasConflict);
        // Last-writer-wins is applied; the flag is what makes it visible rather than a finding that
        // changed direction on its own.
        Assert.Contains("FalsePositive", conflict.ConflictDetail!);

        await _svc.ResolveConflictAsync(conflict.Id);
        Assert.Empty(await _svc.GetConflictsAsync());
    }

    [Fact]
    public async Task ADeletedIssueIsRecordedRatherThanUnlinked()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        await _svc.CreateIssueAsync(connection.Id, 42, 1);

        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SEC-1?", "gone", 404);

        var result = await _svc.PollConnectionAsync(connection.Id);

        Assert.Equal(1, result.Errors);

        // Unlinking on the operator's behalf would lose the evidence that the ticket ever existed.
        var link = Assert.Single(await _svc.GetLinksForFindingAsync(42));
        Assert.Contains("no longer exists", link.SyncError!);
    }

    // --- webhooks ---------------------------------------------------------------------------

    [Fact]
    public async Task AJiraWebhookWithTheWrongUrlSecretIsRefused()
    {
        var connection = await ConnectionAsync();

        // Jira cannot sign a body, so the shared secret travels in the URL — and a wrong one must not
        // be able to close findings.
        await Assert.ThrowsAsync<WebhookAuthenticationException>(() =>
            _svc.ApplyWebhookAsync(connection.Id, "{}", new Dictionary<string, string>(), "wrong"));
    }

    [Fact]
    public async Task AJiraWebhookWithTheRightSecretAppliesTheMapping()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        await _svc.CreateIssueAsync(connection.Id, 42, 1);

        await _svc.SetStatusMappingsAsync(connection.Id,
        [
            new IssueStatusMapping { ExternalStatus = "Done", Action = IssueSyncAction.MarkMitigated }
        ]);

        const string body = """
            {"issue":{"key":"SEC-1","fields":{"status":{"name":"Done","statusCategory":{"key":"done"}}}}}
            """;

        var result = await _svc.ApplyWebhookAsync(connection.Id, body,
            new Dictionary<string, string>(), "webhook-secret");

        Assert.Equal(1, result.Applied);

        await using var db = OpenContext();
        Assert.Equal(FindingStatus.Mitigated, db.Vulnerabilities.Single(v => v.Id == 42).LifecycleStatus);
    }

    [Fact]
    public async Task AWebhookAboutAnUntrackedIssueIsNotAnError()
    {
        var connection = await ConnectionAsync();

        const string body = """{"issue":{"key":"SEC-999","fields":{"status":{"name":"Done"}}}}""";

        var result = await _svc.ApplyWebhookAsync(connection.Id, body,
            new Dictionary<string, string>(), "webhook-secret");

        // An issue NetRisk does not track changing state is simply not interesting.
        Assert.Equal(0, result.Examined);
    }

    [Fact]
    public void OnlyTheProvidersThatCannotSignNeedAUrlSecret()
    {
        Assert.True(IssueTrackerService.RequiresUrlSecret(IssueTrackerProviderKind.Jira));
        Assert.True(IssueTrackerService.RequiresUrlSecret(IssueTrackerProviderKind.AzureDevOps));
        // GitHub and GitLab authenticate the delivery itself.
        Assert.False(IssueTrackerService.RequiresUrlSecret(IssueTrackerProviderKind.GitHub));
        Assert.False(IssueTrackerService.RequiresUrlSecret(IssueTrackerProviderKind.GitLab));
    }

    // --- outbound synchronization -----------------------------------------------------------

    [Fact]
    public async Task ANetRiskTransitionIsPushedOntoTheLinkedIssue()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        await _svc.CreateIssueAsync(connection.Id, 42, 1);

        await _svc.SetStatusMappingsAsync(connection.Id,
        [
            new IssueStatusMapping
            {
                ExternalStatus = "Done", Action = IssueSyncAction.MarkMitigated, OutboundTransition = "Done"
            }
        ]);

        FakeOutboundHttpClient.RuleFor("/comment", "{}");
        FakeOutboundHttpClient.RuleFor("/transitions",
            """{"transitions":[{"id":"31","name":"Done","to":{"name":"Done"}}]}""");
        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SEC-1?",
            """{"key":"SEC-1","fields":{"status":{"name":"Done"}}}""");

        var pushed = await _svc.PushFindingTransitionAsync(42, FindingStatus.Mitigated,
            "Verified by re-scan.");

        Assert.Equal(1, pushed);
        Assert.Contains(FakeOutboundHttpClient.Requests,
            r => r.Url.Contains("/comment") && r.Body!.Contains("Verified by re-scan"));
    }

    [Fact]
    public async Task AnInboundChangeIsNotEchoedBackOut()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        await _svc.CreateIssueAsync(connection.Id, 42, 1);

        await _svc.SetStatusMappingsAsync(connection.Id,
        [
            new IssueStatusMapping { ExternalStatus = "Done", Action = IssueSyncAction.MarkMitigated }
        ]);

        FakeOutboundHttpClient.RuleFor("/rest/api/3/issue/SEC-1?", """
            {"key":"SEC-1","fields":{"status":{"name":"Done","statusCategory":{"key":"done"}}}}
            """);

        await _svc.PollConnectionAsync(connection.Id);

        // The finding only reached Mitigated because the tracker asked for it. Pushing it back would post
        // a comment that the tracker reports as a change, which comes back in as another inbound sync.
        var pushed = await _svc.PushFindingTransitionAsync(42, FindingStatus.Mitigated);

        Assert.Equal(0, pushed);
    }

    [Fact]
    public async Task AConnectionThatDoesNotWantOutboundPushesIsSkipped()
    {
        var connection = await ConnectionAsync(pushUpdates: false);
        JiraCreateReturns();

        await _svc.CreateIssueAsync(connection.Id, 42, 1);

        Assert.Equal(0, await _svc.PushFindingTransitionAsync(42, FindingStatus.Mitigated));
    }

    [Fact]
    public async Task ATrackerThatRefusesTheCommentRecordsTheErrorRatherThanThrowing()
    {
        var connection = await ConnectionAsync();
        JiraCreateReturns();

        await _svc.CreateIssueAsync(connection.Id, 42, 1);

        FakeOutboundHttpClient.RuleFor("/comment", "no permission", 403);

        // A tracker that refuses a comment must not fail the NetRisk transition that triggered it.
        var pushed = await _svc.PushFindingTransitionAsync(42, FindingStatus.Mitigated);

        Assert.Equal(0, pushed);

        var link = Assert.Single(await _svc.GetLinksForFindingAsync(42));
        Assert.Contains("403", link.SyncError!);
    }

    // --- policy mode ------------------------------------------------------------------------

    [Fact]
    public async Task ThePolicyAutoCreatesForAFindingAtOrAboveItsThreshold()
    {
        var connection = await ConnectionAsync(autoCreateMinSeverity: 4);
        JiraCreateReturns();

        var created = await _svc.ApplyAutoCreatePolicyAsync(42, 1);

        Assert.Single(created);
    }

    [Fact]
    public async Task ThePolicyIgnoresAFindingBelowItsThreshold()
    {
        await ConnectionAsync(autoCreateMinSeverity: 4);
        JiraCreateReturns();

        // Ticket-per-finding noise is the failure this whole milestone is trying to avoid.
        Assert.Empty(await _svc.ApplyAutoCreatePolicyAsync(43, 1));
    }

    [Fact]
    public async Task ConnectionsWithoutAPolicyNeverAutoCreate()
    {
        await ConnectionAsync();
        JiraCreateReturns();

        // Manual-only is the default.
        Assert.Empty(await _svc.ApplyAutoCreatePolicyAsync(42, 1));
    }

    [Fact]
    public async Task AnOutOfRangeAutoCreateThresholdIsRefused()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.CreateConnectionAsync(new IssueTrackerConnection
            {
                Name = "x", Provider = IssueTrackerProviderKind.Jira,
                BaseUrl = "https://a.atlassian.net", ProjectKey = "SEC", AutoCreateMinSeverity = 9
            }, null, null, 1));
    }

    // --- status mappings --------------------------------------------------------------------

    [Fact]
    public async Task DuplicateStatusMappingsAreRefused()
    {
        var connection = await ConnectionAsync();

        // Two rows mapping "Done" to different actions is a configuration whose behaviour depends on row
        // order.
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.SetStatusMappingsAsync(connection.Id,
            [
                new IssueStatusMapping { ExternalStatus = "Done", Action = IssueSyncAction.MarkMitigated },
                new IssueStatusMapping { ExternalStatus = "done", Action = IssueSyncAction.ScheduleReverify }
            ]));
    }

    [Fact]
    public async Task SettingMappingsReplacesTheWholeSet()
    {
        var connection = await ConnectionAsync();

        await _svc.SetStatusMappingsAsync(connection.Id,
        [
            new IssueStatusMapping { ExternalStatus = "Done", Action = IssueSyncAction.MarkMitigated },
            new IssueStatusMapping { ExternalStatus = "Won't Do", Action = IssueSyncAction.MarkFalsePositive }
        ]);

        await _svc.SetStatusMappingsAsync(connection.Id,
        [
            new IssueStatusMapping { ExternalStatus = "Done", Action = IssueSyncAction.ScheduleReverify }
        ]);

        var mappings = await _svc.GetStatusMappingsAsync(connection.Id);

        // A partial save would leave a half-configured mapping applying to live findings.
        Assert.Equal(IssueSyncAction.ScheduleReverify, Assert.Single(mappings).Action);
    }

    [Fact]
    public async Task AMappingWithNoExternalStatusIsRefused()
    {
        var connection = await ConnectionAsync();

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.SetStatusMappingsAsync(connection.Id,
                [new IssueStatusMapping { ExternalStatus = "  ", Action = IssueSyncAction.MarkMitigated }]));
    }
}

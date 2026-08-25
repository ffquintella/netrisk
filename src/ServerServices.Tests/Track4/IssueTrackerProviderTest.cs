using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Integrations;
using Serilog;
using ServerServices.Integrations.IssueTrackers;
using ServerServices.Tests.Mock;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// The four issue-tracker providers' request shapes and webhook validation
/// (Track 4 milestone 4.2.2).
///
/// Each provider has one detail that is easy to get wrong and produces a useless error when it is:
/// Jira v3 takes Atlassian Document Format rather than a string and transitions by id rather than by
/// name; GitHub has no priority field; GitLab addresses an issue by its per-project <c>iid</c>, not its
/// global id; Azure DevOps takes a JSON Patch document and answers an invalid PAT with a 203 sign-in
/// page. All four are asserted here rather than discovered against a customer's instance.
/// </summary>
[TestSubject(typeof(JiraIssueTrackerProvider))]
public class IssueTrackerProviderTest
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private static IssueTrackerConnection Connection(IssueTrackerProviderKind provider, string baseUrl,
        string project) => new()
    {
        Id = 1,
        Name = "test",
        Provider = provider,
        BaseUrl = baseUrl,
        ProjectKey = project,
        AuthUser = "ci@acme.com",
        IssueType = "Bug"
    };

    private static IssueDraft Draft() => new()
    {
        Title = "SQL injection on /billing",
        Description = "NetRisk finding **#42**\n\n| Severity | Critical |\n\nLine two.",
        Priority = "Highest",
        Labels = ["netrisk", "security finding"],
        IssueType = "Bug",
        FindingId = 42
    };

    // --- Jira -------------------------------------------------------------------------------

    [Fact]
    public async Task JiraCreatesAnIssueWithAnAdfDescription()
    {
        var http = new FakeOutboundHttpClient()
            .EnqueueJson("""{"id":"10001","key":"SEC-1"}""", 201);

        var provider = new JiraIssueTrackerProvider(Log, http);

        var issue = await provider.CreateIssueAsync(
            Connection(IssueTrackerProviderKind.Jira, "https://acme.atlassian.net", "SEC"), "token", Draft());

        Assert.Equal("SEC-1", issue.Key);
        Assert.Equal("https://acme.atlassian.net/browse/SEC-1", issue.Url);

        var request = Assert.Single(http.Requests);

        using var body = JsonDocument.Parse(request.Body!);
        var fields = body.RootElement.GetProperty("fields");

        // v3 rejects a plain string here outright.
        Assert.Equal("doc", fields.GetProperty("description").GetProperty("type").GetString());
        Assert.Equal("Highest", fields.GetProperty("priority").GetProperty("name").GetString());

        // Jira rejects the whole request when a label contains whitespace.
        var labels = fields.GetProperty("labels").EnumerateArray().Select(l => l.GetString()).ToList();
        Assert.Contains("security-finding", labels);

        // Basic auth with email:token is what Atlassian issues for Cloud.
        Assert.StartsWith("Basic ", request.Headers["Authorization"]);
        Assert.Equal("ci@acme.com:token", Encoding.UTF8.GetString(
            Convert.FromBase64String(request.Headers["Authorization"]["Basic ".Length..])));
    }

    [Fact]
    public void JiraTruncatesASummaryLongerThanJiraAccepts()
    {
        var http = new FakeOutboundHttpClient().EnqueueJson("""{"id":"1","key":"SEC-1"}""", 201);
        var provider = new JiraIssueTrackerProvider(Log, http);

        var draft = Draft();
        draft.Title = new string('x', 400);

        provider.CreateIssueAsync(Connection(IssueTrackerProviderKind.Jira, "https://a.atlassian.net", "SEC"),
            "t", draft).GetAwaiter().GetResult();

        using var body = JsonDocument.Parse(http.Requests[0].Body!);

        // 255 is Jira's hard limit; a longer summary is a 400 with no useful message.
        Assert.True(body.RootElement.GetProperty("fields").GetProperty("summary").GetString()!.Length <= 255);
    }

    [Fact]
    public void JiraAdfWritesOneParagraphPerLineAndNoEmptyTextNodes()
    {
        var buffer = new System.IO.MemoryStream();

        using (var json = new Utf8JsonWriter(buffer))
        {
            JiraIssueTrackerProvider.WriteAdf(json, "first\n\nthird");
        }

        using var document = JsonDocument.Parse(Encoding.UTF8.GetString(buffer.ToArray()));
        var content = document.RootElement.GetProperty("content").EnumerateArray().ToList();

        Assert.Equal(3, content.Count);
        // An ADF paragraph may not contain an empty text node; the blank line is an empty paragraph.
        Assert.Empty(content[1].GetProperty("content").EnumerateArray());
    }

    [Fact]
    public async Task JiraResolvesATransitionByNameOrDestinationState()
    {
        var http = new FakeOutboundHttpClient()
            .RuleFor("/transitions", """
                {"transitions":[{"id":"31","name":"Close Issue","to":{"name":"Done"}}]}
                """)
            .RuleFor("/comment", "{}")
            .RuleFor("/issue/SEC-1?", """
                {"key":"SEC-1","fields":{"status":{"name":"Done","statusCategory":{"key":"done"}}}}
                """);

        var provider = new JiraIssueTrackerProvider(Log, http);

        // Configured with the destination state rather than the transition name — operators use
        // whichever of the two they see in their Jira, and the two are often different words.
        var issue = await provider.UpdateIssueAsync(
            Connection(IssueTrackerProviderKind.Jira, "https://a.atlassian.net", "SEC"), "t", "SEC-1",
            "NetRisk closed this.", "Done");

        Assert.True(issue.IsClosed);

        var executed = http.Requests.Last(r => r.Url.Contains("/transitions") && r.Method == "POST");
        Assert.Contains("\"id\":\"31\"", executed.Body!);
    }

    [Fact]
    public async Task JiraReportsWhichTransitionsAreAvailableWhenTheConfiguredOneIsNot()
    {
        var http = new FakeOutboundHttpClient()
            .RuleFor("/transitions", """{"transitions":[{"id":"11","name":"Start Progress","to":{"name":"In Progress"}}]}""");

        var provider = new JiraIssueTrackerProvider(Log, http);

        var thrown = await Assert.ThrowsAsync<IntegrationRequestException>(() =>
            provider.UpdateIssueAsync(
                Connection(IssueTrackerProviderKind.Jira, "https://a.atlassian.net", "SEC"), "t", "SEC-1",
                null, "Done"));

        // Jira's workflow decides what is reachable from the current state, so naming what is available
        // is more useful than passing on a 400.
        Assert.Contains("Start Progress", thrown.Message);
    }

    [Fact]
    public async Task JiraUsesTheStatusCategoryToDecideWhetherAnIssueIsClosed()
    {
        var http = new FakeOutboundHttpClient().EnqueueJson("""
            {"key":"SEC-1","fields":{"status":{"name":"Shipped","statusCategory":{"key":"done"}}}}
            """);

        var provider = new JiraIssueTrackerProvider(Log, http);

        var issue = await provider.GetIssueAsync(
            Connection(IssueTrackerProviderKind.Jira, "https://a.atlassian.net", "SEC"), "t", "SEC-1");

        // A workflow that renamed "Done" to "Shipped" is still a terminal state; string-matching the
        // name would miss it.
        Assert.True(issue!.IsClosed);
        Assert.Equal("Shipped", issue.Status);
    }

    [Fact]
    public async Task JiraTestChecksTheProjectAndNotOnlyTheCredentials()
    {
        var http = new FakeOutboundHttpClient()
            .RuleFor("/myself", """{"displayName":"CI Bot"}""")
            .RuleFor("/project/SEC", "not found", 404);

        var provider = new JiraIssueTrackerProvider(Log, http);

        var result = await provider.TestConnectionAsync(
            Connection(IssueTrackerProviderKind.Jira, "https://a.atlassian.net", "SEC"), "t");

        // A valid token against a mistyped project key is the failure an operator is most likely to make.
        Assert.False(result.Success);
        Assert.Contains("SEC", result.Message);
    }

    [Fact]
    public async Task JiraAnUnreachableHostIsReportedAsSuch()
    {
        var http = new FakeOutboundHttpClient().EnqueueTransportError("No such host");

        var provider = new JiraIssueTrackerProvider(Log, http);

        var result = await provider.TestConnectionAsync(
            Connection(IssueTrackerProviderKind.Jira, "https://a.atlassian.net", "SEC"), "t");

        Assert.False(result.Success);
        Assert.Contains("No such host", result.Message);
    }

    // --- GitHub -----------------------------------------------------------------------------

    [Fact]
    public async Task GitHubExpressesPriorityAsALabelBecauseItHasNoPriorityField()
    {
        var http = new FakeOutboundHttpClient()
            .EnqueueJson("""{"id":1,"number":88,"state":"open","html_url":"https://github.com/a/b/issues/88"}""", 201);

        var provider = new GitHubIssueTrackerProvider(Log, http);

        var issue = await provider.CreateIssueAsync(
            Connection(IssueTrackerProviderKind.GitHub, "https://api.github.com", "acme/web"), "pat", Draft());

        Assert.Equal("88", issue.Key);

        using var body = JsonDocument.Parse(http.Requests[0].Body!);
        var labels = body.RootElement.GetProperty("labels").EnumerateArray()
            .Select(l => l.GetString()).ToList();

        Assert.Contains("priority:Highest", labels);
        Assert.False(provider.Capabilities.SupportsPriority);
    }

    [Fact]
    public async Task GitHubPinsTheApiVersion()
    {
        var http = new FakeOutboundHttpClient()
            .EnqueueJson("""{"id":1,"number":88,"state":"open"}""", 201);

        var provider = new GitHubIssueTrackerProvider(Log, http);

        await provider.CreateIssueAsync(
            Connection(IssueTrackerProviderKind.GitHub, "https://api.github.com", "acme/web"), "pat", Draft());

        // Pinning is what stops a GitHub-side default change from altering the response shape under a
        // running deployment.
        Assert.Equal("2022-11-28", http.Requests[0].Headers["X-GitHub-Api-Version"]);
    }

    [Fact]
    public async Task GitHubReportsARepositoryWithIssuesDisabled()
    {
        var http = new FakeOutboundHttpClient()
            .EnqueueJson("""{"full_name":"acme/web","has_issues":false}""");

        var provider = new GitHubIssueTrackerProvider(Log, http);

        var result = await provider.TestConnectionAsync(
            Connection(IssueTrackerProviderKind.GitHub, "https://api.github.com", "acme/web"), "pat");

        // Otherwise the test passes and every create fails.
        Assert.False(result.Success);
        Assert.Contains("Issues disabled", result.Message);
    }

    [Fact]
    public async Task GitHubExplainsThat404MayMeanAPrivateRepository()
    {
        var http = new FakeOutboundHttpClient().EnqueueFailure(404);

        var provider = new GitHubIssueTrackerProvider(Log, http);

        var result = await provider.TestConnectionAsync(
            Connection(IssueTrackerProviderKind.GitHub, "https://api.github.com", "acme/web"), "pat");

        Assert.False(result.Success);
        Assert.Contains("private repository", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GitHubVerifiesTheSignatureInFixedTime()
    {
        const string body = """{"action":"closed","issue":{"id":1,"number":88,"state":"closed"}}""";
        const string secret = "whsec";

        var expected = "sha256=" + Convert.ToHexString(
            System.Security.Cryptography.HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        Assert.True(GitHubIssueTrackerProvider.VerifySignature(body, secret, expected));
        Assert.False(GitHubIssueTrackerProvider.VerifySignature(body, "wrong", expected));
        Assert.False(GitHubIssueTrackerProvider.VerifySignature(body + " ", secret, expected));
        Assert.False(GitHubIssueTrackerProvider.VerifySignature(body, secret, null));
    }

    [Fact]
    public void GitHubRefusesAnUnsignedWebhookAndOneWithNoConfiguredSecret()
    {
        var provider = new GitHubIssueTrackerProvider(Log, new FakeOutboundHttpClient());
        var connection = Connection(IssueTrackerProviderKind.GitHub, "https://api.github.com", "acme/web");

        const string body = """{"issue":{"id":1,"number":88,"state":"closed"}}""";

        // An unauthenticated caller must not be able to close findings.
        Assert.Null(provider.ParseWebhook(connection, null, body,
            new System.Collections.Generic.Dictionary<string, string>()));

        Assert.Null(provider.ParseWebhook(connection, "whsec", body,
            new System.Collections.Generic.Dictionary<string, string>
            {
                [GitHubIssueTrackerProvider.SignatureHeader] = "sha256=deadbeef"
            }));
    }

    [Fact]
    public void GitHubParsesAVerifiedWebhook()
    {
        var provider = new GitHubIssueTrackerProvider(Log, new FakeOutboundHttpClient());
        var connection = Connection(IssueTrackerProviderKind.GitHub, "https://api.github.com", "acme/web");

        const string body = """{"issue":{"id":1,"number":88,"state":"closed","title":"x"}}""";
        const string secret = "whsec";

        var signature = "sha256=" + Convert.ToHexString(
            System.Security.Cryptography.HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes(body))).ToLowerInvariant();

        var issue = provider.ParseWebhook(connection, secret, body,
            new System.Collections.Generic.Dictionary<string, string>
            {
                [GitHubIssueTrackerProvider.SignatureHeader] = signature
            });

        Assert.Equal("88", issue!.Key);
        Assert.True(issue.IsClosed);
    }

    [Theory]
    [InlineData("88", "88")]
    [InlineData("#88", "88")]
    [InlineData("https://github.com/acme/web/issues/88", "88")]
    public void GitHubAcceptsEveryShapeAPersonPastes(string input, string expected)
    {
        Assert.Equal(expected, GitHubIssueTrackerProvider.Number(input));
    }

    // --- GitLab -----------------------------------------------------------------------------

    [Fact]
    public async Task GitLabUrlEncodesAPathProjectAndAddressesIssuesByIid()
    {
        var http = new FakeOutboundHttpClient()
            .EnqueueJson("""{"id":9001,"iid":12,"state":"opened","web_url":"https://gitlab.com/g/p/-/issues/12"}""", 201);

        var provider = new GitLabIssueTrackerProvider(Log, http);

        var issue = await provider.CreateIssueAsync(
            Connection(IssueTrackerProviderKind.GitLab, "https://gitlab.com", "group/sub/project"),
            "glpat", Draft());

        // iid, not the global id: using the global id in a path is the classic GitLab mistake and
        // produces a 404 against a project that does have the issue.
        Assert.Equal("12", issue.Key);
        Assert.Equal("9001", issue.Id);

        Assert.Contains("projects/group%2Fsub%2Fproject/issues", http.Requests[0].Url);
        Assert.Equal("glpat", http.Requests[0].Headers["PRIVATE-TOKEN"]);
    }

    [Fact]
    public void GitLabLeavesANumericProjectIdAlone()
    {
        Assert.Equal("451", GitLabIssueTrackerProvider.ProjectId(
            Connection(IssueTrackerProviderKind.GitLab, "https://gitlab.com", "451")));
    }

    [Fact]
    public async Task GitLabClosesAnIssueWithAStateEvent()
    {
        var http = new FakeOutboundHttpClient()
            .RuleFor("/notes", "{}")
            .RuleFor("/issues/12", """{"id":1,"iid":12,"state":"closed"}""");

        var provider = new GitLabIssueTrackerProvider(Log, http);

        var issue = await provider.UpdateIssueAsync(
            Connection(IssueTrackerProviderKind.GitLab, "https://gitlab.com", "g/p"), "t", "12",
            "NetRisk closed this.", "close");

        Assert.True(issue.IsClosed);

        var update = http.Requests.Last(r => r.Method == "PUT");
        // A state field would be ignored; GitLab takes a state_event.
        Assert.Contains("state_event", update.Body!);
    }

    [Fact]
    public async Task GitLabRefusesAStateItDoesNotHave()
    {
        var provider = new GitLabIssueTrackerProvider(Log, new FakeOutboundHttpClient());

        var thrown = await Assert.ThrowsAsync<IntegrationRequestException>(() =>
            provider.UpdateIssueAsync(
                Connection(IssueTrackerProviderKind.GitLab, "https://gitlab.com", "g/p"), "t", "12",
                null, "In Review"));

        Assert.Contains("close", thrown.Message);
    }

    [Fact]
    public void GitLabComparesTheWebhookTokenAndReadsObjectAttributes()
    {
        var provider = new GitLabIssueTrackerProvider(Log, new FakeOutboundHttpClient());
        var connection = Connection(IssueTrackerProviderKind.GitLab, "https://gitlab.com", "g/p");

        const string body = """{"object_attributes":{"id":1,"iid":12,"state":"closed"}}""";

        var headers = new System.Collections.Generic.Dictionary<string, string>
        {
            [GitLabIssueTrackerProvider.TokenHeader] = "shared"
        };

        var issue = provider.ParseWebhook(connection, "shared", body, headers);

        // GitLab's issue hook nests the issue under object_attributes, not "issue".
        Assert.Equal("12", issue!.Key);

        headers[GitLabIssueTrackerProvider.TokenHeader] = "wrong";
        Assert.Null(provider.ParseWebhook(connection, "shared", body, headers));
    }

    // --- Azure DevOps -----------------------------------------------------------------------

    [Fact]
    public async Task AzureDevOpsCreatesAWorkItemWithAJsonPatchDocument()
    {
        var http = new FakeOutboundHttpClient()
            .EnqueueJson("""{"id":4712,"fields":{"System.State":"New","System.Title":"x"}}""", 200);

        var provider = new AzureDevOpsIssueTrackerProvider(Log, http);

        var draft = Draft();
        draft.Priority = "1";

        var issue = await provider.CreateIssueAsync(
            Connection(IssueTrackerProviderKind.AzureDevOps, "https://dev.azure.com/acme", "Platform"),
            "pat", draft);

        Assert.Equal("4712", issue.Key);

        var request = Assert.Single(http.Requests);

        // The work-item type is part of the URL, and the body is a patch document rather than an object.
        Assert.Contains("/wit/workitems/$Bug", request.Url);
        Assert.Equal("application/json-patch+json", request.ContentType);

        using var body = JsonDocument.Parse(request.Body!);
        var operations = body.RootElement.EnumerateArray().ToList();

        Assert.Contains(operations, o => o.GetProperty("path").GetString() == "/fields/System.Title");
        Assert.Contains(operations,
            o => o.GetProperty("path").GetString() == "/fields/Microsoft.VSTS.Common.Priority");
    }

    [Fact]
    public void AzureDevOpsEncodesDescriptionHtmlBecauseTheFieldIsHtml()
    {
        var html = AzureDevOpsIssueTrackerProvider.Html("<script>alert(1)</script>\nsecond line");

        // An unescaped finding title here is an HTML injection into someone's work item.
        Assert.DoesNotContain("<script>", html);
        Assert.Contains("<br/>", html);
    }

    [Fact]
    public async Task AzureDevOpsRecognisesTheSignInPageAnInvalidPatProduces()
    {
        var http = new FakeOutboundHttpClient().EnqueueFailure(203, "<html>sign in</html>");

        var provider = new AzureDevOpsIssueTrackerProvider(Log, http);

        var result = await provider.TestConnectionAsync(
            Connection(IssueTrackerProviderKind.AzureDevOps, "https://dev.azure.com/acme", "Platform"), "pat");

        // ADO answers an invalid PAT with a 203 and a sign-in page rather than a 401, which is the single
        // most confusing thing about this API.
        Assert.False(result.Success);
        Assert.Contains("PAT", result.Message);
    }

    [Fact]
    public async Task AzureDevOpsRecognisesAnHtmlBodyBehindA200()
    {
        var http = new FakeOutboundHttpClient().EnqueueJson("<html>sign in</html>");

        var provider = new AzureDevOpsIssueTrackerProvider(Log, http);

        var result = await provider.TestConnectionAsync(
            Connection(IssueTrackerProviderKind.AzureDevOps, "https://dev.azure.com/acme", "Platform"), "pat");

        Assert.False(result.Success);
        Assert.Contains("sign-in page", result.Message);
    }

    [Fact]
    public void AzureDevOpsWebhookReadsTheRevisionOfAnUpdateHook()
    {
        var provider = new AzureDevOpsIssueTrackerProvider(Log, new FakeOutboundHttpClient());

        const string body = """
            {"resource":{"id":4712,"revision":{"id":4712,"fields":{"System.State":"Closed"}}}}
            """;

        var issue = provider.ParseWebhook(
            Connection(IssueTrackerProviderKind.AzureDevOps, "https://dev.azure.com/acme", "Platform"),
            null, body, new System.Collections.Generic.Dictionary<string, string>());

        Assert.Equal("4712", issue!.Key);
        Assert.True(issue.IsClosed);
    }
}

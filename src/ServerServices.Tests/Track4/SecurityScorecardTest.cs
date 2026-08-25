using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Contracts.Importers;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Integrations;
using Serilog;
using ServerServices.Integrations.SecurityScorecard;
using ServerServices.Interfaces;
using ServerServices.Tests.Mock;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// SecurityScorecard posture, factor history and finding ingestion
/// (Track 4 milestone 4.5).
///
/// Two mappings here are easy to get backwards and would be silently wrong if they were: the API
/// authenticates with <c>Authorization: Token</c> rather than <c>Bearer</c>, and its score is 0–100
/// where *higher is better* while NetRisk's Cyber Risk Index is 0–100 where higher is worse. An A-rated
/// company must end up with a low index, not a high one.
/// </summary>
[TestSubject(typeof(SecurityScorecardService))]
public class SecurityScorecardTest : InMemoryServiceTestBase
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private readonly ISecurityScorecardService _svc;

    private static readonly DateTime Now = new(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);

    public SecurityScorecardTest()
    {
        _svc = GetService<ISecurityScorecardService>();

        Seed(ctx =>
        {
            ctx.Entities.Add(new Entity
            {
                Id = 7, DefinitionName = "Acme", DefinitionVersion = "1", Status = "active"
            });

            ctx.SlaConfigurations.AddRange(
                Sla(4, 2, 15), Sla(3, 5, 30), Sla(2, 10, 60), Sla(1, 15, 90));
        });
    }

    private static SlaConfiguration Sla(int severity, int triage, int remediation) => new()
    {
        Severity = severity, MaxTriageDays = triage, MaxRemediationDays = remediation,
        EffectiveFrom = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc),
        CreatedAt = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)
    };

    private static SecurityScorecardConnection Entity() => new()
    {
        Id = 1, Name = "acme", Domain = "acme.com", BaseUrl = "https://api.securityscorecard.io"
    };

    private Task<SecurityScorecardConnectionView> ConnectionAsync(bool syncVulnerabilities = true,
        bool syncIssues = true, string domain = "acme.com", string name = "Acme scorecard") =>
        _svc.CreateConnectionAsync(new SecurityScorecardConnection
        {
            Name = name,
            Domain = domain,
            BaseUrl = "https://api.securityscorecard.io",
            EntityId = 7,
            Enabled = true,
            SyncIntervalHours = 24,
            SyncVulnerabilities = syncVulnerabilities,
            SyncIssues = syncIssues
        }, "api-token");

    private void StubApi(string? company = null, string? factors = null, string? vulnerabilities = null,
        string? issues = null)
    {
        FakeOutboundHttpClient
            .RuleFor("/issues/potentially_vulnerable", vulnerabilities ?? """{"entries":[]}""")
            .RuleFor("/factors", factors ?? """{"entries":[]}""")
            .RuleFor("/issues", issues ?? """{"entries":[]}""")
            .RuleFor("/companies/acme.com", company ?? """{"domain":"acme.com","name":"Acme","score":88,"grade":"B"}""");
    }

    // --- client parsing ---------------------------------------------------------------------

    [Fact]
    public async Task TheTokenTravelsAsTokenNotBearer()
    {
        var http = new FakeOutboundHttpClient()
            .EnqueueJson("""{"domain":"acme.com","score":88,"grade":"B"}""");

        await new SecurityScorecardClient(Log, http).TestAsync(Entity(), "k");

        // SecurityScorecard rejects Bearer outright, and the resulting 401 against a valid key is
        // exactly the puzzling failure this assertion prevents.
        Assert.Equal("Token k", http.Requests[0].Headers["Authorization"]);
    }

    [Fact]
    public async Task A404OnTheCompanyEndpointExplainsTheLikelyCause()
    {
        var http = new FakeOutboundHttpClient().EnqueueFailure(404);

        var result = await new SecurityScorecardClient(Log, http).TestAsync(Entity(), "k");

        Assert.False(result.Success);
        Assert.Contains("registered domain", result.Message);
    }

    [Fact]
    public async Task A401NamesTheHeaderShape()
    {
        var http = new FakeOutboundHttpClient().EnqueueFailure(401);

        var result = await new SecurityScorecardClient(Log, http).TestAsync(Entity(), "k");

        Assert.Contains("Authorization: Token", result.Message);
    }

    [Fact]
    public void FactorsAreParsedIntoTheirNamesAndScores()
    {
        var company = SecurityScorecardClient.ParseCompany("acme.com",
            """{"domain":"acme.com","name":"Acme","score":"88","grade":"B","last_seen":"2026-08-24T00:00:00Z"}""");

        // "score" has appeared as both a number and a numeric string in this API.
        Assert.Equal(88, company!.Score);
        Assert.Equal("B", company.Grade);
        Assert.NotNull(company.LastSeen);
    }

    [Fact]
    public void IssuesAreParsedIncludingCveAndTarget()
    {
        var issues = new System.Collections.Generic.List<SecurityScorecardIssue>();

        var count = SecurityScorecardClient.ParseIssues("""
            {"entries":[
              {"type":"spf_record_missing","severity":"medium","factor":"dns_health",
               "hostname":"mail.acme.com","first_seen_time":"2026-01-01T00:00:00Z"},
              {"type":"patching_cadence_high","severity":"high","cve":"CVE-2026-9999",
               "ip_address":"203.0.113.7","port":"443","cvss":8.1}
            ]}
            """, isVulnerability: false, issues);

        Assert.Equal(2, count);

        var spf = issues.Single(i => i.Type == "spf_record_missing");
        Assert.Equal("mail.acme.com", spf.Target);
        Assert.Equal("dns_health", spf.FactorName);
        Assert.NotNull(spf.FirstSeen);
        Assert.False(spf.IsVulnerability);

        var cve = issues.Single(i => i.CveId == "CVE-2026-9999");
        // A CVE makes it a vulnerability even when it came from the general issues endpoint.
        Assert.True(cve.IsVulnerability);
        Assert.Equal(8.1, cve.CvssScore);
        Assert.Equal("443", cve.Port);
    }

    [Fact]
    public async Task IssuePagesAreFollowedUntilAShortPage()
    {
        var http = new FakeOutboundHttpClient();

        var full = "{\"entries\":[" + string.Join(",",
            Enumerable.Range(0, 500).Select(i => $$"""{"type":"issue_{{i}}","severity":"low"}""")) + "]}";

        http.EnqueueJson(full).EnqueueJson("""{"entries":[{"type":"last_one","severity":"low"}]}""");

        var issues = await new SecurityScorecardClient(Log, http).GetIssuesAsync(Entity(), "k");

        Assert.Equal(501, issues.Count);
        Assert.Equal(2, http.Requests.Count);
        Assert.Contains("offset=500", http.Requests[1].Url);
    }

    [Fact]
    public async Task A404OnTheIssuesEndpointMeansNoneRatherThanAnError()
    {
        var http = new FakeOutboundHttpClient().EnqueueFailure(404);

        // A domain with no issues of that kind is a good outcome.
        Assert.Empty(await new SecurityScorecardClient(Log, http).GetIssuesAsync(Entity(), "k"));
    }

    [Fact]
    public async Task AFailedFactorsCallIsAnIntegrationFailure()
    {
        var http = new FakeOutboundHttpClient().EnqueueFailure(500);

        await Assert.ThrowsAsync<IntegrationRequestException>(
            () => new SecurityScorecardClient(Log, http).GetFactorsAsync(Entity(), "k"));
    }

    // --- connection validation --------------------------------------------------------------

    [Theory]
    [InlineData("https://acme.com")]
    [InlineData("acme.com/path")]
    [InlineData("someone@acme.com")]
    [InlineData("acme")]
    public async Task ADomainThatIsAUrlOrAnAddressIsRefused(string domain)
    {
        // Typing a URL here produces a 404 from SecurityScorecard that reads as "no scorecard exists"
        // rather than "you typed a URL".
        var thrown = await Assert.ThrowsAsync<InvalidParameterException>(() => ConnectionAsync(domain: domain));

        Assert.Contains("bare registered domain", thrown.Message);
    }

    [Fact]
    public async Task TheDomainIsLowerCasedAndTheBaseUrlDefaulted()
    {
        var view = await _svc.CreateConnectionAsync(new SecurityScorecardConnection
        {
            Name = "Acme", Domain = "ACME.com", BaseUrl = "", SyncIntervalHours = 24
        }, "t");

        Assert.Equal("acme.com", view.Domain);
        Assert.Equal("https://api.securityscorecard.io", view.BaseUrl);
    }

    [Fact]
    public async Task TheTokenIsStoredEncryptedAndNeverReturned()
    {
        await ConnectionAsync();

        await using var db = OpenContext();
        Assert.NotEqual("api-token", db.SecurityScorecardConnections.Single().EncryptedApiToken);
        Assert.Null(typeof(SecurityScorecardConnectionView).GetProperty("ApiToken"));
    }

    [Fact]
    public async Task DuplicateConnectionNamesAreRefused()
    {
        await ConnectionAsync();

        await Assert.ThrowsAsync<InvalidParameterException>(() => ConnectionAsync());
    }

    // --- posture ----------------------------------------------------------------------------

    [Fact]
    public async Task TheOverallScoreIsInvertedIntoTheCyberRiskIndex()
    {
        var view = await ConnectionAsync(syncVulnerabilities: false, syncIssues: false);

        StubApi(company: """{"domain":"acme.com","name":"Acme","score":88,"grade":"B"}""");

        var result = await _svc.SyncAsync(view.Id);

        // 88 out of 100 where higher is better becomes an index of 12 where higher is worse. Getting
        // this backwards would report a well-rated company as the riskiest entity in the register.
        Assert.Equal(12, result.CyberRiskIndex);

        await using var db = OpenContext();
        var entity = db.Entities.Single(e => e.Id == 7);

        Assert.Equal(12, entity.CyberRiskIndex);
        Assert.Equal("B", entity.PostureGrade);
        Assert.Equal(SecurityScorecardService.ProviderName, entity.PostureSource);
    }

    [Fact]
    public async Task EveryFactorIsAppendedToTheHistory()
    {
        var view = await ConnectionAsync(syncVulnerabilities: false, syncIssues: false);

        StubApi(factors: """
            {"entries":[
              {"name":"network_security","score":91,"grade":"A","issue_count":2},
              {"name":"patching_cadence","score":54,"grade":"F","issue_count":31}
            ]}
            """);

        var result = await _svc.SyncAsync(view.Id);

        // Two factors plus the synthetic overall row.
        Assert.Equal(3, result.PostureRowsWritten);

        var history = await _svc.GetFactorHistoryAsync(view.Id);

        Assert.Equal(54, history.Single(f => f.FactorName == "patching_cadence").Score);
        Assert.Single(history, f => f.IsOverall);
    }

    [Fact]
    public async Task FactorHistoryIsAppendOnlySoATrendIsVisible()
    {
        var view = await ConnectionAsync(syncVulnerabilities: false, syncIssues: false);

        StubApi(factors: """{"entries":[{"name":"patching_cadence","score":54,"grade":"F"}]}""");
        await _svc.SyncAsync(view.Id);

        FakeOutboundHttpClient.Rules.Clear();
        StubApi(factors: """{"entries":[{"name":"patching_cadence","score":71,"grade":"C"}]}""");
        await _svc.SyncAsync(view.Id);

        var history = await _svc.GetFactorHistoryAsync(view.Id);

        // Overwriting yesterday's score would leave the product knowing the current value and nothing
        // about whether it is getting worse.
        Assert.Equal(2, history.Count(f => f.FactorName == "patching_cadence"));
    }

    [Fact]
    public async Task TheTenFactorNamesAreOffered()
    {
        Assert.Equal(10, SecurityScorecardFactors.All.Count);
        Assert.Contains("patching_cadence", SecurityScorecardFactors.All);
        Assert.Equal("Patching Cadence", SecurityScorecardFactors.Humanize("patching_cadence"));

        await Task.CompletedTask;
    }

    // --- issue ingestion --------------------------------------------------------------------

    [Fact]
    public async Task IssuesBecomeFindingsAgainstASyntheticDomainAsset()
    {
        var view = await ConnectionAsync();

        StubApi(issues: """
            {"entries":[
              {"type":"spf_record_missing","severity":"medium","factor":"dns_health",
               "hostname":"mail.acme.com"},
              {"type":"ssl_certificate_expired","severity":"high","hostname":"www.acme.com"}
            ]}
            """);

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(2, result.FindingsCreated);
        Assert.Equal(1, result.HostsCreated);

        await using var db = OpenContext();

        // SecurityScorecard rates a domain, not a machine; a single domain asset gives those findings
        // somewhere coherent to live in an asset-oriented register.
        var host = db.Hosts.Single();
        Assert.Equal("acme.com", host.HostName);
        Assert.Equal(SecurityScorecardService.ProviderName, host.ExternalProvider);

        var spf = db.Vulnerabilities.Single(v => v.Title!.Contains("Spf Record Missing"));
        Assert.Equal(host.Id, spf.HostId);
        Assert.Equal(SecurityScorecardService.ImporterName, spf.ImportSource);
        Assert.Contains("Dns Health", spf.Details!);
    }

    [Fact]
    public async Task DomainCvesBecomeFindingsWithTheirCveIds()
    {
        var view = await ConnectionAsync(syncIssues: false);

        StubApi(vulnerabilities: """
            {"entries":[{"type":"patching_cadence_high","cve":"CVE-2026-9999","severity":"high",
                         "ip_address":"203.0.113.7","cvss":8.1}]}
            """);

        await _svc.SyncAsync(view.Id);

        await using var db = OpenContext();
        var finding = db.Vulnerabilities.Single();

        Assert.Contains("CVE-2026-9999", finding.Cves!);
        Assert.Contains("CVE-2026-9999", finding.Title!);
        // Through the ingestion pipeline, so it has an SLA due date like any other finding.
        Assert.NotNull(finding.SlaDueDate);
    }

    [Fact]
    public async Task ARerunUpdatesRatherThanDuplicating()
    {
        var view = await ConnectionAsync(syncVulnerabilities: false);

        StubApi(issues: """{"entries":[{"type":"spf_record_missing","severity":"medium","hostname":"mail.acme.com"}]}""");

        await _svc.SyncAsync(view.Id);
        var second = await _svc.SyncAsync(view.Id);

        Assert.Equal(0, second.FindingsCreated);

        await using var db = OpenContext();
        Assert.Single(db.Vulnerabilities);
    }

    [Fact]
    public async Task EachHalfOfTheIngestionCanBeTurnedOffIndependently()
    {
        var view = await ConnectionAsync(syncVulnerabilities: false, syncIssues: false);

        StubApi(
            vulnerabilities: """{"entries":[{"type":"x","cve":"CVE-2026-1","severity":"high"}]}""",
            issues: """{"entries":[{"type":"spf_record_missing","severity":"medium"}]}""");

        await _svc.SyncAsync(view.Id);

        await using var db = OpenContext();
        Assert.Empty(db.Vulnerabilities);
    }

    [Fact]
    public void AnIssueTitleIsReadableRatherThanAMachineName()
    {
        Assert.Equal("Spf Record Missing — mail.acme.com", SecurityScorecardService.TitleFor(
            new SecurityScorecardIssue { Type = "spf_record_missing", Target = "mail.acme.com" }));

        Assert.Equal("CVE-2026-1 on 203.0.113.7", SecurityScorecardService.TitleFor(
            new SecurityScorecardIssue { Type = "x", CveId = "CVE-2026-1", Target = "203.0.113.7" }));
    }

    [Theory]
    [InlineData("critical", null, NormalizedSeverity.Critical)]
    [InlineData("high", null, NormalizedSeverity.High)]
    [InlineData("positive", null, NormalizedSeverity.None)]
    [InlineData("info", null, NormalizedSeverity.None)]
    [InlineData(null, 9.5, NormalizedSeverity.Critical)]
    [InlineData(null, null, NormalizedSeverity.Low)]
    public void SeverityMapsFromTheWordThenCvss(string? word, double? cvss, NormalizedSeverity expected)
    {
        // "positive" means good news — a finding NetRisk should not carry at all.
        Assert.Equal(expected, SecurityScorecardService.MapSeverity(new SecurityScorecardIssue
        {
            Type = "x", Severity = word, CvssScore = cvss
        }));
    }

    // --- scheduling and the log -------------------------------------------------------------

    [Fact]
    public async Task EverySyncWritesALogRow()
    {
        var view = await ConnectionAsync(syncVulnerabilities: false, syncIssues: false);

        StubApi();

        await _svc.SyncAsync(view.Id);

        var log = Assert.Single(await _svc.GetSyncLogAsync());

        Assert.Equal(IntegrationSyncStatus.Succeeded, log.Status);
        Assert.NotNull(log.FinishedAt);
    }

    [Fact]
    public async Task AFailedSyncIsRecordedOnTheConnection()
    {
        var view = await ConnectionAsync();

        FakeOutboundHttpClient.DefaultResponse = new OutboundHttpResponse { StatusCode = 500 };

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(1, result.Errors);

        var connection = await _svc.GetConnectionAsync(view.Id);
        Assert.Equal(IntegrationSyncStatus.Failed, connection.LastSyncStatus);
    }

    [Fact]
    public async Task OnlyConnectionsWhoseIntervalHasElapsedAreSynced()
    {
        var view = await ConnectionAsync(syncVulnerabilities: false, syncIssues: false);

        StubApi();

        await _svc.SyncAsync(view.Id);

        var immediately = await _svc.SyncDueConnectionsAsync(Now);
        Assert.Equal(0, immediately.PostureRowsWritten);

        var later = await _svc.SyncDueConnectionsAsync(DateTime.UtcNow.AddDays(2));
        Assert.True(later.PostureRowsWritten > 0);
    }

    [Fact]
    public async Task AnUnknownConnectionIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetConnectionAsync(404));
    }

    [Fact]
    public async Task DeletingAConnectionRemovesIt()
    {
        var view = await ConnectionAsync();

        await _svc.DeleteConnectionAsync(view.Id);

        Assert.Empty(await _svc.GetConnectionsAsync());
    }
}

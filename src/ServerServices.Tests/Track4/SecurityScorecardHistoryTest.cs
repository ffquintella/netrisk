using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
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
/// M57, T274 (S40 §6.9, §7.2, §7.3, §9.5; S39 D-12): a factor SecurityScorecard sends without a score is stored as null,
/// never 0; each factor keeps its score impact per issue type when the connection asks for it; and the score history is
/// recovered once, back to <c>history_backfill_days</c>, then only the days still pending.
/// </summary>
[TestSubject(typeof(SecurityScorecardService))]
public class SecurityScorecardHistoryTest : InMemoryServiceTestBase
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();
    private static readonly DateTime Today = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
    private readonly ISecurityScorecardService _svc;

    public SecurityScorecardHistoryTest()
    {
        _svc = GetService<ISecurityScorecardService>();
        Seed(ctx => ctx.Entities.Add(new Entity { Id = 7, DefinitionName = "Acme", DefinitionVersion = "1", Status = "active" }));
    }

    private Task<SecurityScorecardConnectionView> ConnectionAsync(int backfillDays = 0, bool storeImpact = true) =>
        _svc.CreateConnectionAsync(new SecurityScorecardConnection
        {
            Name = "Acme scorecard", Domain = "acme.com", BaseUrl = "https://api.securityscorecard.io", EntityId = 7,
            Enabled = true, SyncIntervalHours = 24, SyncVulnerabilities = false, SyncIssues = false,
            HistoryBackfillDays = backfillDays, StoreIssueImpact = storeImpact
        }, "api-token");

    private static string Day(int daysAgo) => Today.AddDays(-daysAgo).ToString("yyyy-MM-ddT00:00:00.000Z");

    /// <summary>History rules first: the fake answers with its first match, and "/factors" also matches the factor history.</summary>
    private void Stub(string factors = """{"entries":[]}""", string? scoreHistory = null, string? factorHistory = null,
        int historyStatus = 200)
    {
        FakeOutboundHttpClient
            .RuleFor("/history/factors/score", factorHistory ?? """{"entries":[]}""", historyStatus)
            .RuleFor("/history/score", scoreHistory ?? """{"entries":[]}""", historyStatus)
            .RuleFor("/factors", factors)
            .RuleFor("/companies/acme.com", """{"domain":"acme.com","name":"Acme","score":88,"grade":"B"}""");
    }

    // --- null, not 0 (D-12) ---------------------------------------------------------------------------------------------

    /// <summary>Fails on the pre-T274 code, which clamped a missing score to 0 — the worst grade there is.</summary>
    [Fact]
    public async Task TestAFactorWithoutAScoreIsStoredAsNullNotZero()
    {
        var view = await ConnectionAsync();
        Stub(factors: """{"entries":[{"name":"patching_cadence","grade":null},{"name":"dns_health","score":90,"grade":"A"}]}""");

        await _svc.SyncAsync(view.Id);

        await using var db = OpenContext();
        Assert.Null(db.SecurityScorecardFactors.Single(f => f.FactorName == "patching_cadence").Score);
        Assert.Equal(90, db.SecurityScorecardFactors.Single(f => f.FactorName == "dns_health").Score);
    }

    [Fact]
    public async Task TestTheClientReadsAMissingFactorScoreAsNull()
    {
        var http = new FakeOutboundHttpClient().EnqueueJson("""{"entries":[{"name":"ip_reputation"},{"name":"x","score":"71.6"}]}""");

        var factors = await new SecurityScorecardClient(Log, http).GetFactorsAsync(
            new SecurityScorecardConnection { Name = "a", Domain = "acme.com", BaseUrl = "https://api.securityscorecard.io" }, "k");

        Assert.Equal([null, 72], factors.Select(f => f.Score).ToArray());
    }

    // --- issue impact ---------------------------------------------------------------------------------------------------

    private const string FactorsWithImpact = """
        {"entries":[{"name":"patching_cadence","score":61,"issue_summary":[
            {"type":"patching_cadence_high","severity":"high","count":3,"total_score_impact":4.5},
            {"type":"patching_cadence_low","severity":"low"}]}]}
        """;

    [Fact]
    public async Task TestEachFactorKeepsItsScoreImpactPerIssueType()
    {
        var view = await ConnectionAsync();
        Stub(factors: FactorsWithImpact);

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(2, result.IssueSummariesWritten);
        await using var db = OpenContext();
        var factor = db.SecurityScorecardFactors.Include(f => f.IssueSummaries).Single(f => f.FactorName == "patching_cadence");
        var high = factor.IssueSummaries.Single(i => i.IssueType == "patching_cadence_high");
        Assert.Equal(("high", 3, 4.5), (high.Severity, high.IssueCount!.Value, high.TotalScoreImpact!.Value));
        var low = factor.IssueSummaries.Single(i => i.IssueType == "patching_cadence_low");
        Assert.Equal(((int?)null, (double?)null), (low.IssueCount, low.TotalScoreImpact));
    }

    [Fact]
    public async Task TestWithTheImpactOptionOffNoIssueSummaryIsKept()
    {
        var view = await ConnectionAsync(storeImpact: false);
        Stub(factors: FactorsWithImpact);

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(0, result.IssueSummariesWritten);
        await using var db = OpenContext();
        Assert.Empty(db.SecurityScorecardIssueSummaries.ToList());
    }

    // --- history backfill -----------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheScoreHistoryIsRecoveredOnceThenOnlyThePendingDays()
    {
        var view = await ConnectionAsync(backfillDays: 3);
        Stub(scoreHistory: $$"""
                {"entries":[{"date":"{{Day(5)}}","score":70},{"date":"{{Day(3)}}","score":81},
                            {"date":"{{Day(2)}}","score":82},{"date":"{{Day(1)}}"}]}
                """,
            factorHistory: $$"""{"entries":[{"date":"{{Day(2)}}","factors":[{"name":"dns_health","score":95},{"name":"ip_reputation"}]}]}""");

        var first = await _svc.SyncAsync(view.Id);

        Assert.Equal(4, first.HistoryRowsBackfilled);
        await using (var db = OpenContext())
        {
            var backfilled = db.SecurityScorecardFactors.Where(f => f.IsBackfill).OrderBy(f => f.CapturedAt).ThenBy(f => f.FactorName)
                .ToList();
            // Day −5 is outside the window and day −1 carries no score; the factor without a score is kept as null.
            Assert.Equal(
                [(Today.AddDays(-3), "overall", (int?)81), (Today.AddDays(-2), "dns_health", 95), (Today.AddDays(-2), "ip_reputation", null),
                 (Today.AddDays(-2), "overall", 82)],
                backfilled.Select(f => (f.CapturedAt, f.FactorName, f.Score)).ToArray());
            Assert.All(backfilled, f => Assert.Equal(7, f.EntityId));
            Assert.Equal(Today.AddDays(-3), db.SecurityScorecardConnections.Single().HistoryBackfilledFrom);
        }

        FakeOutboundHttpClient.Requests.Clear();
        var second = await _svc.SyncAsync(view.Id);
        Assert.Equal(0, second.HistoryRowsBackfilled);
        Assert.DoesNotContain(FakeOutboundHttpClient.Requests, r => r.Url.Contains("/history/"));

        // Raising the window recovers only the days before what is already covered.
        await _svc.UpdateConnectionAsync(new SecurityScorecardConnection
        {
            Id = view.Id, Name = view.Name, Domain = "acme.com", BaseUrl = view.BaseUrl, EntityId = 7, Enabled = true,
            SyncIntervalHours = 24, SyncVulnerabilities = false, SyncIssues = false, HistoryBackfillDays = 5,
            StoreIssueImpact = true
        }, null);
        await _svc.SyncAsync(view.Id);

        var request = FakeOutboundHttpClient.Requests.Last(r => r.Url.Contains("/history/score"));
        Assert.Contains($"from={Today.AddDays(-5):yyyy-MM-dd}&to={Today.AddDays(-4):yyyy-MM-dd}", request.Url);
        await using var after = OpenContext();
        Assert.Equal(70, after.SecurityScorecardFactors.Single(f => f.IsBackfill && f.CapturedAt == Today.AddDays(-5)).Score);
    }

    [Fact]
    public async Task TestADayASyncAlreadyReadIsNotDuplicatedByTheBackfill()
    {
        var view = await ConnectionAsync(backfillDays: 3);
        Seed(ctx => ctx.SecurityScorecardFactors.Add(new SecurityScorecardFactor
        {
            ConnectionId = view.Id, EntityId = 7, FactorName = "overall", Score = 80, IsOverall = true,
            CapturedAt = Today.AddDays(-2).AddHours(4)
        }));
        Stub(scoreHistory: $$"""{"entries":[{"date":"{{Day(2)}}","score":82},{"date":"{{Day(1)}}","score":83}]}""");

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(1, result.HistoryRowsBackfilled);
        await using var db = OpenContext();
        Assert.Equal(80, db.SecurityScorecardFactors.Single(f => f.IsOverall && f.CapturedAt.Date == Today.AddDays(-2)).Score);
    }

    [Fact]
    public async Task TestAFailedBackfillIsReportedAndStaysPending()
    {
        var view = await ConnectionAsync(backfillDays: 3);
        Stub(historyStatus: 403);

        var result = await _svc.SyncAsync(view.Id);

        Assert.Equal(1, result.Errors);
        Assert.Contains(result.Messages, m => m.Contains("score history"));
        Assert.True(result.PostureRowsWritten > 0);
        await using var db = OpenContext();
        Assert.Null(db.SecurityScorecardConnections.Single().HistoryBackfilledFrom);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(366)]
    public async Task TestABackfillWindowOutside0To365DaysIsRefused(int days)
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() => ConnectionAsync(backfillDays: days));
    }

    [Fact]
    public async Task TestTheIndexSkipsAnOverallRowWithoutAScore()
    {
        var view = await ConnectionAsync();
        Seed(ctx => ctx.SecurityScorecardFactors.Add(new SecurityScorecardFactor
        {
            ConnectionId = view.Id, EntityId = 7, FactorName = "overall", Score = null, IsOverall = true, CapturedAt = DateTime.UtcNow
        }));

        await using var db = OpenContext();
        var set = await ServerServices.RiskIndex.RiskIndexObjectCollector.CollectAsync(db,
            new Model.RiskIndex.RiskIndexParameters { EnabledSources = [Model.RiskIndex.RiskIndexSources.SscTotal] },
            DAL.Enums.RiskIndexMode.Interim, DateTime.UtcNow);

        var domain = set.Rows.Single(r => r.Key.Kind == DAL.Enums.RiskIndexObjectKind.ExternalDomain);
        Assert.Equal(DAL.Enums.RiskIndexObjectState.Presumed, domain.State);
        Assert.Empty(domain.Factors);
    }
}

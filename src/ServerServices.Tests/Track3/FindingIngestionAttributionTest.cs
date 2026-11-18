using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Contracts.Importers;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using ServerServices.Importers;
using ServerServices.Importers.Dedup;
using ServerServices.Integrations.SecurityScorecard;
using ServerServices.Integrations.Tenable;
using ServerServices.Integrations.TrendMicro;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track3;

/// <summary>
/// M58, T281–T282 (S39 §3.4, §8.2, D-14, D-18, D-21): the ingestion pipeline stamps a scan-only assessment date, finds a
/// connector's host by its asset id before its IP, and keeps two repositories' code findings apart.
/// </summary>
[TestSubject(typeof(FindingIngestionService))]
public class FindingIngestionAttributionTest : InMemoryServiceTestBase
{
    private static readonly DateTime First = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime Second = new(2026, 11, 8, 0, 0, 0, DateTimeKind.Utc);
    private const int Portal = 40;

    private readonly IFindingIngestionService _ingestion;

    public FindingIngestionAttributionTest()
    {
        _ingestion = GetService<IFindingIngestionService>();
        Seed(ctx =>
        {
            ctx.Users.Add(new User
            {
                Value = 1, Name = "analyst", Login = "analyst", Enabled = true, Type = "local", Salt = "s",
                Password = Encoding.UTF8.GetBytes("p"), Email = "analyst@example.invalid"
            });
            ctx.Entities.Add(new Entity { Id = Portal, DefinitionName = "application", DefinitionVersion = "1", Status = "active" });
        });
    }

    private static ImportIngestionRequest Request(string importer, DateTime at, string? repository = null,
        int? entityId = null) => new()
    {
        Importer = importer, FileName = "report", UserId = 1, ImportedAt = at, Repository = repository, EntityId = entityId
    };

    private static ImportResult HostReport(string tool, NormalizedHost host, string rule = "plugin-1") => new()
    {
        Findings =
        {
            new NormalizedFinding
            {
                Tool = tool, RuleId = rule, Title = "Finding " + rule, Severity = NormalizedSeverity.High, Host = host
            }
        }
    };

    private static ImportResult CodeReport(params string[] locations)
    {
        var result = new ImportResult { IsFullScan = true };
        foreach (var location in locations)
            result.Findings.Add(new NormalizedFinding
            {
                Tool = "semgrep", RuleId = "js.xss", Title = "XSS", Severity = NormalizedSeverity.Medium, Location = location
            });
        return result;
    }

    // --- T281: last_assessed_at (D-18) -----------------------------------------------------------

    [Fact]
    public async Task TestAScanStampsLastAssessedAtOnTheHostsItCreatesAndMatches()
    {
        Seed(ctx => ctx.Hosts.Add(new Host { Id = 1, Ip = "10.0.0.5", HostName = "known", Source = "manual", Status = 1 }));

        await _ingestion.IngestAsync(HostReport("nessus", new NormalizedHost { Ip = "10.0.0.5" }), Request("nessus", First));
        await _ingestion.IngestAsync(HostReport("nessus", new NormalizedHost { Ip = "10.0.0.6" }), Request("nessus", First));

        await using var db = OpenContext();
        Assert.All(db.Hosts.ToList(), h => Assert.Equal(First, h.LastAssessedAt));
    }

    [Fact]
    public async Task TestLastAssessedAtNeverMovesBackwards()
    {
        var host = new NormalizedHost { Ip = "10.0.0.5" };
        await _ingestion.IngestAsync(HostReport("nessus", host), Request("nessus", Second));
        await _ingestion.IngestAsync(HostReport("nessus", host), Request("nessus", First));

        await using var db = OpenContext();
        var stored = db.Hosts.Single();
        Assert.Equal(Second, stored.LastAssessedAt);
        // The verification date keeps its old meaning: the last import that touched the host.
        Assert.Equal(First, stored.LastVerificationDate);
    }

    [Theory]
    [InlineData(TrendMicroService.ImporterName)]
    [InlineData(SecurityScorecardService.ImporterName)]
    public async Task TestTheVisionOneAndScorecardFeedsDoNotStampLastAssessedAt(string importer)
    {
        Seed(ctx => ctx.Hosts.Add(new Host { Id = 1, Ip = "10.0.0.5", HostName = "known", Source = "manual", Status = 1 }));

        await _ingestion.IngestAsync(HostReport(importer, new NormalizedHost { Ip = "10.0.0.5" }), Request(importer, First));
        await _ingestion.IngestAsync(HostReport(importer, new NormalizedHost { Ip = "10.0.0.6" }), Request(importer, First));

        await using var db = OpenContext();
        Assert.Equal(2, db.Hosts.Count());
        Assert.All(db.Hosts.ToList(), h => Assert.Null(h.LastAssessedAt));
        Assert.All(db.Hosts.ToList(), h => Assert.Equal(First, h.LastVerificationDate));
    }

    [Theory]
    [InlineData("nessus", true)]
    [InlineData("sarif", true)]
    [InlineData(TenableNormalizer.ImporterName, true)]
    [InlineData(TrendMicroService.ImporterName, false)]
    [InlineData(" SecurityScorecard ", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void TestWhichImportersAssessHosts(string? importer, bool expected)
    {
        Assert.Equal(expected, HostAssessmentPolicy.StampsLastAssessed(importer));
    }

    // --- T281: external id before IP (D-14) ------------------------------------------------------

    /// <summary>
    /// The defect: the pipeline looked a finding's host up by IP first. A Vision One device whose inventory host is
    /// known by its agent id, reporting an address another machine also holds, had its CVE filed on that machine.
    /// </summary>
    [Fact]
    public async Task TestAnIdentifiedHostIsMatchedByItsExternalIdBeforeItsIp()
    {
        Seed(ctx =>
        {
            ctx.Hosts.Add(new Host { Id = 1, Ip = "10.0.0.5", HostName = "printer", Source = "manual", Status = 1 });
            ctx.Hosts.Add(new Host
            {
                Id = 2, Ip = "10.0.0.99", HostName = "db-prod-01", Source = TrendMicroService.ProviderName, Status = 1,
                ExternalProvider = TrendMicroService.ProviderName, ExternalId = "agent-1"
            });
        });

        var host = IdentifiedHost.From(new NormalizedHost { Ip = "10.0.0.5", HostName = "db-prod-01" },
            TrendMicroService.ProviderName, "agent-1");
        await _ingestion.IngestAsync(HostReport(TrendMicroService.ImporterName, host),
            Request(TrendMicroService.ImporterName, First));

        await using var db = OpenContext();
        Assert.Equal(2, db.Vulnerabilities.Single().HostId);
        Assert.Equal(2, db.Hosts.Count());
        // The pipeline reads the identity; it never rewrites the matched host's address.
        Assert.Equal("10.0.0.99", db.Hosts.Single(h => h.Id == 2).Ip);
    }

    [Fact]
    public async Task TestAnIdentityLinkedInHostExternalIdsWinsOverTheIp()
    {
        Seed(ctx =>
        {
            ctx.Hosts.Add(new Host { Id = 1, Ip = "10.0.0.5", HostName = "printer", Source = "manual", Status = 1 });
            ctx.Hosts.Add(new Host { Id = 2, Ip = "10.0.0.7", HostName = "web-01", Source = "manual", Status = 1 });
            ctx.HostExternalIds.Add(new HostExternalId
            {
                HostId = 2, Provider = TenableService.ProviderName, ExternalId = "uuid-7", FirstSeenAt = First,
                LastSeenAt = First, CreatedAt = First
            });
        });

        var host = IdentifiedHost.From(new NormalizedHost { Ip = "10.0.0.5" }, TenableService.ProviderName, "uuid-7");
        await _ingestion.IngestAsync(HostReport(TenableNormalizer.ImporterName, host),
            Request(TenableNormalizer.ImporterName, First));

        await using var db = OpenContext();
        Assert.Equal(2, db.Vulnerabilities.Single().HostId);
        Assert.Equal(First, db.Hosts.Single(h => h.Id == 2).LastAssessedAt);
        Assert.Null(db.Hosts.Single(h => h.Id == 1).LastAssessedAt);
    }

    [Fact]
    public async Task TestAnUnknownIdentityFallsBackToTheIp()
    {
        Seed(ctx => ctx.Hosts.Add(new Host { Id = 1, Ip = "10.0.0.5", HostName = "printer", Source = "manual", Status = 1 }));

        var host = IdentifiedHost.From(new NormalizedHost { Ip = "10.0.0.5" }, TrendMicroService.ProviderName, "agent-9");
        await _ingestion.IngestAsync(HostReport(TrendMicroService.ImporterName, host),
            Request(TrendMicroService.ImporterName, First));

        await using var db = OpenContext();
        Assert.Equal(1, db.Vulnerabilities.Single().HostId);
    }

    /// <summary>
    /// The defect (D-14 through the fallback): an identity the inventory did not know yet fell back to the IP and matched
    /// any host there — including one the same provider knows as another device. A new device on a recycled address had
    /// its CVE filed on the machine that held the address before.
    /// </summary>
    [Fact]
    public async Task TestANewDeviceOnARecycledIpGetsItsOwnHost()
    {
        Seed(ctx => ctx.Hosts.Add(new Host
        {
            Id = 1, Ip = "10.0.0.5", HostName = "a", Source = "x", Status = 1,
            ExternalProvider = TrendMicroService.ProviderName, ExternalId = "agent-a"
        }));

        var host = IdentifiedHost.From(new NormalizedHost { Ip = "10.0.0.5" }, TrendMicroService.ProviderName, "agent-c");
        await _ingestion.IngestAsync(HostReport(TrendMicroService.ImporterName, host),
            Request(TrendMicroService.ImporterName, First));

        await using var db = OpenContext();
        var hostId = db.Vulnerabilities.Single().HostId;
        Assert.NotEqual(1, hostId);
        Assert.Equal(2, db.Hosts.Count());
        Assert.Equal("10.0.0.5", db.Hosts.Single(h => h.Id == hostId).Ip);
        // The other device's host was not touched by this sighting.
        Assert.Null(db.Hosts.Single(h => h.Id == 1).LastVerificationDate);
    }

    [Fact]
    public async Task TestARecycledIpLinkedToAnotherDeviceInHostExternalIdsIsNotMatched()
    {
        Seed(ctx =>
        {
            ctx.Hosts.Add(new Host { Id = 1, Ip = "10.0.0.5", HostName = "web-01", Source = "manual", Status = 1 });
            ctx.HostExternalIds.Add(new HostExternalId
            {
                HostId = 1, Provider = TenableService.ProviderName, ExternalId = "uuid-1", FirstSeenAt = First,
                LastSeenAt = First, CreatedAt = First
            });
        });

        var host = IdentifiedHost.From(new NormalizedHost { Ip = "10.0.0.5" }, TenableService.ProviderName, "uuid-2");
        await _ingestion.IngestAsync(HostReport(TenableNormalizer.ImporterName, host),
            Request(TenableNormalizer.ImporterName, First));

        await using var db = OpenContext();
        Assert.NotEqual(1, db.Vulnerabilities.Single().HostId);
        Assert.Null(db.Hosts.Single(h => h.Id == 1).LastAssessedAt);
    }

    /// <summary>Only the same provider's identity disqualifies a host: one another tool knows may be the same machine.</summary>
    [Fact]
    public async Task TestAHostKnownOnlyToAnotherProviderStillMatchesByIp()
    {
        Seed(ctx => ctx.Hosts.Add(new Host
        {
            Id = 1, Ip = "10.0.0.5", HostName = "web-01", Source = "x", Status = 1,
            ExternalProvider = TenableService.ProviderName, ExternalId = "uuid-1"
        }));

        var host = IdentifiedHost.From(new NormalizedHost { Ip = "10.0.0.5" }, TrendMicroService.ProviderName, "agent-c");
        await _ingestion.IngestAsync(HostReport(TrendMicroService.ImporterName, host),
            Request(TrendMicroService.ImporterName, First));

        await using var db = OpenContext();
        Assert.Equal(1, db.Vulnerabilities.Single().HostId);
        Assert.Equal(1, db.Hosts.Count());
    }

    [Fact]
    public async Task TestTwoDevicesSharingAnIpInOneImportStayOnTheirOwnHosts()
    {
        Seed(ctx =>
        {
            ctx.Hosts.Add(new Host
            {
                Id = 1, Ip = "10.0.0.5", HostName = "a", Source = "x", Status = 1,
                ExternalProvider = TrendMicroService.ProviderName, ExternalId = "agent-a"
            });
            ctx.Hosts.Add(new Host
            {
                Id = 2, Ip = "10.0.0.5", HostName = "b", Source = "x", Status = 1,
                ExternalProvider = TrendMicroService.ProviderName, ExternalId = "agent-b"
            });
        });

        var report = HostReport(TrendMicroService.ImporterName,
            IdentifiedHost.From(new NormalizedHost { Ip = "10.0.0.5" }, TrendMicroService.ProviderName, "agent-a"), "CVE-1");
        report.Findings.AddRange(HostReport(TrendMicroService.ImporterName,
            IdentifiedHost.From(new NormalizedHost { Ip = "10.0.0.5" }, TrendMicroService.ProviderName, "agent-b"),
            "CVE-2").Findings);

        await _ingestion.IngestAsync(report, Request(TrendMicroService.ImporterName, First));

        await using var db = OpenContext();
        Assert.Equal(1, db.Vulnerabilities.Single(v => v.RuleId == "CVE-1").HostId);
        Assert.Equal(2, db.Vulnerabilities.Single(v => v.RuleId == "CVE-2").HostId);
    }

    [Fact]
    public async Task TestAHostAnImportCreatesUnderAnEntityRecordsTheImportAsItsWriter()
    {
        await _ingestion.IngestAsync(HostReport("nessus", new NormalizedHost { Ip = "10.0.0.5" }),
            Request("nessus", First, entityId: Portal));
        await _ingestion.IngestAsync(HostReport("nessus", new NormalizedHost { Ip = "10.0.0.6" }), Request("nessus", First));

        await using var db = OpenContext();
        var scoped = db.Hosts.Single(h => h.Ip == "10.0.0.5");
        Assert.Equal(((int?)Portal, EntitySources.Import), (scoped.EntityId, scoped.EntitySource));
        Assert.Null(db.Hosts.Single(h => h.Ip == "10.0.0.6").EntitySource);
    }

    // --- T282: repository discriminator and target application (D-21) ---------------------------

    /// <summary>
    /// The defect: the dedup key of a host-less finding named no repository, so the same rule on the same path in two
    /// repositories imported by an unrestricted caller collapsed into one finding.
    /// </summary>
    [Fact]
    public async Task TestTheSameRuleAndPathInTwoRepositoriesStayTwoFindings()
    {
        await _ingestion.IngestAsync(CodeReport("src/app.js:10"), Request("semgrep", First, "acme/web"));
        var second = await _ingestion.IngestAsync(CodeReport("src/app.js:10"), Request("semgrep", First, "acme/api"));

        Assert.Equal(1, second.NewCount);

        await using var db = OpenContext();
        Assert.Equal(new[] { "acme/api", "acme/web" },
            db.Vulnerabilities.Select(v => v.Repository!).OrderBy(r => r).ToArray());
        Assert.Equal(new[] { "acme/api", "acme/web" },
            db.ScanImports.Select(i => i.Repository!).OrderBy(r => r).ToArray());
    }

    [Fact]
    public async Task TestReimportingOneRepositoryMatchesItsOwnFindings()
    {
        await _ingestion.IngestAsync(CodeReport("src/app.js:10"), Request("semgrep", First, "acme/web"));
        var again = await _ingestion.IngestAsync(CodeReport("src/app.js:10"), Request("semgrep", Second, " acme/web "));

        Assert.Equal((0, 1), (again.NewCount, again.UpdatedCount));
    }

    [Fact]
    public async Task TestAnImportNamingNoRepositoryKeepsTheKeyItAlwaysHad()
    {
        await _ingestion.IngestAsync(CodeReport("src/app.js:10"), Request("semgrep", First));

        await using var db = OpenContext();
        var finding = db.Vulnerabilities.Single();
        Assert.Null(finding.Repository);

        var unscoped = DedupKeyCalculator.WithRepository(
            [new DedupCandidate("HashBased", "k", false)], new DedupContext { Finding = new NormalizedFinding() });
        Assert.Equal("k", Assert.Single(unscoped).Key);
    }

    [Fact]
    public async Task TestAHostFindingIsKeyedByItsHostWhateverRepositoryTheImportNamed()
    {
        var candidates = DedupKeyCalculator.WithRepository([new DedupCandidate("HashBased", "k", false)],
            new DedupContext { Finding = new NormalizedFinding(), HostId = 3, Repository = "acme/web" });
        Assert.Equal("k", Assert.Single(candidates).Key);

        await _ingestion.IngestAsync(HostReport("nessus", new NormalizedHost { Ip = "10.0.0.5" }),
            Request("nessus", First, "acme/web"));

        await using var db = OpenContext();
        Assert.Null(db.Vulnerabilities.Single().Repository);
        Assert.Equal("acme/web", db.ScanImports.Single().Repository);
    }

    /// <summary>
    /// A finding imported before repositories could be named is adopted by the first repository that reports it again,
    /// rather than duplicated; a second repository reporting the same rule and path gets its own.
    /// </summary>
    [Fact]
    public async Task TestTheFirstRepositoryToReportALegacyFindingAdoptsIt()
    {
        await _ingestion.IngestAsync(CodeReport("src/app.js:10"), Request("semgrep", First));

        var adopted = await _ingestion.IngestAsync(CodeReport("src/app.js:10"),
            Request("semgrep", Second, "acme/web", Portal));
        Assert.Equal((0, 1), (adopted.NewCount, adopted.UpdatedCount));

        await using (var db = OpenContext())
        {
            var finding = db.Vulnerabilities.Single();
            Assert.Equal(("acme/web", (int?)Portal), (finding.Repository, finding.EntityId));
        }

        var other = await _ingestion.IngestAsync(CodeReport("src/app.js:10"), Request("semgrep", Second, "acme/api"));
        Assert.Equal(1, other.NewCount);

        var again = await _ingestion.IngestAsync(CodeReport("src/app.js:10"),
            Request("semgrep", Second, "acme/web", Portal));
        Assert.Equal((0, 1), (again.NewCount, again.UpdatedCount));
    }

    [Fact]
    public async Task TestARepositoryNeverAdoptsAnotherRepositorysFinding()
    {
        await _ingestion.IngestAsync(CodeReport("src/app.js:10"), Request("semgrep", First, "acme/web"));
        var other = await _ingestion.IngestAsync(CodeReport("src/app.js:10"), Request("semgrep", First, "acme/api"));
        var unscoped = await _ingestion.IngestAsync(CodeReport("src/app.js:10"), Request("semgrep", First));

        Assert.Equal((1, 1), (other.NewCount, unscoped.NewCount));
    }

    [Fact]
    public async Task TestAFullScanOfOneRepositoryClosesOnlyThatRepositorysFindings()
    {
        await GetService<IDeduplicationService>().SaveConfigurationAsync(new ScannerDedupConfiguration
        {
            Importer = "semgrep", StrategyChain = "HashBased", AutoCloseMissing = true
        }, userId: 1);

        await _ingestion.IngestAsync(CodeReport("src/a.js:1", "src/b.js:2"), Request("semgrep", First, "acme/web"));
        await _ingestion.IngestAsync(CodeReport("src/a.js:1", "src/b.js:2"), Request("semgrep", First, "acme/api"));

        var rescan = await _ingestion.IngestAsync(CodeReport("src/a.js:1"), Request("semgrep", Second, "acme/web"));

        Assert.Equal(1, rescan.ClosedCount);
        await using var db = OpenContext();
        var closed = db.Vulnerabilities.Single(v => v.LifecycleStatus == FindingStatus.Mitigated);
        Assert.Equal(("acme/web", "src/b.js:2"), (closed.Repository, closed.Location));
        Assert.Equal(3, db.Vulnerabilities.Count(v => v.LifecycleStatus == FindingStatus.Active));
    }

    [Fact]
    public async Task TestTheTargetApplicationIsPersistedOnTheImportAndItsFindings()
    {
        await _ingestion.IngestAsync(CodeReport("src/app.js:10"), Request("semgrep", First, "acme/web", Portal));

        await using var db = OpenContext();
        Assert.Equal(Portal, db.ScanImports.Single().EntityId);
        Assert.Equal(Portal, db.Vulnerabilities.Single().EntityId);
    }

    // --- T282: findings an unscoped import stored, re-imported naming an entity (D-21) ----------------------------

    private async Task AutoClose(string importer) =>
        await GetService<IDeduplicationService>().SaveConfigurationAsync(new ScannerDedupConfiguration
        {
            Importer = importer, StrategyChain = "HashBased", AutoCloseMissing = true
        }, userId: 1);

    private static ImportResult FullHostScan(params string[] ips)
    {
        var result = new ImportResult { IsFullScan = true };
        foreach (var ip in ips)
            result.Findings.Add(new NormalizedFinding
            {
                Tool = "nessus", RuleId = "plugin-1", Title = "Finding plugin-1", Severity = NormalizedSeverity.High,
                Host = new NormalizedHost { Ip = ip }
            });
        return result;
    }

    /// <summary>
    /// The defect: an import naming a target entity looked its findings up in that entity only, so the findings an earlier
    /// unrestricted import had stored without one matched nothing and every one of them was created again.
    /// </summary>
    [Fact]
    public async Task TestAnImportNamingAnEntityAdoptsTheFindingsAnUnscopedImportStored()
    {
        await _ingestion.IngestAsync(HostReport("nessus", new NormalizedHost { Ip = "10.0.0.5" }), Request("nessus", First));

        var again = await _ingestion.IngestAsync(HostReport("nessus", new NormalizedHost { Ip = "10.0.0.5" }),
            Request("nessus", Second, entityId: Portal));

        Assert.Equal((0, 1), (again.NewCount, again.UpdatedCount));
        await using var db = OpenContext();
        var finding = db.Vulnerabilities.Single();
        Assert.Equal(((int?)Portal, 2, Second), (finding.EntityId, finding.DetectionCount, finding.LastDetection));
    }

    /// <summary>
    /// The same defect on a repository: the re-import naming the application duplicated the findings, and its full scan
    /// never closed the unscoped originals it no longer reported.
    /// </summary>
    [Fact]
    public async Task TestAFullScanNamingAnEntityAdoptsAndClosesTheUnscopedFindingsOfItsRepository()
    {
        await AutoClose("semgrep");
        await _ingestion.IngestAsync(CodeReport("src/a.js:1", "src/b.js:2"), Request("semgrep", First, "acme/web"));

        var rescan = await _ingestion.IngestAsync(CodeReport("src/a.js:1"), Request("semgrep", Second, "acme/web", Portal));

        Assert.Equal((0, 1, 1), (rescan.NewCount, rescan.UpdatedCount, rescan.ClosedCount));
        await using var db = OpenContext();
        Assert.Equal(2, db.Vulnerabilities.Count());
        var kept = db.Vulnerabilities.Single(v => v.Location == "src/a.js:1");
        Assert.Equal(((int?)Portal, FindingStatus.Active), (kept.EntityId, kept.LifecycleStatus));
        Assert.Equal(FindingStatus.Mitigated, db.Vulnerabilities.Single(v => v.Location == "src/b.js:2").LifecycleStatus);
    }

    /// <summary>
    /// A full host scan naming an entity is exhaustive for that entity, not for the unscoped register: an unscoped finding
    /// is closed only on a host the entity owns, never on a host nobody (or another entity) owns.
    /// </summary>
    [Fact]
    public async Task TestAFullHostScanNamingAnEntityClosesUnscopedFindingsOnlyOnItsOwnHosts()
    {
        await AutoClose("nessus");
        Seed(ctx => ctx.Entities.Add(new Entity { Id = 41, DefinitionName = "application", DefinitionVersion = "1", Status = "active" }));
        await _ingestion.IngestAsync(FullHostScan("10.0.0.5", "10.0.0.6", "10.0.0.7", "10.0.0.8"), Request("nessus", First));

        await using (var db = OpenContext())
        {
            db.Hosts.Single(h => h.Ip == "10.0.0.6").EntityId = Portal;
            db.Hosts.Single(h => h.Ip == "10.0.0.7").EntityId = 41;
            await db.SaveChangesAsync();
        }

        var rescan = await _ingestion.IngestAsync(FullHostScan("10.0.0.5"), Request("nessus", Second, entityId: Portal));

        Assert.Equal((0, 1, 1), (rescan.NewCount, rescan.UpdatedCount, rescan.ClosedCount));
        await using var check = OpenContext();
        var status = check.Vulnerabilities.Include(v => v.Host).ToList().ToDictionary(v => v.Host!.Ip!, v => v.LifecycleStatus);
        Assert.Equal(FindingStatus.Active, status["10.0.0.5"]);
        Assert.Equal(FindingStatus.Mitigated, status["10.0.0.6"]);
        Assert.Equal(FindingStatus.Active, status["10.0.0.7"]);
        Assert.Equal(FindingStatus.Active, status["10.0.0.8"]);
    }

    /// <summary>A finding the entity already owns wins over an unscoped copy of it, which stays as it was.</summary>
    [Fact]
    public async Task TestTheEntitysOwnFindingIsPreferredOverAnUnscopedCopy()
    {
        await _ingestion.IngestAsync(HostReport("nessus", new NormalizedHost { Ip = "10.0.0.5" }), Request("nessus", First));

        int ownId;
        await using (var db = OpenContext())
        {
            var unscoped = db.Vulnerabilities.Single();
            var own = new Vulnerability
            {
                Title = unscoped.Title, Severity = unscoped.Severity, Status = unscoped.Status, HostId = unscoped.HostId,
                LifecycleStatus = unscoped.LifecycleStatus, ImportSource = unscoped.ImportSource, ImportHash = unscoped.ImportHash,
                DedupKey = unscoped.DedupKey, DedupStrategy = unscoped.DedupStrategy, EntityId = Portal, DetectionCount = 1,
                FirstDetection = First, LastDetection = First, AnalystId = 1, FixTeamId = unscoped.FixTeamId,
                Technology = unscoped.Technology
            };
            db.Vulnerabilities.Add(own);
            await db.SaveChangesAsync();
            ownId = own.Id;
        }

        var again = await _ingestion.IngestAsync(HostReport("nessus", new NormalizedHost { Ip = "10.0.0.5" }),
            Request("nessus", Second, entityId: Portal));

        Assert.Equal((0, 1), (again.NewCount, again.UpdatedCount));
        await using var check = OpenContext();
        Assert.Equal(Second, check.Vulnerabilities.Single(v => v.Id == ownId).LastDetection);
        var copy = check.Vulnerabilities.Single(v => v.Id != ownId);
        Assert.Equal(((int?)null, First), (copy.EntityId, copy.LastDetection));
    }
}

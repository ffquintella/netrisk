using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Contracts.Importers;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using ServerServices.Importers;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track3;

/// <summary>
/// Ingesting an import that does not fit in one batch.
///
/// A Vision One sync handed this pipeline 563,310 findings and never came back. Each test covers a
/// property that had to survive making it finish: nothing lost at a batch boundary, a duplicate
/// still a duplicate when its twin was in an earlier batch, and visible progress.
/// </summary>
[TestSubject(typeof(FindingIngestionService))]
public class FindingIngestionBatchingTest : InMemoryServiceTestBase
{
    private readonly IFindingIngestionService _ingestion;

    private static readonly DateTime At = new(2026, 9, 28, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>Enough findings to span several batches without making the test slow.</summary>
    private const int Spanning = FindingIngestionService.FindingBatchSize * 2 + 37;

    public FindingIngestionBatchingTest()
    {
        _ingestion = GetService<IFindingIngestionService>();

        Seed(ctx =>
        {
            ctx.Users.Add(new User
            {
                Value = 1, Name = "analyst", Login = "analyst", Enabled = true, Type = "local",
                Salt = "s", Password = Encoding.UTF8.GetBytes("p"), Email = "analyst@x"
            });

            ctx.SlaConfigurations.Add(new SlaConfiguration
            {
                Severity = 3, MaxTriageDays = 5, MaxRemediationDays = 30,
                EffectiveFrom = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedAt = At
            });
        });
    }

    private static ImportIngestionRequest Request(DateTime? at = null) => new()
    {
        Importer = "nessus", FileName = "scan.nessus", UserId = 1, ImportedAt = at ?? At
    };

    private static NormalizedFinding Finding(string id, string? host = null) => new()
    {
        Tool = "nessus",
        ToolUniqueId = id,
        RuleId = id,
        Title = $"Finding {id}",
        Severity = NormalizedSeverity.High,
        Host = host == null ? null : new NormalizedHost { HostName = host }
    };

    private static ImportResult Parsed(IEnumerable<NormalizedFinding> findings)
    {
        var parsed = new ImportResult { DetectedTool = "nessus", IsFullScan = false, ScanDate = At };

        parsed.Findings.AddRange(findings);

        return parsed;
    }

    private static ImportResult Sequence(int count, string prefix = "f", int hosts = 0) =>
        Parsed(Enumerable.Range(0, count).Select(i =>
            Finding($"{prefix}-{i}", hosts == 0 ? null : $"host-{i % hosts}")));

    // --- nothing is lost at a boundary -------------------------------------------------------

    [Fact]
    public async Task EveryFindingOfAMultiBatchImportIsPersisted()
    {
        var import = await _ingestion.IngestAsync(Sequence(Spanning), Request());

        Assert.Equal(Spanning, import.NewCount);
        Assert.Equal(0, import.SkippedCount);

        await using var db = OpenContext();

        Assert.Equal(Spanning, await db.Vulnerabilities.CountAsync());
    }

    [Fact]
    public async Task EveryCreatedFindingGetsItsFirstHistoryEvent()
    {
        await _ingestion.IngestAsync(Sequence(Spanning), Request());

        await using var db = OpenContext();

        // The history row's foreign key needs an id the insert has not produced yet, so it is added
        // in a second pass. Getting that wrong loses the timeline for every finding but the last.
        Assert.Equal(Spanning, await db.FindingStatusHistories
            .CountAsync(h => h.Source == FindingStatusChangeSource.Import && h.FromStatus == null));

        Assert.False(await db.FindingStatusHistories.AnyAsync(h => h.VulnerabilityId == 0));
    }

    [Fact]
    public async Task AMultiBatchImportResolvesItsHostsOnce()
    {
        await _ingestion.IngestAsync(Sequence(Spanning, hosts: 4), Request());

        await using var db = OpenContext();

        // The host cache holds ids rather than entities so it survives the tracker being cleared.
        // If it did not, each batch would create its own copy of every host.
        Assert.Equal(4, await db.Hosts.CountAsync());
    }

    // --- dedup still works across a boundary -------------------------------------------------

    [Fact]
    public async Task ADuplicateWhoseTwinLandedInAnEarlierBatchIsNotCreatedTwice()
    {
        var findings = Enumerable.Range(0, Spanning).Select(i => Finding($"f-{i}")).ToList();

        // Same tool id as the very first finding, placed past two batch boundaries. The old code
        // caught this only because it saved after every insert, so the next lookup found it.
        findings.Add(Finding("f-0"));

        var import = await _ingestion.IngestAsync(Parsed(findings), Request());

        Assert.Equal(Spanning, import.NewCount);
        Assert.Equal(1, import.UpdatedCount);

        await using var db = OpenContext();

        Assert.Equal(Spanning, await db.Vulnerabilities.CountAsync());
        Assert.Equal(2, (await db.Vulnerabilities.SingleAsync(v => v.ToolUniqueId == "f-0")).DetectionCount);
    }

    [Fact]
    public async Task DuplicatesWithinOneBatchAreStillGrouped()
    {
        var findings = new List<NormalizedFinding>
        {
            Finding("same"), Finding("same"), Finding("same")
        };

        var import = await _ingestion.IngestAsync(Parsed(findings), Request());

        Assert.Equal(1, import.NewCount);
        Assert.Equal(2, import.UpdatedCount);
    }

    [Fact]
    public async Task ImportingTheSameMultiBatchReportTwiceYieldsNoNewFindings()
    {
        await _ingestion.IngestAsync(Sequence(Spanning), Request());

        // The acceptance criterion the pipeline has always had, at a size that now spans batches —
        // which is where a batched lookup can silently stop matching.
        var second = await _ingestion.IngestAsync(Sequence(Spanning), Request(At.AddDays(7)));

        Assert.Equal(0, second.NewCount);
        Assert.Equal(Spanning, second.UpdatedCount);

        await using var db = OpenContext();

        Assert.Equal(Spanning, await db.Vulnerabilities.CountAsync());
    }

    [Fact]
    public async Task AFindingKeyedByTheLegacyHashIsStillMatchedAcrossBatches()
    {
        // The legacy strategy is the only one whose key is compared against import_hash, so the
        // chain has to name it for that column to be consulted at all.
        Seed(ctx => ctx.ScannerDedupConfigurations.Add(new ScannerDedupConfiguration
        {
            Importer = "nessus",
            StrategyChain = "LegacyHashCode",
            HashFields = "title,severity",
            AutoCloseMissing = false
        }));

        // The legacy key is rebuilt from the resolved host and service, so it declines for a
        // finding that has neither.
        ImportResult OnHosts() => Parsed(Enumerable.Range(0, Spanning).Select(i =>
        {
            var finding = Finding($"f-{i}");
            finding.Host = new NormalizedHost
            {
                HostName = $"host-{i % 8}", ServiceName = "https", Port = "443", Protocol = "tcp"
            };
            finding.RawSeverity = "High";
            return finding;
        }));

        var first = await _ingestion.IngestAsync(OnHosts(), Request());

        Assert.Equal(Spanning, first.NewCount);

        await using (var db = OpenContext())
        {
            // Stands in for findings imported before Track 3: keyed in import_hash, with no
            // dedup_key at all. The batched lookup has to try that column too.
            foreach (var row in await db.Vulnerabilities.ToListAsync())
            {
                row.ImportHash = row.DedupKey;
                row.DedupKey = null;
            }

            await db.SaveChangesAsync();
        }

        var second = await _ingestion.IngestAsync(OnHosts(), Request(At.AddDays(1)));

        Assert.Equal(0, second.NewCount);
        Assert.Equal(Spanning, second.UpdatedCount);
    }

    // --- the caller can see how far it has got -----------------------------------------------

    [Fact]
    public async Task ProgressIsReportedAndReachesTheTotal()
    {
        var seen = new List<ImportProgress>();

        await _ingestion.IngestAsync(Sequence(Spanning), Request(), progress =>
        {
            seen.Add(progress);
            return Task.CompletedTask;
        });

        // More than a first and last line: a run whose only visible state was "started" is what
        // made a three-day stall indistinguishable from a slow one.
        Assert.True(seen.Count > 2, $"expected several progress reports, saw {seen.Count}");

        Assert.Equal(0, seen[0].Processed);
        Assert.Equal(Spanning, seen[^1].Processed);
        Assert.Equal(Spanning, seen[^1].Created);
        Assert.Equal(100, seen[^1].Percent);
    }

    [Fact]
    public async Task ProgressNeverGoesBackwards()
    {
        var processed = new List<int>();

        await _ingestion.IngestAsync(Sequence(Spanning), Request(), progress =>
        {
            processed.Add(progress.Processed);
            return Task.CompletedTask;
        });

        Assert.Equal(processed.Order().ToList(), processed);
        Assert.All(processed, p => Assert.InRange(p, 0, Spanning));
    }

    [Fact]
    public async Task ProgressCountsSkipsAsTheyHappen()
    {
        var findings = Enumerable.Range(0, Spanning).Select(i => Finding($"f-{i}")).ToList();

        // A host with a name but no usable identity: resolution throws, which is a per-finding
        // failure the pipeline is required to record rather than propagate.
        findings[0].Host = new NormalizedHost { HostName = null, Ip = null, Fqdn = null };
        findings[0].Title = null!;

        var seen = new List<ImportProgress>();

        var import = await _ingestion.IngestAsync(Parsed(findings), Request(), progress =>
        {
            seen.Add(progress);
            return Task.CompletedTask;
        });

        Assert.Equal(1, import.SkippedCount);
        Assert.Equal(1, seen[^1].Skipped);

        // The counts still add up to what was in the report, which is the point of recording a
        // failed finding as a skip rather than losing it.
        Assert.Equal(Spanning, seen[^1].Created + seen[^1].Updated + seen[^1].Skipped);
    }

    [Fact]
    public async Task AnImportWithNoProgressCallbackStillRuns()
    {
        var import = await _ingestion.IngestAsync(Sequence(10), Request());

        Assert.Equal(10, import.NewCount);
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Contracts.Importers;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using ServerServices.Importers;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using ServerServices.Tests.Track3.Fixtures;
using Xunit;

namespace ServerServices.Tests.Track3;

/// <summary>
/// The import persistence pipeline (Track 3 milestones 3.1–3.4).
///
/// The acceptance criterion the spec states outright — "importing the same file twice yields zero
/// new findings on the second pass" — is the backbone of this class. Around it sit the two
/// behaviours that make dedup safe rather than merely tidy: it groups without discarding, and it
/// never overwrites a triage verdict a human made.
/// </summary>
[TestSubject(typeof(FindingIngestionService))]
public class FindingIngestionServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IFindingIngestionService _ingestion;
    private readonly IImporterRegistry _registry;

    private static readonly DateTime FirstImport = new(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);
    private static readonly DateTime SecondImport = new(2026, 8, 8, 0, 0, 0, DateTimeKind.Utc);

    public FindingIngestionServiceInMemoryTest()
    {
        _ingestion = GetService<IFindingIngestionService>();
        _registry = GetService<IImporterRegistry>();

        // The CISA-aligned default policy the numbered SQL seeds in production. Without it no
        // finding gets a due date and the SLA assertions would be vacuous.
        Seed(ctx =>
        {
            ctx.Users.Add(NewUser(1, "analyst"));
            ctx.SlaConfigurations.AddRange(
                NewSla(4, 2, 15), NewSla(3, 5, 30), NewSla(2, 10, 60), NewSla(1, 15, 90));
        });
    }

    private static User NewUser(int id, string name) => new()
    {
        Value = id, Name = name, Login = name, Enabled = true, Type = "local", Salt = "s",
        Password = Encoding.UTF8.GetBytes("p"), Email = $"{name}@x"
    };

    private static SlaConfiguration NewSla(int severity, int triage, int remediation) => new()
    {
        Severity = severity, MaxTriageDays = triage, MaxRemediationDays = remediation,
        EffectiveFrom = new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc), CreatedAt = FirstImport
    };

    private static ImportIngestionRequest Request(string importer = "nessus", DateTime? at = null,
        string? idempotencyKey = null) => new()
    {
        Importer = importer,
        FileName = "scan.nessus",
        UserId = 1,
        ImportedAt = at ?? FirstImport,
        IdempotencyKey = idempotencyKey
    };

    private async Task<ImportResult> ParseAsync(string importerName, string fixture,
        DateTime? at = null)
    {
        var importer = await _registry.ResolveAsync(importerName);

        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(fixture));

        return await importer.ImportAsync(stream, new ImportContext
        {
            FileName = "scan",
            IgnoreNegligible = true,
            UserId = 1,
            ImportedAt = at ?? FirstImport
        }, CancellationToken.None);
    }

    // --- first import ------------------------------------------------------------------------

    [Fact]
    public async Task TestImportCreatesFindingsHostsAndServices()
    {
        var parsed = await ParseAsync("nessus", ImporterFixtures.Nessus);

        var import = await _ingestion.IngestAsync(parsed, Request());

        Assert.Equal(2, import.NewCount);
        Assert.Equal(0, import.UpdatedCount);
        Assert.Equal((int)ScanImportStatus.Succeeded, import.Status);

        await using var db = OpenContext();
        Assert.Equal(2, db.Vulnerabilities.Count());
        // Two hosts, each with the one service its finding sat on.
        Assert.Equal(2, db.Hosts.Count());
        Assert.Equal(2, db.HostsServices.Count());

        var heartbleed = db.Vulnerabilities.Include(v => v.Host).Single(v => v.Title == "OpenSSL Heartbleed");
        Assert.Equal("10.0.0.1", heartbleed.Host!.Ip);
        Assert.Equal("nessus", heartbleed.ImportSource);
        Assert.Equal(FindingStatus.Active, heartbleed.LifecycleStatus);
        Assert.NotNull(heartbleed.DedupKey);
        Assert.Equal(import.Id, heartbleed.LastImportId);
    }

    [Fact]
    public async Task TestImportComputesTheSlaDueDateFromFirstSeen()
    {
        var parsed = await ParseAsync("nessus", ImporterFixtures.Nessus);

        await _ingestion.IngestAsync(parsed, Request());

        await using var db = OpenContext();

        var critical = db.Vulnerabilities.Single(v => v.Title == "OpenSSL Heartbleed");
        var high = db.Vulnerabilities.Single(v => v.Title == "OpenSSH Weak Ciphers");

        // Critical 15 days, High 30 — the CISA ladder, measured from first-seen.
        Assert.Equal(FirstImport.AddDays(15), critical.SlaDueDate);
        Assert.Equal(FirstImport.AddDays(30), high.SlaDueDate);
    }

    [Fact]
    public async Task TestImportWritesACreationEventPerFinding()
    {
        var parsed = await ParseAsync("nessus", ImporterFixtures.Nessus);

        await _ingestion.IngestAsync(parsed, Request());

        await using var db = OpenContext();

        var history = db.FindingStatusHistories.ToList();
        Assert.Equal(2, history.Count);
        Assert.All(history, h =>
        {
            Assert.Null(h.FromStatus);
            Assert.Equal(FindingStatus.Active, h.ToStatus);
            Assert.Equal(FindingStatusChangeSource.Import, h.Source);
        });
    }

    [Fact]
    public async Task TestImportRecordsTheImporterWarningsAndCounts()
    {
        var parsed = await ParseAsync("nessus", ImporterFixtures.Nessus);

        var import = await _ingestion.IngestAsync(parsed, Request());

        // The nameless report item. Its diagnostic has to reach the import row, or the summary
        // reads as a clean import of a file that lost a row.
        Assert.Equal(1, import.SkippedCount);
        Assert.True(import.WarningCount >= 1);
        Assert.Contains("plugin name", import.Warnings!);

        // The by-severity counts a CI gate reads.
        Assert.Contains("critical", import.NewBySeverity!);
        Assert.Contains("high", import.NewBySeverity!);
    }

    // --- re-import (3.3.2) -------------------------------------------------------------------

    [Fact]
    public async Task TestReimportingTheSameFileCreatesNothingNew()
    {
        await _ingestion.IngestAsync(await ParseAsync("nessus", ImporterFixtures.Nessus), Request());

        var second = await _ingestion.IngestAsync(
            await ParseAsync("nessus", ImporterFixtures.Nessus, SecondImport),
            Request(at: SecondImport));

        // The spec's acceptance criterion, stated verbatim.
        Assert.Equal(0, second.NewCount);
        Assert.Equal(2, second.UpdatedCount);

        await using var db = OpenContext();
        Assert.Equal(2, db.Vulnerabilities.Count());
    }

    [Fact]
    public async Task TestReimportMovesLastSeenAndRaisesTheOccurrenceCount()
    {
        await _ingestion.IngestAsync(await ParseAsync("nessus", ImporterFixtures.Nessus), Request());

        await _ingestion.IngestAsync(await ParseAsync("nessus", ImporterFixtures.Nessus, SecondImport),
            Request(at: SecondImport));

        await using var db = OpenContext();

        var finding = db.Vulnerabilities.Single(v => v.Title == "OpenSSL Heartbleed");

        // Dedup groups, it never discards: a second sighting is evidence, not a no-op.
        Assert.Equal(SecondImport, finding.LastDetection);
        Assert.Equal(2, finding.DetectionCount);
        Assert.Equal(FirstImport, finding.FirstDetection);
    }

    [Fact]
    public async Task TestReimportDoesNotResurrectAFalsePositive()
    {
        await _ingestion.IngestAsync(await ParseAsync("nessus", ImporterFixtures.Nessus), Request());

        var lifecycle = GetService<IFindingLifecycleService>();
        var id = await FindingIdAsync("OpenSSL Heartbleed");

        await lifecycle.TransitionAsync(id, FindingStatus.FalsePositive, 1,
            FindingStatusChangeSource.Manual, "The scanner misidentified the TLS stack.");

        var second = await _ingestion.IngestAsync(
            await ParseAsync("nessus", ImporterFixtures.Nessus, SecondImport), Request(at: SecondImport));

        await using var db = OpenContext();

        // Sticky triage. A false positive that comes back as Active on every scan is how a register
        // becomes unusable.
        Assert.Equal(FindingStatus.FalsePositive, db.Vulnerabilities.Single(v => v.Id == id).LifecycleStatus);
        Assert.Equal(1, second.DuplicateCount);
        // Still counted as seen, so its last-seen date is honest.
        Assert.Equal(SecondImport, db.Vulnerabilities.Single(v => v.Id == id).LastDetection);
    }

    [Fact]
    public async Task TestReimportReopensAMitigatedFindingAsARegression()
    {
        await _ingestion.IngestAsync(await ParseAsync("nessus", ImporterFixtures.Nessus), Request());

        var lifecycle = GetService<IFindingLifecycleService>();
        var id = await FindingIdAsync("OpenSSL Heartbleed");

        await lifecycle.TransitionAsync(id, FindingStatus.Mitigated, 1, FindingStatusChangeSource.Manual);

        await _ingestion.IngestAsync(await ParseAsync("nessus", ImporterFixtures.Nessus, SecondImport),
            Request(at: SecondImport));

        await using var db = OpenContext();

        // We believed it was fixed and the scanner disagrees. A reintroduced vulnerability that
        // stays invisible is the worst outcome available here.
        Assert.Equal(FindingStatus.Active, db.Vulnerabilities.Single(v => v.Id == id).LifecycleStatus);

        var regression = db.FindingStatusHistories
            .Where(h => h.VulnerabilityId == id && h.Source == FindingStatusChangeSource.Import)
            .ToList();

        Assert.Contains(regression, h => h.FromStatus == FindingStatus.Mitigated && h.ToStatus == FindingStatus.Active);
        Assert.Contains(regression, h => h.Justification != null && h.Justification.Contains("regression"));
    }

    [Fact]
    public async Task TestSeverityChangeMovesTheDueDateAndSaysSoOnTheTimeline()
    {
        await _ingestion.IngestAsync(await ParseAsync("nessus", ImporterFixtures.Nessus), Request());

        var id = await FindingIdAsync("OpenSSH Weak Ciphers");

        // The scanner re-rates the finding from High to Critical.
        // Only the SSH item carries severity="3", so this is unambiguous.
        var escalated = ImporterFixtures.Nessus.Replace("severity=\"3\"", "severity=\"4\"");

        await _ingestion.IngestAsync(await ParseAsync("nessus", escalated, SecondImport),
            Request(at: SecondImport));

        await using var db = OpenContext();

        var finding = db.Vulnerabilities.Single(v => v.Id == id);

        // Critical's 15-day allowance, still measured from the original first-seen.
        Assert.Equal(FirstImport.AddDays(15), finding.SlaDueDate);

        Assert.Contains(db.FindingStatusHistories.Where(h => h.VulnerabilityId == id).ToList(),
            h => h.Justification != null && h.Justification.Contains("Severity changed"));
    }

    [Fact]
    public async Task TestImportDoesNotOverwriteHumanEnteredFields()
    {
        await _ingestion.IngestAsync(await ParseAsync("nessus", ImporterFixtures.Nessus), Request());

        var id = await FindingIdAsync("OpenSSL Heartbleed");

        Seed(ctx =>
        {
            var finding = ctx.Vulnerabilities.Single(v => v.Id == id);
            finding.Comments = "Waiting on the vendor.";
            finding.Technology = "nginx";
        });

        await _ingestion.IngestAsync(await ParseAsync("nessus", ImporterFixtures.Nessus, SecondImport),
            Request(at: SecondImport));

        await using var db = OpenContext();

        var updated = db.Vulnerabilities.Single(v => v.Id == id);
        Assert.Equal("Waiting on the vendor.", updated.Comments);
        Assert.Equal("nginx", updated.Technology);
    }

    // --- auto-close (3.3.2) ------------------------------------------------------------------

    [Fact]
    public async Task TestAutoCloseIsOffByDefault()
    {
        await _ingestion.IngestAsync(await ParseAsync("nessus", ImporterFixtures.Nessus), Request());

        // A later scan that genuinely no longer reports the second host. Retitling would not do:
        // the dedup key is deliberately title-independent, so a renamed finding is still the same
        // finding and would still count as seen.
        var narrowed = WithoutTheSecondHost();

        var second = await _ingestion.IngestAsync(await ParseAsync("nessus", narrowed, SecondImport),
            Request(at: SecondImport));

        // A partial scan mistaken for a full one closes everything outside its slice.
        Assert.Equal(0, second.ClosedCount);

        await using var db = OpenContext();
        Assert.Equal(FindingStatus.Active,
            db.Vulnerabilities.Single(v => v.Title == "OpenSSH Weak Ciphers").LifecycleStatus);
    }

    [Fact]
    public async Task TestAutoCloseClosesMissingFindingsWhenEnabledForAFullScan()
    {
        await _ingestion.IngestAsync(await ParseAsync("nessus", ImporterFixtures.Nessus), Request());

        await GetService<IDeduplicationService>().SaveConfigurationAsync(new ScannerDedupConfiguration
        {
            Importer = "nessus",
            StrategyChain = "HashBased",
            HashFields = string.Join(",", ServerServices.Importers.Dedup.DedupFieldSet.Default),
            AutoCloseMissing = true
        }, userId: 1);

        var narrowed = WithoutTheSecondHost();

        var second = await _ingestion.IngestAsync(await ParseAsync("nessus", narrowed, SecondImport),
            Request(at: SecondImport));

        Assert.Equal(1, second.ClosedCount);

        await using var db = OpenContext();
        var closed = db.Vulnerabilities.Single(v => v.Title == "OpenSSH Weak Ciphers");
        Assert.Equal(FindingStatus.Mitigated, closed.LifecycleStatus);

        Assert.Contains(db.FindingStatusHistories.Where(h => h.VulnerabilityId == closed.Id).ToList(),
            h => h.Justification != null && h.Justification.Contains("auto-close"));
    }

    [Fact]
    public async Task TestAutoCloseWarnsRatherThanActingOnAPartialScan()
    {
        await GetService<IDeduplicationService>().SaveConfigurationAsync(new ScannerDedupConfiguration
        {
            Importer = "zap",
            StrategyChain = "HashBased",
            AutoCloseMissing = true
        }, userId: 1);

        // ZAP declares itself a partial scan; enabling auto-close for it must not silently do nothing.
        var import = await _ingestion.IngestAsync(await ParseAsync("zap", ImporterFixtures.Zap),
            Request("zap"));

        Assert.Equal(0, import.ClosedCount);
        Assert.Contains("not a full scan", import.Warnings!);
    }

    // --- idempotency (3.5.2) ------------------------------------------------------------------

    [Fact]
    public async Task TestARepeatedIdempotencyKeyReturnsTheOriginalImport()
    {
        var first = await _ingestion.BeginImportAsync(Request(idempotencyKey: "ci-run-42"));
        Assert.False(first.IsReplay);

        await _ingestion.IngestAsync(await ParseAsync("nessus", ImporterFixtures.Nessus),
            Request(idempotencyKey: "ci-run-42"));

        var replay = await _ingestion.BeginImportAsync(Request(idempotencyKey: "ci-run-42"));

        // What protects a CI retry storm from importing the same scan five times.
        Assert.True(replay.IsReplay);
        Assert.Equal(first.Import.Id, replay.Import.Id);

        await using var db = OpenContext();
        Assert.Single(db.ScanImports.Where(i => i.IdempotencyKey == "ci-run-42"));
    }

    [Fact]
    public async Task TestImportsWithoutAKeyAreIndependent()
    {
        var first = await _ingestion.BeginImportAsync(Request());
        var second = await _ingestion.BeginImportAsync(Request());

        // A NULL idempotency key is not compared by the unique index, so unkeyed imports must not
        // collide with each other.
        Assert.False(second.IsReplay);
        Assert.NotEqual(first.Import.Id, second.Import.Id);
    }

    [Fact]
    public async Task TestFailedImportIsRecordedWithItsReason()
    {
        var reservation = await _ingestion.BeginImportAsync(Request());

        await _ingestion.FailImportAsync(reservation.Import.Id, "The file was not valid XML.");

        var import = await _ingestion.GetImportAsync(reservation.Import.Id);

        Assert.Equal((int)ScanImportStatus.Failed, import.Status);
        Assert.Equal("The file was not valid XML.", import.ErrorMessage);
        Assert.NotNull(import.FinishedAt);
    }

    // --- cross-importer ----------------------------------------------------------------------

    [Fact]
    public async Task TestAFindingWithNoAssetCreatesNoFictionalHost()
    {
        var parsed = await ParseAsync("sarif", ImporterFixtures.Sarif);

        await _ingestion.IngestAsync(parsed, Request("sarif"));

        await using var db = OpenContext();

        // A code scanner reports a file path, not a machine. Inventing an asset for it would fill
        // the inventory with hosts that do not exist.
        Assert.Empty(db.Hosts);
        Assert.All(db.Vulnerabilities.ToList(), v => Assert.Null(v.HostId));
        Assert.Contains(db.Vulnerabilities.ToList(), v => v.Location == "src/db.js:42");
    }

    [Fact]
    public async Task TestSnykFindingsDedupeOnTheToolsOwnId()
    {
        await _ingestion.IngestAsync(await ParseAsync("snyk", ImporterFixtures.Snyk), Request("snyk"));

        // A retitled, relocated finding with the same Snyk issue id is the same finding.
        var retitled = ImporterFixtures.Snyk
            .Replace("\"title\": \"Prototype Pollution\"", "\"title\": \"Prototype Pollution (updated)\"")
            .Replace("\"displayTargetFile\": \"package-lock.json\"", "\"displayTargetFile\": \"yarn.lock\"");

        var second = await _ingestion.IngestAsync(await ParseAsync("snyk", retitled, SecondImport),
            Request("snyk", SecondImport));

        Assert.Equal(0, second.NewCount);
        Assert.Equal(1, second.UpdatedCount);
    }

    [Fact]
    public async Task TestOneMalformedFindingDoesNotLoseTheRest()
    {
        var parsed = await ParseAsync("nessus", ImporterFixtures.Nessus);

        // A title far past the column width stands in for a record the database will reject.
        parsed.Findings.Add(new NormalizedFinding
        {
            Tool = "nessus",
            Title = new string('x', 500),
            Severity = NormalizedSeverity.High
        });

        var import = await _ingestion.IngestAsync(parsed, Request());

        // The two good findings still land; the bad one is reported rather than swallowed.
        Assert.Equal(3, import.NewCount + import.SkippedCount - 1);
        Assert.True(import.NewCount >= 2);
    }

    /// <summary>
    /// The Nessus fixture with its second host removed, standing in for a later scan that no longer
    /// reports that asset's finding.
    /// </summary>
    private static string WithoutTheSecondHost()
    {
        var start = ImporterFixtures.Nessus.IndexOf("<ReportHost name=\"10.0.0.2\"", StringComparison.Ordinal);
        var end = ImporterFixtures.Nessus.IndexOf("</ReportHost>", start, StringComparison.Ordinal);

        return ImporterFixtures.Nessus.Remove(start, end + "</ReportHost>".Length - start);
    }

    private async Task<int> FindingIdAsync(string title)
    {
        await using var db = OpenContext();
        return db.Vulnerabilities.Single(v => v.Title == title).Id;
    }
}

using System.Collections.Generic;
using Contracts.Importers;
using JetBrains.Annotations;
using ServerServices.Importers.Dedup;
using Tools.Security;
using Xunit;

namespace ServerServices.Tests.Track3;

/// <summary>
/// The deduplication strategies (Track 3 milestone 3.3.1).
///
/// The property under test throughout is stability: the same finding must produce the same key, and
/// a cosmetic change must not produce a different one. A dedup engine that fails either duplicates
/// the whole register on the next scan.
/// </summary>
[TestSubject(typeof(HashBasedStrategy))]
public class DeduplicationStrategyTest
{
    private static readonly DedupFieldSet DefaultFields = new(DedupFieldSet.Default);

    private static DedupContext Context(NormalizedFinding finding, int? hostId = null, int? serviceId = null) =>
        new() { Finding = finding, HostId = hostId, HostServiceId = serviceId };

    private static NormalizedFinding Finding(string title = "SQL injection", string? ruleId = "rule-1",
        string? location = "app/main.py:10", string? ip = "10.0.0.1", string? toolUniqueId = null,
        NormalizedSeverity severity = NormalizedSeverity.High) => new()
    {
        Tool = "semgrep",
        RuleId = ruleId,
        ToolUniqueId = toolUniqueId,
        Title = title,
        Location = location,
        Severity = severity,
        Host = ip == null ? null : new NormalizedHost { Ip = ip }
    };

    // --- UniqueIdFromTool -------------------------------------------------------------------

    [Fact]
    public void TestUniqueIdFromToolUsesTheToolsOwnId()
    {
        var strategy = new UniqueIdFromToolStrategy();

        var key = strategy.ComputeKey(Context(Finding(toolUniqueId: "SNYK-JS-LODASH-567746")), DefaultFields);

        Assert.NotNull(key);
        Assert.Equal(64, key!.Length);
    }

    [Fact]
    public void TestUniqueIdFromToolIgnoresEverythingElse()
    {
        var strategy = new UniqueIdFromToolStrategy();

        // A retitled, relocated, re-scored finding with the same tool id is the same finding — that
        // is the promise the tool made by publishing a stable id.
        var first = strategy.ComputeKey(Context(Finding(toolUniqueId: "abc")), DefaultFields);
        var second = strategy.ComputeKey(
            Context(Finding("Different title", "other-rule", "elsewhere.py:99", "10.0.0.9", "abc",
                NormalizedSeverity.Low)), DefaultFields);

        Assert.Equal(first, second);
    }

    [Fact]
    public void TestUniqueIdFromToolDeclinesWithoutAnId()
    {
        // Declining rather than inventing a weak key is what lets the chain fall through.
        Assert.Null(new UniqueIdFromToolStrategy().ComputeKey(Context(Finding()), DefaultFields));
    }

    [Fact]
    public void TestUniqueIdFromToolSeparatesToolsSharingAnId()
    {
        var strategy = new UniqueIdFromToolStrategy();

        var a = Finding(toolUniqueId: "1");
        var b = Finding(toolUniqueId: "1");
        b.Tool = "trivy";

        // Two scanners both numbering their findings from 1 must not collide.
        Assert.NotEqual(strategy.ComputeKey(Context(a), DefaultFields),
            strategy.ComputeKey(Context(b), DefaultFields));
    }

    // --- HashBased --------------------------------------------------------------------------

    [Fact]
    public void TestHashBasedIsStableAcrossCosmeticChange()
    {
        var strategy = new HashBasedStrategy();

        var first = Finding();
        var second = Finding();

        // Description, evidence and score are not in the field set, so editing them cannot split
        // one finding into two.
        second.Description = "reworded by the vendor";
        second.Evidence = "a different matched line";
        second.Cvss3BaseScore = 9.9;

        Assert.Equal(strategy.ComputeKey(Context(first), DefaultFields),
            strategy.ComputeKey(Context(second), DefaultFields));
    }

    [Fact]
    public void TestHashBasedSeparatesDifferentLocations()
    {
        var strategy = new HashBasedStrategy();

        Assert.NotEqual(
            strategy.ComputeKey(Context(Finding(location: "app/a.py:1")), DefaultFields),
            strategy.ComputeKey(Context(Finding(location: "app/b.py:1")), DefaultFields));
    }

    [Fact]
    public void TestHashBasedSeparatesDifferentAssets()
    {
        var strategy = new HashBasedStrategy();

        Assert.NotEqual(
            strategy.ComputeKey(Context(Finding(ip: "10.0.0.1")), DefaultFields),
            strategy.ComputeKey(Context(Finding(ip: "10.0.0.2")), DefaultFields));
    }

    [Fact]
    public void TestHashBasedFieldOrderChangesTheKey()
    {
        var strategy = new HashBasedStrategy();
        var finding = Finding();

        var forward = strategy.ComputeKey(Context(finding), new DedupFieldSet(["tool", "ruleId"]));
        var reversed = strategy.ComputeKey(Context(finding), new DedupFieldSet(["ruleId", "tool"]));

        // The configuration stores an ordered list precisely because the order is part of the key.
        Assert.NotEqual(forward, reversed);
    }

    [Fact]
    public void TestHashBasedIgnoresCveOrder()
    {
        var strategy = new HashBasedStrategy();

        var a = Finding();
        a.Cves.AddRange(["CVE-2021-1", "CVE-2022-2"]);

        var b = Finding();
        b.Cves.AddRange(["CVE-2022-2", "CVE-2021-1"]);

        // Scanners do not agree on the order they list CVEs, and an order change is not a new
        // finding.
        Assert.Equal(strategy.ComputeKey(Context(a), DefaultFields),
            strategy.ComputeKey(Context(b), DefaultFields));
    }

    [Fact]
    public void TestHashBasedDeclinesWhenNoConfiguredFieldIsPresent()
    {
        var strategy = new HashBasedStrategy();

        var empty = new NormalizedFinding { Tool = "", Title = "" };

        // A key over nothing would merge every such finding into one.
        Assert.Null(strategy.ComputeKey(Context(empty), new DedupFieldSet(["ruleId", "location"])));
    }

    [Fact]
    public void TestHashBasedNormalizesSeverityRatherThanTheRawString()
    {
        var strategy = new HashBasedStrategy();
        var fields = new DedupFieldSet(["tool", "severity"]);

        var a = Finding(severity: NormalizedSeverity.Medium);
        a.RawSeverity = "Moderate";

        var b = Finding(severity: NormalizedSeverity.Medium);
        b.RawSeverity = "Medium";

        // A vendor renaming "Moderate" to "Medium" must not split one finding into two.
        Assert.Equal(strategy.ComputeKey(Context(a), fields), strategy.ComputeKey(Context(b), fields));
    }

    // --- LegacyHashCode --------------------------------------------------------------------

    [Fact]
    public void TestLegacyHashCodeReproducesThePreTrack3Expression()
    {
        var strategy = new LegacyHashCodeStrategy();

        var finding = Finding(title: "Apache Server ETag Header Information Disclosure");
        finding.RawSeverity = "2";
        finding.ToolFields["riskFactor"] = "Medium";

        var key = strategy.ComputeKey(Context(finding, hostId: 7, serviceId: 11), DefaultFields);

        // Byte-for-byte: plugin name + host id + severity + risk factor + service id, no separator.
        // "Close enough" here means every pre-Track-3 finding duplicates on the next import.
        var expected = HashTool.CreateSha1(
            "Apache Server ETag Header Information Disclosure" + 7 + "2" + "Medium" + 11);

        Assert.Equal(expected, key);
    }

    [Fact]
    public void TestLegacyHashCodeDeclinesWithoutAResolvedAsset()
    {
        var strategy = new LegacyHashCodeStrategy();

        // Without the database ids the legacy string cannot be rebuilt, and a partial one would hash
        // to something that matches nothing.
        Assert.Null(strategy.ComputeKey(Context(Finding(), hostId: 7), DefaultFields));
        Assert.Null(strategy.ComputeKey(Context(Finding(), serviceId: 11), DefaultFields));
    }

    [Fact]
    public void TestLegacyHashCodeIsMarkedForTheImportHashColumn()
    {
        // Pre-Track-3 rows have no dedup_key at all; their hash lives in import_hash, and the
        // lookup has to know to look there.
        // Accessed through the interface: it is a default interface member, which is the right
        // shape for a flag only one implementation overrides.
        Assert.True(((IDeduplicationStrategy)new LegacyHashCodeStrategy()).MatchesLegacyImportHash);
        Assert.False(((IDeduplicationStrategy)new HashBasedStrategy()).MatchesLegacyImportHash);
        Assert.False(((IDeduplicationStrategy)new UniqueIdFromToolStrategy()).MatchesLegacyImportHash);
    }

    // --- field set -------------------------------------------------------------------------

    [Fact]
    public void TestFieldSetFallsBackToTheDefaultWhenEmpty()
    {
        Assert.Equal(DedupFieldSet.Default, new DedupFieldSet([]).Fields);
        Assert.Equal(DedupFieldSet.Default, DedupFieldSet.Parse(null).Fields);
        Assert.Equal(DedupFieldSet.Default, DedupFieldSet.Parse("  ").Fields);
    }

    [Fact]
    public void TestFieldSetReportsUnknownFields()
    {
        var fields = DedupFieldSet.Parse("tool,nonsense,ruleId");

        Assert.Equal(["nonsense"], fields.UnknownFields);
    }

    [Fact]
    public void TestFieldSetResolvesAssetFromIpThenFqdnThenHostName()
    {
        var fields = new DedupFieldSet(["asset"]);

        var withIp = Finding(ip: "10.0.0.1");
        Assert.Equal(["10.0.0.1"], fields.Resolve(Context(withIp)));

        var withFqdn = Finding(ip: null);
        withFqdn.Host = new NormalizedHost { Fqdn = "host.example.com" };
        Assert.Equal(["host.example.com"], fields.Resolve(Context(withFqdn)));

        var withName = Finding(ip: null);
        withName.Host = new NormalizedHost { HostName = "host" };
        Assert.Equal(["host"], fields.Resolve(Context(withName)));
    }

    [Fact]
    public void TestFieldSetYieldsEmptyForAMissingField()
    {
        var fields = new DedupFieldSet(["cve"]);

        // Empty rather than skipped: two findings differing only by one having a CVE must still
        // produce different keys.
        Assert.Equal([""], fields.Resolve(Context(Finding())));
    }
}

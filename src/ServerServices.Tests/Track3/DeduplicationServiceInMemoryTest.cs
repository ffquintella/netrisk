using System;
using System.Linq;
using System.Threading.Tasks;
using Contracts.Importers;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using ServerServices.Importers.Dedup;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track3;

/// <summary>
/// The deduplication engine's configuration surface and preview panel (Track 3 milestone 3.3.3).
///
/// The preview is the reason this milestone exists: a dedup heuristic change silently alters what
/// counts as "the same finding" from that moment on, and an administrator has no other way to see
/// what a change will do before it does it.
/// </summary>
[TestSubject(typeof(DeduplicationService))]
public class DeduplicationServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IDeduplicationService _svc;

    public DeduplicationServiceInMemoryTest()
    {
        _svc = GetService<IDeduplicationService>();
    }

    private static NormalizedFinding Finding(string title = "SQL injection", string? ruleId = "rule-1",
        string? location = "app/main.py:10", string? ip = "10.0.0.1") => new()
    {
        Tool = "semgrep", RuleId = ruleId, Title = title, Location = location,
        Severity = NormalizedSeverity.High,
        Host = ip == null ? null : new NormalizedHost { Ip = ip }
    };

    private static DedupContext Context(NormalizedFinding finding) => new() { Finding = finding };

    [Fact]
    public async Task TestAnUnconfiguredImporterGetsADefaultWithoutWritingARow()
    {
        var configuration = await _svc.GetConfigurationAsync("brand-new-scanner");

        Assert.Equal(DeduplicationService.DefaultStrategyChain, configuration.StrategyChain);
        Assert.Equal(0, configuration.Id);

        // A read path that writes would leave a row for every importer anyone ever glanced at.
        await using var db = OpenContext();
        Assert.Empty(db.ScannerDedupConfigurations);
    }

    [Fact]
    public async Task TestSavingRecordsTheChangeInHistory()
    {
        await _svc.SaveConfigurationAsync(new ScannerDedupConfiguration
        {
            Importer = "nessus", StrategyChain = "HashBased", HashFields = "tool,ruleId", AutoCloseMissing = false
        }, userId: 1);

        await _svc.SaveConfigurationAsync(new ScannerDedupConfiguration
        {
            Importer = "nessus", StrategyChain = "UniqueIdFromTool,HashBased", HashFields = "tool,ruleId,asset",
            AutoCloseMissing = true
        }, userId: 1);

        var history = await _svc.GetConfigurationHistoryAsync("nessus");

        // When the register's numbers jump, this is the table that explains why.
        Assert.Equal(2, history.Count);
        Assert.Equal("UniqueIdFromTool,HashBased", history[0].NewStrategyChain);
        Assert.Equal("HashBased", history[0].OldStrategyChain);
        Assert.True(history[0].NewAutoCloseMissing);
        Assert.False(history[0].OldAutoCloseMissing);

        // One configuration per importer, whatever the number of edits.
        Assert.Single(await _svc.GetConfigurationsAsync());
    }

    [Fact]
    public async Task TestAnUnknownStrategyIsRefusedRatherThanSilentlyDropped()
    {
        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.SaveConfigurationAsync(new ScannerDedupConfiguration
            {
                Importer = "nessus", StrategyChain = "HashBased,Telepathy"
            }, 1));

        // A chain that quietly lost a strategy changes what counts as the same finding with nobody
        // being told. The error names the available strategies so the fix is obvious.
        Assert.Contains("Telepathy", ex.Message);
        Assert.Contains("HashBased", ex.Message);
    }

    [Fact]
    public async Task TestAnUnknownHashFieldIsRefused()
    {
        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.SaveConfigurationAsync(new ScannerDedupConfiguration
            {
                Importer = "nessus", StrategyChain = "HashBased", HashFields = "tool,vibes"
            }, 1));

        Assert.Contains("vibes", ex.Message);
    }

    [Fact]
    public async Task TestAnEmptyChainIsRefused()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            _svc.SaveConfigurationAsync(new ScannerDedupConfiguration
            {
                Importer = "nessus", StrategyChain = "   "
            }, 1));
    }

    [Fact]
    public async Task TestKnownStrategiesAreTheBuiltInsWhenNoPluginIsInstalled()
    {
        var strategies = await _svc.KnownStrategyNamesAsync();

        Assert.Contains(UniqueIdFromToolStrategy.StrategyName, strategies);
        Assert.Contains(HashBasedStrategy.StrategyName, strategies);
        Assert.Contains(LegacyHashCodeStrategy.StrategyName, strategies);
    }

    [Fact]
    public async Task TestComputeKeyReturnsEveryCandidateInChainOrder()
    {
        await _svc.SaveConfigurationAsync(new ScannerDedupConfiguration
        {
            Importer = "snyk", StrategyChain = "UniqueIdFromTool,HashBased"
        }, 1);

        var configuration = await _svc.GetConfigurationAsync("snyk");

        var finding = Finding();
        finding.ToolUniqueId = "SNYK-1";

        var result = await _svc.ComputeKeyAsync(Context(finding), configuration);

        // The whole list matters: a finding imported last month was keyed by whatever led the chain
        // then, so matching only the current primary key would duplicate the register.
        Assert.Equal(2, result.Candidates.Count);
        Assert.Equal(UniqueIdFromToolStrategy.StrategyName, result.Candidates[0].Strategy);
        Assert.Equal(HashBasedStrategy.StrategyName, result.Candidates[1].Strategy);
        Assert.Equal(result.Candidates[0].Key, result.PrimaryKey);
    }

    [Fact]
    public async Task TestChainFallsThroughWhenTheFirstStrategyDeclines()
    {
        var configuration = await _svc.GetConfigurationAsync("nessus");

        // No tool id, so UniqueIdFromTool declines and the field hash carries it.
        var result = await _svc.ComputeKeyAsync(Context(Finding()), configuration);

        Assert.Equal(HashBasedStrategy.StrategyName, result.PrimaryStrategy);
        Assert.True(result.HasKey);
    }

    [Fact]
    public async Task TestPreviewSaysTwoIdenticalFindingsWouldMerge()
    {
        var preview = await _svc.PreviewAsync(Context(Finding()), Context(Finding()), "semgrep");

        Assert.True(preview.WouldMerge);
        Assert.NotEmpty(preview.SharedKeys);
        // Every candidate key is reported so a surprising verdict can be explained.
        Assert.NotEmpty(preview.Left.Candidates);
        Assert.NotEmpty(preview.Right.Candidates);
    }

    [Fact]
    public async Task TestPreviewSaysDifferentLocationsWouldNotMerge()
    {
        var preview = await _svc.PreviewAsync(
            Context(Finding(location: "app/a.py:1")),
            Context(Finding(location: "app/b.py:1")),
            "semgrep");

        Assert.False(preview.WouldMerge);
        Assert.Empty(preview.SharedKeys);
    }

    [Fact]
    public async Task TestPreviewFollowsTheSavedConfiguration()
    {
        // With only the tool and rule id in the hash, two findings at different locations merge.
        await _svc.SaveConfigurationAsync(new ScannerDedupConfiguration
        {
            Importer = "semgrep", StrategyChain = "HashBased", HashFields = "tool,ruleId"
        }, 1);

        var preview = await _svc.PreviewAsync(
            Context(Finding(location: "app/a.py:1")),
            Context(Finding(location: "app/b.py:1")),
            "semgrep");

        Assert.True(preview.WouldMerge);
        Assert.Equal("tool,ruleId", preview.Configuration.HashFields);
    }

    [Fact]
    public async Task TestPreviewMatchesOnALaterStrategyInTheChain()
    {
        await _svc.SaveConfigurationAsync(new ScannerDedupConfiguration
        {
            Importer = "snyk", StrategyChain = "UniqueIdFromTool,HashBased"
        }, 1);

        var withId = Finding();
        withId.ToolUniqueId = "SNYK-1";

        var withoutId = Finding();

        var preview = await _svc.PreviewAsync(Context(withId), Context(withoutId), "snyk");

        // Their primary keys differ (one has a tool id, one does not) but both produce the same
        // field hash. Answering with the primary keys alone would report a non-merge wrongly.
        Assert.NotEqual(preview.Left.PrimaryKey, preview.Right.PrimaryKey);
        Assert.True(preview.WouldMerge);
    }

    [Fact]
    public async Task TestPreviewHasNoSideEffects()
    {
        await _svc.PreviewAsync(Context(Finding()), Context(Finding()), "brand-new-scanner");

        // The point of the panel is trying a heuristic before committing to it.
        await using var db = OpenContext();
        Assert.Empty(db.ScannerDedupConfigurations);
        Assert.Empty(db.ScannerDedupConfigurationHistories);
    }
}

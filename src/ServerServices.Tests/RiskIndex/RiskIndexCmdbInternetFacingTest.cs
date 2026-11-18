using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.RiskIndex;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Engine;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// M58, T291 (S39 §3.2 CMDB-INET, §5.3): a host's stated internet exposure is the evidence <c>m_net</c> reads for its
/// findings — only once the snapshot's profile enables CMDB-INET, a CMDB value only inside the source's window, a manual
/// one without ageing, and a stated "no" over the public-IP heuristic. DB-02 carries the seed's nessus findings.
/// </summary>
[TestSubject(typeof(RiskIndexObjectCollector))]
public class RiskIndexCmdbInternetFacingTest : RiskIndexSeedTestBase
{
    private void Expose(bool? value, string? source, DateTime? seenAt, string? ip = null) => SeedUnscoped(ctx =>
    {
        var host = ctx.Hosts.Single(h => h.Id == Db);
        host.InternetFacing = value;
        host.InternetFacingSource = source;
        host.InternetFacingSeenAt = seenAt;
        if (ip != null) host.Ip = ip;
    });

    private async Task<bool> NetApplied(bool cmdbInet = true, bool heuristic = false)
    {
        var sources = RiskIndexSources.DefaultEnabled();
        if (cmdbInet) sources.Add(RiskIndexSources.CmdbInternetFacing);
        var p = new RiskIndexParameters { EnabledSources = sources, PublicIpHeuristic = heuristic };

        await using var db = OpenContext();
        var set = await RiskIndexObjectCollector.CollectAsync(db, p, RiskIndexMode.Interim, Now);
        var row = set.Rows.Single(r => r.Key == new IcrObjectKey(RiskIndexObjectKind.Host, Db.ToString()) &&
                                       r.Category == RiskIndexCategory.Exp);

        Assert.NotEmpty(row.Signals);
        return row.Signals.Any(s => s.Flags.Contains("m_net"));
    }

    [Fact]
    public async Task TestAFreshCmdbValueAppliesTheMultiplierOnlyUnderCmdbInet()
    {
        Expose(true, AssetContextSources.Cmdb, Now.AddDays(-2));

        Assert.False(await NetApplied(cmdbInet: false));
        Assert.True(await NetApplied());
    }

    [Fact]
    public async Task TestACmdbValueOutsideTheWindowNoLongerCounts()
    {
        Expose(true, AssetContextSources.Cmdb, Now.AddDays(-31));

        Assert.False(await NetApplied());
    }

    [Fact]
    public async Task TestAManualValueDoesNotAge()
    {
        Expose(true, AssetContextSources.Manual, Now.AddYears(-2));

        Assert.True(await NetApplied());
    }

    [Fact]
    public async Task TestAStatedNoOverridesThePublicIpHeuristic()
    {
        Expose(false, AssetContextSources.Cmdb, Now.AddDays(-1), ip: "203.0.113.10");

        Assert.False(await NetApplied(heuristic: true));
    }

    [Fact]
    public async Task TestWithoutAStatementTheHeuristicStillDecides()
    {
        Expose(null, null, null, ip: "203.0.113.10");

        Assert.True(await NetApplied(heuristic: true));
        Assert.False(await NetApplied());
    }

    [Fact]
    public async Task TestAValueWithAnUnknownWriterIsIgnored()
    {
        // Not a writer the hosts service or the import records: never trusted as evidence.
        Expose(true, "visionone", Now.AddDays(-1));

        Assert.False(await NetApplied());
    }
}

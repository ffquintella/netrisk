using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.RiskIndex;
using ServerServices.Integrations.Tenable;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Engine;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// M56, T266–T267 (S39 §3.2, §3.4, §5.1; S40 §6.5, §6.8, §12.2 <c>RiskIndexEngineGoldenTest</c> addition): collected Tenable
/// readings move the index only when the snapshot's profile enables their source — ACR as <c>⌈ACR/2⌉</c> below manual and
/// CMDB and above Vision One, AES/1000 in EXP.D (MAX with V1-DEV), Tenable VM findings in EXP.V except on an asset with an
/// AES reading.
/// </summary>
[TestSubject(typeof(RiskIndexObjectCollector))]
public class RiskIndexTenableSourcesTest : RiskIndexSeedTestBase
{
    private const int NoCrit = 201, Manual = 202, VisionOne = 203, Aes = 204, VmOnly = 205;

    public RiskIndexTenableSourcesTest()
    {
        SeedUnscoped(ctx =>
        {
            ctx.Hosts.AddRange(
                Host(NoCrit, null, null), Host(Manual, 2, null), Host(VisionOne, 3, "TrendMicroVisionOne"), Host(Aes, null, null),
                Host(VmOnly, null, null));

            foreach (var host in new[] { NoCrit, Manual, VisionOne })
                ctx.VendorScores.Add(Score(host, VendorScoreSource.TenableAcr, 7, 10));
            ctx.VendorScores.Add(Score(Aes, VendorScoreSource.TenableAes, 800, 1000));

            ctx.Vulnerabilities.Add(Finding(Aes, TenableNormalizer.ImporterName, vpr: 9.5f, cvss3: 9.8f, cve: "CVE-2026-1001"));
            ctx.Vulnerabilities.Add(Finding(VmOnly, TenableNormalizer.ImporterName, vpr: 9.5f, cvss3: 9.8f, cve: "CVE-2026-1002"));
        });
    }

    private static Host Host(int id, int? criticality, string? provider) => new()
    {
        Id = id, HostName = $"t-{id}", Ip = $"10.2.0.{id % 250}", EntityId = UnitX, Source = "test", Status = 1,
        Criticality = criticality, ExternalProvider = provider, RegistrationDate = Now.AddDays(-10)
    };

    private static VendorScore Score(int host, VendorScoreSource source, double value, double scale) => new()
    {
        Source = source, ConnectionId = 1, HostId = host, EntityId = UnitX, Value = value, ScaleMax = scale,
        FirstCapturedAt = Now.AddDays(-20), LastCapturedAt = Now.AddDays(-1), CreatedAt = Now.AddDays(-20)
    };

    private async Task<RiskIndexObjectSet> Collect(params string[] extra)
    {
        var p = new RiskIndexParameters { EnabledSources = RiskIndexSources.DefaultEnabled().Concat(extra).ToList() };
        await using var db = OpenContext();
        return await RiskIndexObjectCollector.CollectAsync(db, p, RiskIndexMode.Interim, Now);
    }

    private static RiskIndexObjectRow Exp(RiskIndexObjectSet set, int host) =>
        set.Rows.Single(r => r.Key == new IcrObjectKey(RiskIndexObjectKind.Host, host.ToString()) && r.Category == RiskIndexCategory.Exp);

    [Fact]
    public async Task TestACollectedAcrLeavesTheWeightAloneUntilTenAcrIsEnabled()
    {
        var off = await Collect();
        Assert.Equal((3, "default"), (Exp(off, NoCrit).Criticality, Exp(off, NoCrit).CriticalitySource));
        Assert.Equal((3, "visionone"), (Exp(off, VisionOne).Criticality, Exp(off, VisionOne).CriticalitySource));

        var on = await Collect(RiskIndexSources.TenableAcr);
        Assert.Equal((4, "tenable_acr"), (Exp(on, NoCrit).Criticality, Exp(on, NoCrit).CriticalitySource));
        Assert.Equal((4, "tenable_acr"), (Exp(on, VisionOne).Criticality, Exp(on, VisionOne).CriticalitySource));
        Assert.Equal((2, "unknown"), (Exp(on, Manual).Criticality, Exp(on, Manual).CriticalitySource));
        Assert.True(Exp(on, NoCrit).Weight > Exp(off, NoCrit).Weight);
    }

    [Fact]
    public async Task TestAStaleAcrFallsThroughToTheNextSource()
    {
        SeedUnscoped(ctx =>
        {
            foreach (var row in ctx.VendorScores.Where(v => v.HostId == NoCrit)) row.LastCapturedAt = Now.AddDays(-45);
        });

        var on = await Collect(RiskIndexSources.TenableAcr);

        Assert.Equal("default", Exp(on, NoCrit).CriticalitySource);
    }

    [Fact]
    public async Task TestAesIsReadInExpDOnlyWhenEnabledAndThenKeepsTheAssetsTenableFindingsOut()
    {
        var vmOnly = await Collect(RiskIndexSources.TenableVm);
        Assert.Contains(Exp(vmOnly, Aes).Factors, f => f.Factor == RiskIndexFactors.ExpVulnerabilities && f.Source == RiskIndexSources.TenableVm);
        Assert.DoesNotContain(Exp(vmOnly, Aes).Factors, f => f.Source == RiskIndexSources.TenableAes);

        var both = await Collect(RiskIndexSources.TenableVm, RiskIndexSources.TenableAes);
        var aes = Assert.Single(Exp(both, Aes).Factors);
        Assert.Equal((RiskIndexFactors.ExpDevice, RiskIndexSources.TenableAes, 0.8), (aes.Factor, aes.Source, aes.X));
        Assert.True(RiskIndexSourceBits.Has(Exp(both, Aes).SourceMask, RiskIndexSources.TenableAes));
        Assert.Contains(Exp(both, VmOnly).Factors, f => f.Source == RiskIndexSources.TenableVm);
    }

    [Fact]
    public async Task TestTenableFindingsDoNotScoreUntilTenVmIsEnabled()
    {
        var off = await Collect();

        Assert.DoesNotContain(off.Rows, r => r.Key == new IcrObjectKey(RiskIndexObjectKind.Host, VmOnly.ToString()) &&
                                             r.Category == RiskIndexCategory.Exp &&
                                             r.Factors.Any(f => f.Source == RiskIndexSources.TenableVm));
    }
}

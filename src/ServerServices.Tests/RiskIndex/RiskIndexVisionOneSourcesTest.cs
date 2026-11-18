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
/// M57 (S39 §3.2, §5.1): the Vision One vendor assets score as objects of their own only when the snapshot's profile enables
/// their source — FQDN/IP in EXP.X, accounts in EXP.I, cloud assets and local apps in EXP.N (T272); the tenant's
/// configuration adoption scores CTL.C as <c>1 − adoption</c> (V1-SCS, T271); and a host's criticality is ranked by the
/// writer <c>hosts.criticality_source</c> records, not guessed from its provider (T275, D-10).
/// </summary>
[TestSubject(typeof(RiskIndexObjectCollector))]
public class RiskIndexVisionOneSourcesTest : RiskIndexSeedTestBase
{
    private const long Fqdn = 1, Account = 2, Cloud = 3, Gone = 4;
    private const int ManualHost = 301, VisionOneHost = 302, LegacyHost = 303;

    public RiskIndexVisionOneSourcesTest()
    {
        SeedUnscoped(ctx =>
        {
            ctx.TrendMicroConnections.Add(new TrendMicroConnection
            {
                Id = 5, Name = "v1", Region = "eu", BaseUrl = "https://api.eu.xdr.trendmicro.com", EntityId = UnitX, Enabled = true,
                SyncPosture = true, CreatedAt = Now.AddYears(-1)
            });

            ctx.VendorAssets.AddRange(
                Asset(Fqdn, VendorAssetKind.Fqdn, 4, VendorAssetStatus.Active),
                Asset(Account, VendorAssetKind.DomainAccount, 5, VendorAssetStatus.Active),
                Asset(Cloud, VendorAssetKind.CloudAsset, null, VendorAssetStatus.Active),
                Asset(Gone, VendorAssetKind.Fqdn, null, VendorAssetStatus.Gone));
            ctx.VendorScores.AddRange(
                Reading(VendorScoreSource.V1VendorAsset, Fqdn, null, 80, Now.AddDays(-1)),
                Reading(VendorScoreSource.V1VendorAsset, Account, null, 40, Now.AddDays(-30)),
                Reading(VendorScoreSource.V1VendorAsset, Gone, null, 99, Now.AddDays(-1)),
                Reading(VendorScoreSource.V1SecurityConfig, null, "tenant", 0.75, Now.AddDays(-2)));

            ctx.Hosts.AddRange(
                CritHost(ManualHost, 5, CriticalitySources.Manual), CritHost(VisionOneHost, 5, CriticalitySources.VisionOne),
                CritHost(LegacyHost, 5, null));
        });
    }

    private static VendorAsset Asset(long id, VendorAssetKind kind, int? criticality, VendorAssetStatus status) => new()
    {
        Id = id, Provider = IntegrationKind.TrendMicroVisionOne, ConnectionId = 5, AssetKind = kind, ExternalId = $"x-{id}",
        Name = $"asset-{id}", Criticality = criticality, EntityId = UnitX, Status = status, FirstSeenAt = Now.AddDays(-60),
        LastSeenAt = Now.AddDays(-1), CreatedAt = Now.AddDays(-60)
    };

    private static VendorScore Reading(VendorScoreSource source, long? asset, string? reference, double value, DateTime last) =>
        new()
        {
            Source = source, ConnectionId = 5, VendorAssetId = asset, ExternalRef = reference, EntityId = UnitX, Value = value,
            ObjectKind = asset is null ? VendorScoreObjectKind.Tenant : VendorScoreObjectKind.VendorAsset,
            ScaleMax = asset is null ? 1 : 100, FirstCapturedAt = Now.AddDays(-60), LastCapturedAt = last, CreatedAt = Now.AddDays(-60)
        };

    /// <summary>A Vision One host (the provider alone would make it "visionone") whose recorded writer decides its rank.</summary>
    private static Host CritHost(int id, int criticality, string? source) => new()
    {
        Id = id, HostName = $"c-{id}", Ip = $"10.3.0.{id % 250}", EntityId = UnitX, Source = "test", Status = 1,
        Criticality = criticality, CriticalitySource = source, ExternalProvider = "TrendMicroVisionOne",
        RegistrationDate = Now.AddDays(-10)
    };

    private async Task<RiskIndexObjectSet> Collect(params string[] extra)
    {
        var p = new RiskIndexParameters { EnabledSources = RiskIndexSources.DefaultEnabled().Concat(extra).ToList() };
        await using var db = OpenContext();
        return await RiskIndexObjectCollector.CollectAsync(db, p, RiskIndexMode.Interim, Now);
    }

    private static RiskIndexObjectRow? Row(RiskIndexObjectSet set, RiskIndexObjectKind kind, object id, RiskIndexCategory category) =>
        set.Rows.SingleOrDefault(r => r.Key == new IcrObjectKey(kind, id.ToString()!) && r.Category == category);

    [Fact]
    public async Task TestVendorAssetsScoreOnlyWhenTheirSourceIsEnabled()
    {
        var off = await Collect();
        Assert.DoesNotContain(off.Rows, r => r.Key.Kind == RiskIndexObjectKind.VendorAsset);

        var on = await Collect(RiskIndexSources.V1InternetFacing);
        var fqdn = Row(on, RiskIndexObjectKind.VendorAsset, Fqdn, RiskIndexCategory.Exp)!;
        Assert.Equal((RiskIndexAssetClass.InternetFacing, RiskIndexObjectState.Assessed, UnitX), (fqdn.AssetClass, fqdn.State,
            fqdn.ScopeEntityId!.Value));
        var factor = Assert.Single(fqdn.Factors);
        Assert.Equal((RiskIndexFactors.ExpExternal, RiskIndexSources.V1InternetFacing, 0.8), (factor.Factor, factor.Source, factor.X));
        Assert.Equal((4, CriticalitySources.VisionOne), (fqdn.Criticality, fqdn.CriticalitySource));

        // Only the enabled kind, and never an asset the last read no longer listed.
        Assert.Null(Row(on, RiskIndexObjectKind.VendorAsset, Account, RiskIndexCategory.Exp));
        Assert.Null(Row(on, RiskIndexObjectKind.VendorAsset, Gone, RiskIndexCategory.Exp));
    }

    [Fact]
    public async Task TestAccountsAndCloudAssetsScoreInTheirFactorsAndAStaleOrMissingReadingIsNotAssessed()
    {
        var on = await Collect(RiskIndexSources.V1Accounts, RiskIndexSources.V1Cloud);

        var account = Row(on, RiskIndexObjectKind.VendorAsset, Account, RiskIndexCategory.Exp)!;
        Assert.Equal((RiskIndexAssetClass.Identity, RiskIndexFactors.ExpIdentities), (account.AssetClass, account.Factors.Single().Factor));
        // A Vision One criticality of 5 is capped at crit_max_vision_one (S39 §5.1); 30 days is past W = 7.
        Assert.Equal(4, account.Criticality);
        Assert.Equal(RiskIndexObjectState.Presumed, account.State);

        var cloud = Row(on, RiskIndexObjectKind.VendorAsset, Cloud, RiskIndexCategory.Exp)!;
        Assert.Equal((RiskIndexAssetClass.Cloud, RiskIndexObjectState.Presumed, "default"), (cloud.AssetClass, cloud.State,
            cloud.CriticalitySource));
        Assert.Empty(cloud.Factors);
        Assert.True(on.SourceEnabled[RiskIndexCategory.Exp]);
    }

    [Fact]
    public async Task TestTheTenantConfigurationAdoptionScoresCtlConfigurationOnlyWhenEnabled()
    {
        var off = await Collect();
        Assert.Null(Row(off, RiskIndexObjectKind.Tenant, 5, RiskIndexCategory.Ctl));

        var on = await Collect(RiskIndexSources.V1SecurityConfiguration);
        var tenant = Row(on, RiskIndexObjectKind.Tenant, 5, RiskIndexCategory.Ctl)!;
        Assert.Equal((RiskIndexPopulation.Tenants, RiskIndexObjectState.Assessed), (tenant.Population, tenant.State));
        Assert.Equal(0.25, tenant.Factors.Single().X, 6);
        Assert.Equal(RiskIndexFactors.CtlConfiguration, tenant.Factors.Single().Factor);
        Assert.True(on.SourceEnabled[RiskIndexCategory.Ctl]);
    }

    [Fact]
    public async Task TestTheRecordedWriterRanksTheCriticalityNotTheProvider()
    {
        var set = await Collect();

        RiskIndexObjectRow Exp(int host) => Row(set, RiskIndexObjectKind.Host, host, RiskIndexCategory.Exp)!;

        // A manual edit on a Vision One host outranks the cap; Vision One's own value is capped at 4.
        Assert.Equal((5, CriticalitySources.Manual), (Exp(ManualHost).Criticality, Exp(ManualHost).CriticalitySource));
        Assert.Equal((4, CriticalitySources.VisionOne), (Exp(VisionOneHost).Criticality, Exp(VisionOneHost).CriticalitySource));
        // Written before the column existed: the provider decides, as the D-10 interim rule did.
        Assert.Equal((4, CriticalitySources.VisionOne), (Exp(LegacyHost).Criticality, Exp(LegacyHost).CriticalitySource));
    }

    [Fact]
    public async Task TestAManualCriticalityStillOutranksTenableAcrOnAVisionOneHost()
    {
        SeedUnscoped(ctx => ctx.VendorScores.Add(new VendorScore
        {
            Source = VendorScoreSource.TenableAcr, ConnectionId = 1, HostId = ManualHost, EntityId = UnitX, Value = 2, ScaleMax = 10,
            FirstCapturedAt = Now.AddDays(-5), LastCapturedAt = Now.AddDays(-1), CreatedAt = Now.AddDays(-5)
        }));

        var set = await Collect(RiskIndexSources.TenableAcr);

        var row = Row(set, RiskIndexObjectKind.Host, ManualHost, RiskIndexCategory.Exp)!;
        Assert.Equal((5, CriticalitySources.Manual), (row.Criticality, row.CriticalitySource));
    }
}

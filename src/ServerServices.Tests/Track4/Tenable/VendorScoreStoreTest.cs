using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using ServerServices.Integrations;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4.Tenable;

/// <summary>
/// M56, T267 (S40 §7.2, §12.2 <c>VendorScoreStoreTest</c>): an equal value extends the current row, a different one opens a
/// new row, and the current reading is the row captured last — per source, so ACR and AES never share a row.
/// </summary>
[TestSubject(typeof(VendorScoreStore))]
public class VendorScoreStoreTest : InMemoryServiceTestBase
{
    private static readonly DateTime T0 = new(2026, 11, 1, 0, 0, 0, DateTimeKind.Utc);

    public VendorScoreStoreTest() =>
        Seed(ctx => ctx.Hosts.Add(new Host { Id = 1, HostName = "web-01", Source = "test", RegistrationDate = T0, Status = 1 }));

    private async Task<int> ApplyAsync(DateTime at, params (VendorScoreSource Source, double Value)[] readings)
    {
        await using var db = OpenContext();
        var opened = await VendorScoreStore.ApplyAsync(db,
            readings.Select(r => new VendorScoreReading(r.Source, 1, 1, "a-1", 7, r.Value, 1000, null)).ToList(), at);
        await db.SaveChangesAsync();
        return opened;
    }

    [Fact]
    public async Task TestAnEqualValueExtendsTheRow()
    {
        Assert.Equal(1, await ApplyAsync(T0, (VendorScoreSource.TenableAes, 500)));
        Assert.Equal(0, await ApplyAsync(T0.AddDays(1), (VendorScoreSource.TenableAes, 500)));

        await using var db = OpenContext();
        var row = db.VendorScores.Single();
        Assert.Equal((T0, T0.AddDays(1), (int?)7), (row.FirstCapturedAt, row.LastCapturedAt, row.EntityId));
    }

    [Fact]
    public async Task TestADifferentValueOpensARowAndTheLatestIsCurrent()
    {
        await ApplyAsync(T0, (VendorScoreSource.TenableAes, 500));
        await ApplyAsync(T0.AddDays(1), (VendorScoreSource.TenableAes, 640));
        await ApplyAsync(T0.AddDays(2), (VendorScoreSource.TenableAes, 640));

        await using var db = OpenContext();
        var rows = db.VendorScores.OrderBy(v => v.Id).ToList();
        Assert.Equal([500.0, 640.0], rows.Select(r => r.Value));
        Assert.Equal((T0, T0.AddDays(2)), (rows[1].FirstCapturedAt.AddDays(-1), rows[1].LastCapturedAt));
        Assert.Equal(T0, rows[0].LastCapturedAt);
    }

    [Fact]
    public async Task TestEachSourceKeepsItsOwnRow()
    {
        Assert.Equal(2, await ApplyAsync(T0, (VendorScoreSource.TenableAes, 9), (VendorScoreSource.TenableAcr, 9)));
        Assert.Equal(0, await ApplyAsync(T0.AddDays(1), (VendorScoreSource.TenableAes, 9), (VendorScoreSource.TenableAcr, 9)));
    }

    /// <summary>M57, T271–T273: tenant, asset-group and vendor-asset readings are piecewise constant per object too.</summary>
    [Fact]
    public async Task TestObjectReadingsArePiecewiseConstantPerObjectAndConnection()
    {
        Seed(ctx => ctx.VendorAssets.Add(new VendorAsset
        {
            Id = 11, Provider = IntegrationKind.TrendMicroVisionOne, ConnectionId = 1, AssetKind = VendorAssetKind.Fqdn,
            ExternalId = "f1", Name = "www", FirstSeenAt = T0, LastSeenAt = T0, CreatedAt = T0
        }));

        async Task<int> Apply(DateTime at, params VendorObjectReading[] readings)
        {
            await using var db = OpenContext();
            var opened = await VendorScoreStore.ApplyObjectsAsync(db, readings, at);
            await db.SaveChangesAsync();
            return opened;
        }

        VendorObjectReading Cri(int connection, double value) => new(VendorScoreSource.V1Cri, connection,
            VendorScoreObjectKind.Tenant, null, "tenant", 7, value, 100, "high", null);
        VendorObjectReading Group(string id, double value) => new(VendorScoreSource.V1AssetGroup, 1,
            VendorScoreObjectKind.AssetGroup, null, id, 7, value, 100, null, null);
        VendorObjectReading Asset(double value) => new(VendorScoreSource.V1VendorAsset, 1, VendorScoreObjectKind.VendorAsset,
            11, "f1", 7, value, 100, null, null);

        Assert.Equal(5, await Apply(T0, Cri(1, 60), Cri(2, 60), Group("g1", 50), Group("g2", 50), Asset(30)));
        // Same values (the group id in another case) extend; one change opens one row.
        Assert.Equal(1, await Apply(T0.AddDays(1), Cri(1, 60), Cri(2, 61), Group("G1", 50), Group("g2", 50), Asset(30)));

        await using var check = OpenContext();
        Assert.Equal(6, check.VendorScores.Count());
        var asset = check.VendorScores.Single(v => v.VendorAssetId == 11);
        Assert.Equal((T0, T0.AddDays(1)), (asset.FirstCapturedAt, asset.LastCapturedAt));
        Assert.Equal([60d, 61d], check.VendorScores.Where(v => v.ConnectionId == 2).OrderBy(v => v.Id).Select(v => v.Value).ToArray());
    }
}

using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// M58, T291 (S39 CMDB-INET): <c>hosts.internet_facing_source</c> records who stated the exposure, as for the criticality
/// (T275) — a person through the hosts service is <c>manual</c>, which no import overwrites; an edit that leaves the value
/// alone keeps the stored writer and stamp; a body forges neither; clearing the value hands the host back to the CMDB.
/// </summary>
[TestSubject(typeof(ServerServices.Services.HostsService))]
public class HostInternetFacingSourceTest : InMemoryServiceTestBase
{
    private static readonly DateTime Seen = new(2026, 11, 1, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, AssetContextSources.Cmdb, true)]
    [InlineData(AssetContextSources.Cmdb, AssetContextSources.Cmdb, true)]
    [InlineData(AssetContextSources.Manual, AssetContextSources.Cmdb, false)]
    [InlineData(AssetContextSources.Cmdb, AssetContextSources.Manual, true)]
    [InlineData(AssetContextSources.Manual, AssetContextSources.Manual, true)]
    public void TestTheWriterPrecedence(string? stored, string writer, bool allowed)
    {
        Assert.Equal(allowed, AssetContextSources.MayOverwrite(stored, writer));
    }

    [Fact]
    public async Task TestAnExposureSetThroughTheServiceIsManualAndAnUnchangedOneKeepsItsWriter()
    {
        var hosts = GetService<IHostsService>();
        Seed(ctx => ctx.Hosts.Add(new Host
        {
            Id = 1, HostName = "srv", Source = "test", Status = 1, RegistrationDate = Seen, InternetFacing = true,
            InternetFacingSource = AssetContextSources.Cmdb, InternetFacingSeenAt = Seen
        }));

        // An edit of another field, with a forged writer and stamp in the body: the stored ones stay.
        await hosts.UpdateAsync(Load(1, h =>
        {
            h.Comment = "edited";
            h.InternetFacingSource = AssetContextSources.Manual;
            h.InternetFacingSeenAt = Seen.AddYears(5);
        }));
        Assert.Equal((true, AssetContextSources.Cmdb, Seen), Stored(1));

        await hosts.UpdateAsync(Load(1, h => h.InternetFacing = false));
        var (value, source, seenAt) = Stored(1);
        Assert.Equal((false, AssetContextSources.Manual), (value, source));
        Assert.True(seenAt > Seen);

        await hosts.UpdateAsync(Load(1, h => h.InternetFacing = null));
        Assert.Equal(((bool?)null, (string?)null, (DateTime?)null), Stored(1));

        var created = await hosts.CreateAsync(new Host
        {
            HostName = "new", Source = "test", Status = 1, RegistrationDate = Seen, InternetFacing = true,
            InternetFacingSource = AssetContextSources.Cmdb
        });
        Assert.Equal((true, AssetContextSources.Manual), (Stored(created.Id).Item1, Stored(created.Id).Item2));
    }

    private Host Load(int id, Action<Host> change)
    {
        using var db = OpenContext();
        var host = db.Hosts.AsNoTrackingWithIdentityResolution().Single(h => h.Id == id);
        change(host);
        return host;
    }

    private (bool?, string?, DateTime?) Stored(int id)
    {
        using var db = OpenContext();
        var host = db.Hosts.Single(h => h.Id == id);
        return (host.InternetFacing, host.InternetFacingSource, host.InternetFacingSeenAt);
    }
}

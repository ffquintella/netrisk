using System;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using ServerServices.Interfaces;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// M57, T275 (S39 §5.1, D-10): <c>hosts.criticality_source</c> records who wrote the criticality — a person through the
/// hosts service is <c>manual</c>, the highest precedence; an edit that leaves the criticality alone keeps the stored
/// writer; a body cannot forge the source; and a writer never replaces a value of higher precedence.
/// </summary>
[TestSubject(typeof(ServerServices.Services.HostsService))]
public class HostCriticalitySourceTest : InMemoryServiceTestBase
{
    private static readonly DateTime Now = new(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc);

    [Theory]
    [InlineData(null, CriticalitySources.VisionOne, true)]
    [InlineData(CriticalitySources.VisionOne, CriticalitySources.VisionOne, true)]
    [InlineData(CriticalitySources.VisionOne, CriticalitySources.Cmdb, true)]
    [InlineData(CriticalitySources.Cmdb, CriticalitySources.VisionOne, false)]
    [InlineData(CriticalitySources.Manual, CriticalitySources.Cmdb, false)]
    [InlineData(CriticalitySources.Manual, CriticalitySources.Manual, true)]
    public void TestTheWriterPrecedence(string? stored, string writer, bool allowed)
    {
        Assert.Equal(allowed, CriticalitySources.MayOverwrite(stored, writer));
    }

    [Fact]
    public async Task TestACriticalitySetThroughTheServiceIsManualAndAnUnchangedOneKeepsItsWriter()
    {
        var hosts = GetService<IHostsService>();
        Seed(ctx => ctx.Hosts.Add(new Host
        {
            Id = 1, HostName = "srv", Source = "test", Status = 1, RegistrationDate = Now, Criticality = 4,
            CriticalitySource = CriticalitySources.VisionOne
        }));

        // An edit of another field, with a forged source in the body: the stored writer stays.
        await hosts.UpdateAsync(Load(1, h => { h.Comment = "edited"; h.CriticalitySource = CriticalitySources.Manual; }));
        Assert.Equal((4, CriticalitySources.VisionOne), Stored(1));

        await hosts.UpdateAsync(Load(1, h => h.Criticality = 2));
        Assert.Equal((2, CriticalitySources.Manual), Stored(1));

        await hosts.UpdateAsync(Load(1, h => h.Criticality = null));
        Assert.Equal(((int?)null, (string?)null), Stored(1));

        var created = await hosts.CreateAsync(new Host
        {
            HostName = "new", Source = "test", Status = 1, RegistrationDate = Now, Criticality = 3,
            CriticalitySource = CriticalitySources.Cmdb
        });
        Assert.Equal((3, CriticalitySources.Manual), Stored(created.Id));
    }

    private Host Load(int id, Action<Host> change)
    {
        using var db = OpenContext();
        var host = db.Hosts.AsNoTrackingWithIdentityResolution().Single(h => h.Id == id);
        change(host);
        return host;
    }

    private (int?, string?) Stored(int id)
    {
        using var db = OpenContext();
        var host = db.Hosts.Single(h => h.Id == id);
        return (host.Criticality, host.CriticalitySource);
    }
}

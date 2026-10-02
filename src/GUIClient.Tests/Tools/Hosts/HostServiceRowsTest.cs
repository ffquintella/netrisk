using System.Linq;
using DAL.Entities;
using GUIClient.Tools.Hosts;
using JetBrains.Annotations;
using Xunit;

namespace GUIClient.Tests.Tools.Hosts;

/// <summary>
/// The Overview chips and the Services tab. The per-service vulnerability count comes from the
/// host's findings, because <c>GET /Hosts/{id}/Services</c> leaves <c>HostsService.Vulnerabilities</c>
/// empty.
/// </summary>
[TestSubject(typeof(HostServiceRows))]
public class HostServiceRowsTest
{
    private static HostsService Service(int id, string name, int? port, string protocol = "tcp") =>
        new() { Id = id, HostId = 1, Name = name, Port = port, Protocol = protocol };

    [Fact]
    public void EachServiceCountsTheFindingsThatNameIt()
    {
        var services = new[] { Service(10, "www", 443), Service(11, "ssh", 22), Service(12, "general", 0) };
        var findings = new[]
        {
            new Vulnerability { Id = 1, HostServiceId = 10, Title = "a" },
            new Vulnerability { Id = 2, HostServiceId = 10, Title = "b" },
            new Vulnerability { Id = 3, HostServiceId = 11, Title = "c" },
            new Vulnerability { Id = 4, HostServiceId = null, Title = "host-level" },
            new Vulnerability { Id = 5, HostServiceId = 99, Title = "another host's service" },
        };

        var rows = HostServiceRows.Build(services, findings).ToDictionary(r => r.Service.Id, r => r.VulnerabilityCount);

        Assert.Equal(2, rows[10]);
        Assert.Equal(1, rows[11]);
        Assert.Equal(0, rows[12]);
    }

    [Fact]
    public void RowsAreOrderedByPortThenNameWithPortlessServicesLast()
    {
        var services = new[] { Service(1, "zeta", null), Service(2, "www", 443), Service(3, "ssh", 22), Service(4, "alpha", 22) };

        var names = HostServiceRows.Build(services, []).Select(r => r.Name).ToArray();

        Assert.Equal(new[] { "alpha", "ssh", "www", "zeta" }, names);
    }

    [Theory]
    [InlineData("www", 443, "www · 443")]
    [InlineData("general", 0, "general")]
    [InlineData("general", null, "general")]
    public void TheChipReadsNameDotPortAndDropsAMissingPort(string name, int? port, string expected)
    {
        var row = HostServiceRows.Build([Service(1, name, port)], []).Single();

        Assert.Equal(expected, row.ChipText);
    }
}

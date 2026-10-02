using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Entities;

namespace GUIClient.Tools.Hosts;

/// <summary>One of a host's services as the Overview chips and the Services tab show it.</summary>
/// <param name="Service">The service as the server returned it.</param>
/// <param name="VulnerabilityCount">Findings on this host that name this service.</param>
public sealed record HostServiceRow(HostsService Service, int VulnerabilityCount)
{
    public string Name => Service.Name;
    public string Protocol => Service.Protocol;
    public int? Port => Service.Port;

    /// <summary><c>name · port</c>, or just the name for a service with no port (port 0 included).</summary>
    public string ChipText => Service.Port is > 0
        ? Service.Name + " · " + Service.Port.Value.ToString(CultureInfo.InvariantCulture)
        : Service.Name;
}

/// <summary>
/// Pairs a host's services with how many of its findings each one carries.
///
/// <c>GET /Hosts/{id}/Services</c> does not populate <c>HostsService.Vulnerabilities</c>, but the
/// view already holds every finding on the host and each names its service
/// (<c>Vulnerability.HostServiceId</c>), so the count costs no request.
/// </summary>
public static class HostServiceRows
{
    public static IReadOnlyList<HostServiceRow> Build(IEnumerable<HostsService> services,
        IEnumerable<Vulnerability> vulnerabilities)
    {
        var counts = vulnerabilities
            .Where(v => v.HostServiceId != null)
            .GroupBy(v => v.HostServiceId!.Value)
            .ToDictionary(g => g.Key, g => g.Count());

        return services
            .OrderBy(s => s.Port ?? int.MaxValue)
            .ThenBy(s => s.Name, System.StringComparer.OrdinalIgnoreCase)
            .Select(s => new HostServiceRow(s, counts.GetValueOrDefault(s.Id)))
            .ToList();
    }
}

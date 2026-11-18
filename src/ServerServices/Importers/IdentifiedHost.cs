using Contracts.Importers;

namespace ServerServices.Importers;

/// <summary>
/// A finding's host as a connector knows it: the provider's own asset id beside the addresses (M58, T281, S39 D-14).
/// The ingestion pipeline matches it by that id before any address — an IP is the weakest identity there is, and
/// matching on it first is how a CVE lands on whichever machine DHCP gave the address to last. Server-side rather than
/// a field of the SDK's <see cref="NormalizedHost"/>, because only the built-in connectors know an asset id; the pipeline
/// reads the identity and never writes it (the inventory sync owns <c>hosts.external_id</c> and <c>host_external_ids</c>).
/// </summary>
public sealed class IdentifiedHost : NormalizedHost
{
    /// <summary>The provider as <c>hosts.external_provider</c> / <c>host_external_ids.provider</c> spell it.</summary>
    public required string Provider { get; init; }

    /// <summary>The provider's id for the asset (a Vision One device id, a Tenable asset uuid).</summary>
    public required string ExternalId { get; init; }

    /// <summary>A copy of <paramref name="host"/> carrying the provider's identity.</summary>
    public static IdentifiedHost From(NormalizedHost host, string provider, string externalId) => new()
    {
        Provider = provider,
        ExternalId = externalId,
        Ip = host.Ip,
        HostName = host.HostName,
        Fqdn = host.Fqdn,
        MacAddress = host.MacAddress,
        OperatingSystem = host.OperatingSystem,
        Properties = host.Properties,
        ServiceName = host.ServiceName,
        Port = host.Port,
        Protocol = host.Protocol
    };
}

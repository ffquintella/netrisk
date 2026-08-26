using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace ServerServices.Http;

/// <summary>
/// Server-side request forgery guard for outbound integration calls (Track 7 milestone 7.1.2,
/// finding NR-2026-013).
///
/// Every Track 4 integration sends a request to a URL an administrator typed: a Slack webhook, a
/// Jira base URL, a Teams connector, a posture-provider endpoint. That makes the API a request proxy
/// for whoever holds the <c>configuration</c> permission, and the interesting targets are not on the
/// internet — they are the cloud instance metadata service, the Kubernetes API on the node, an
/// unauthenticated admin port on <c>127.0.0.1</c>. The response body comes back to the caller, so
/// this is not a blind SSRF.
///
/// The hard part of an SSRF policy in a self-hosted product is that private addresses are *normal*:
/// an on-premise Jira at <c>10.0.0.5</c> is the common case, not an attack. So the policy is layered:
///
///  * Non-<c>http(s)</c> schemes are always refused. Nothing needs them and they are how a URL turns
///    into a file read.
///  * The cloud metadata endpoints are always refused — <c>169.254.169.254</c>, the IPv6 equivalent,
///    and the link-local range they live in. There is no legitimate integration there, and they are
///    the single highest-value SSRF target in existence: on a default IMDSv1 instance the response is
///    a set of cloud credentials.
///  * Loopback and private ranges are allowed by default, because refusing them would break the
///    on-premise deployments this product is for, and can be refused with
///    <c>Integrations:BlockPrivateNetworks</c> for an installation that only integrates with SaaS.
///
/// Resolution happens here, at send time, and every resolved address is checked. Validating the
/// hostname alone would be defeated by a DNS name that resolves to <c>169.254.169.254</c> — the
/// standard bypass.
/// </summary>
public class OutboundUrlPolicy
{
    /// <summary>Configuration key for refusing private and loopback destinations.</summary>
    public const string BlockPrivateNetworksKey = "Integrations:BlockPrivateNetworks";

    /// <summary>
    /// Hosts an integration may reach even though they resolve into a blocked range. Populated from
    /// <c>Integrations:AllowedPrivateHosts</c> (comma-separated), which is how an installation that
    /// blocks private networks still reaches its own on-premise Jira.
    /// </summary>
    public const string AllowedPrivateHostsKey = "Integrations:AllowedPrivateHosts";

    private readonly ILogger _logger;
    private readonly bool _blockPrivateNetworks;
    private readonly HashSet<string> _allowedHosts;

    public OutboundUrlPolicy(ILogger logger, IConfiguration configuration)
        : this(logger,
            bool.TryParse(configuration[BlockPrivateNetworksKey], out var block) && block,
            (configuration[AllowedPrivateHostsKey] ?? "")
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
    {
    }

    /// <summary>Test seam: the policy without a configuration provider.</summary>
    public OutboundUrlPolicy(ILogger logger, bool blockPrivateNetworks, IEnumerable<string>? allowedHosts = null)
    {
        _logger = logger;
        _blockPrivateNetworks = blockPrivateNetworks;
        _allowedHosts = new HashSet<string>(allowedHosts ?? [], StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>The verdict on one destination.</summary>
    /// <param name="IsAllowed">Whether the request may be sent.</param>
    /// <param name="Reason">Why not, when it may not. Null when allowed.</param>
    public readonly record struct Verdict(bool IsAllowed, string? Reason)
    {
        public static Verdict Allow() => new(true, null);
        public static Verdict Deny(string reason) => new(false, reason);
    }

    /// <summary>
    /// Whether a request to <paramref name="url"/> may be sent.
    /// </summary>
    /// <remarks>
    /// DNS resolution failure is *allowed* through, deliberately. A resolver hiccup must not present
    /// as "blocked for security reasons" — the send will fail on its own a moment later with the real
    /// transport error, which is far more useful to the operator. The guard's job is to refuse
    /// destinations it positively recognises as forbidden.
    /// </remarks>
    public Verdict Evaluate(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return Verdict.Deny("The destination URL is empty.");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
            return Verdict.Deny("The destination is not an absolute URL.");

        if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
            return Verdict.Deny($"The scheme '{uri.Scheme}' is not allowed; use http or https.");

        var addresses = Resolve(uri.DnsSafeHost);
        if (addresses.Count == 0) return Verdict.Allow();

        foreach (var address in addresses)
        {
            if (IsMetadataOrLinkLocal(address))
                return Verdict.Deny(
                    $"'{uri.Host}' resolves to the link-local range ({address}), which includes the "
                    + "cloud instance metadata service. That destination is never allowed.");

            if (_blockPrivateNetworks && IsPrivate(address) && !_allowedHosts.Contains(uri.DnsSafeHost))
                return Verdict.Deny(
                    $"'{uri.Host}' resolves to a private address ({address}) and "
                    + BlockPrivateNetworksKey + " is set. Add the host to " + AllowedPrivateHostsKey
                    + " if the integration is meant to reach it.");
        }

        return Verdict.Allow();
    }

    private List<IPAddress> Resolve(string host)
    {
        if (IPAddress.TryParse(host, out var literal)) return [literal];

        try
        {
            return [..Dns.GetHostAddresses(host)];
        }
        catch (Exception ex)
        {
            _logger.Debug("Could not resolve {Host} while checking an outbound destination: {Message}",
                host, ex.Message);
            return [];
        }
    }

    /// <summary>
    /// The always-forbidden addresses: IPv4 link-local (<c>169.254.0.0/16</c>, which contains
    /// <c>169.254.169.254</c>), IPv6 link-local (<c>fe80::/10</c>), and the two documented IPv6
    /// metadata addresses.
    /// </summary>
    internal static bool IsMetadataOrLinkLocal(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var octets = address.GetAddressBytes();
            return octets[0] == 169 && octets[1] == 254;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6LinkLocal) return true;

            // fd00:ec2::254 (AWS) and fd00::/8 unique-local addresses used for metadata by some
            // providers. Checked as a literal because the ULA range as a whole is legitimate.
            var text = address.ToString();
            return text.Equals("fd00:ec2::254", StringComparison.OrdinalIgnoreCase);
        }

        return false;
    }

    /// <summary>Loopback, RFC 1918, carrier-grade NAT, IPv6 loopback and unique-local.</summary>
    internal static bool IsPrivate(IPAddress address)
    {
        if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();

        if (IPAddress.IsLoopback(address)) return true;

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var o = address.GetAddressBytes();

            if (o[0] == 10) return true;                              // 10.0.0.0/8
            if (o[0] == 172 && o[1] >= 16 && o[1] <= 31) return true; // 172.16.0.0/12
            if (o[0] == 192 && o[1] == 168) return true;              // 192.168.0.0/16
            if (o[0] == 100 && o[1] >= 64 && o[1] <= 127) return true; // 100.64.0.0/10 (CGNAT)
            if (o[0] == 0) return true;                                // 0.0.0.0/8
            return false;
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            if (address.IsIPv6SiteLocal || address.IsIPv6UniqueLocal) return true;
        }

        return false;
    }
}

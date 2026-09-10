using Model.Exceptions;
using Model.Secrets;

namespace ServerServices.Secrets;

/// <summary>Whether a vault connection's address names one node or a cluster to be discovered.</summary>
public enum VaultAddressKind
{
    /// <summary>An absolute <c>http(s)</c> URL. Used exactly as typed.</summary>
    Direct,

    /// <summary>A DNS name whose SRV records list the cluster's nodes.</summary>
    ServiceDiscovery
}

/// <summary>
/// A vault connection's <c>BaseUrl</c>, parsed.
///
/// The field carries two different things and always has, informally: an address, or the name of a
/// cluster. BastionVault's own client accepts both — <c>https://vault.example.com:4200</c> for one
/// node, a bare <c>vault.example.com</c> for SRV-based discovery of an HA cluster — and a NetRisk
/// connection that could only express the first is a connection that has to name a single node and
/// therefore has a single point of failure that the vault deployment does not.
///
/// Parsing is separated from resolving on purpose. This type touches no network, so the
/// save-time validation an administrator sees while looking at the form is the same code that
/// decides, hours later inside a background job, what to look up. The three accepted forms:
///
/// <list type="bullet">
///   <item><c>https://host[:port][/path]</c> — one node, no DNS beyond ordinary name resolution.</item>
///   <item><c>srv+https://_bvault._tcp.vault.example.com</c> — discovery, owner name written out.</item>
///   <item><c>vault.example.com</c> — discovery, owner name built with
///     <see cref="SecretVaultDefaults.SrvServiceLabel"/>.</item>
/// </list>
///
/// A scheme-less <c>host:port</c> is refused rather than guessed at: <c>Uri</c> reads
/// <c>vault.example.com:4200</c> as a scheme named <c>vault.example.com</c>, and an address that
/// parses as something the operator did not mean is worse than one that is rejected while they are
/// still looking at it.
/// </summary>
public sealed class VaultAddress
{
    private VaultAddress(VaultAddressKind kind, string scheme, string serviceName, Uri? direct,
        string original)
    {
        Kind = kind;
        Scheme = scheme;
        ServiceName = serviceName;
        Direct = direct;
        Original = original;
    }

    public VaultAddressKind Kind { get; }

    /// <summary>The scheme every resulting request uses: <c>http</c> or <c>https</c>.</summary>
    public string Scheme { get; }

    /// <summary>
    /// The SRV owner name to query, for <see cref="VaultAddressKind.ServiceDiscovery"/>. Empty for a
    /// direct address.
    /// </summary>
    public string ServiceName { get; }

    /// <summary>The node URL, for <see cref="VaultAddressKind.Direct"/>. Null for discovery.</summary>
    public Uri? Direct { get; }

    /// <summary>What the operator typed, trimmed. Kept for messages, which have to quote it back.</summary>
    public string Original { get; }

    public bool IsDiscovery => Kind == VaultAddressKind.ServiceDiscovery;

    /// <summary>
    /// The base URL to hand a plugin for a direct address.
    /// </summary>
    public string DirectBaseUrl => Direct is null
        ? throw new InvalidOperationException("A discovery address has no single base URL.")
        : Direct.GetLeftPart(UriPartial.Path).TrimEnd('/');

    /// <summary>Builds the base URL for one discovered node.</summary>
    public string NodeBaseUrl(string target, int port)
    {
        // The trailing dot of a fully-qualified SRV target is legal in DNS and illegal in a URL
        // authority, and leaving it in produces a host that resolves nowhere.
        var host = target.TrimEnd('.');

        return $"{Scheme}://{host}:{port}";
    }

    /// <summary>
    /// Parses <paramref name="baseUrl"/>, or throws <see cref="InvalidParameterException"/> with a
    /// message written for whoever is looking at the connection form.
    /// </summary>
    public static VaultAddress Parse(string? baseUrl, string parameterName = "BaseUrl")
    {
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidParameterException(parameterName, "A vault base URL is required.");

        var value = baseUrl.Trim();

        if (value.StartsWith(SecretVaultDefaults.SrvSchemePrefix, StringComparison.OrdinalIgnoreCase))
            return ParseDiscovery(value[SecretVaultDefaults.SrvSchemePrefix.Length..], value, parameterName);

        if (value.Contains("://", StringComparison.Ordinal))
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidParameterException(parameterName,
                    "The vault base URL must be an absolute http:// or https:// URL, an "
                    + "srv+https:// discovery name, or a bare cluster DNS name.");

            return new VaultAddress(VaultAddressKind.Direct, uri.Scheme, string.Empty, uri, value);
        }

        // No scheme at all: a bare cluster name, which is the BastionVault convention. Anything with
        // a port, a path or a userinfo in it was meant to be a URL and is missing its scheme.
        if (value.Contains('/', StringComparison.Ordinal)
            || value.Contains(':', StringComparison.Ordinal)
            || value.Contains('@', StringComparison.Ordinal))
            throw new InvalidParameterException(parameterName,
                $"'{value}' is neither a URL nor a bare DNS name. Add https:// to address one node, "
                + "or remove the port and path to discover a cluster by SRV record.");

        return BuildDiscovery(Uri.UriSchemeHttps, value, value, parameterName);
    }

    private static VaultAddress ParseDiscovery(string remainder, string original, string parameterName)
    {
        var separator = remainder.IndexOf("://", StringComparison.Ordinal);

        if (separator <= 0)
            throw new InvalidParameterException(parameterName,
                "A discovery address looks like srv+https://vault.example.com — the srv+ prefix is "
                + "followed by a scheme and a DNS name.");

        var scheme = remainder[..separator].ToLowerInvariant();

        if (scheme != Uri.UriSchemeHttp && scheme != Uri.UriSchemeHttps)
            throw new InvalidParameterException(parameterName,
                "A discovery address must use srv+http:// or srv+https://.");

        var name = remainder[(separator + 3)..].Trim().Trim('/');

        if (name.Length == 0)
            throw new InvalidParameterException(parameterName,
                "A discovery address must name the cluster to look up, e.g. srv+https://vault.example.com.");

        if (name.Contains('/', StringComparison.Ordinal) || name.Contains(':', StringComparison.Ordinal))
            throw new InvalidParameterException(parameterName,
                $"'{name}' carries a port or a path. A discovery name is a DNS name only — the port "
                + "comes from the SRV record.");

        return BuildDiscovery(scheme, name, original, parameterName);
    }

    private static VaultAddress BuildDiscovery(string scheme, string name, string original,
        string parameterName)
    {
        if (!IsPlausibleDnsName(name))
            throw new InvalidParameterException(parameterName,
                $"'{name}' is not a DNS name. A cluster is discovered by name, e.g. vault.example.com.");

        // An owner name that already carries a `_service._proto` prefix is used as written; anything
        // else gets the product's default label. Detected by the leading underscore rather than by
        // counting labels, because `_bvault._tcp.vault` and `vault.example.com` have the same shape
        // otherwise, and guessing wrong turns a working address into an NXDOMAIN.
        var serviceName = name.StartsWith('_')
            ? name
            : SecretVaultDefaults.SrvServiceLabel + "." + name;

        return new VaultAddress(VaultAddressKind.ServiceDiscovery, scheme, serviceName.TrimEnd('.'),
            null, original);
    }

    private static bool IsPlausibleDnsName(string name)
    {
        if (name.Length is 0 or > 253) return false;

        foreach (var label in name.TrimEnd('.').Split('.'))
        {
            if (label.Length is 0 or > 63) return false;

            foreach (var c in label)
                if (!char.IsAsciiLetterOrDigit(c) && c != '-' && c != '_')
                    return false;
        }

        return true;
    }
}

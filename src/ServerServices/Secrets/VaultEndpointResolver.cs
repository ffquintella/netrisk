using System.Collections.Concurrent;
using System.Security.Cryptography;
using Model.Exceptions;
using Model.Secrets;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Secrets;

/// <summary>
/// SRV discovery and health scoring for vault connections.
///
/// A BastionVault HA cluster is three or more nodes, one of them active and the rest standby, behind
/// a set of SRV records. Naming one node in the connection works right up to the moment that node is
/// the one being patched, so the address may instead be a cluster name — see <see cref="VaultAddress"/>
/// for the accepted forms.
///
/// Choosing among the nodes has three parts, in this order:
///
///  1. <b>RFC 2782 ordering.</b> Priority ascending, then a weighted shuffle within each priority
///     band. The shuffle matters: without it every NetRisk instance in an installation sends every
///     request to whichever node sorts first, which is the opposite of what weights are for.
///  2. <b>A health probe.</b> <see cref="SecretVaultDefaults.HealthProbePath"/> on each candidate in
///     order, first healthy one wins. This is why the API key is not needed here — the endpoint is
///     unauthenticated on a Vault-compatible server, so discovery costs nothing in the vault's audit
///     log, which a probe that read a secret would not.
///  3. <b>Falling back to order.</b> If every probe fails the first candidate is returned anyway,
///     with a note. A vault that does not serve the health path, or a network that blocks the probe
///     but not the API, must not be turned into a connection that cannot be used at all — and if the
///     node really is down, the plugin's own error names the node and says so.
///
/// The result is cached per connection for the shortest of the records' TTLs, clamped to
/// [<see cref="SecretVaultDefaults.MinEndpointCacheSeconds"/>,
/// <see cref="SecretVaultDefaults.MaxEndpointCacheSeconds"/>]. Without a cache a sync job resolving
/// twenty credentials does twenty DNS lookups and sixty health probes; with an unbounded one, a node
/// removed from the cluster keeps being used until the process restarts.
/// </summary>
public class VaultEndpointResolver : IVaultEndpointResolver
{
    private readonly ILogger _logger;
    private readonly IDnsSrvLookup _dns;
    private readonly IOutboundHttpClient _http;
    private readonly TimeProvider _time;

    private readonly ConcurrentDictionary<int, CachedSelection> _cache = new();

    /// <summary>
    /// Health probes are short on purpose. Three unreachable nodes at the outbound client's default
    /// thirty seconds is a ninety-second stall in front of a credential read; at two seconds it is
    /// six, and a vault node that needs longer than two seconds to answer an unauthenticated health
    /// check is not the node to send a secret read to.
    /// </summary>
    private static readonly TimeSpan ProbeTimeout = TimeSpan.FromSeconds(2);

    public VaultEndpointResolver(ILogger logger, IDnsSrvLookup dns, IOutboundHttpClient http,
        TimeProvider? time = null)
    {
        _logger = logger;
        _dns = dns;
        _http = http;
        _time = time ?? TimeProvider.System;
    }

    private sealed record CachedSelection(VaultEndpointSelection Selection, string Address,
        DateTimeOffset ExpiresAt);

    public void Invalidate(int connectionId) => _cache.TryRemove(connectionId, out _);

    public async Task<VaultEndpointSelection> ResolveAsync(int connectionId, string baseUrl,
        bool allowInvalidCertificate = false, CancellationToken ct = default)
    {
        var address = VaultAddress.Parse(baseUrl);

        if (!address.IsDiscovery)
        {
            var direct = address.DirectBaseUrl;

            return new VaultEndpointSelection(direct, [direct], false, null);
        }

        // Keyed on the address as well as the id so that an address change invalidates the entry even
        // if nothing called Invalidate — the cache must not be the reason a repointed connection
        // keeps talking to the old cluster.
        if (_cache.TryGetValue(connectionId, out var cached)
            && string.Equals(cached.Address, address.Original, StringComparison.Ordinal)
            && cached.ExpiresAt > _time.GetUtcNow())
            return cached.Selection;

        var records = await LookupAsync(address, ct);

        var candidates = Order(records)
            .Select(r => address.NodeBaseUrl(r.Target, r.Port))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (candidates.Count == 0)
            throw new SecretVaultResolutionException(
                $"No vault nodes are published for '{address.ServiceName}'. Check the SRV records, or "
                + "address one node directly with an https:// URL.");

        var (chosen, note) = await ChooseAsync(candidates, allowInvalidCertificate, ct);

        var selection = new VaultEndpointSelection(chosen, candidates, true, note);

        var ttl = TimeSpan.FromSeconds(Math.Clamp(
            records.Count == 0 ? SecretVaultDefaults.MinEndpointCacheSeconds : records.Min(r => r.TimeToLiveSeconds),
            SecretVaultDefaults.MinEndpointCacheSeconds,
            SecretVaultDefaults.MaxEndpointCacheSeconds));

        _cache[connectionId] = new CachedSelection(selection, address.Original, _time.GetUtcNow() + ttl);

        return selection;
    }

    private async Task<List<DnsSrvRecord>> LookupAsync(VaultAddress address, CancellationToken ct)
    {
        try
        {
            return (await _dns.LookupAsync(address.ServiceName, ct)).ToList();
        }
        catch (DnsSrvLookupException ex)
        {
            // Wrapped rather than propagated: every caller of this resolver already handles
            // SecretVaultResolutionException, and the operator's problem is "the vault could not be
            // located", not which library reported it.
            throw new SecretVaultResolutionException(
                $"The vault cluster '{address.Original}' could not be looked up: {ex.Message}", null, ex);
        }
    }

    /// <summary>
    /// RFC 2782 ordering: priority ascending, and within one priority a weighted random draw without
    /// replacement.
    ///
    /// Records of weight zero still take part — the RFC gives them a small chance rather than none —
    /// which is why the running sum starts each draw from the remaining records rather than skipping
    /// zeros.
    /// </summary>
    private static IEnumerable<DnsSrvRecord> Order(List<DnsSrvRecord> records)
    {
        foreach (var band in records.GroupBy(r => r.Priority).OrderBy(g => g.Key))
        {
            var remaining = band.ToList();

            while (remaining.Count > 0)
            {
                var total = remaining.Sum(r => r.Weight + 1);
                var draw = RandomNumberGenerator.GetInt32(total);

                var running = 0;
                var picked = remaining[^1];

                foreach (var record in remaining)
                {
                    running += record.Weight + 1;

                    if (draw < running)
                    {
                        picked = record;
                        break;
                    }
                }

                remaining.Remove(picked);
                yield return picked;
            }
        }
    }

    private async Task<(string Chosen, string? Note)> ChooseAsync(List<string> candidates,
        bool allowInvalidCertificate, CancellationToken ct)
    {
        var failures = new List<string>();

        foreach (var candidate in candidates)
        {
            var verdict = await ProbeAsync(candidate, allowInvalidCertificate, ct);

            if (verdict is null)
                return (candidate, failures.Count == 0
                    ? null
                    : $"{failures.Count} of {candidates.Count} vault nodes did not pass their health "
                      + $"check ({string.Join("; ", failures)}); using {candidate}.");

            failures.Add($"{candidate}: {verdict}");
        }

        _logger.Warning(
            "No vault node among {Count} discovered candidates passed its health probe; falling back to "
            + "{Chosen}. Probe results: {Failures}",
            candidates.Count, candidates[0], string.Join("; ", failures));

        return (candidates[0],
            $"No vault node passed its health check ({string.Join("; ", failures)}); trying {candidates[0]} anyway.");
    }

    /// <summary>
    /// Probes one node. Null means healthy; a string is the reason it is not.
    ///
    /// The status codes are HashiCorp Vault's, which is the protocol BastionVault implements: 200 is
    /// an unsealed active node, 429 an unsealed standby, 472 a DR secondary and 473 a performance
    /// standby. All four can serve a read or redirect one. 501 (uninitialised) and 503 (sealed)
    /// cannot, and are exactly the nodes discovery exists to skip.
    /// </summary>
    private async Task<string?> ProbeAsync(string candidate, bool allowInvalidCertificate,
        CancellationToken ct)
    {
        try
        {
            var response = await _http.SendAsync(new OutboundHttpRequest
            {
                Method = "GET",
                Url = candidate + SecretVaultDefaults.HealthProbePath,
                Timeout = ProbeTimeout,
                AllowInvalidCertificate = allowInvalidCertificate
            }, ct);

            if (response.StatusCode is 200 or 429 or 472 or 473) return null;

            if (response.StatusCode == 0)
                return response.TransportError ?? "unreachable";

            return $"HTTP {response.StatusCode}";
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return "the health probe timed out";
        }
        catch (Exception ex)
        {
            // A probe is an optimisation. Anything it throws — an SSRF refusal, a TLS failure — is
            // recorded as a failed probe and the next candidate is tried; it must never be the reason
            // a credential cannot be read.
            return ex.Message;
        }
    }
}

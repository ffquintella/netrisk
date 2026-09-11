using System;
using System.Linq;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Secrets;
using Serilog;
using ServerServices.Secrets;
using ServerServices.Tests.Mock;
using Xunit;

namespace ServerServices.Tests.Secrets;

/// <summary>
/// Node selection for a vault connection whose address names a cluster.
///
/// The point of the feature is that a NetRisk pointed at a three-node HA vault keeps working while
/// one node is sealed, patched or gone — so the behaviours that matter are the unhappy ones. Each
/// test here corresponds to a way the naive implementation fails in production:
///
///  * Talking to a sealed node because it sorted first.
///  * Doing a DNS lookup and three probes for every one of a sync job's twenty credential reads.
///  * Still talking to the old cluster after an operator repointed the connection.
///  * Refusing to work at all against a vault that does not serve the health path.
/// </summary>
[TestSubject(typeof(VaultEndpointResolver))]
public class VaultEndpointResolverTest
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private readonly FakeDnsSrvLookup _dns = new();
    private readonly FakeOutboundHttpClient _http = new();
    private readonly StoppedClock _time = new(new DateTimeOffset(2026, 9, 10, 12, 0, 0, TimeSpan.Zero));

    private VaultEndpointResolver Resolver() => new(Log, _dns, _http, _time);

    private const string Service = "_bvault._tcp.vault.example.com";

    /// <summary>Health-probe answers, keyed by the node's host name.</summary>
    private void Health(string host, int status)
    {
        _http.Rules.Add((request => request.Url.Contains(host, StringComparison.OrdinalIgnoreCase),
            new ServerServices.Interfaces.OutboundHttpResponse { StatusCode = status, Body = "{}" }));
    }

    // --- direct addresses -----------------------------------------------------------------------

    [Fact]
    public async Task ADirectAddressIsPassedThroughWithoutDnsOrAProbe()
    {
        var selection = await Resolver().ResolveAsync(1, "https://vault.example.com:4200");

        Assert.Equal("https://vault.example.com:4200", selection.BaseUrl);
        Assert.False(selection.Discovered);
        Assert.Null(selection.Note);

        // No DNS and no probe: a single-node connection must not become slower, or newly dependent on
        // an unauthenticated endpoint, because discovery exists.
        Assert.Empty(_dns.Queries);
        Assert.Empty(_http.Requests);
    }

    // --- discovery ------------------------------------------------------------------------------

    [Fact]
    public async Task ABareNameIsLookedUpUnderTheDefaultSrvLabel()
    {
        _dns.With(Service, "node1.example.com", 4200);
        Health("node1", 200);

        var selection = await Resolver().ResolveAsync(1, "vault.example.com");

        Assert.Equal(Service, Assert.Single(_dns.Queries));
        Assert.Equal("https://node1.example.com:4200", selection.BaseUrl);
        Assert.True(selection.Discovered);
        Assert.Null(selection.Note);
    }

    [Fact]
    public async Task LowerPriorityWins()
    {
        _dns.With(Service,
            new DnsSrvRecord("standby.example.com", 4200, 20, 10, 30),
            new DnsSrvRecord("active.example.com", 4200, 10, 10, 30));

        Health("active", 200);
        Health("standby", 200);

        var selection = await Resolver().ResolveAsync(1, "vault.example.com");

        Assert.Equal("https://active.example.com:4200", selection.BaseUrl);
        Assert.Equal(2, selection.Candidates.Count);
        Assert.Equal("https://active.example.com:4200", selection.Candidates[0]);
    }

    /// <summary>
    /// The behaviour the whole feature is for: the preferred node is sealed, so the next one is used
    /// and the operator is told which and why.
    /// </summary>
    [Fact]
    public async Task ASealedNodeIsSkippedForAHealthyOne()
    {
        _dns.With(Service,
            new DnsSrvRecord("sealed.example.com", 4200, 10, 10, 30),
            new DnsSrvRecord("healthy.example.com", 4200, 20, 10, 30));

        Health("sealed", 503);
        Health("healthy", 200);

        var selection = await Resolver().ResolveAsync(1, "vault.example.com");

        Assert.Equal("https://healthy.example.com:4200", selection.BaseUrl);
        Assert.NotNull(selection.Note);
        Assert.Contains("sealed.example.com", selection.Note);
    }

    /// <summary>
    /// 429 is an unsealed standby and 473 a performance standby. Both serve or redirect a read, so
    /// treating either as unhealthy would skip past a working node — and on a cluster with one active
    /// leader, past most of the cluster.
    /// </summary>
    [Theory]
    [InlineData(200)]
    [InlineData(429)]
    [InlineData(472)]
    [InlineData(473)]
    public async Task AStandbyOrSecondaryCountsAsHealthy(int status)
    {
        _dns.With(Service, "node1.example.com", 4200);
        Health("node1", status);

        var selection = await Resolver().ResolveAsync(1, "vault.example.com");

        Assert.Equal("https://node1.example.com:4200", selection.BaseUrl);
        Assert.Null(selection.Note);
    }

    [Fact]
    public async Task AnUnreachableNodeIsSkipped()
    {
        _dns.With(Service,
            new DnsSrvRecord("down.example.com", 4200, 10, 10, 30),
            new DnsSrvRecord("up.example.com", 4200, 20, 10, 30));

        _http.Rules.Add((r => r.Url.Contains("down"),
            new ServerServices.Interfaces.OutboundHttpResponse
                { StatusCode = 0, TransportError = "connection refused" }));
        Health("up", 200);

        var selection = await Resolver().ResolveAsync(1, "vault.example.com");

        Assert.Equal("https://up.example.com:4200", selection.BaseUrl);
        Assert.Contains("connection refused", selection.Note);
    }

    /// <summary>
    /// A vault that does not serve the health path, or a network that blocks the probe but not the
    /// API, must still be usable: the probe is an optimisation and cannot be allowed to become a
    /// prerequisite. The note says the choice was made blind.
    /// </summary>
    [Fact]
    public async Task WhenNoNodePassesTheFirstIsUsedAnyway()
    {
        _dns.With(Service,
            new DnsSrvRecord("n1.example.com", 4200, 10, 10, 30),
            new DnsSrvRecord("n2.example.com", 4200, 20, 10, 30));

        _http.DefaultResponse = new ServerServices.Interfaces.OutboundHttpResponse { StatusCode = 404 };

        var selection = await Resolver().ResolveAsync(1, "vault.example.com");

        Assert.Equal("https://n1.example.com:4200", selection.BaseUrl);
        Assert.Contains("No vault node passed", selection.Note);
    }

    // --- failures -------------------------------------------------------------------------------

    [Fact]
    public async Task ANameWithNoSrvRecordsIsAnError()
    {
        var ex = await Assert.ThrowsAsync<SecretVaultResolutionException>(
            () => Resolver().ResolveAsync(1, "vault.example.com"));

        Assert.Contains(Service, ex.Message);
    }

    [Fact]
    public async Task AResolverFailureIsReportedAsAVaultProblem()
    {
        _dns.FailWith = "SERVFAIL";

        var ex = await Assert.ThrowsAsync<SecretVaultResolutionException>(
            () => Resolver().ResolveAsync(1, "vault.example.com"));

        Assert.Contains("SERVFAIL", ex.Message);
    }

    [Fact]
    public async Task AMalformedAddressIsRefusedBeforeAnyLookup()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(
            () => Resolver().ResolveAsync(1, "vault.example.com:4200"));

        Assert.Empty(_dns.Queries);
    }

    // --- caching --------------------------------------------------------------------------------

    [Fact]
    public async Task TheChoiceIsReusedWithinTheRecordTtl()
    {
        _dns.With(Service, new DnsSrvRecord("node1.example.com", 4200, 10, 10, 30));
        Health("node1", 200);

        var resolver = Resolver();

        await resolver.ResolveAsync(1, "vault.example.com");
        await resolver.ResolveAsync(1, "vault.example.com");
        await resolver.ResolveAsync(1, "vault.example.com");

        // One lookup and one probe for three resolutions. Without this a sync job reading twenty
        // credentials would do twenty lookups and sixty probes.
        Assert.Single(_dns.Queries);
        Assert.Single(_http.Requests);
    }

    [Fact]
    public async Task TheChoiceIsRemadeOnceTheTtlHasPassed()
    {
        _dns.With(Service, new DnsSrvRecord("node1.example.com", 4200, 10, 10, 30));
        Health("node1", 200);

        var resolver = Resolver();

        await resolver.ResolveAsync(1, "vault.example.com");

        _time.Advance(TimeSpan.FromSeconds(31));

        await resolver.ResolveAsync(1, "vault.example.com");

        Assert.Equal(2, _dns.Queries.Count);
    }

    /// <summary>
    /// The TTL is clamped, so a record published with a one-day TTL does not pin a node until the
    /// process restarts.
    /// </summary>
    [Fact]
    public async Task ALongRecordTtlIsClampedToTheMaximum()
    {
        _dns.With(Service, new DnsSrvRecord("node1.example.com", 4200, 10, 10, 86400));
        Health("node1", 200);

        var resolver = Resolver();

        await resolver.ResolveAsync(1, "vault.example.com");

        _time.Advance(TimeSpan.FromSeconds(SecretVaultDefaults.MaxEndpointCacheSeconds + 1));

        await resolver.ResolveAsync(1, "vault.example.com");

        Assert.Equal(2, _dns.Queries.Count);
    }

    [Fact]
    public async Task InvalidatingForgetsTheChoice()
    {
        _dns.With(Service, new DnsSrvRecord("node1.example.com", 4200, 10, 10, 300));
        Health("node1", 200);

        var resolver = Resolver();

        await resolver.ResolveAsync(1, "vault.example.com");
        resolver.Invalidate(1);
        await resolver.ResolveAsync(1, "vault.example.com");

        Assert.Equal(2, _dns.Queries.Count);
    }

    /// <summary>
    /// A repointed connection must not keep the previous cluster's node even if nothing called
    /// <c>Invalidate</c> — the cache is keyed on the address as well as the id, so a changed address
    /// is a miss by construction rather than by remembering to evict.
    /// </summary>
    [Fact]
    public async Task ChangingTheAddressIsACacheMiss()
    {
        _dns.With(Service, new DnsSrvRecord("node1.example.com", 4200, 10, 10, 300))
            .With("_bvault._tcp.other.example.com", new DnsSrvRecord("node9.example.com", 4200, 10, 10, 300));

        Health("node1", 200);
        Health("node9", 200);

        var resolver = Resolver();

        await resolver.ResolveAsync(1, "vault.example.com");
        var second = await resolver.ResolveAsync(1, "other.example.com");

        Assert.Equal("https://node9.example.com:4200", second.BaseUrl);
        Assert.Equal(2, _dns.Queries.Count);
    }

    /// <summary>
    /// Two connections do not share an entry. They may well point at different clusters, and a cache
    /// keyed only on the address would be right by accident while one keyed only on the id would be
    /// wrong on purpose.
    /// </summary>
    [Fact]
    public async Task ConnectionsAreCachedSeparately()
    {
        _dns.With(Service, new DnsSrvRecord("node1.example.com", 4200, 10, 10, 300));
        Health("node1", 200);

        var resolver = Resolver();

        await resolver.ResolveAsync(1, "vault.example.com");
        await resolver.ResolveAsync(2, "vault.example.com");

        Assert.Equal(2, _dns.Queries.Count);
    }

    // --- RFC 2782 weighting ---------------------------------------------------------------------

    /// <summary>
    /// Within one priority band the order is a weighted draw, not a fixed sort. Asserted
    /// statistically: over enough resolutions both nodes must come first at least once, or every
    /// NetRisk in the installation is sending every request to whichever node sorts first — which is
    /// the opposite of what weights are for.
    /// </summary>
    [Fact]
    public async Task EqualPriorityNodesAreBothChosenOverTime()
    {
        _dns.With(Service,
            new DnsSrvRecord("a.example.com", 4200, 10, 50, 30),
            new DnsSrvRecord("b.example.com", 4200, 10, 50, 30));

        Health("a.example.com", 200);
        Health("b.example.com", 200);

        var firsts = new System.Collections.Generic.HashSet<string>();

        for (var i = 0; i < 60; i++)
        {
            // A fresh resolver each time: the cache exists precisely to stop the choice being remade,
            // so reusing one would sample the draw exactly once.
            var selection = await Resolver().ResolveAsync(i, "vault.example.com");
            firsts.Add(selection.Candidates[0]);
        }

        Assert.Equal(2, firsts.Count);
    }

    /// <summary>
    /// A cluster whose certificate the host does not trust must not read as a cluster whose nodes are
    /// all down. The probe carries the connection's TLS setting, or discovery reports the wrong fault
    /// and an operator goes looking for a network problem that is not there.
    /// </summary>
    [Fact]
    public async Task HealthProbesCarryTheConnectionsCertificateSetting()
    {
        _dns.With(Service, "node1.example.com", 4200);
        Health("node1", 200);

        await Resolver().ResolveAsync(1, "vault.example.com", allowInvalidCertificate: true);

        Assert.NotEmpty(_http.Requests);
        Assert.All(_http.Requests, r => Assert.True(r.AllowInvalidCertificate));
    }

    [Fact]
    public async Task HealthProbesValidateCertificatesByDefault()
    {
        _dns.With(Service, "node1.example.com", 4200);
        Health("node1", 200);

        await Resolver().ResolveAsync(1, "vault.example.com");

        Assert.NotEmpty(_http.Requests);
        Assert.All(_http.Requests, r => Assert.False(r.AllowInvalidCertificate));
    }

    /// <summary>
    /// The probe must ask for <c>/v1/sys/health</c>, the whole path.
    ///
    /// Regression: the constant was <c>/sys/health</c>, without the API version every route on a
    /// Vault-compatible server lives under. Against a real BastionVault cluster that is a 404 from
    /// every node, so the resolver reported "No vault node passed its health check" for two nodes
    /// that were both answering normally, and then picked one at random out of the SRV shuffle.
    ///
    /// The other tests here match the probe by host substring, which is precisely why none of them
    /// saw it — the URL's path was never asserted on by anything.
    /// </summary>
    [Fact]
    public async Task TheHealthProbeAsksForTheVersionedVaultHealthPath()
    {
        _dns.With(Service, "node1.example.com", 4200);
        Health("node1", 200);

        await Resolver().ResolveAsync(1, "vault.example.com");

        var probe = Assert.Single(_http.Requests);
        Assert.Equal("https://node1.example.com:4200/v1/sys/health", probe.Url);
    }

    /// <summary>
    /// A standby answers 429 and a performance standby 473; both serve reads, so both are healthy.
    ///
    /// Paired with the test above because together they are the whole of what went wrong in
    /// production: the path was wrong, so the one node that would have scored 200 and the one that
    /// would have scored 429 both scored 404 instead and the accept-list never got to matter.
    /// </summary>
    [Theory]
    [InlineData(200)]
    [InlineData(429)]
    [InlineData(472)]
    [InlineData(473)]
    public async Task ANodeServingTheVersionedPathIsHealthyOnEveryServingStatus(int status)
    {
        _dns.With(Service, "node1.example.com", 4200);
        Health("node1", status);

        var selection = await Resolver().ResolveAsync(1, "vault.example.com");

        Assert.Equal("https://node1.example.com:4200", selection.BaseUrl);
        Assert.Null(selection.Note);
    }
}

using System;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Secrets;
using ServerServices.Secrets;
using Xunit;

namespace ServerServices.Tests.Secrets;

/// <summary>
/// The grammar of a vault connection's address.
///
/// Worth its own class because the field is overloaded — it carries either one node or the name of a
/// cluster — and the two are told apart by shape alone. The failure this guards against is silent:
/// a bare <c>vault.example.com</c> read as a direct address produces a request to a URL with no
/// scheme, and a <c>https://vault.example.com</c> read as a discovery name produces an SRV query for
/// a name containing a colon. Neither throws where anyone is looking.
/// </summary>
[TestSubject(typeof(VaultAddress))]
public class VaultAddressTest
{
    [Theory]
    [InlineData("https://vault.example.com", "https://vault.example.com")]
    [InlineData("https://vault.example.com:4200", "https://vault.example.com:4200")]
    [InlineData("http://10.0.0.5:8200", "http://10.0.0.5:8200")]
    [InlineData("https://vault.example.com/", "https://vault.example.com")]
    [InlineData("  https://vault.example.com  ", "https://vault.example.com")]
    public void AnAbsoluteUrlIsOneNode(string input, string expected)
    {
        var address = VaultAddress.Parse(input);

        Assert.Equal(VaultAddressKind.Direct, address.Kind);
        Assert.False(address.IsDiscovery);
        Assert.Equal(expected, address.DirectBaseUrl);
    }

    /// <summary>
    /// A path prefix survives. Some deployments serve the vault behind a reverse proxy at
    /// <c>/vault</c>, and dropping it turns every request into a 404 from the proxy.
    /// </summary>
    [Fact]
    public void APathPrefixIsKept()
    {
        Assert.Equal("https://gw.example.com/vault",
            VaultAddress.Parse("https://gw.example.com/vault/").DirectBaseUrl);
    }

    [Fact]
    public void ABareDnsNameIsDiscoveredWithTheDefaultLabel()
    {
        var address = VaultAddress.Parse("vault.example.com");

        Assert.True(address.IsDiscovery);
        Assert.Equal(SecretVaultDefaults.SrvServiceLabel + ".vault.example.com", address.ServiceName);

        // https, not http: a bare name carries no scheme, and defaulting a credential channel to
        // plaintext because the operator did not type one is not a default worth having.
        Assert.Equal("https", address.Scheme);
    }

    [Fact]
    public void AnSrvPrefixedNameKeepsItsOwnLabel()
    {
        var address = VaultAddress.Parse("srv+https://_bv._tcp.vault.example.com");

        Assert.True(address.IsDiscovery);
        Assert.Equal("_bv._tcp.vault.example.com", address.ServiceName);
        Assert.Equal("https", address.Scheme);
    }

    /// <summary>
    /// The prefix form without an underscore label still gets the product default, so an operator can
    /// choose plaintext discovery without also having to know the SRV label.
    /// </summary>
    [Fact]
    public void AnSrvPrefixedBareNameGetsTheDefaultLabelAndTheGivenScheme()
    {
        var address = VaultAddress.Parse("srv+http://vault.example.com");

        Assert.Equal(SecretVaultDefaults.SrvServiceLabel + ".vault.example.com", address.ServiceName);
        Assert.Equal("http", address.Scheme);
    }

    [Fact]
    public void ADiscoveredNodeUrlUsesTheSrvTargetAndPort()
    {
        var address = VaultAddress.Parse("vault.example.com");

        // The trailing dot is legal in DNS and illegal in a URL authority.
        Assert.Equal("https://node1.example.com:4200",
            address.NodeBaseUrl("node1.example.com.", 4200));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void AnEmptyAddressIsRefused(string? input) =>
        Assert.Throws<InvalidParameterException>(() => VaultAddress.Parse(input));

    [Theory]
    // A scheme that is not http(s) — the classic way a URL field turns into a file read.
    [InlineData("file:///etc/passwd")]
    [InlineData("ftp://vault.example.com")]
    // Scheme-less host:port. Uri reads this as a scheme named "vault.example.com", so guessing is
    // worse than refusing while the operator is still looking at the form.
    [InlineData("vault.example.com:4200")]
    [InlineData("vault.example.com/vault")]
    // A discovery name cannot carry the port; the port is what the SRV record is for.
    [InlineData("srv+https://vault.example.com:4200")]
    [InlineData("srv+ftp://vault.example.com")]
    [InlineData("srv+https://")]
    [InlineData("srv+vault.example.com")]
    [InlineData("not a dns name")]
    public void AMalformedAddressIsRefused(string input) =>
        Assert.Throws<InvalidParameterException>(() => VaultAddress.Parse(input));

    /// <summary>
    /// The parameter name reaches the exception, because the API turns it into the field the form
    /// highlights.
    /// </summary>
    [Fact]
    public void TheRefusalNamesTheField()
    {
        var ex = Assert.Throws<InvalidParameterException>(
            () => VaultAddress.Parse("vault.example.com:4200", "BaseUrl"));

        Assert.Contains("BaseUrl", ex.Message + ex.ParameterName);
    }

    [Fact]
    public void ADirectAddressHasNoSingleDiscoveryName()
    {
        var address = VaultAddress.Parse("vault.example.com");

        Assert.Throws<InvalidOperationException>(() => address.DirectBaseUrl);
    }
}

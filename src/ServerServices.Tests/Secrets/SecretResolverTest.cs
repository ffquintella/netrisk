using System.Threading;
using System.Threading.Tasks;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Secrets;
using NSubstitute;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Secrets;
using ServerServices.Security;
using Xunit;

namespace ServerServices.Tests.Secrets;

/// <summary>
/// The two-branch read path every integration credential now goes through.
///
/// The reason this substitution was worth making across nineteen call sites: after the vault feature
/// exists, a credential column holds either ciphertext or a reference, and only one place should have
/// to know that. These tests pin the branch, and in particular pin the two cases where getting it
/// wrong is silent — a reference handed to a third party as a literal, and a malformed reference
/// treated as a credential.
/// </summary>
[TestSubject(typeof(SecretResolver))]
public class SecretResolverTest
{
    private static readonly ILogger Log = new LoggerConfiguration().CreateLogger();

    private readonly ISecretVaultService _vaults = Substitute.For<ISecretVaultService>();
    private readonly SecretProtector _protector = new(Log, "root-secret");
    private readonly SecretResolver _resolver;

    public SecretResolverTest()
    {
        _resolver = new SecretResolver(_protector, _vaults);
    }

    [Fact]
    public async Task DecryptsALiteralCredential()
    {
        var ciphertext = _protector.Protect("vision-one-key");

        Assert.Equal("vision-one-key", await _resolver.ResolveAsync(ciphertext));

        await _vaults.DidNotReceiveWithAnyArgs().ResolveAsync(null!, CancellationToken.None);
    }

    [Fact]
    public async Task ResolvesAVaultReferenceThroughTheVaultService()
    {
        var reference = SecretReference.Create(3, "db-prod", "password");

        _vaults.ResolveAsync(Arg.Any<SecretReference>(), Arg.Any<CancellationToken>())
            .Returns("p4ss");

        Assert.Equal("p4ss", await _resolver.ResolveAsync(reference.ToString()));

        await _vaults.Received(1).ResolveAsync(
            Arg.Is<SecretReference>(r => r.ConnectionId == 3
                                         && r.SecretId == "db-prod"
                                         && r.Field == "password"),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public async Task NothingStoredResolvesToNull(string? stored)
    {
        Assert.Null(await _resolver.ResolveAsync(stored));
    }

    [Fact]
    public async Task RefusesAMarkedValueThatDoesNotParse()
    {
        // Truncated in a database migration, or edited by hand. It is not a credential, and sending it
        // as one produces a 401 from someone else's API whose cause is invisible.
        var ex = await Assert.ThrowsAsync<SecretVaultResolutionException>(
            () => _resolver.ResolveAsync("vault:v1:notanumber:abc"));

        Assert.Contains("could not be parsed", ex.Message);

        await _vaults.DidNotReceiveWithAnyArgs().ResolveAsync(null!, CancellationToken.None);
    }

    [Fact]
    public async Task PassesAnUnencryptedLegacyValueThrough()
    {
        // A row written before credential encryption existed. The protector warns; the resolver must
        // still return the value, or an upgrade breaks every connection at once.
        Assert.Equal("plain-token", await _resolver.ResolveAsync("plain-token"));
    }

    [Fact]
    public async Task LetsAnUndecryptableCiphertextFail()
    {
        var foreign = new SecretProtector(Log, "another-installation").Protect("token");

        // Not swallowed into null: a connection that quietly authenticates with an empty credential is
        // the failure mode SecretProtectionException was introduced to remove.
        await Assert.ThrowsAsync<SecretProtectionException>(() => _resolver.ResolveAsync(foreign));
    }

    [Fact]
    public async Task PropagatesAVaultResolutionFailure()
    {
        _vaults.ResolveAsync(Arg.Any<SecretReference>(), Arg.Any<CancellationToken>())
            .Returns<string>(_ => throw new SecretVaultResolutionException("the vault said no"));

        var ex = await Assert.ThrowsAsync<SecretVaultResolutionException>(
            () => _resolver.ResolveAsync(SecretReference.Create(1, "s").ToString()));

        Assert.Equal("the vault said no", ex.Message);
    }

    [Fact]
    public void RecognisesAReferenceWithoutResolvingIt()
    {
        Assert.True(_resolver.IsVaultReference(SecretReference.Create(1, "s").ToString()));
        Assert.False(_resolver.IsVaultReference(_protector.Protect("token")));
        Assert.False(_resolver.IsVaultReference(null));
    }

    [Fact]
    public async Task ForwardsTheCancellationToken()
    {
        using var cts = new CancellationTokenSource();

        _vaults.ResolveAsync(Arg.Any<SecretReference>(), Arg.Any<CancellationToken>()).Returns("v");

        await _resolver.ResolveAsync(SecretReference.Create(1, "s").ToString(), cts.Token);

        await _vaults.Received(1).ResolveAsync(Arg.Any<SecretReference>(), cts.Token);
    }
}

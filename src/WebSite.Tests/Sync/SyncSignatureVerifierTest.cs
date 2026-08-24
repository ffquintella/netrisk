using System.Text;
using JetBrains.Annotations;
using Microsoft.Extensions.Caching.Memory;
using SyncContracts;
using WebSiteData.Sync;
using Xunit;

namespace WebSite.Tests.Sync;

/// <summary>
/// Covers the website's authentication boundary for the server -> website sync channel:
/// header presence, timestamp freshness, enrollment/key-id match, nonce replay and the
/// ECDSA signature itself.
/// </summary>
[TestSubject(typeof(SyncSignatureVerifier))]
public class SyncSignatureVerifierTest : IDisposable
{
    private const string Method = "POST";
    private static readonly byte[] Body = Encoding.UTF8.GetBytes("""{"cursor":42}""");

    private readonly SqliteDbContextFactory _factory = new();
    private readonly MemoryCache _cache = new(new MemoryCacheOptions());
    private readonly SyncSignatureVerifier _verifier;
    private readonly SyncKeyPair _key = SyncKeyPair.Create("enrolled-key-01");

    public SyncSignatureVerifierTest()
    {
        _verifier = new SyncSignatureVerifier(_factory, _cache);
    }

    private static long Now => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    private static string Sign(SyncKeyPair key, long timestamp, string nonce,
        string path = SyncRoutes.Push, byte[]? body = null)
        => SyncSignature.Sign(key.PrivateKeyPem, Method, path, timestamp, nonce, body ?? Body);

    // ---------------- header presence ----------------

    [Theory]
    [InlineData(null, "1700000000", "nonce", "sig")]
    [InlineData("", "1700000000", "nonce", "sig")]
    [InlineData("key", null, "nonce", "sig")]
    [InlineData("key", "", "nonce", "sig")]
    [InlineData("key", "1700000000", null, "sig")]
    [InlineData("key", "1700000000", "", "sig")]
    [InlineData("key", "1700000000", "nonce", null)]
    [InlineData("key", "1700000000", "nonce", "")]
    public async Task TestMissingSignatureHeadersAreRejected(string? keyId, string? timestamp,
        string? nonce, string? signature)
    {
        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, keyId, timestamp, nonce,
            signature, Body);

        Assert.False(result.Ok);
        Assert.Equal("Missing signature headers", result.Error);
    }

    [Theory]
    [InlineData("not-a-number")]
    [InlineData("12.5")]
    [InlineData("1700000000000000000000")]
    public async Task TestNonNumericTimestampIsRejected(string timestamp)
    {
        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId, timestamp,
            "nonce", "sig", Body);

        Assert.False(result.Ok);
        Assert.Equal("Invalid timestamp", result.Error);
    }

    [Theory]
    [InlineData(-301)]
    [InlineData(-100000)]
    [InlineData(301)]
    [InlineData(100000)]
    public async Task TestTimestampOutsideSkewWindowIsRejected(int offsetSeconds)
    {
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
        var timestamp = Now + offsetSeconds;

        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), "nonce", Sign(_key, timestamp, "nonce"), Body);

        Assert.False(result.Ok);
        Assert.Equal("Timestamp outside allowed window", result.Error);
    }

    [Theory]
    [InlineData(-290)]
    [InlineData(0)]
    [InlineData(290)]
    public async Task TestTimestampJustInsideSkewWindowIsAccepted(int offsetSeconds)
    {
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
        var timestamp = Now + offsetSeconds;
        var nonce = $"nonce-{offsetSeconds}";

        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), nonce, Sign(_key, timestamp, nonce), Body);

        Assert.True(result.Ok);
        Assert.Null(result.Error);
    }

    // ---------------- enrollment ----------------

    [Fact]
    public async Task TestNotEnrolledWhenNoSyncStateRowExists()
    {
        var timestamp = Now;

        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), "nonce", Sign(_key, timestamp, "nonce"), Body);

        Assert.False(result.Ok);
        Assert.Equal("Website is not enrolled", result.Error);
    }

    [Fact]
    public async Task TestNotEnrolledWhenPublicKeyIsNull()
    {
        _factory.SeedSyncState(_key.KeyId, null);
        var timestamp = Now;

        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), "nonce", Sign(_key, timestamp, "nonce"), Body);

        Assert.False(result.Ok);
        Assert.Equal("Website is not enrolled", result.Error);
    }

    [Fact]
    public async Task TestNotEnrolledWhenKeyIdIsNull()
    {
        _factory.SeedSyncState(null, _key.PublicKeyPem);
        var timestamp = Now;

        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), "nonce", Sign(_key, timestamp, "nonce"), Body);

        Assert.False(result.Ok);
        Assert.Equal("Website is not enrolled", result.Error);
    }

    [Fact]
    public async Task TestUnknownKeyIdIsRejected()
    {
        _factory.SeedSyncState("enrolled-key-01", _key.PublicKeyPem);
        var timestamp = Now;

        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, "some-other-key",
            timestamp.ToString(), "nonce", Sign(_key, timestamp, "nonce"), Body);

        Assert.False(result.Ok);
        Assert.Equal("Unknown key id", result.Error);
    }

    // ---------------- replay ----------------

    [Fact]
    public async Task TestReplayedNonceIsRejected()
    {
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
        var timestamp = Now;
        const string nonce = "replay-me";
        var signature = Sign(_key, timestamp, nonce);

        var first = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), nonce, signature, Body);
        var second = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), nonce, signature, Body);

        Assert.True(first.Ok);
        Assert.False(second.Ok);
        Assert.Equal("Replayed nonce", second.Error);
    }

    [Fact]
    public async Task TestSameNonceUnderDifferentKeyIdIsNotTreatedAsReplay()
    {
        // The nonce cache is scoped per key id, so the same nonce value from a different
        // (also enrolled) key must still be accepted.
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
        var timestamp = Now;
        const string nonce = "shared-nonce";

        var first = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), nonce, Sign(_key, timestamp, nonce), Body);

        var other = SyncKeyPair.Create("other-key-02");
        var second = await _verifier.VerifyAsync(Method, SyncRoutes.Push, other.KeyId,
            timestamp.ToString(), nonce, Sign(other, timestamp, nonce), Body,
            other.PublicKeyPem);

        Assert.True(first.Ok);
        Assert.True(second.Ok);
    }

    [Fact]
    public async Task TestNonceIsNotConsumedWhenSignatureIsInvalid()
    {
        // A failed verification must not burn the nonce, otherwise an attacker could grief a
        // legitimate request by pre-sending its nonce with a bogus signature.
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
        var timestamp = Now;
        const string nonce = "not-burned";

        var bad = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), nonce, "AAAA", Body);
        var good = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), nonce, Sign(_key, timestamp, nonce), Body);

        Assert.False(bad.Ok);
        Assert.True(good.Ok);
    }

    // ---------------- signature ----------------

    [Fact]
    public async Task TestSignatureFromWrongPrivateKeyIsRejected()
    {
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
        var attacker = SyncKeyPair.Create(_key.KeyId);
        var timestamp = Now;

        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), "nonce", Sign(attacker, timestamp, "nonce"), Body);

        Assert.False(result.Ok);
        Assert.Equal("Invalid signature", result.Error);
    }

    [Fact]
    public async Task TestMalformedSignatureIsRejected()
    {
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
        var timestamp = Now;

        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), "nonce", "this-is-not-base64!!", Body);

        Assert.False(result.Ok);
        Assert.Equal("Invalid signature", result.Error);
    }

    [Fact]
    public async Task TestTamperedBodyIsRejected()
    {
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
        var timestamp = Now;
        var signature = Sign(_key, timestamp, "nonce");

        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), "nonce", signature, Encoding.UTF8.GetBytes("""{"cursor":43}"""));

        Assert.False(result.Ok);
        Assert.Equal("Invalid signature", result.Error);
    }

    [Fact]
    public async Task TestSignatureForAnotherPathIsRejected()
    {
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
        var timestamp = Now;
        var signature = Sign(_key, timestamp, "nonce", SyncRoutes.Fast);

        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), "nonce", signature, Body);

        Assert.False(result.Ok);
        Assert.Equal("Invalid signature", result.Error);
    }

    [Fact]
    public async Task TestValidSignedRequestIsAccepted()
    {
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
        var timestamp = Now;
        const string nonce = "fresh-nonce";

        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), nonce, Sign(_key, timestamp, nonce), Body);

        Assert.True(result.Ok);
        Assert.Null(result.Error);
    }

    [Theory]
    [InlineData(SyncRoutes.Push)]
    [InlineData(SyncRoutes.Fast)]
    [InlineData(SyncRoutes.Ack)]
    [InlineData(SyncRoutes.RotateKey)]
    public async Task TestValidSignedRequestIsAcceptedForEverySyncRoute(string path)
    {
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
        var timestamp = Now;
        var nonce = $"nonce{path}";

        var result = await _verifier.VerifyAsync(Method, path, _key.KeyId, timestamp.ToString(),
            nonce, Sign(_key, timestamp, nonce, path), Body);

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task TestEmptyBodyIsSignedAndVerifiedConsistently()
    {
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
        var timestamp = Now;
        const string nonce = "empty-body";
        var empty = Array.Empty<byte>();

        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Ack, _key.KeyId,
            timestamp.ToString(), nonce, Sign(_key, timestamp, nonce, SyncRoutes.Ack, empty),
            empty);

        Assert.True(result.Ok);
    }

    // ---------------- publicKeyPemOverride ----------------

    [Fact]
    public async Task TestPublicKeyOverrideTakesPrecedenceOverStoredKey()
    {
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
        var rotated = SyncKeyPair.Create("rotated-key-03");
        var timestamp = Now;
        const string nonce = "override-nonce";

        // Signed with the override's private key, which the stored public key cannot verify.
        var result = await _verifier.VerifyAsync(Method, SyncRoutes.RotateKey, rotated.KeyId,
            timestamp.ToString(), nonce, Sign(rotated, timestamp, nonce, SyncRoutes.RotateKey),
            Body, rotated.PublicKeyPem);

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task TestPublicKeyOverrideBypassesEnrollmentAndKeyIdChecks()
    {
        // No SyncState row at all, and an arbitrary key id: the override short-circuits both.
        var rotated = SyncKeyPair.Create("unknown-key");
        var timestamp = Now;
        const string nonce = "override-no-enrollment";

        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, rotated.KeyId,
            timestamp.ToString(), nonce, Sign(rotated, timestamp, nonce), Body,
            rotated.PublicKeyPem);

        Assert.True(result.Ok);
    }

    [Fact]
    public async Task TestPublicKeyOverrideStillRejectsAWrongSignature()
    {
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
        var rotated = SyncKeyPair.Create("rotated-key-04");
        var timestamp = Now;

        // Signed with the *stored* key while the override names the rotated one.
        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, rotated.KeyId,
            timestamp.ToString(), "nonce", Sign(_key, timestamp, "nonce"), Body,
            rotated.PublicKeyPem);

        Assert.False(result.Ok);
        Assert.Equal("Invalid signature", result.Error);
    }

    [Fact]
    public async Task TestPublicKeyOverrideThatIsNotAValidPemIsRejected()
    {
        _factory.SeedSyncState(_key.KeyId, _key.PublicKeyPem);
        var timestamp = Now;

        var result = await _verifier.VerifyAsync(Method, SyncRoutes.Push, _key.KeyId,
            timestamp.ToString(), "nonce", Sign(_key, timestamp, "nonce"), Body, "not-a-pem");

        Assert.False(result.Ok);
        Assert.Equal("Invalid signature", result.Error);
    }

    public void Dispose()
    {
        _cache.Dispose();
        _factory.Dispose();
    }
}

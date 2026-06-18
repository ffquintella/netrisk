using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using SyncContracts;

namespace WebSiteData.Sync;

public class SignatureCheck
{
    public bool Ok { get; init; }
    public string? Error { get; init; }
    public static SignatureCheck Success() => new() { Ok = true };
    public static SignatureCheck Fail(string error) => new() { Ok = false, Error = error };
}

/// <summary>
/// Verifies a signed sync request against the enrolled public key: key id match, timestamp
/// freshness (anti-replay window), nonce uniqueness, and ECDSA signature validity.
/// </summary>
public class SyncSignatureVerifier
{
    private const int MaxSkewSeconds = 300;
    private readonly IDbContextFactory<WebSiteDbContext> _factory;
    private readonly IMemoryCache _cache;

    public SyncSignatureVerifier(IDbContextFactory<WebSiteDbContext> factory, IMemoryCache cache)
    {
        _factory = factory;
        _cache = cache;
    }

    /// <summary>Verifies headers + body. When <paramref name="publicKeyPemOverride"/> is set
    /// (key rotation), it is used instead of the stored key.</summary>
    public async Task<SignatureCheck> VerifyAsync(string method, string path,
        string? keyId, string? timestampHeader, string? nonce, string? signature,
        byte[] body, string? publicKeyPemOverride = null)
    {
        if (string.IsNullOrEmpty(keyId) || string.IsNullOrEmpty(timestampHeader) ||
            string.IsNullOrEmpty(nonce) || string.IsNullOrEmpty(signature))
            return SignatureCheck.Fail("Missing signature headers");

        if (!long.TryParse(timestampHeader, out var timestamp))
            return SignatureCheck.Fail("Invalid timestamp");

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (Math.Abs(now - timestamp) > MaxSkewSeconds)
            return SignatureCheck.Fail("Timestamp outside allowed window");

        string publicKeyPem;
        if (publicKeyPemOverride != null)
        {
            publicKeyPem = publicKeyPemOverride;
        }
        else
        {
            await using var db = await _factory.CreateDbContextAsync();
            var state = await db.SyncState.AsNoTracking().FirstOrDefaultAsync();
            if (state?.ApiPublicKeyPem == null || state.ApiKeyId == null)
                return SignatureCheck.Fail("Website is not enrolled");
            if (state.ApiKeyId != keyId)
                return SignatureCheck.Fail("Unknown key id");
            publicKeyPem = state.ApiPublicKeyPem;
        }

        var nonceKey = $"sync-nonce:{keyId}:{nonce}";
        if (_cache.TryGetValue(nonceKey, out _))
            return SignatureCheck.Fail("Replayed nonce");

        if (!SyncSignature.Verify(publicKeyPem, method, path, timestamp, nonce, body, signature))
            return SignatureCheck.Fail("Invalid signature");

        _cache.Set(nonceKey, true, TimeSpan.FromSeconds(MaxSkewSeconds * 2));
        return SignatureCheck.Success();
    }
}

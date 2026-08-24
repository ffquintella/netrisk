using System.Security.Cryptography;
using System.Text;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Findings;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Findings;

/// <summary>
/// Scoped, revocable API tokens for non-interactive callers (Track 3 milestone 3.5.1).
///
/// Token shape: <c>nrk_&lt;keyId&gt;_&lt;secret&gt;</c>. The prefix makes a leaked token grep-able by
/// secret scanners, and the key id is stored in clear so authentication is one indexed lookup rather
/// than a hash comparison against every row.
/// </summary>
public class ApiTokensService(ILogger logger, IDalService dalService) : ServiceBase(logger, dalService),
    IApiTokensService
{
    /// <summary>Bytes of entropy in the secret half. 32 bytes is 256 bits, per the spec.</summary>
    private const int SecretBytes = 32;

    /// <summary>Bytes in the public key id. 8 bytes is ample to make collisions a non-event.</summary>
    private const int KeyIdBytes = 8;

    public async Task<IssuedApiToken> IssueAsync(string name, string scopes, int actsAsUserId,
        int? createdByUserId, DateTime? expiresAt = null, int? entityId = null, int? rateLimitPerMinute = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new InvalidParameterException(nameof(name), "An API token requires a name.");

        var unknown = ApiTokenScopes.Unknown(scopes);
        if (unknown.Length > 0)
            throw new InvalidParameterException(nameof(scopes),
                $"Unknown scope: {string.Join(", ", unknown)}. Available: {string.Join(", ", ApiTokenScopes.All)}.");

        var granted = ApiTokenScopes.Parse(scopes);
        if (granted.Length == 0)
            throw new InvalidParameterException(nameof(scopes),
                "An API token must grant at least one scope; a token with none can do nothing.");

        if (expiresAt != null && expiresAt <= DateTime.UtcNow)
            throw new InvalidParameterException(nameof(expiresAt),
                "An API token's expiry must be in the future.");

        var keyId = Convert.ToHexString(RandomNumberGenerator.GetBytes(KeyIdBytes)).ToLowerInvariant();
        var secret = Base64Url(RandomNumberGenerator.GetBytes(SecretBytes));

        await using var db = DalService.GetContext();

        var token = new ApiToken
        {
            Name = name,
            KeyId = keyId,
            SecretHash = HashSecret(secret),
            Scopes = string.Join(",", granted),
            ExpiresAt = expiresAt,
            EntityId = entityId,
            UserId = actsAsUserId,
            CreatedAt = DateTime.UtcNow,
            CreatedById = createdByUserId,
            RateLimitPerMinute = rateLimitPerMinute
        };

        db.ApiTokens.Add(token);
        await db.SaveChangesAsync();

        Logger.Information(
            "API token {KeyId} ({Name}) issued by user {Creator}, acting as user {User}, scopes {Scopes}, expires {Expiry}",
            keyId, name, createdByUserId, actsAsUserId, token.Scopes, expiresAt);

        return new IssuedApiToken
        {
            Id = token.Id,
            Name = token.Name,
            KeyId = keyId,
            Secret = Compose(keyId, secret),
            Scopes = granted,
            ExpiresAt = expiresAt,
            EntityId = entityId
        };
    }

    public async Task<ApiToken?> AuthenticateAsync(string presentedToken)
    {
        if (!TryParse(presentedToken, out var keyId, out var secret)) return null;

        await using var db = DalService.GetContext();

        var token = await db.ApiTokens
            .Include(t => t.User)
            .FirstOrDefaultAsync(t => t.KeyId == keyId);

        if (token == null) return null;
        if (!token.IsUsable(DateTime.UtcNow)) return null;

        // Fixed-time comparison: a byte-by-byte early exit on the stored hash leaks how much of a
        // guessed secret was right, which is enough to reconstruct it a character at a time.
        var candidate = HashSecret(secret);
        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(candidate), Encoding.UTF8.GetBytes(token.SecretHash)))
            return null;

        return token;
    }

    public async Task TouchAsync(int tokenId, DateTime whenUtc)
    {
        try
        {
            await using var db = DalService.GetContext();

            var token = await db.ApiTokens.FirstOrDefaultAsync(t => t.Id == tokenId);
            if (token == null) return;

            token.LastUsedAt = whenUtc;
            await db.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // last_used_at is an operational nicety — it makes an unused token findable. It is not
            // part of authentication, and a failure to write it must never fail a valid request.
            Logger.Warning("Could not record last use of API token {Token}: {Message}", tokenId, ex.Message);
        }
    }

    public async Task<List<ApiToken>> GetTokensAsync(bool includeRevoked = false)
    {
        await using var db = DalService.GetContext();

        var query = db.ApiTokens.AsNoTracking().Include(t => t.User).AsQueryable();
        if (!includeRevoked) query = query.Where(t => t.RevokedAt == null);

        return await query.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).ToListAsync();
    }

    public async Task<ApiToken> GetTokenAsync(int id)
    {
        await using var db = DalService.GetContext();

        var token = await db.ApiTokens.AsNoTracking().Include(t => t.User).FirstOrDefaultAsync(t => t.Id == id);
        if (token == null)
            throw new DataNotFoundException("api_tokens", id.ToString(), new Exception("API token not found"));

        return token;
    }

    public async Task<ApiToken> RevokeAsync(int id, int? revokedByUserId)
    {
        await using var db = DalService.GetContext();

        var token = await db.ApiTokens.FirstOrDefaultAsync(t => t.Id == id);
        if (token == null)
            throw new DataNotFoundException("api_tokens", id.ToString(), new Exception("API token not found"));

        if (token.RevokedAt != null) return token;

        token.RevokedAt = DateTime.UtcNow;
        token.RevokedById = revokedByUserId;

        await db.SaveChangesAsync();

        Logger.Information("API token {KeyId} ({Name}) revoked by user {User}", token.KeyId, token.Name,
            revokedByUserId);

        return token;
    }

    /// <summary>
    /// Splits <c>nrk_&lt;keyId&gt;_&lt;secret&gt;</c>. Returns false for anything else, including a
    /// JWT — the handler needs to be able to tell "not one of ours" from "one of ours and wrong".
    /// </summary>
    internal static bool TryParse(string? presented, out string keyId, out string secret)
    {
        keyId = string.Empty;
        secret = string.Empty;

        if (string.IsNullOrWhiteSpace(presented)) return false;

        var trimmed = presented.Trim();
        if (!trimmed.StartsWith(ApiToken.SecretPrefix, StringComparison.Ordinal)) return false;

        var body = trimmed.Substring(ApiToken.SecretPrefix.Length);
        var separator = body.IndexOf('_');
        if (separator <= 0 || separator == body.Length - 1) return false;

        keyId = body.Substring(0, separator);
        secret = body.Substring(separator + 1);

        return keyId.Length > 0 && secret.Length > 0;
    }

    internal static string Compose(string keyId, string secret) =>
        $"{ApiToken.SecretPrefix}{keyId}_{secret}";

    /// <summary>
    /// SHA-256, not bcrypt.
    ///
    /// A password needs a slow hash because it is low-entropy and guessable. This secret is 256 bits
    /// of CSPRNG output, so brute force is not the threat — and a deliberately slow hash on a code
    /// path that runs on every CI request would be a denial-of-service surface of its own.
    /// </summary>
    internal static string HashSecret(string secret) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

    /// <summary>
    /// URL-safe, unpadded base64: the token travels in an HTTP header and, in practice, through
    /// shell scripts and CI variable stores, none of which handle <c>+</c>, <c>/</c> or <c>=</c>
    /// reliably.
    /// </summary>
    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

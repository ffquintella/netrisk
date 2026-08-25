using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DAL.Entities;
using Fido2NetLib;
using Fido2NetLib.Objects;
using Microsoft.EntityFrameworkCore;
using Model.Authentication.WebAuthn;
using Model.Exceptions;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;

namespace ServerServices.Auth;

/// <summary>
/// FIDO2/WebAuthn ceremonies, the hardware-factor policy, and recovery codes
/// (Track 4 milestone 4.3.3).
///
/// The cryptography is fido2-net-lib's, not hand-rolled: attestation parsing, COSE key handling and
/// assertion verification are exactly the places where a home-grown implementation is subtly wrong and
/// nobody notices until it is bypassed.
///
/// Relying-party configuration comes from <c>authentication:webauthn:*</c>. The relying-party id is a
/// domain, and it must match the origin the ceremony page is served from — a mismatch is the single
/// most common WebAuthn setup failure, so <see cref="GetHardwareFactorStatusAsync"/> reports when it is
/// unset rather than letting every ceremony fail with a cryptic error.
/// </summary>
public class WebAuthnService : ServiceBase, IWebAuthnService
{
    /// <summary>
    /// Pending ceremonies, in memory. A challenge lives for a couple of minutes and is worthless
    /// afterwards; persisting it would put a live authentication challenge at rest for no gain. The
    /// consequence — a ceremony must complete against the instance that started it — is documented
    /// alongside the federated sign-in flow, which has the same shape.
    /// </summary>
    private static readonly ConcurrentDictionary<string, PendingCeremony> Ceremonies = new();

    private static readonly TimeSpan CeremonyLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Recovery codes per batch, and the characters they are drawn from.</summary>
    private const int RecoveryCodeLength = 10;

    private const string RecoveryAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
    private readonly Fido2 _fido2;
    private readonly string _relyingPartyId;

    public WebAuthnService(ILogger logger, IDalService dalService,
        Microsoft.Extensions.Configuration.IConfiguration configuration) : base(logger, dalService)
    {
        _configuration = configuration;

        // Empty is treated as absent throughout: a configuration key that is present but blank is the
        // same thing as an unset one, and the difference between the two is the kind of detail that
        // turns into an unparseable origin at construction time.
        _relyingPartyId = Setting(configuration, "authentication:webauthn:relyingPartyId")
                          ?? HostOf(Setting(configuration, "app:baseUrl"))
                          ?? "localhost";

        var origins = (Setting(configuration, "authentication:webauthn:origins")
                       ?? Setting(configuration, "app:baseUrl") ?? string.Empty)
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(origin => Uri.TryCreate(origin, UriKind.Absolute, out _))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        if (origins.Count == 0) origins.Add($"https://{_relyingPartyId}");

        _fido2 = new Fido2(new Fido2Configuration
        {
            ServerDomain = _relyingPartyId,
            ServerName = Setting(configuration, "authentication:webauthn:relyingPartyName") ?? "NetRisk",
            Origins = origins,
            // Two minutes of wall clock for a human to touch a key. The spec's default is 60 seconds,
            // which is short for someone fetching a YubiKey from a drawer.
            Timeout = 120_000
        });
    }

    private record PendingCeremony(
        string Id,
        int? UserId,
        string OptionsJson,
        bool IsRegistration,
        string? AuthenticatorName,
        DateTime ExpiresAt);

    // --- registration -----------------------------------------------------------------------

    public async Task<List<WebAuthnCredentialView>> GetCredentialsAsync(int userId, bool includeRevoked = false)
    {
        await using var db = DalService.GetContext();

        var credentials = await db.WebAuthnCredentials
            .Where(c => c.UserId == userId && (includeRevoked || c.RevokedAt == null))
            .OrderByDescending(c => c.Id)
            .ToListAsync();

        return credentials.Select(ToView).ToList();
    }

    public async Task<WebAuthnCeremonyOptions> BeginRegistrationAsync(int userId, string? authenticatorName)
    {
        await using var db = DalService.GetContext(bypassEntityScope: true);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Value == userId)
                   ?? throw new DataNotFoundException("user", userId.ToString(),
                       new Exception($"User {userId} was not found."));

        var existing = await db.WebAuthnCredentials
            .Where(c => c.UserId == userId && c.RevokedAt == null)
            .Select(c => c.CredentialId)
            .ToListAsync();

        var options = _fido2.RequestNewCredential(new RequestNewCredentialParams
        {
            User = new Fido2User
            {
                // The user handle. The NetRisk id rather than the login: a login can be renamed, and a
                // credential bound to a stale handle is a credential that no longer authenticates
                // anyone.
                Id = Encoding.UTF8.GetBytes(userId.ToString()),
                Name = user.Login,
                DisplayName = user.Name
            },
            // Excluding what the user already has is what makes the browser say "you have already
            // registered this key" instead of silently creating a second credential on the same device.
            ExcludeCredentials = existing
                .Select(id => new PublicKeyCredentialDescriptor(FromBase64Url(id)))
                .ToList(),
            AuthenticatorSelection = new AuthenticatorSelection
            {
                // Cross-platform so a hardware key qualifies; platform authenticators (Windows Hello,
                // Touch ID) are equally acceptable and this does not exclude them.
                UserVerification = UserVerificationRequirement.Preferred,
                ResidentKey = ResidentKeyRequirement.Preferred
            },
            // "None" by default: requiring attestation means maintaining an authenticator allow-list and
            // the FIDO metadata service, and a deployment that has not decided on one should not be
            // unable to enrol a key. Overridable for deployments that have.
            AttestationPreference = ParseAttestationPreference(
                Setting(_configuration, "authentication:webauthn:attestation"))
        });

        var ceremonyId = Base64Url(RandomNumberGenerator.GetBytes(24));
        var json = options.ToJson();

        Prune();
        Ceremonies[ceremonyId] = new PendingCeremony(ceremonyId, userId, json, true, authenticatorName,
            DateTime.UtcNow.Add(CeremonyLifetime));

        return new WebAuthnCeremonyOptions
        {
            CeremonyId = ceremonyId,
            OptionsJson = json,
            ExpiresInSeconds = (int)CeremonyLifetime.TotalSeconds
        };
    }

    public async Task<WebAuthnRegistrationResult> CompleteRegistrationAsync(string ceremonyId,
        string attestationJson)
    {
        var ceremony = Redeem(ceremonyId, expectRegistration: true);

        if (ceremony == null)
            return WebAuthnRegistrationResult.Fail(
                "This registration has expired or was already completed. Start it again.");

        AuthenticatorAttestationRawResponse? response;

        try
        {
            response = JsonSerializer.Deserialize<AuthenticatorAttestationRawResponse>(attestationJson,
                JsonOptions);
        }
        catch (JsonException ex)
        {
            return WebAuthnRegistrationResult.Fail($"The authenticator response was not valid JSON: {ex.Message}");
        }

        if (response == null) return WebAuthnRegistrationResult.Fail("The authenticator response was empty.");

        var options = CredentialCreateOptions.FromJson(ceremony.OptionsJson);

        try
        {
            var credential = await _fido2.MakeNewCredentialAsync(new MakeNewCredentialParams
            {
                AttestationResponse = response,
                OriginalOptions = options,
                IsCredentialIdUniqueToUserCallback = IsCredentialUniqueAsync
            }, CancellationToken.None);

            await using var db = DalService.GetContext(bypassEntityScope: true);

            var stored = new WebAuthnCredential
            {
                UserId = ceremony.UserId!.Value,
                CredentialId = Base64Url(credential.Id),
                PublicKey = Convert.ToBase64String(credential.PublicKey),
                SignCount = credential.SignCount,
                AaGuid = credential.AaGuid == Guid.Empty ? null : credential.AaGuid.ToString(),
                AttestationFormat = credential.AttestationFormat,
                Name = string.IsNullOrWhiteSpace(ceremony.AuthenticatorName)
                    ? DescribeAuthenticator(credential)
                    : ceremony.AuthenticatorName.Trim(),
                IsBackupEligible = credential.IsBackupEligible,
                IsBackedUp = credential.IsBackedUp,
                CreatedAt = DateTime.UtcNow
            };

            db.WebAuthnCredentials.Add(stored);
            await db.SaveChangesAsync();

            Logger.Information(
                "User {User} registered WebAuthn authenticator '{Name}' (format {Format}, backup-eligible {Backup})",
                stored.UserId, stored.Name, stored.AttestationFormat, stored.IsBackupEligible);

            return WebAuthnRegistrationResult.Ok(ToView(stored));
        }
        catch (Fido2VerificationException ex)
        {
            // The library's message names the specific check that failed (origin, challenge, RP id
            // hash), which is exactly what an administrator debugging a setup needs.
            Logger.Warning("WebAuthn registration for user {User} failed verification: {Message}",
                ceremony.UserId, ex.Message);

            return WebAuthnRegistrationResult.Fail($"The authenticator response was rejected: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "WebAuthn registration for user {User} threw", ceremony.UserId);
            return WebAuthnRegistrationResult.Fail($"The registration could not be completed: {ex.Message}");
        }
    }

    // --- authentication ---------------------------------------------------------------------

    public async Task<WebAuthnCeremonyOptions> BeginAssertionAsync(int? userId)
    {
        await using var db = DalService.GetContext(bypassEntityScope: true);

        var allowed = new List<PublicKeyCredentialDescriptor>();

        if (userId != null)
        {
            var credentialIds = await db.WebAuthnCredentials
                .Where(c => c.UserId == userId && c.RevokedAt == null)
                .Select(c => c.CredentialId)
                .ToListAsync();

            if (credentialIds.Count == 0)
                throw new InvalidParameterException(nameof(userId),
                    "This account has no registered authenticator.");

            allowed.AddRange(credentialIds.Select(id => new PublicKeyCredentialDescriptor(FromBase64Url(id))));
        }

        var options = _fido2.GetAssertionOptions(new GetAssertionOptionsParams
        {
            // Empty for a discoverable-credential ceremony: the authenticator then chooses, which is how
            // a passkey login without a username works.
            AllowedCredentials = allowed,
            UserVerification = UserVerificationRequirement.Preferred
        });

        var ceremonyId = Base64Url(RandomNumberGenerator.GetBytes(24));
        var json = options.ToJson();

        Prune();
        Ceremonies[ceremonyId] = new PendingCeremony(ceremonyId, userId, json, false, null,
            DateTime.UtcNow.Add(CeremonyLifetime));

        return new WebAuthnCeremonyOptions
        {
            CeremonyId = ceremonyId,
            OptionsJson = json,
            ExpiresInSeconds = (int)CeremonyLifetime.TotalSeconds
        };
    }

    public async Task<WebAuthnAssertionResult> CompleteAssertionAsync(string ceremonyId, string assertionJson)
    {
        var ceremony = Redeem(ceremonyId, expectRegistration: false);

        if (ceremony == null)
            return WebAuthnAssertionResult.Fail(
                "This sign-in has expired or was already completed. Start it again.");

        AuthenticatorAssertionRawResponse? response;

        try
        {
            response = JsonSerializer.Deserialize<AuthenticatorAssertionRawResponse>(assertionJson, JsonOptions);
        }
        catch (JsonException ex)
        {
            return WebAuthnAssertionResult.Fail($"The authenticator response was not valid JSON: {ex.Message}");
        }

        if (response?.RawId == null) return WebAuthnAssertionResult.Fail("The authenticator response was empty.");

        var credentialId = Base64Url(response.RawId);

        await using var db = DalService.GetContext(bypassEntityScope: true);

        var stored = await db.WebAuthnCredentials
            .FirstOrDefaultAsync(c => c.CredentialId == credentialId && c.RevokedAt == null);

        if (stored == null)
            return WebAuthnAssertionResult.Fail("That authenticator is not registered for any account.");

        // For a ceremony started for a specific user, the credential must belong to them. Without this
        // check, anyone with any registered key could complete a challenge issued for someone else.
        if (ceremony.UserId != null && stored.UserId != ceremony.UserId)
            return WebAuthnAssertionResult.Fail("That authenticator is not registered for this account.");

        var options = AssertionOptions.FromJson(ceremony.OptionsJson);

        try
        {
            var result = await _fido2.MakeAssertionAsync(new MakeAssertionParams
            {
                AssertionResponse = response,
                OriginalOptions = options,
                StoredPublicKey = Convert.FromBase64String(stored.PublicKey),
                StoredSignatureCounter = (uint)stored.SignCount,
                IsUserHandleOwnerOfCredentialIdCallback = (parameters, _) =>
                    Task.FromResult(Encoding.UTF8.GetString(parameters.UserHandle)
                                    == stored.UserId.ToString())
            }, CancellationToken.None);

            // The spec's clone detection: a counter that does not advance means either a cloned
            // authenticator or a replayed assertion. Authenticators that report 0 are exempt, because a
            // constant 0 is explicitly allowed and is what Apple's platform authenticator does.
            if (result.SignCount != 0 && result.SignCount <= stored.SignCount)
            {
                Logger.Error(
                    "WebAuthn assertion for user {User} refused: signature counter did not advance "
                    + "({Presented} <= {Stored}). This is the documented signal of a cloned authenticator.",
                    stored.UserId, result.SignCount, stored.SignCount);

                return WebAuthnAssertionResult.Fail(
                    "The authenticator's signature counter did not advance, which can indicate a cloned "
                    + "credential. The credential was not accepted; remove and re-register it.",
                    counterRegression: true);
            }

            stored.SignCount = result.SignCount;
            stored.LastUsedAt = DateTime.UtcNow;
            stored.IsBackedUp = result.IsBackedUp;

            await db.SaveChangesAsync();

            Logger.Information("User {User} authenticated with WebAuthn authenticator '{Name}'",
                stored.UserId, stored.Name);

            return WebAuthnAssertionResult.Ok(stored.UserId);
        }
        catch (Fido2VerificationException ex)
        {
            Logger.Warning("WebAuthn assertion for user {User} failed verification: {Message}",
                stored.UserId, ex.Message);

            return WebAuthnAssertionResult.Fail($"The authenticator response was rejected: {ex.Message}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "WebAuthn assertion for user {User} threw", stored.UserId);
            return WebAuthnAssertionResult.Fail($"The sign-in could not be completed: {ex.Message}");
        }
    }

    public async Task<WebAuthnCredentialView> RevokeCredentialAsync(int credentialId, int actingUserId)
    {
        await using var db = DalService.GetContext(bypassEntityScope: true);

        var stored = await db.WebAuthnCredentials.FirstOrDefaultAsync(c => c.Id == credentialId)
                     ?? throw new DataNotFoundException("webauthn_credentials", credentialId.ToString(),
                         new Exception($"Credential {credentialId} was not found."));

        if (stored.RevokedAt == null)
        {
            stored.RevokedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();

            Logger.Warning("WebAuthn authenticator '{Name}' for user {User} was revoked by user {Actor}",
                stored.Name, stored.UserId, actingUserId);
        }

        return ToView(stored);
    }

    // --- recovery codes ---------------------------------------------------------------------

    public async Task<RecoveryCodeBatch> GenerateRecoveryCodesAsync(int userId, int? generatedByUserId,
        int count = 10)
    {
        var wanted = Math.Clamp(count, 1, 25);

        await using var db = DalService.GetContext(bypassEntityScope: true);

        if (!await db.Users.AnyAsync(u => u.Value == userId))
            throw new DataNotFoundException("user", userId.ToString(),
                new Exception($"User {userId} was not found."));

        // Unused codes from an earlier batch are removed. Generating a new batch is what someone does
        // after losing the old one, so leaving the old codes valid would defeat the point.
        var previous = await db.MfaRecoveryCodes
            .Where(c => c.UserId == userId && c.UsedAt == null)
            .ToListAsync();

        db.MfaRecoveryCodes.RemoveRange(previous);

        var codes = new List<string>();

        for (var index = 0; index < wanted; index++)
        {
            var code = GenerateCode();
            codes.Add(code);

            db.MfaRecoveryCodes.Add(new MfaRecoveryCode
            {
                UserId = userId,
                CodeHash = HashCode(code),
                CreatedAt = DateTime.UtcNow,
                CreatedById = generatedByUserId
            });
        }

        await db.SaveChangesAsync();

        // Audited as a warning, not information: issuing recovery codes creates a way past the hardware
        // factor, and that is worth being able to find in a log later.
        Logger.Warning("{Count} MFA recovery code(s) generated for user {User} by user {Actor}",
            wanted, userId, generatedByUserId);

        return new RecoveryCodeBatch
        {
            UserId = userId,
            Codes = codes,
            GeneratedAt = DateTime.UtcNow
        };
    }

    public async Task<bool> RedeemRecoveryCodeAsync(int userId, string code)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;

        var hash = HashCode(Normalize(code));

        await using var db = DalService.GetContext(bypassEntityScope: true);

        var stored = await db.MfaRecoveryCodes
            .FirstOrDefaultAsync(c => c.UserId == userId && c.UsedAt == null && c.CodeHash == hash);

        if (stored == null) return false;

        stored.UsedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();

        Logger.Warning("User {User} signed in with an MFA recovery code", userId);

        return true;
    }

    public async Task<HardwareFactorStatus> GetHardwareFactorStatusAsync(int userId)
    {
        await using var db = DalService.GetContext(bypassEntityScope: true);

        var user = await db.Users.FirstOrDefaultAsync(u => u.Value == userId)
                   ?? throw new DataNotFoundException("user", userId.ToString(),
                       new Exception($"User {userId} was not found."));

        var authenticators = await db.WebAuthnCredentials
            .CountAsync(c => c.UserId == userId && c.RevokedAt == null);

        var recoveryCodes = await db.MfaRecoveryCodes
            .CountAsync(c => c.UserId == userId && c.UsedAt == null);

        var policy = bool.TryParse(Setting(_configuration, "authentication:requireHardwareFactorForAdmins"),
            out var enforce) && enforce;

        // The role's admin flag counts as well as the user's own: "users in admin roles" is what the
        // policy says, and a role-granted administrator is exactly the account that needs it most.
        var isAdmin = user.Admin
                      || await db.Roles.AnyAsync(r => r.Value == user.RoleId && r.Admin);

        var required = policy && isAdmin;
        var satisfied = !required || authenticators > 0;

        string? guidance = null;

        if (required && authenticators == 0)
            guidance = "This account holds an administrative role and the hardware-factor policy is on. "
                       + "Register a security key before the policy is enforced at login.";
        else if (required && recoveryCodes == 0)
            guidance = "No recovery codes remain. Generate a batch so a lost security key does not lock "
                       + "this account out.";
        else if (!policy && authenticators > 0)
            guidance = "A security key is registered. The hardware-factor policy is currently off, so it "
                       + "is not required at login.";

        return new HardwareFactorStatus
        {
            UserId = userId,
            Required = required,
            RegisteredAuthenticators = authenticators,
            UnusedRecoveryCodes = recoveryCodes,
            Satisfied = satisfied,
            Guidance = guidance
        };
    }

    // --- helpers ----------------------------------------------------------------------------

    /// <summary>
    /// The spec requires a credential id to be unique across users. Enforced here as well as by the
    /// unique index, because the library asks before it stores anything and a clear answer at that
    /// point is better than a constraint violation afterwards.
    /// </summary>
    private async Task<bool> IsCredentialUniqueAsync(IsCredentialIdUniqueToUserParams parameters,
        CancellationToken ct)
    {
        var credentialId = Base64Url(parameters.CredentialId);

        await using var db = DalService.GetContext(bypassEntityScope: true);

        return !await db.WebAuthnCredentials.AnyAsync(c => c.CredentialId == credentialId, ct);
    }

    private static PendingCeremony? Redeem(string ceremonyId, bool expectRegistration)
    {
        Prune();

        if (string.IsNullOrWhiteSpace(ceremonyId)) return null;

        // Removed on redemption, so a challenge cannot be answered twice.
        if (!Ceremonies.TryRemove(ceremonyId, out var ceremony)) return null;

        if (ceremony.ExpiresAt <= DateTime.UtcNow) return null;
        if (ceremony.IsRegistration != expectRegistration) return null;

        return ceremony;
    }

    private static void Prune()
    {
        var now = DateTime.UtcNow;

        foreach (var (id, ceremony) in Ceremonies)
            if (ceremony.ExpiresAt <= now) Ceremonies.TryRemove(id, out _);
    }

    private static AttestationConveyancePreference ParseAttestationPreference(string? configured) =>
        (configured ?? string.Empty).Trim().ToLowerInvariant() switch
        {
            "indirect" => AttestationConveyancePreference.Indirect,
            "direct" => AttestationConveyancePreference.Direct,
            "enterprise" => AttestationConveyancePreference.Enterprise,
            _ => AttestationConveyancePreference.None
        };

    /// <summary>
    /// A default label when the user did not supply one. The AAGUID identifies the model, but resolving
    /// it to a product name needs the FIDO metadata service, so the format plus a short AAGUID prefix
    /// is as specific as this can honestly be.
    /// </summary>
    private static string DescribeAuthenticator(RegisteredPublicKeyCredential credential)
    {
        if (credential.AaGuid == Guid.Empty)
            return credential.IsBackupEligible ? "Passkey" : "Security key";

        return $"Authenticator {credential.AaGuid.ToString()[..8]}";
    }

    private static string GenerateCode()
    {
        var characters = new char[RecoveryCodeLength];

        for (var index = 0; index < characters.Length; index++)
            characters[index] = RecoveryAlphabet[RandomNumberGenerator.GetInt32(RecoveryAlphabet.Length)];

        // Grouped for legibility; the separator is stripped on redemption so either form is accepted.
        return new string(characters[..5]) + "-" + new string(characters[5..]);
    }

    private static string Normalize(string code) =>
        code.Trim().Replace("-", "").Replace(" ", "").ToUpperInvariant();

    private static string HashCode(string code) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Normalize(code)))).ToLowerInvariant();

    private static WebAuthnCredentialView ToView(WebAuthnCredential credential) => new()
    {
        Id = credential.Id,
        UserId = credential.UserId,
        Name = credential.Name,
        AttestationFormat = credential.AttestationFormat,
        AaGuid = credential.AaGuid,
        IsBackupEligible = credential.IsBackupEligible,
        IsBackedUp = credential.IsBackedUp,
        CreatedAt = credential.CreatedAt,
        LastUsedAt = credential.LastUsedAt,
        RevokedAt = credential.RevokedAt
    };

    private static string? HostOf(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : null;

    /// <summary>A configuration value, with blank treated as unset.</summary>
    private static string? Setting(Microsoft.Extensions.Configuration.IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static string Base64Url(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] FromBase64Url(string value)
    {
        var padded = value.Replace('-', '+').Replace('_', '/');
        padded += new string('=', (4 - padded.Length % 4) % 4);
        return Convert.FromBase64String(padded);
    }
}

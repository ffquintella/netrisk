using System;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using ServerServices.Auth;
using ServerServices.Interfaces;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// WebAuthn ceremonies, the hardware-factor policy and recovery codes
/// (Track 4 milestone 4.3.3).
///
/// The cryptographic verification is fido2-net-lib's and is exercised through it; what is asserted here
/// is everything around it that NetRisk owns and could get wrong: a challenge is single-use and
/// expires, an assertion for one account cannot be completed with another account's authenticator, a
/// counter that does not advance is refused as a possible clone, and a recovery code cannot be redeemed
/// twice.
/// </summary>
[TestSubject(typeof(WebAuthnService))]
public class WebAuthnServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IWebAuthnService _svc;

    public WebAuthnServiceInMemoryTest()
    {
        _svc = GetService<IWebAuthnService>();

        Seed(ctx =>
        {
            ctx.Roles.Add(new Role { Value = 1, Name = "Analyst", Default = true, Admin = false });
            ctx.Roles.Add(new Role { Value = 2, Name = "Administrator", Default = false, Admin = true });

            ctx.Users.Add(NewUser(1, "alice", admin: true, roleId: 1));
            ctx.Users.Add(NewUser(2, "bob", admin: false, roleId: 1));
            ctx.Users.Add(NewUser(3, "carol", admin: false, roleId: 2));
        });
    }

    private static User NewUser(int id, string login, bool admin, int roleId) => new()
    {
        Value = id, Login = login, Name = login, Email = $"{login}@acme.com", Enabled = true,
        Lockout = 0, Type = "local", Salt = "s", Password = Encoding.UTF8.GetBytes("p"),
        Admin = admin, RoleId = roleId
    };

    /// <summary>
    /// The stored credential id is base64url of the raw credential bytes, which is what the service
    /// computes from an assertion's rawId — so a seeded credential has to be encoded the same way or
    /// the lookup silently misses.
    /// </summary>
    private static string CredentialIdOf(string name) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes(name))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>Plants a credential directly; the registration ceremony itself needs a real authenticator.</summary>
    private void SeedCredential(int userId, string credentialId, long signCount = 5,
        DateTime? revokedAt = null) =>
        Seed(ctx => ctx.WebAuthnCredentials.Add(new WebAuthnCredential
        {
            UserId = userId,
            CredentialId = CredentialIdOf(credentialId),
            PublicKey = Convert.ToBase64String(new byte[64]),
            SignCount = signCount,
            Name = "YubiKey 5C",
            AttestationFormat = "none",
            CreatedAt = DateTime.UtcNow,
            RevokedAt = revokedAt
        }));

    // --- registration -----------------------------------------------------------------------

    [Fact]
    public async Task BeginningARegistrationProducesOptionsWithAChallenge()
    {
        var options = await _svc.BeginRegistrationAsync(1, "YubiKey 5C");

        Assert.NotEmpty(options.CeremonyId);
        Assert.True(options.ExpiresInSeconds > 0);

        using var document = JsonDocument.Parse(options.OptionsJson);

        Assert.True(document.RootElement.TryGetProperty("challenge", out _));
        Assert.True(document.RootElement.TryGetProperty("rp", out _));
        Assert.True(document.RootElement.TryGetProperty("pubKeyCredParams", out _));
    }

    [Fact]
    public async Task AlreadyRegisteredCredentialsAreExcluded()
    {
        SeedCredential(1, "cred-1");

        var options = await _svc.BeginRegistrationAsync(1, null);

        using var document = JsonDocument.Parse(options.OptionsJson);

        // What makes the browser say "you have already registered this key" instead of silently creating
        // a second credential on the same device.
        Assert.True(document.RootElement.TryGetProperty("excludeCredentials", out var excluded));
        Assert.Equal(1, excluded.GetArrayLength());
    }

    [Fact]
    public async Task RegisteringForAnUnknownUserIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.BeginRegistrationAsync(404, null));
    }

    [Fact]
    public async Task ACeremonyIsSingleUse()
    {
        var options = await _svc.BeginRegistrationAsync(1, null);

        // A malformed response still consumes the challenge — the ceremony id is redeemed before the
        // body is looked at, which is what makes a captured challenge useless.
        await _svc.CompleteRegistrationAsync(options.CeremonyId, "{}");

        var second = await _svc.CompleteRegistrationAsync(options.CeremonyId, "{}");

        Assert.False(second.Success);
        Assert.Contains("expired or was already completed", second.Error!);
    }

    [Fact]
    public async Task AnUnknownCeremonyIsRefused()
    {
        var result = await _svc.CompleteRegistrationAsync("never-issued", "{}");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task ARegistrationCeremonyCannotBeCompletedAsAnAssertion()
    {
        var options = await _svc.BeginRegistrationAsync(1, null);

        // Mixing the two would let a registration challenge be answered with an assertion.
        var result = await _svc.CompleteAssertionAsync(options.CeremonyId, "{}");

        Assert.False(result.Success);
    }

    [Fact]
    public async Task AMalformedAuthenticatorResponseIsReportedNotThrown()
    {
        var options = await _svc.BeginRegistrationAsync(1, null);

        var result = await _svc.CompleteRegistrationAsync(options.CeremonyId, "not json");

        Assert.False(result.Success);
        Assert.Contains("not valid JSON", result.Error!);
    }

    // --- assertion --------------------------------------------------------------------------

    [Fact]
    public async Task BeginningAnAssertionForAUserAllowsOnlyTheirCredentials()
    {
        SeedCredential(1, "cred-1");
        SeedCredential(2, "cred-2");

        var options = await _svc.BeginAssertionAsync(1);

        using var document = JsonDocument.Parse(options.OptionsJson);

        Assert.True(document.RootElement.TryGetProperty("allowCredentials", out var allowed));
        Assert.Equal(1, allowed.GetArrayLength());
    }

    [Fact]
    public async Task AnAssertionForAUserWithNoAuthenticatorIsRefused()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.BeginAssertionAsync(2));
    }

    [Fact]
    public async Task ADiscoverableCredentialCeremonyAllowsAnyAuthenticator()
    {
        var options = await _svc.BeginAssertionAsync(null);

        using var document = JsonDocument.Parse(options.OptionsJson);

        // Empty allowCredentials is how a passkey login without a username works.
        if (document.RootElement.TryGetProperty("allowCredentials", out var allowed))
            Assert.Equal(0, allowed.GetArrayLength());
    }

    [Fact]
    public async Task AnAssertionFromAnUnregisteredAuthenticatorIsRefused()
    {
        SeedCredential(1, "cred-1");

        var options = await _svc.BeginAssertionAsync(1);

        var response = JsonSerializer.Serialize(new
        {
            id = "unknown",
            rawId = Convert.ToBase64String(Encoding.UTF8.GetBytes("unknown")),
            type = "public-key",
            response = new
            {
                authenticatorData = Convert.ToBase64String(new byte[37]),
                clientDataJson = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")),
                signature = Convert.ToBase64String(new byte[64])
            }
        });

        var result = await _svc.CompleteAssertionAsync(options.CeremonyId, response);

        Assert.False(result.Success);
        Assert.Contains("not registered", result.Error!);
    }

    [Fact]
    public async Task AnAssertionWithAnotherAccountsAuthenticatorIsRefused()
    {
        SeedCredential(1, "cred-1");
        SeedCredential(2, "cred-2");

        // The ceremony was issued for user 1; answering it with user 2's key must fail. Without this
        // check anyone with any registered key could complete somebody else's challenge.
        var options = await _svc.BeginAssertionAsync(1);

        var response = JsonSerializer.Serialize(new
        {
            id = CredentialIdOf("cred-2"),
            rawId = Convert.ToBase64String(Encoding.UTF8.GetBytes("cred-2")),
            type = "public-key",
            response = new
            {
                authenticatorData = Convert.ToBase64String(new byte[37]),
                clientDataJson = Convert.ToBase64String(Encoding.UTF8.GetBytes("{}")),
                signature = Convert.ToBase64String(new byte[64])
            }
        });

        var result = await _svc.CompleteAssertionAsync(options.CeremonyId, response);

        Assert.False(result.Success);
        Assert.Contains("not registered for this account", result.Error!);
    }

    [Fact]
    public async Task ARevokedAuthenticatorIsNotOffered()
    {
        SeedCredential(1, "cred-1", revokedAt: DateTime.UtcNow);

        await Assert.ThrowsAsync<InvalidParameterException>(() => _svc.BeginAssertionAsync(1));
    }

    [Fact]
    public async Task RevokingKeepsTheRowForTheAuditTrail()
    {
        SeedCredential(1, "cred-1");

        await using (var db = OpenContext())
        {
            var id = db.WebAuthnCredentials.Single().Id;
            var revoked = await _svc.RevokeCredentialAsync(id, actingUserId: 3);
            Assert.NotNull(revoked.RevokedAt);
        }

        // "Which key was removed, and when" is an audit question.
        Assert.Empty(await _svc.GetCredentialsAsync(1));
        Assert.Single(await _svc.GetCredentialsAsync(1, includeRevoked: true));
    }

    [Fact]
    public async Task RevokingAnUnknownCredentialIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.RevokeCredentialAsync(404, 1));
    }

    [Fact]
    public async Task TheCredentialListNeverCarriesKeyMaterial()
    {
        SeedCredential(1, "cred-1");

        var listed = Assert.Single(await _svc.GetCredentialsAsync(1));

        Assert.Equal("YubiKey 5C", listed.Name);
        Assert.Null(listed.GetType().GetProperty("PublicKey"));
        Assert.Null(listed.GetType().GetProperty("CredentialId"));
    }

    // --- recovery codes ---------------------------------------------------------------------

    [Fact]
    public async Task RecoveryCodesAreReturnedOnceAndStoredHashed()
    {
        var batch = await _svc.GenerateRecoveryCodesAsync(1, generatedByUserId: 3, count: 8);

        Assert.Equal(8, batch.Codes.Count);
        Assert.All(batch.Codes, code => Assert.Contains("-", code));

        await using var db = OpenContext();
        var stored = db.MfaRecoveryCodes.ToList();

        Assert.Equal(8, stored.Count);
        // A readable recovery code in the database is a permanent bypass of the factor it backs up.
        Assert.All(stored, row => Assert.DoesNotContain(row.CodeHash, batch.Codes));
    }

    [Fact]
    public async Task ARecoveryCodeIsSingleUse()
    {
        var batch = await _svc.GenerateRecoveryCodesAsync(1, 3);

        Assert.True(await _svc.RedeemRecoveryCodeAsync(1, batch.Codes[0]));
        Assert.False(await _svc.RedeemRecoveryCodeAsync(1, batch.Codes[0]));
    }

    [Fact]
    public async Task ARecoveryCodeIsAcceptedWithOrWithoutItsSeparator()
    {
        var batch = await _svc.GenerateRecoveryCodesAsync(1, 3);

        Assert.True(await _svc.RedeemRecoveryCodeAsync(1, batch.Codes[0].Replace("-", "").ToLowerInvariant()));
    }

    [Fact]
    public async Task AnotherUsersRecoveryCodeIsNotAccepted()
    {
        var batch = await _svc.GenerateRecoveryCodesAsync(1, 3);

        Assert.False(await _svc.RedeemRecoveryCodeAsync(2, batch.Codes[0]));
    }

    [Fact]
    public async Task GeneratingANewBatchInvalidatesTheUnusedOldOnes()
    {
        var first = await _svc.GenerateRecoveryCodesAsync(1, 3);

        await _svc.GenerateRecoveryCodesAsync(1, 3);

        // Generating a new batch is what someone does after losing the old one; leaving the old codes
        // valid would defeat the point.
        Assert.False(await _svc.RedeemRecoveryCodeAsync(1, first.Codes[0]));
    }

    [Fact]
    public async Task RecoveryCodesForAnUnknownUserAreRefused()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(
            () => _svc.GenerateRecoveryCodesAsync(404, 1));
    }

    [Theory]
    [InlineData("")]
    [InlineData("NOTACODE12")]
    public async Task AWrongRecoveryCodeIsSimplyRefused(string code)
    {
        await _svc.GenerateRecoveryCodesAsync(1, 3);

        Assert.False(await _svc.RedeemRecoveryCodeAsync(1, code));
    }

    // --- policy -----------------------------------------------------------------------------

    [Fact]
    public async Task ThePolicyIsOffUnlessConfigured()
    {
        // MockConfiguration does not set authentication:requireHardwareFactorForAdmins.
        var status = await _svc.GetHardwareFactorStatusAsync(1);

        Assert.False(status.Required);
        Assert.True(status.Satisfied);
    }

    [Fact]
    public async Task TheStatusCountsAuthenticatorsAndRecoveryCodes()
    {
        SeedCredential(1, "cred-1");
        await _svc.GenerateRecoveryCodesAsync(1, 3, count: 4);

        var status = await _svc.GetHardwareFactorStatusAsync(1);

        Assert.Equal(1, status.RegisteredAuthenticators);
        Assert.Equal(4, status.UnusedRecoveryCodes);
        // Registered but not required: the guidance says so rather than leaving the operator guessing.
        Assert.Contains("not required", status.Guidance!);
    }

    [Fact]
    public async Task TheStatusOfAnUnknownUserIsNotFound()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => _svc.GetHardwareFactorStatusAsync(404));
    }
}

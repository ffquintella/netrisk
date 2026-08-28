using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ClientServices.Services;
using Microsoft.IdentityModel.Tokens;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// The renewal window the desktop client applies to its session token.
///
/// This exists because the window used to be a hard-coded 300 minutes while the API mints 60-minute
/// tokens by default, so every token the client received was condemned on arrival and the client
/// asked for another one on every single REST call.
/// </summary>
public class TokenRenewalPolicyTest
{
    [Theory]
    [InlineData(1440, 5)]   // the old day-long token: capped at the maximum
    [InlineData(60, 5)]     // JwtDefaults.TimeoutMinutes, the current server default
    [InlineData(20, 5)]     // a quarter is still above the cap
    [InlineData(16, 4)]
    [InlineData(4, 1)]
    [InlineData(2, 0)]      // a quarter rounds below a minute: renew only once it is actually expired
    public void SlackIsAQuarterOfTheLifetimeCappedAtFiveMinutes(int lifetimeMinutes, int expectedSlack)
    {
        var validFrom = new DateTime(2026, 8, 28, 19, 34, 0, DateTimeKind.Utc);

        var slack = TokenRenewalPolicy.SlackMinutesFor(validFrom, validFrom.AddMinutes(lifetimeMinutes));

        Assert.Equal(expectedSlack, slack);
    }

    /// <summary>
    /// The invariant the incident violated: the client must never demand more remaining validity
    /// than the server grants in the first place, whatever <c>JWT:Timeout</c> is set to.
    /// </summary>
    [Fact]
    public void SlackIsAlwaysSmallerThanTheTokenLifetime()
    {
        var validFrom = new DateTime(2026, 8, 28, 19, 34, 0, DateTimeKind.Utc);

        for (var lifetime = 1; lifetime <= 1440; lifetime++)
        {
            var slack = TokenRenewalPolicy.SlackMinutesFor(validFrom, validFrom.AddMinutes(lifetime));

            Assert.True(slack < lifetime,
                $"a {lifetime}-minute token would be renewed on arrival: slack {slack}");
            Assert.True(slack >= 0, $"negative slack for a {lifetime}-minute token: {slack}");
        }
    }

    [Fact]
    public void AnAlreadyExpiredOrZeroLifetimeTokenGetsNoSlack()
    {
        var validFrom = new DateTime(2026, 8, 28, 19, 34, 0, DateTimeKind.Utc);

        Assert.Equal(0, TokenRenewalPolicy.SlackMinutesFor(validFrom, validFrom));
        Assert.Equal(0, TokenRenewalPolicy.SlackMinutesFor(validFrom, validFrom.AddMinutes(-30)));
    }

    [Fact]
    public void SlackIsReadFromARealTokenLifetime()
    {
        Assert.Equal(5, TokenRenewalPolicy.SlackMinutesFor(TestTokens.Create(lifetimeMinutes: 60)));
        Assert.Equal(1, TokenRenewalPolicy.SlackMinutesFor(TestTokens.Create(lifetimeMinutes: 4)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-a-jwt")]
    [InlineData("aGVhZGVy.cGF5bG9hZA.c2ln")]
    public void AnUnreadableTokenGetsNoSlackInsteadOfThrowing(string? token)
    {
        Assert.Equal(0, TokenRenewalPolicy.SlackMinutesFor(token));
    }
}

/// <summary>Mints signed JWTs shaped like the ones AuthenticationController issues.</summary>
internal static class TestTokens
{
    private static readonly byte[] Key = Encoding.UTF8.GetBytes("netrisk-client-services-test-signing-key-0123456789");

    public static string Create(int lifetimeMinutes, DateTime? issuedAt = null)
    {
        var now = issuedAt ?? DateTime.UtcNow;
        var handler = new JwtSecurityTokenHandler();

        return handler.WriteToken(handler.CreateToken(new SecurityTokenDescriptor
        {
            Subject = new ClaimsIdentity(new List<Claim> { new(ClaimTypes.Name, "felipe") }),
            Issuer = "netrisk-api",
            Audience = "netrisk-clients",
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddMinutes(lifetimeMinutes),
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Key), SecurityAlgorithms.HmacSha256Signature)
        }));
    }
}

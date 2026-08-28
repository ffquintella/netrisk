using System;
using System.IdentityModel.Tokens.Jwt;

namespace ClientServices.Services;

/// <summary>
/// How much life a session token has to have left before the client renews it.
///
/// This used to be a hard-coded 300 minutes in <see cref="RestService.GetClient"/> — the client
/// refused to use any token that was not valid for another five hours. That was survivable only
/// while the API minted day-long tokens; Track 7 shortened the default lifetime to
/// <c>JwtDefaults.TimeoutMinutes</c> (60), and every freshly minted token then failed the check the
/// moment it arrived. The result was an endless loop: every REST call asked for a new token, judged
/// it expired, and asked again — hundreds of "Authentication token created" lines per minute on the
/// server and a desktop client that never got past sign-in.
///
/// So the slack is derived from the token itself instead of being asserted independently of it: a
/// quarter of the token's own lifetime, capped at <see cref="MaxSlackMinutes"/>. That is strictly
/// less than the lifetime for any positive lifetime, so a token that has just been issued is always
/// usable no matter what <c>JWT:Timeout</c> the server is configured with.
/// </summary>
public static class TokenRenewalPolicy
{
    /// <summary>Never ask for more than this much remaining validity, however long-lived the token.</summary>
    public const int MaxSlackMinutes = 5;

    /// <summary>
    /// The renewal slack, in minutes, for <paramref name="token"/>. Zero when the token cannot be
    /// read — the caller's validity check reports the malformed token, and renewing on the
    /// expiry instant is the safe reading of "no idea how long this was meant to last".
    /// </summary>
    public static int SlackMinutesFor(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return 0;

        try
        {
            if (new JwtSecurityTokenHandler().ReadToken(token) is not JwtSecurityToken jwt) return 0;
            return SlackMinutesFor(jwt.ValidFrom, jwt.ValidTo);
        }
        catch (Exception)
        {
            return 0;
        }
    }

    /// <summary>
    /// The renewal slack for a token minted at <paramref name="validFrom"/> and expiring at
    /// <paramref name="validTo"/>. Always strictly smaller than the lifetime, so it can never
    /// condemn a token that was just issued.
    /// </summary>
    public static int SlackMinutesFor(DateTime validFrom, DateTime validTo)
    {
        var lifetime = validTo - validFrom;
        if (lifetime <= TimeSpan.Zero) return 0;

        var slack = TimeSpan.FromTicks(lifetime.Ticks / 4);
        if (slack > TimeSpan.FromMinutes(MaxSlackMinutes))
            slack = TimeSpan.FromMinutes(MaxSlackMinutes);

        return (int)Math.Floor(slack.TotalMinutes);
    }
}

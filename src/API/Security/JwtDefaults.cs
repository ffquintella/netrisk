namespace API.Security;

/// <summary>
/// The issuer, audience and lifetime a NetRisk session token is minted with and validated against
/// (Track 7 milestone 7.3.2).
///
/// Kept in one place because the value used to sign and the value used to validate have to agree:
/// they are set from configuration in two different files (<see cref="AuthenticationBootstrapper"/>
/// validates, <c>AuthenticationController.GenerateToken</c> signs), and a mismatch would present as
/// "every token is rejected" only at runtime.
/// </summary>
public static class JwtDefaults
{
    /// <summary>Default <c>iss</c> when <c>JWT:Issuer</c> is not configured.</summary>
    public const string Issuer = "netrisk-api";

    /// <summary>Default <c>aud</c> when <c>JWT:Audience</c> is not configured.</summary>
    public const string Audience = "netrisk-clients";

    /// <summary>
    /// Default access-token lifetime in minutes when <c>JWT:Timeout</c> is not configured.
    ///
    /// The shipped configuration used to say 1440 — a full day for a bearer token with no
    /// revocation. OWASP's guidance for an access token is minutes, not hours; 60 is the compromise
    /// between that and the fact that NetRisk has no refresh-token flow yet, so the timeout is also
    /// how long a desktop session lasts before the user re-authenticates.
    /// </summary>
    public const int TimeoutMinutes = 60;

    /// <summary>
    /// Upper bound accepted from configuration, in minutes. An operator who sets
    /// <c>JWT:Timeout</c> to a week has almost certainly not thought about what a stolen token then
    /// buys, so the value is clamped and the clamp is logged rather than silently honoured.
    /// </summary>
    public const int MaxTimeoutMinutes = 1440;
}

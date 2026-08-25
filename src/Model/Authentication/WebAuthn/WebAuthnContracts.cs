namespace Model.Authentication.WebAuthn;

/// <summary>
/// One registered authenticator, as the account screen shows it (Track 4 milestone 4.3.3).
///
/// No public key and no credential id: neither is a secret, but neither is any use to a client
/// either, and a list endpoint that returns key material invites someone to start comparing it.
/// </summary>
public class WebAuthnCredentialView
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public string Name { get; set; } = string.Empty;

    public string? AttestationFormat { get; set; }

    public string? AaGuid { get; set; }

    public bool IsBackupEligible { get; set; }

    public bool IsBackedUp { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    public DateTime? RevokedAt { get; set; }
}

/// <summary>
/// The options a browser needs to run a ceremony, plus the handle NetRisk uses to find the pending
/// challenge again (Track 4 milestone 4.3.3).
///
/// <see cref="OptionsJson"/> is passed to the browser verbatim. Serializing it here rather than
/// re-modelling the WebAuthn option types means the ceremony cannot fail because NetRisk's copy of the
/// spec's JSON drifted from the library's.
/// </summary>
public class WebAuthnCeremonyOptions
{
    /// <summary>Opaque handle. The browser sends it back so the server can find the challenge.</summary>
    public string CeremonyId { get; set; } = string.Empty;

    /// <summary>The <c>PublicKeyCredentialCreationOptions</c> or <c>PublicKeyCredentialRequestOptions</c> JSON.</summary>
    public string OptionsJson { get; set; } = string.Empty;

    public int ExpiresInSeconds { get; set; }
}

/// <summary>Result of completing a registration ceremony.</summary>
public class WebAuthnRegistrationResult
{
    public bool Success { get; init; }

    public string? Error { get; init; }

    public WebAuthnCredentialView? Credential { get; init; }

    public static WebAuthnRegistrationResult Ok(WebAuthnCredentialView credential) =>
        new() { Success = true, Credential = credential };

    public static WebAuthnRegistrationResult Fail(string error) => new() { Success = false, Error = error };
}

/// <summary>Result of completing an authentication ceremony.</summary>
public class WebAuthnAssertionResult
{
    public bool Success { get; init; }

    public int? UserId { get; init; }

    public string? Error { get; init; }

    /// <summary>
    /// True when the authenticator's signature counter went backwards, which the spec names as the
    /// signal of a cloned credential. The assertion is refused, and this is what says why.
    /// </summary>
    public bool CounterRegression { get; init; }

    public static WebAuthnAssertionResult Ok(int userId) => new() { Success = true, UserId = userId };

    public static WebAuthnAssertionResult Fail(string error, bool counterRegression = false) =>
        new() { Success = false, Error = error, CounterRegression = counterRegression };
}

/// <summary>
/// A batch of recovery codes, returned exactly once at generation (Track 4 milestone 4.3.3).
/// </summary>
public class RecoveryCodeBatch
{
    public int UserId { get; set; }

    /// <summary>
    /// The codes in clear. Present only in the response to the generate call — they are stored hashed,
    /// so this is the only moment they exist in readable form.
    /// </summary>
    public List<string> Codes { get; set; } = new();

    public DateTime GeneratedAt { get; set; }
}

/// <summary>Whether an account satisfies the hardware-factor policy, and why or why not.</summary>
public class HardwareFactorStatus
{
    public int UserId { get; set; }

    /// <summary>Policy applies to this account (it holds an administrative role).</summary>
    public bool Required { get; set; }

    public int RegisteredAuthenticators { get; set; }

    public int UnusedRecoveryCodes { get; set; }

    /// <summary>True when the policy applies and the account has at least one live authenticator.</summary>
    public bool Satisfied { get; set; }

    /// <summary>What the account still has to do, in words the UI can show unchanged.</summary>
    public string? Guidance { get; set; }
}

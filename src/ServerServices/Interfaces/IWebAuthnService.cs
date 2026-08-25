using DAL.Entities;
using Model.Authentication.WebAuthn;

namespace ServerServices.Interfaces;

/// <summary>
/// FIDO2/WebAuthn registration and authentication, plus the hardware-factor policy and recovery codes
/// (Track 4 milestone 4.3.3).
///
/// WebAuthn is a browser API, so both ceremonies run through the system-browser flow established for
/// federated sign-in: the desktop client opens a page, the browser talks to the authenticator, and the
/// page posts the result back here. There is no way to do this from inside a native window, and
/// pretending otherwise would produce an unusable feature.
/// </summary>
public interface IWebAuthnService
{
    /// <summary>A user's authenticators, newest first. Revoked ones included for the audit trail.</summary>
    Task<List<WebAuthnCredentialView>> GetCredentialsAsync(int userId, bool includeRevoked = false);

    /// <summary>
    /// Starts a registration ceremony. Credentials the user already has are excluded, so an
    /// authenticator cannot be enrolled twice and the browser says so before the user touches the key.
    /// </summary>
    Task<WebAuthnCeremonyOptions> BeginRegistrationAsync(int userId, string? authenticatorName);

    /// <summary>
    /// Completes a registration ceremony from the browser's attestation response and stores the
    /// credential.
    /// </summary>
    Task<WebAuthnRegistrationResult> CompleteRegistrationAsync(string ceremonyId, string attestationJson);

    /// <summary>
    /// Starts an authentication ceremony. When <paramref name="userId"/> is null the ceremony is
    /// discoverable-credential (passkey) style and the authenticator supplies the user handle.
    /// </summary>
    Task<WebAuthnCeremonyOptions> BeginAssertionAsync(int? userId);

    /// <summary>
    /// Completes an authentication ceremony, verifying the signature and the authenticator's signature
    /// counter.
    /// </summary>
    Task<WebAuthnAssertionResult> CompleteAssertionAsync(string ceremonyId, string assertionJson);

    /// <summary>
    /// Withdraws an authenticator. Kept as a row with a revocation date rather than deleted, because
    /// "which key was removed, and when" is an audit question.
    /// </summary>
    Task<WebAuthnCredentialView> RevokeCredentialAsync(int credentialId, int actingUserId);

    /// <summary>
    /// Generates a fresh batch of recovery codes, invalidating any unused ones. Returns them in clear
    /// once; they are stored hashed.
    /// </summary>
    Task<RecoveryCodeBatch> GenerateRecoveryCodesAsync(int userId, int? generatedByUserId, int count = 10);

    /// <summary>
    /// Redeems a recovery code. Single use: the row is marked used inside the same save, so two
    /// concurrent attempts cannot both succeed.
    /// </summary>
    Task<bool> RedeemRecoveryCodeAsync(int userId, string code);

    /// <summary>
    /// Whether the hardware-factor policy applies to this account and whether it is satisfied — what
    /// the login flow and the account screen both branch on.
    /// </summary>
    Task<HardwareFactorStatus> GetHardwareFactorStatusAsync(int userId);
}

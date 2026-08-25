namespace DAL.Entities;

/// <summary>
/// One registered FIDO2/WebAuthn authenticator for one user (Track 4 milestone 4.3.3).
///
/// Several per user on purpose: a hardware key that can only be replaced by an administrator is a
/// lockout waiting to happen, so the enrollment ceremony encourages a second one and the UI names
/// them.
/// </summary>
public class WebAuthnCredential
{
    public int Id { get; set; }

    public int UserId { get; set; }

    /// <summary>The credential id, base64url. Unique across users — the spec requires it.</summary>
    public string CredentialId { get; set; } = null!;

    /// <summary>COSE public key, base64. What the assertion signature is verified against.</summary>
    public string PublicKey { get; set; } = null!;

    /// <summary>
    /// The authenticator's signature counter as of the last assertion. A counter that goes backwards
    /// is the documented signal of a cloned authenticator, which is why it is persisted rather than
    /// ignored.
    /// </summary>
    public long SignCount { get; set; }

    /// <summary>Authenticator model identifier, when the attestation carries one.</summary>
    public string? AaGuid { get; set; }

    /// <summary>Attestation statement format — <c>none</c>, <c>packed</c>, <c>apple</c>, …</summary>
    public string? AttestationFormat { get; set; }

    /// <summary>User-supplied label: "YubiKey 5C — desk drawer".</summary>
    public string Name { get; set; } = null!;

    /// <summary>Whether the credential may be backed up (a passkey) as opposed to device-bound.</summary>
    public bool IsBackupEligible { get; set; }

    public bool IsBackedUp { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? LastUsedAt { get; set; }

    /// <summary>Set when an administrator withdraws the authenticator; the row is kept for audit.</summary>
    public DateTime? RevokedAt { get; set; }

    public virtual User? User { get; set; }
}

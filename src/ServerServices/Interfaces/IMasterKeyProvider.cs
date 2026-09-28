namespace ServerServices.Interfaces;

/// <summary>
/// Where the installation's credential-encryption key came from. Reported so that an operator can
/// tell a hardware-backed install from one falling back to a file without reading the code.
/// </summary>
public enum MasterKeyBacking
{
    /// <summary>Supplied out of band as <c>NETRISK_SECRET_MASTER_KEY</c>. Nothing is written to disk.</summary>
    Environment,

    /// <summary>Sealed to the host TPM 2.0 (Linux, <c>tpm2-tools</c> present).</summary>
    Tpm,

    /// <summary>Stored in the macOS keychain, which is protected by the Secure Enclave on Apple silicon.</summary>
    Keychain,

    /// <summary>Wrapped with Windows DPAPI at machine scope — TPM-bound where the OS binds DPAPI keys to one.</summary>
    Dpapi,

    /// <summary>Owner-only (0600) file in the server's application-data folder. The last resort.</summary>
    ProtectedFile,

    /// <summary>An explicit key handed to the constructor. Tests only.</summary>
    Explicit
}

/// <summary>
/// The per-installation master key that <see cref="ISecretProtector"/> encrypts stored credentials
/// under.
///
/// It exists as its own thing rather than reusing <see cref="IEnvironmentService.ServerSecretToken"/>
/// for two reasons. The JWT signing key is a value a host may legitimately want to rotate — rotating
/// it should invalidate sessions, not make every stored integration credential unreadable, which is
/// exactly what sharing one value did. And a signing key has no reason to live in a TPM or a
/// keychain, while a key that decrypts stored credentials does.
/// </summary>
public interface IMasterKeyProvider
{
    /// <summary>The key, base64 of 32 random bytes. Stable for the life of the installation.</summary>
    string GetMasterKey();

    /// <summary>Which backend <see cref="GetMasterKey"/> resolved to.</summary>
    MasterKeyBacking Backing { get; }

    /// <summary>Human-readable location of the key — a file path, or the keychain item's name.</summary>
    string Location { get; }
}

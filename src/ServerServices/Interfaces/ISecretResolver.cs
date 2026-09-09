namespace ServerServices.Interfaces;

/// <summary>
/// The read path for anything stored in a credential column: hand it what the database holds, get
/// back the credential to present to a third party.
///
/// It replaces bare <c>ISecretProtector.Unprotect</c> at every call site that consumes a credential,
/// and the reason is that after this feature exists, a credential column holds one of two things —
/// ciphertext, or a <see cref="Model.Secrets.SecretReference"/> — and only the read path can tell
/// which. Leaving <c>Unprotect</c> in place would mean each integration authenticating with the
/// literal string <c>vault:v1:3:…</c>, which fails as a 401 from someone else's API with no
/// indication of why.
///
/// It is deliberately async where <c>Unprotect</c> was synchronous. Resolving may cross the network,
/// and hiding that behind a blocking call on a request thread is how a slow vault becomes a hung
/// API.
/// </summary>
public interface ISecretResolver
{
    /// <summary>
    /// The live credential for a stored value: decrypted when it is a literal, fetched (or served
    /// from the obfuscated cache) when it is a vault reference, null when nothing is stored.
    /// </summary>
    /// <exception cref="Model.Exceptions.SecretVaultResolutionException">
    /// The value is a reference that cannot currently be resolved.
    /// </exception>
    /// <exception cref="Model.Exceptions.SecretProtectionException">
    /// The value is ciphertext this installation's key cannot decrypt.
    /// </exception>
    Task<string?> ResolveAsync(string? stored, CancellationToken ct = default);

    /// <summary>
    /// Whether a stored value is a vault reference. Lets a caller report "this field is vault-backed"
    /// without resolving it — which the connection list does, since listing connections must not
    /// make a vault call per row.
    /// </summary>
    bool IsVaultReference(string? stored);
}

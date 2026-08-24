using System.Security.Cryptography;

namespace WebSite.Tests.Sync;

/// <summary>
/// A real ECDSA P-256 key pair in the same PEM shape the server's <c>SyncKeyService</c> produces
/// (PKCS#8 private key, SubjectPublicKeyInfo public key).
/// </summary>
public sealed record SyncKeyPair(string KeyId, string PrivateKeyPem, string PublicKeyPem)
{
    public static SyncKeyPair Create(string? keyId = null)
    {
        using var ecdsa = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        return new SyncKeyPair(
            keyId ?? Guid.NewGuid().ToString("N")[..16],
            ecdsa.ExportPkcs8PrivateKeyPem(),
            ecdsa.ExportSubjectPublicKeyInfoPem());
    }
}

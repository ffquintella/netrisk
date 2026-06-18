using System.Security.Cryptography;
using System.Text;

namespace SyncContracts;

/// <summary>
/// Shared canonicalization + ECDSA-P256-SHA256 sign/verify so the server signer and the
/// website verifier agree byte-for-byte. The canonical string is:
/// <c>METHOD\nPATH\nTIMESTAMP\nNONCE\nBASE64(SHA256(body))</c>.
/// </summary>
public static class SyncSignature
{
    public static byte[] BuildCanonical(string method, string path, long timestamp, string nonce, byte[] body)
    {
        var bodyHash = Convert.ToBase64String(SHA256.HashData(body));
        var canonical = $"{method.ToUpperInvariant()}\n{path}\n{timestamp}\n{nonce}\n{bodyHash}";
        return Encoding.UTF8.GetBytes(canonical);
    }

    public static string Sign(string privateKeyPem, string method, string path, long timestamp, string nonce, byte[] body)
    {
        using var ecdsa = ECDsa.Create();
        ecdsa.ImportFromPem(privateKeyPem);
        var canonical = BuildCanonical(method, path, timestamp, nonce, body);
        var signature = ecdsa.SignData(canonical, HashAlgorithmName.SHA256);
        return Convert.ToBase64String(signature);
    }

    public static bool Verify(string publicKeyPem, string method, string path, long timestamp, string nonce,
        byte[] body, string signatureB64)
    {
        try
        {
            using var ecdsa = ECDsa.Create();
            ecdsa.ImportFromPem(publicKeyPem);
            var canonical = BuildCanonical(method, path, timestamp, nonce, body);
            var signature = Convert.FromBase64String(signatureB64);
            return ecdsa.VerifyData(canonical, signature, HashAlgorithmName.SHA256);
        }
        catch
        {
            return false;
        }
    }
}

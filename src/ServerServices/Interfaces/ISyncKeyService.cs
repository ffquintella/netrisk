namespace ServerServices.Interfaces;

/// <summary>
/// Owns the server's ECDSA P-256 signing key used to authenticate to the website's sync
/// endpoint. The private key never leaves the server; the public key is enrolled on the
/// website (TOFU) and used there to verify every signed request.
/// </summary>
public interface ISyncKeyService
{
    bool KeyExists();

    string GetKeyId();

    string GetPublicKeyPem();

    /// <summary>Creates a fresh keypair. Throws if one already exists unless <paramref name="force"/>.</summary>
    void CreateKeyPair(bool force = false);

    /// <summary>Generates a candidate next keypair without persisting it. Use the returned
    /// material to build a rotate request (signed with the current key), then call
    /// <see cref="CommitRotation"/> on success.</summary>
    (string KeyId, string PublicKeyPem, string PrivateKeyPem) PrepareRotation();

    /// <summary>Promotes a prepared keypair to be the active one.</summary>
    void CommitRotation(string keyId, string publicKeyPem, string privateKeyPem);

    /// <summary>Signs the canonical request material with the current private key.</summary>
    string Sign(string method, string path, long timestamp, string nonce, byte[] body);
}

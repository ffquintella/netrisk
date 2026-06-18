namespace SyncContracts;

/// <summary>
/// Names of the HTTP headers that carry the signature material for a signed sync request.
/// The website verifies these against the enrolled public key; the server populates them.
/// </summary>
public static class SyncHeaders
{
    public const string KeyId = "X-Sync-KeyId";
    public const string Timestamp = "X-Sync-Timestamp";
    public const string Nonce = "X-Sync-Nonce";
    public const string Signature = "X-Sync-Signature";
}

/// <summary>
/// Enrollment request (TOFU). The first call that reaches an un-enrolled website wins;
/// subsequent calls are rejected until <c>SyncState</c> is cleared manually.
/// </summary>
public class EnrollRequest
{
    public string KeyId { get; set; } = "";
    public string PublicKeyPem { get; set; } = "";
}

/// <summary>
/// Key-rotation request. The HTTP request body is signed with the <em>current</em> enrolled
/// key, proving the caller already controls the trusted private key.
/// </summary>
public class RotateKeyRequest
{
    public string NewKeyId { get; set; } = "";
    public string NewPublicKeyPem { get; set; } = "";
}

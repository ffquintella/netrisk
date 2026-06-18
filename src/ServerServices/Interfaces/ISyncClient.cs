using SyncContracts;

namespace ServerServices.Interfaces;

/// <summary>
/// Signed HTTP client the server uses to talk to a website's sync endpoint. Centralizes
/// signing (via <see cref="ISyncKeyService"/>) so both the CLI and the background jobs share it.
/// </summary>
public interface ISyncClient
{
    /// <summary>TOFU enrollment: pushes the current public key to the website. Returns true on success.</summary>
    Task<bool> EnrollAsync(string websiteUrl, bool insecure = false, CancellationToken ct = default);

    /// <summary>Rotates the local key and installs the new public key on the website using a
    /// request signed with the current key. Commits the rotation only on success.</summary>
    Task<bool> RotateAsync(string websiteUrl, bool insecure = false, CancellationToken ct = default);

    /// <summary>Sends the bulk display snapshot; returns the website's pending outbox (or null on failure).</summary>
    Task<SyncResponse?> PushAsync(string websiteUrl, PushPayload payload, bool insecure = false, CancellationToken ct = default);

    /// <summary>Sends the fast-lane (links) payload; returns the website's pending outbox.</summary>
    Task<SyncResponse?> FastPushAsync(string websiteUrl, FastPushPayload payload, bool insecure = false, CancellationToken ct = default);

    /// <summary>Acknowledges applied outbox actions so the website stops re-sending them.</summary>
    Task<bool> AckAsync(string websiteUrl, AckRequest ack, bool insecure = false, CancellationToken ct = default);
}

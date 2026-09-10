namespace ServerServices.Secrets;

/// <summary>
/// The node a vault connection should be talked to right now, and how that was decided.
/// </summary>
/// <param name="BaseUrl">The base URL to hand the plugin.</param>
/// <param name="Candidates">Every node discovery found, in preference order. One element for a direct address.</param>
/// <param name="Discovered">True when SRV discovery produced <paramref name="Candidates"/>.</param>
/// <param name="Note">
/// Why this node and not another, when there was a choice worth explaining — "two of three nodes
/// failed their health probe". Null when there is nothing to say. Surfaced in the connection test so
/// an operator sees a degraded cluster before it becomes an outage.
/// </param>
public readonly record struct VaultEndpointSelection(
    string BaseUrl,
    IReadOnlyList<string> Candidates,
    bool Discovered,
    string? Note);

/// <summary>
/// Turns a vault connection's configured address into the base URL of a node that is answering.
///
/// A separate service rather than a method on <c>SecretVaultService</c> because it is the only part
/// of vault handling that does network I/O of its own — DNS and a health probe — and a test of
/// reference resolution should not have to stub either.
/// </summary>
public interface IVaultEndpointResolver
{
    /// <summary>
    /// Resolves <paramref name="baseUrl"/> for connection <paramref name="connectionId"/>.
    /// </summary>
    /// <exception cref="Model.Exceptions.InvalidParameterException">The address does not parse.</exception>
    /// <exception cref="Model.Exceptions.SecretVaultResolutionException">
    /// Discovery found no nodes at all, which is not something the caller can work around.
    /// </exception>
    Task<VaultEndpointSelection> ResolveAsync(int connectionId, string baseUrl,
        CancellationToken ct = default);

    /// <summary>
    /// Forgets the cached choice for a connection. Called when its address changes, so that an
    /// operator who repoints a connection and presses Test does not get the previous cluster.
    /// </summary>
    void Invalidate(int connectionId);
}

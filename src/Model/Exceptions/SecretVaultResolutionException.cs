namespace Model.Exceptions;

/// <summary>
/// A stored secret reference could not be turned into a credential.
///
/// Its own type, and not a <see cref="SecretProtectionException"/>, because the two have different
/// remedies and different blame. A protection failure means the value must be re-entered on this
/// installation. A resolution failure means the vault side of the arrangement is wrong — the
/// connection was disabled, its plugin was removed, the API key was revoked, the secret was deleted —
/// and re-entering anything in NetRisk fixes none of that.
///
/// It exists at all, rather than the resolver returning null, because null would be handed to a
/// third-party API as an empty credential. The 401 that comes back names NetRisk's request, not the
/// vault, and the operator spends the evening looking at the wrong integration.
/// </summary>
public class SecretVaultResolutionException : Exception
{
    /// <summary>The reference that failed, in its stored form. Not a secret — it names one.</summary>
    public string? Reference { get; }

    public SecretVaultResolutionException(string message, string? reference = null) : base(message)
    {
        Reference = reference;
    }

    public SecretVaultResolutionException(string message, string? reference, Exception innerException)
        : base(message, innerException)
    {
        Reference = reference;
    }
}

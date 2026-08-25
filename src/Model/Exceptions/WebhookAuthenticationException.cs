namespace Model.Exceptions;

/// <summary>
/// An inbound integration webhook could not be authenticated (Track 4 milestone 4.2.3).
///
/// Separate from a permission exception because there is no user involved and no permission to name:
/// the caller presented a wrong or missing shared secret. The API answers 401, and the message
/// deliberately says nothing about which part was wrong — an endpoint that distinguishes "no secret"
/// from "wrong secret" is an oracle.
/// </summary>
public class WebhookAuthenticationException(string provider)
    : Exception($"The {provider} webhook could not be authenticated.")
{
    public string Provider { get; } = provider;
}

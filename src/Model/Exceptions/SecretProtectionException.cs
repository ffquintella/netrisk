namespace Model.Exceptions;

/// <summary>
/// A stored integration credential could not be decrypted (Track 4).
///
/// Its own exception type rather than a generic one because the remedy is specific and worth naming
/// in the message the operator sees: the value was encrypted with a different install's key, so it
/// has to be re-entered rather than repaired.
/// </summary>
public class SecretProtectionException : Exception
{
    public SecretProtectionException(string message) : base(message)
    {
    }

    public SecretProtectionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}

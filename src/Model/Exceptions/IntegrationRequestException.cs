namespace Model.Exceptions;

/// <summary>
/// A third-party integration refused or failed a request (Track 4).
///
/// Distinct from a generic exception because the API surfaces it as 502 rather than 500: the failure
/// is upstream, not in NetRisk, and telling the operator which provider said what is the whole
/// diagnosis. The provider name is carried separately so the message can be shown without it being
/// re-derived from the text.
/// </summary>
public class IntegrationRequestException : Exception
{
    public IntegrationRequestException(string provider, string message) : base(message)
    {
        Provider = provider;
    }

    public IntegrationRequestException(string provider, string message, Exception innerException)
        : base(message, innerException)
    {
        Provider = provider;
    }

    public string Provider { get; }
}

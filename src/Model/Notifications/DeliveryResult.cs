namespace Model.Notifications;

/// <summary>
/// What one provider's delivery attempt did (Track 4 milestone 4.1.1).
///
/// <see cref="Retryable"/> is separate from <see cref="Success"/> because the dispatcher's decision
/// is three-way, not two: deliver, try again later, or stop and surface the configuration error. A
/// bool would collapse "Slack is rate-limiting us" into the same outcome as "this webhook URL is
/// wrong", and one of those should keep being retried while the other should not.
/// </summary>
public class DeliveryResult
{
    public bool Success { get; init; }

    /// <summary>Whether another attempt could plausibly succeed.</summary>
    public bool Retryable { get; init; }

    /// <summary>Failure detail for the delivery log. Credentials are redacted before this is set.</summary>
    public string? Error { get; init; }

    /// <summary>Provider-requested back-off, honoured by the dispatcher when present.</summary>
    public TimeSpan? RetryAfter { get; init; }

    /// <summary>HTTP status, when the provider is an HTTP one. 0 for email and for transport failures.</summary>
    public int StatusCode { get; init; }

    public static DeliveryResult Delivered(int statusCode = 200) =>
        new() { Success = true, StatusCode = statusCode };

    public static DeliveryResult Retry(string error, int statusCode = 0, TimeSpan? retryAfter = null) =>
        new() { Success = false, Retryable = true, Error = error, StatusCode = statusCode, RetryAfter = retryAfter };

    public static DeliveryResult Permanent(string error, int statusCode = 0) =>
        new() { Success = false, Retryable = false, Error = error, StatusCode = statusCode };
}

/// <summary>
/// The result of the admin UI's "send test message" button (Track 4 milestone 4.1.2).
///
/// Carries a message rather than only a flag: "could not connect" and "the channel does not exist"
/// are both failures, and telling them apart is the entire value of a test button.
/// </summary>
public class ChannelTestResult
{
    public bool Success { get; init; }

    public string Message { get; init; } = string.Empty;

    /// <summary>Milliseconds the round trip took, for the operator's sense of whether it is healthy.</summary>
    public long ElapsedMilliseconds { get; init; }

    public static ChannelTestResult Ok(string message, long elapsed = 0) =>
        new() { Success = true, Message = message, ElapsedMilliseconds = elapsed };

    public static ChannelTestResult Fail(string message, long elapsed = 0) =>
        new() { Success = false, Message = message, ElapsedMilliseconds = elapsed };
}

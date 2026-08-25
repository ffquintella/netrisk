namespace ServerServices.Interfaces;

/// <summary>
/// The single outbound HTTP seam every Track 4 integration goes through — notification channels,
/// issue trackers, Vision One, SecurityScorecard.
///
/// One narrow abstraction rather than <c>HttpClient</c> injected everywhere, for one reason that
/// matters more than the others: a test must never be able to reach a real host. With this, faking
/// Slack is a dictionary lookup; with <c>HttpClient</c> it is a custom message handler per test, and
/// the first provider that news up its own client silently starts making real requests.
/// </summary>
public interface IOutboundHttpClient
{
    Task<OutboundHttpResponse> SendAsync(OutboundHttpRequest request, CancellationToken ct = default);
}

/// <summary>One outbound request. Deliberately dumb — no auth logic, no retries, no serialization policy.</summary>
public class OutboundHttpRequest
{
    public required string Method { get; init; }

    public required string Url { get; init; }

    /// <summary>Header name → value. Authorization belongs here; the provider builds it.</summary>
    public Dictionary<string, string> Headers { get; init; } = new();

    /// <summary>Request body, already serialized. Null for GET/DELETE.</summary>
    public string? Body { get; init; }

    public string ContentType { get; init; } = "application/json";

    /// <summary>Per-request timeout. Integrations talk to third parties; a hung call must not hold a job forever.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(30);
}

/// <summary>
/// One outbound response, including transport failures.
///
/// A failed connection is a response with <see cref="StatusCode"/> 0 rather than an exception,
/// because every caller has to handle "the provider said no" and "the provider was unreachable" the
/// same way — record it on the delivery row and retry — and modelling one as a return value and the
/// other as a throw doubles every call site.
/// </summary>
public class OutboundHttpResponse
{
    /// <summary>HTTP status, or 0 when the request never got an answer.</summary>
    public int StatusCode { get; init; }

    public string? Body { get; init; }

    /// <summary>Response headers, lower-cased names. <c>retry-after</c> is the one providers actually use.</summary>
    public Dictionary<string, string> Headers { get; init; } = new();

    /// <summary>Transport-level failure message when <see cref="StatusCode"/> is 0.</summary>
    public string? TransportError { get; init; }

    public bool IsSuccess => StatusCode is >= 200 and < 300;

    /// <summary>
    /// True for the statuses where trying again later is reasonable: rate limiting, gateway errors,
    /// and a request that never arrived. A 400 or a 403 is a configuration problem, and retrying it
    /// three times only delays the operator finding out.
    /// </summary>
    public bool IsRetryable =>
        StatusCode == 0 || StatusCode == 408 || StatusCode == 429 || StatusCode >= 500;

    /// <summary>
    /// The provider's requested back-off, when it sent one. Slack and GitHub both do, and honouring
    /// it is the difference between a rate limit that clears and one that keeps being re-triggered.
    /// </summary>
    public TimeSpan? RetryAfter
    {
        get
        {
            if (!Headers.TryGetValue("retry-after", out var value)) return null;
            if (int.TryParse(value, out var seconds) && seconds >= 0) return TimeSpan.FromSeconds(seconds);
            return DateTimeOffset.TryParse(value, out var when) && when > DateTimeOffset.UtcNow
                ? when - DateTimeOffset.UtcNow
                : null;
        }
    }
}

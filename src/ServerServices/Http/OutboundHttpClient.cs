using System.Net.Http.Headers;
using System.Text;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Http;

/// <summary>
/// The real <see cref="IOutboundHttpClient"/>, over a single pooled <see cref="HttpClient"/>.
///
/// One shared client rather than one per request: a new <c>HttpClient</c> per call exhausts sockets
/// under a busy notification queue, which is the canonical .NET networking mistake. The per-request
/// timeout is applied with a linked cancellation token because the client's own <c>Timeout</c> is a
/// property of the instance and cannot vary per call.
/// </summary>
public class OutboundHttpClient : IOutboundHttpClient, IDisposable
{
    private readonly ILogger _logger;
    private readonly HttpClient _client;
    private readonly OutboundUrlPolicy _urlPolicy;

    /// <summary>
    /// The second client, for requests that carry
    /// <see cref="OutboundHttpRequest.AllowInvalidCertificate"/>.
    ///
    /// A separate client and not a per-request callback, because certificate validation is a property
    /// of the handler and the handler is shared: flipping it per call would relax validation for
    /// whatever else is in flight on the same connection pool. Created on first use, so an
    /// installation that never turns the option on never has a client that does not validate.
    /// </summary>
    private readonly Lazy<HttpClient> _insecureClient;

    public OutboundHttpClient(ILogger logger, Microsoft.Extensions.Configuration.IConfiguration configuration)
        : this(logger, new OutboundUrlPolicy(logger, configuration))
    {
    }

    /// <summary>Test seam: supply the destination policy directly.</summary>
    public OutboundHttpClient(ILogger logger, OutboundUrlPolicy urlPolicy)
    {
        _logger = logger;
        _urlPolicy = urlPolicy;
        _client = new HttpClient(new SocketsHttpHandler
        {
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
            AllowAutoRedirect = false
        })
        {
            // Infinite here, bounded per request below.
            Timeout = Timeout.InfiniteTimeSpan
        };
        _client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NetRisk", "1.0"));

        _insecureClient = new Lazy<HttpClient>(() =>
        {
            var client = new HttpClient(new SocketsHttpHandler
            {
                PooledConnectionLifetime = TimeSpan.FromMinutes(5),
                AllowAutoRedirect = false,
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = (_, _, _, _) => true
                }
            })
            {
                Timeout = Timeout.InfiniteTimeSpan
            };

            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("NetRisk", "1.0"));

            return client;
        });
    }

    public async Task<OutboundHttpResponse> SendAsync(OutboundHttpRequest request, CancellationToken ct = default)
    {
        // Track 7 finding NR-2026-013 — SSRF. Checked here rather than where each integration builds
        // its URL: there are ten providers and one of them would eventually be written without the
        // check. Reported as a transport error rather than an exception because that is what every
        // caller already handles, and a refused destination is operationally the same kind of event
        // as an unreachable one.
        var verdict = _urlPolicy.Evaluate(request.Url);
        if (!verdict.IsAllowed)
        {
            _logger.Warning("Refused an outbound {Method} to {Host}: {Reason}",
                request.Method, HostOf(request.Url), verdict.Reason);

            return new OutboundHttpResponse
            {
                StatusCode = 0,
                TransportError = verdict.Reason
            };
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(request.Timeout);

        try
        {
            using var message = new HttpRequestMessage(new HttpMethod(request.Method), request.Url);

            if (request.Body != null)
                message.Content = new StringContent(request.Body, Encoding.UTF8, request.ContentType);

            foreach (var (name, value) in request.Headers)
            {
                // Content headers are rejected by the request collection, so they are tried there
                // second rather than being dropped.
                if (!message.Headers.TryAddWithoutValidation(name, value))
                    message.Content?.Headers.TryAddWithoutValidation(name, value);
            }

            var client = _client;

            if (request.AllowInvalidCertificate)
            {
                // Logged every time, at warning, and naming the host: a connection running without
                // certificate validation is a security control somebody switched off, and the only
                // thing that makes that reviewable after the fact is a line in the log.
                _logger.Warning(
                    "Sending an outbound {Method} to {Host} WITHOUT TLS certificate validation, because "
                    + "the calling configuration asked for it",
                    request.Method, HostOf(request.Url));

                client = _insecureClient.Value;
            }

            // ResponseHeadersRead, not the default: the default completes only once the whole body is
            // buffered inside HttpClient, which puts the allocation out of reach before any code here
            // can refuse it. With headers-only completion the body is still a stream we control, and
            // MaxResponseBytes can be enforced while reading instead of reported afterwards.
            using var response = await client.SendAsync(message, HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);

            var body = await ReadBoundedAsync(response, request.MaxResponseBytes, timeout.Token);

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var (name, values) in response.Headers)
                headers[name.ToLowerInvariant()] = string.Join(",", values);
            foreach (var (name, values) in response.Content.Headers)
                headers[name.ToLowerInvariant()] = string.Join(",", values);

            return new OutboundHttpResponse
            {
                StatusCode = (int)response.StatusCode,
                Body = body,
                Headers = headers
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            _logger.Warning("Outbound {Method} to {Host} timed out after {Seconds}s",
                request.Method, HostOf(request.Url), request.Timeout.TotalSeconds);

            return new OutboundHttpResponse
            {
                StatusCode = 0,
                TransportError = $"The request timed out after {request.Timeout.TotalSeconds:0}s."
            };
        }
        catch (ResponseTooLargeException ex)
        {
            _logger.Warning("Outbound {Method} to {Host} returned more than the {Limit} byte cap; the read was abandoned",
                request.Method, HostOf(request.Url), request.MaxResponseBytes);

            return new OutboundHttpResponse { StatusCode = 0, TransportError = ex.Message };
        }
        catch (Exception ex)
        {
            var reason = Describe(ex);

            // The host, not the URL: a webhook URL is itself a credential, and logging it turns the
            // application log into a place where Slack tokens live.
            _logger.Warning("Outbound {Method} to {Host} failed: {Message}",
                request.Method, HostOf(request.Url), reason);

            return new OutboundHttpResponse { StatusCode = 0, TransportError = reason };
        }
    }

    /// <summary>
    /// Reads the response body, giving up as soon as more than <paramref name="maxBytes"/> have
    /// arrived.
    ///
    /// Two checks, because they fail at different moments. A <c>Content-Length</c> already over the
    /// cap is refused before a single byte of body is pulled off the socket — the cheapest possible
    /// rejection, and the common case for an honest remote that simply has more data than NetRisk
    /// will take. A remote that lies about the length, sends none at all, or streams chunked is
    /// caught by the running total instead, which never lets the buffer grow past the cap plus one
    /// read.
    ///
    /// The charset is honoured rather than assumed UTF-8, because <c>ReadAsStringAsync</c> did that
    /// and replacing it with something that mangles a Latin-1 issue title would be a regression
    /// dressed up as a security fix.
    /// </summary>
    private static async Task<string> ReadBoundedAsync(HttpResponseMessage response, long maxBytes,
        CancellationToken ct)
    {
        if (response.Content.Headers.ContentLength is { } declared && declared > maxBytes)
            throw new ResponseTooLargeException(declared, maxBytes);

        await using var stream = await response.Content.ReadAsStreamAsync(ct);

        // Not pre-sized from Content-Length: a remote that declares a length just under the cap
        // would otherwise get the whole allocation up front for free, without sending anything.
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];

        while (true)
        {
            var read = await stream.ReadAsync(chunk, ct);
            if (read == 0) break;

            if (buffer.Length + read > maxBytes) throw new ResponseTooLargeException(null, maxBytes);

            buffer.Write(chunk, 0, read);
        }

        return EncodingOf(response).GetString(buffer.GetBuffer(), 0, (int)buffer.Length);
    }

    private static Encoding EncodingOf(HttpResponseMessage response)
    {
        var charset = response.Content.Headers.ContentType?.CharSet?.Trim().Trim('"');

        if (string.IsNullOrEmpty(charset)) return Encoding.UTF8;

        try
        {
            return Encoding.GetEncoding(charset);
        }
        catch (ArgumentException)
        {
            // A charset no encoding provider knows is the remote's problem; UTF-8 is what the
            // previous implementation would have landed on anyway.
            return Encoding.UTF8;
        }
    }

    /// <summary>
    /// Raised when a response exceeds <see cref="OutboundHttpRequest.MaxResponseBytes"/>. Private,
    /// and converted to a transport error before it leaves <see cref="SendAsync"/> — callers handle
    /// one failure shape, not two.
    /// </summary>
    private sealed class ResponseTooLargeException(long? declared, long limit)
        : Exception(declared is { } bytes
            ? $"The response declared {bytes} bytes, over the {limit} byte limit for this request."
            : $"The response exceeded the {limit} byte limit for this request.");

    /// <summary>
    /// The whole message chain, not just the outermost message.
    ///
    /// A failed TLS handshake arrives as <c>HttpRequestException("The SSL connection could not be
    /// established, see inner exception.")</c>, and the sentence that names the actual cause — an
    /// untrusted root, a hostname mismatch, an expired certificate — exists only on the inner
    /// exception. That text is what an operator reads back out of a vault connection's <c>Last
    /// test</c> field, so dropping it leaves them with a failure that literally refers them to
    /// something they cannot see.
    ///
    /// Bounded at four links and de-duplicated, because a wrapped exception chain often repeats the
    /// same sentence, and this string is stored in a column.
    /// </summary>
    private static string Describe(Exception ex)
    {
        var parts = new List<string>();

        for (Exception? current = ex; current is not null && parts.Count < 4; current = current.InnerException)
        {
            var message = current.Message.Trim();

            // The phrase is a pointer to the next link, which is about to be appended; keeping it
            // would read as a dead end in the middle of the chain.
            if (current.InnerException is not null)
                message = message.Replace(", see inner exception.", ":", StringComparison.Ordinal);

            if (message.Length == 0 || parts.Contains(message, StringComparer.Ordinal)) continue;

            parts.Add(message);
        }

        return parts.Count == 0 ? ex.GetType().Name : string.Join(" ", parts);
    }

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "(unparseable url)";

    public void Dispose()
    {
        _client.Dispose();

        if (_insecureClient.IsValueCreated) _insecureClient.Value.Dispose();
    }
}

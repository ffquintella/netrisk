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

            using var response = await client.SendAsync(message, timeout.Token);

            var body = await response.Content.ReadAsStringAsync(timeout.Token);

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
        catch (Exception ex)
        {
            // The host, not the URL: a webhook URL is itself a credential, and logging it turns the
            // application log into a place where Slack tokens live.
            _logger.Warning("Outbound {Method} to {Host} failed: {Message}",
                request.Method, HostOf(request.Url), ex.Message);

            return new OutboundHttpResponse { StatusCode = 0, TransportError = ex.Message };
        }
    }

    private static string HostOf(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) ? uri.Host : "(unparseable url)";

    public void Dispose()
    {
        _client.Dispose();

        if (_insecureClient.IsValueCreated) _insecureClient.Value.Dispose();
    }
}

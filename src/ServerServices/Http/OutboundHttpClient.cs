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

    public OutboundHttpClient(ILogger logger)
    {
        _logger = logger;
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
    }

    public async Task<OutboundHttpResponse> SendAsync(OutboundHttpRequest request, CancellationToken ct = default)
    {
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

            using var response = await _client.SendAsync(message, timeout.Token);

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

    public void Dispose() => _client.Dispose();
}

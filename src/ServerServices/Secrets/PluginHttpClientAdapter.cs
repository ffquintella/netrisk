using Contracts.Secrets;
using ServerServices.Interfaces;

namespace ServerServices.Secrets;

/// <summary>
/// Hands a plugin the host's outbound HTTP path, so a plugin's requests are subject to the same SSRF
/// policy, the same timeouts and the same logging as every other integration's.
///
/// This adapter is the whole reason <see cref="IPluginHttpClient"/> exists in the SDK. A plugin could
/// obviously new up an <c>HttpClient</c> — nothing stops it, it runs in-process with full trust — but
/// then a base URL an operator pasted into a vault connection would reach whatever it names,
/// including <c>http://169.254.169.254/</c>. Routing through <see cref="IOutboundHttpClient"/> means
/// the destination is checked by <see cref="ServerServices.Http.OutboundUrlPolicy"/> before a packet
/// leaves, and it means a test can substitute the whole network.
///
/// The mapping is one-to-one and deliberately dumb: the two request shapes are the same shape on
/// purpose, so that neither side has a policy the other cannot see.
/// </summary>
public class PluginHttpClientAdapter : IPluginHttpClient
{
    private readonly IOutboundHttpClient _outbound;

    public PluginHttpClientAdapter(IOutboundHttpClient outbound)
    {
        _outbound = outbound;
    }

    public async Task<PluginHttpResponse> SendAsync(PluginHttpRequest request, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var response = await _outbound.SendAsync(new OutboundHttpRequest
        {
            Method = request.Method,
            Url = request.Url,
            Headers = new Dictionary<string, string>(request.Headers),
            Body = request.Body,
            ContentType = request.ContentType,
            Timeout = request.Timeout
        }, ct);

        return new PluginHttpResponse
        {
            StatusCode = response.StatusCode,
            Body = response.Body,
            Headers = new Dictionary<string, string>(response.Headers),
            TransportError = response.TransportError
        };
    }
}

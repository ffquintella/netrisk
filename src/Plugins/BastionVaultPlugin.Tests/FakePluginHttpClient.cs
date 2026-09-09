using Contracts.Secrets;

namespace BastionVaultPlugin.Tests;

/// <summary>
/// The whole network, as a dictionary.
///
/// The plugin never creates an <see cref="System.Net.Http.HttpClient"/> — it is handed
/// <see cref="IPluginHttpClient"/> by the host — which is exactly what makes this possible: there is
/// no message handler to install and no way for a test to accidentally reach a real vault.
///
/// It also records what was sent, because half of what these tests assert is about the request:
/// that the API key is a bearer token, that the machine ID appears only when the connection has one,
/// and that a secret id with a slash in it is escaped into the path rather than splitting it.
/// </summary>
internal sealed class FakePluginHttpClient : IPluginHttpClient
{
    private readonly Dictionary<string, PluginHttpResponse> _responses = new(StringComparer.Ordinal);

    /// <summary>Every request the plugin made, in order.</summary>
    public List<PluginHttpRequest> Requests { get; } = [];

    /// <summary>The response for a URL the test has not stubbed. Null means "fail the test loudly".</summary>
    public PluginHttpResponse? Fallback { get; set; }

    public FakePluginHttpClient Respond(string url, int statusCode, string? body)
    {
        // Assignment rather than Add: a test builds on a fully wired vault and overrides one route to
        // describe the case it is about, so the last writer has to win.
        _responses[url] = new PluginHttpResponse { StatusCode = statusCode, Body = body };
        return this;
    }

    /// <summary>Stubs a transport failure — no answer at all, which is status 0 by contract.</summary>
    public FakePluginHttpClient RespondUnreachable(string url, string error)
    {
        _responses[url] = new PluginHttpResponse { StatusCode = 0, TransportError = error };
        return this;
    }

    public Task<PluginHttpResponse> SendAsync(PluginHttpRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);

        if (_responses.TryGetValue(request.Url, out var response)) return Task.FromResult(response);

        return Task.FromResult(Fallback
                               ?? throw new InvalidOperationException(
                                   $"The plugin requested an unstubbed URL: {request.Url}"));
    }
}

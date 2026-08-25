using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ServerServices.Interfaces;

namespace ServerServices.Tests.Mock;

/// <summary>
/// The outbound HTTP client every Track 4 integration test talks to.
///
/// Two properties matter. It records every request, so a test can assert what a provider actually sent
/// — which is the only way to check a Block Kit payload or an HMAC signature. And it never opens a
/// socket, so a test that accidentally exercises a provider fails on an assertion rather than reaching
/// Slack.
/// </summary>
public class FakeOutboundHttpClient : IOutboundHttpClient
{
    /// <summary>Every request made, in order.</summary>
    public List<OutboundHttpRequest> Requests { get; } = new();

    /// <summary>
    /// Queued responses, consumed in order. When it runs dry, <see cref="DefaultResponse"/> is used —
    /// so a test that only cares about one call does not have to enumerate the rest.
    /// </summary>
    public Queue<OutboundHttpResponse> Responses { get; } = new();

    /// <summary>Used once the queue is empty. 200 with an empty JSON object.</summary>
    public OutboundHttpResponse DefaultResponse { get; set; } =
        new() { StatusCode = 200, Body = "{}" };

    /// <summary>
    /// Matched against the request URL; the first match wins over the queue. For a provider that makes
    /// several calls whose order is an implementation detail — reading transitions before executing one,
    /// say — matching on the path is more robust than counting.
    /// </summary>
    public List<(Func<OutboundHttpRequest, bool> Match, OutboundHttpResponse Response)> Rules { get; } = new();

    public Task<OutboundHttpResponse> SendAsync(OutboundHttpRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);

        foreach (var (match, response) in Rules)
            if (match(request)) return Task.FromResult(response);

        return Task.FromResult(Responses.Count > 0 ? Responses.Dequeue() : DefaultResponse);
    }

    /// <summary>Queues a JSON 200.</summary>
    public FakeOutboundHttpClient EnqueueJson(string body, int statusCode = 200)
    {
        Responses.Enqueue(new OutboundHttpResponse { StatusCode = statusCode, Body = body });
        return this;
    }

    /// <summary>Queues a failure with an optional <c>Retry-After</c>.</summary>
    public FakeOutboundHttpClient EnqueueFailure(int statusCode, string? body = null, string? retryAfter = null)
    {
        var response = new OutboundHttpResponse { StatusCode = statusCode, Body = body };

        if (retryAfter != null) response.Headers["retry-after"] = retryAfter;

        Responses.Enqueue(response);
        return this;
    }

    /// <summary>Queues a transport failure — the shape a DNS error or a refused connection takes.</summary>
    public FakeOutboundHttpClient EnqueueTransportError(string message)
    {
        Responses.Enqueue(new OutboundHttpResponse { StatusCode = 0, TransportError = message });
        return this;
    }

    /// <summary>Answers any request whose URL contains <paramref name="fragment"/> with this body.</summary>
    public FakeOutboundHttpClient RuleFor(string fragment, string body, int statusCode = 200)
    {
        Rules.Add((request => request.Url.Contains(fragment, StringComparison.OrdinalIgnoreCase),
            new OutboundHttpResponse { StatusCode = statusCode, Body = body }));
        return this;
    }

    public void Reset()
    {
        Requests.Clear();
        Responses.Clear();
        Rules.Clear();
    }
}

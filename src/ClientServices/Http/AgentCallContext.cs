using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Model.Agents;

namespace ClientServices.Http;

/// <summary>
/// Which agent registration the current call runs for (T358, S55). The desktop MCP dispatcher opens a scope around every
/// agent tool call; an <see cref="AsyncLocal{T}"/> carries it through the awaits into whichever ClientServices call the
/// tool makes, so the screens' own calls — on the same service instances — never carry it.
/// </summary>
public static class AgentCallContext
{
    private static readonly AsyncLocal<string?> Agent = new();

    /// <summary>The registration id of the agent the current call runs for, or null for the user's own calls.</summary>
    public static string? Current => Agent.Value;

    /// <summary>Marks the calls made until the returned scope is disposed as made through agent <paramref name="registrationId"/>.</summary>
    public static IDisposable Begin(string registrationId)
    {
        if (!AgentContextHeader.IsValid(registrationId))
            throw new ArgumentException("Not a registration id the agent-context header may carry.", nameof(registrationId));
        var previous = Agent.Value;
        Agent.Value = registrationId;
        return new Scope(previous);
    }

    /// <summary>
    /// Clears the agent context until the returned scope is disposed: for GUI-facing work started from inside an agent
    /// call (events, handlers) that must not send the header on the screen's own calls (review M63 #4).
    /// </summary>
    public static IDisposable Suspend()
    {
        var previous = Agent.Value;
        Agent.Value = null;
        return new Scope(previous);
    }

    private sealed class Scope(string? previous) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0) Agent.Value = previous;
        }
    }
}

/// <summary>
/// Adds <c>X-NetRisk-Agent</c> to a request sent while <see cref="AgentCallContext.Current"/> is set, and removes any
/// copy a caller put there otherwise, so the header only ever says what the dispatcher's scope says. Informational: the
/// API never authorizes on it.
/// </summary>
public sealed class AgentContextHandler : DelegatingHandler
{
    public AgentContextHandler(HttpMessageHandler innerHandler) : base(innerHandler)
    {
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Apply(request);
        return base.SendAsync(request, cancellationToken);
    }

    /// <summary>The header rule alone, for the handler and for test doubles of the transport.</summary>
    public static void Apply(HttpRequestMessage request)
    {
        request.Headers.Remove(AgentContextHeader.Name);
        if (AgentContextHeader.Parse(AgentCallContext.Current) is { } agent)
            request.Headers.TryAddWithoutValidation(AgentContextHeader.Name, agent);
    }
}

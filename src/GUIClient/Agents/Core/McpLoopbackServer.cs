using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace GUIClient.Agents;

/// <summary>
/// One connection, one request: read with bounds and a timeout, refuse anything the guard refuses before the body is
/// parsed, then hand POSTs to the dispatcher. Stream-based so tests drive it without a socket.
/// </summary>
public sealed class McpEndpoint
{
    public const string Path = "/mcp";

    private static readonly IReadOnlyDictionary<string, string> AllowHeader =
        new Dictionary<string, string> { ["Allow"] = "POST, DELETE" };

    private readonly LoopbackRequestGuard _guard;
    private readonly McpDispatcher _dispatcher;
    private readonly IAgentAuditSink _audit;
    private readonly TimeProvider _time;

    public McpEndpoint(LoopbackRequestGuard guard, McpDispatcher dispatcher, IAgentAuditSink audit, TimeProvider time)
    {
        _guard = guard;
        _dispatcher = dispatcher;
        _audit = audit;
        _time = time;
    }

    /// <summary>Time allowed to receive the whole request. Waiting for the user happens after this, not within it.</summary>
    public TimeSpan ReadTimeout { get; init; } = TimeSpan.FromSeconds(15);

    public async Task HandleConnectionAsync(Stream stream, IPAddress? peer, CancellationToken cancellationToken)
    {
        LoopbackHttpRequest? request;
        HttpParseError error;
        using (var read = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            read.CancelAfter(ReadTimeout);
            try
            {
                (request, error) = await LoopbackHttp.ReadRequestAsync(stream, read.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (IOException)
            {
                return;
            }
        }

        if (request is null)
        {
            if (error != HttpParseError.ConnectionClosed)
                await RespondAsync(stream, LoopbackHttp.StatusFor(error), ErrorBody("bad_request"), null, cancellationToken)
                    .ConfigureAwait(false);
            return;
        }

        var refusal = _guard.Check(peer, request.Header("host"), request.Header("origin"),
            request.Header(LoopbackRequestGuard.LaunchTokenHeader));
        if (refusal != LoopbackRefusal.None)
        {
            // Which check failed is recorded locally and not told to the caller.
            _audit.Record(new AgentAuditEvent(AgentAuditKind.RequestRejected, _time.GetUtcNow(), null, null,
                "refused: " + refusal));
            await RespondAsync(stream, 403, ErrorBody("forbidden"), null, cancellationToken).ConfigureAwait(false);
            return;
        }

        if (request.Target != Path)
        {
            await RespondAsync(stream, 404, ErrorBody("not_found"), null, cancellationToken).ConfigureAwait(false);
            return;
        }

        var sessionId = request.Header("mcp-session-id");
        switch (request.Method)
        {
            case "POST":
                await HandlePostAsync(stream, request, sessionId, cancellationToken).ConfigureAwait(false);
                return;

            case "DELETE":
                var ended = _dispatcher.EndSession(sessionId);
                await RespondAsync(stream, ended ? 204 : 404, null, null, cancellationToken).ConfigureAwait(false);
                return;

            default:
                // No server-initiated SSE stream (GET) and no CORS pre-flight (OPTIONS): Streamable HTTP allows 405.
                await RespondAsync(stream, 405, ErrorBody("method_not_allowed"), AllowHeader, cancellationToken)
                    .ConfigureAwait(false);
                return;
        }
    }

    private async Task HandlePostAsync(Stream stream, LoopbackHttpRequest request, string? sessionId,
        CancellationToken cancellationToken)
    {
        var contentType = request.Header("content-type") ?? "";
        if (!contentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        {
            await RespondAsync(stream, 415, ErrorBody("unsupported_media_type"), null, cancellationToken)
                .ConfigureAwait(false);
            return;
        }

        // A call may wait for the user; if the client hangs up meanwhile the call is cancelled, so nothing is issued
        // to a connection nobody reads (review finding 2).
        using var clientGone = new CancellationTokenSource();
        using var stopWatching = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var call = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, clientGone.Token);
        var watcher = WatchForDisconnectAsync(stream, clientGone, stopWatching.Token);

        McpHttpResult result;
        try
        {
            result = await _dispatcher.HandlePostAsync(request.Body,
                new McpRequestContext(sessionId, request.Header("authorization"),
                    request.Header("mcp-protocol-version")), call.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            // Logged by type only: the message could echo request content.
            _audit.Record(new AgentAuditEvent(AgentAuditKind.RequestRejected, _time.GetUtcNow(), null, null,
                "internal error: " + ex.GetType().Name));
            result = new McpHttpResult(500, ErrorBody("internal_error"));
        }
        finally
        {
            stopWatching.Cancel();
            await watcher.ConfigureAwait(false);
        }

        await RespondAsync(stream, result.StatusCode, result.Body, result.Headers, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// Reads past the request while it is being handled. A client waiting for its response sends nothing, so
    /// end-of-stream, an error, or any byte at all (pipelining is not supported) means the call is abandoned.
    /// </summary>
    private static async Task WatchForDisconnectAsync(Stream stream, CancellationTokenSource clientGone,
        CancellationToken stopWatching)
    {
        var probe = new byte[1];
        try
        {
            await stream.ReadAsync(probe, stopWatching).ConfigureAwait(false);
            clientGone.Cancel();
        }
        catch (OperationCanceledException)
        {
            // The call finished first: the normal case.
        }
        catch (Exception ex) when (ex is IOException or ObjectDisposedException or NotSupportedException)
        {
            clientGone.Cancel();
        }
    }

    private static string ErrorBody(string code) => "{\"error\":\"" + code + "\"}";

    private static async Task RespondAsync(Stream stream, int status, string? body,
        IReadOnlyDictionary<string, string>? headers, CancellationToken cancellationToken)
    {
        try
        {
            await LoopbackHttp.WriteResponseAsync(stream, status, body, headers, cancellationToken).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The client went away; nothing to tell it.
        }
        catch (ObjectDisposedException)
        {
        }
    }
}

/// <summary>
/// The listener (T344): bound to 127.0.0.1 only — never <c>Any</c>, never <c>::1</c> — on a port the OS picks per
/// launch, with a cap on concurrent connections. Started only when the user has switched the feature on and is
/// signed in.
/// </summary>
public sealed class McpLoopbackServer : IAsyncDisposable
{
    private readonly Func<int, McpEndpoint> _endpointFactory;
    private readonly IAgentAuditSink _audit;
    private readonly TimeProvider _time;
    private readonly SemaphoreSlim _slots;
    private readonly CancellationTokenSource _stop = new();
    private readonly List<Task> _inFlight = new();
    private TcpListener? _listener;
    private Task? _acceptLoop;
    private McpEndpoint? _endpoint;

    /// <param name="endpointFactory">Builds the endpoint once the port is known (the guard needs it).</param>
    public McpLoopbackServer(Func<int, McpEndpoint> endpointFactory, IAgentAuditSink audit, TimeProvider time,
        int maxConnections = 8)
    {
        _endpointFactory = endpointFactory;
        _audit = audit;
        _time = time;
        _slots = new SemaphoreSlim(maxConnections, maxConnections);
    }

    public int Port { get; private set; }

    public IPEndPoint? LocalEndpoint => _listener?.LocalEndpoint as IPEndPoint;

    public void Start()
    {
        if (_listener is not null) throw new InvalidOperationException("Already started.");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        // Windows lets another process bind the same port with SO_REUSEADDR unless this is set.
        if (OperatingSystem.IsWindows()) listener.ExclusiveAddressUse = true;
        listener.Start();

        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        _endpoint = _endpointFactory(Port);
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_stop.Token));
    }

    /// <summary>Stops accepting at once without waiting for open connections (process exit).</summary>
    public void Abort()
    {
        try
        {
            _stop.Cancel();
            _listener?.Stop();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed.
        }
    }

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop.ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }
        }

        Task[] pending;
        lock (_inFlight) pending = _inFlight.ToArray();
        try
        {
            await Task.WhenAll(pending).WaitAsync(TimeSpan.FromSeconds(5)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Connections that do not finish in time are abandoned; their sockets are already closed.
        }
        _stop.Dispose();
        _slots.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken stop)
    {
        while (!stop.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener!.AcceptTcpClientAsync(stop).ConfigureAwait(false);
            }
            catch (Exception) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (SocketException)
            {
                continue;
            }

            if (!_slots.Wait(0))
            {
                _ = RefuseAsync(client, stop);
                continue;
            }

            var task = Task.Run(() => ServeAsync(client, stop), CancellationToken.None);
            lock (_inFlight)
            {
                _inFlight.RemoveAll(t => t.IsCompleted);
                _inFlight.Add(task);
            }
        }
    }

    private async Task ServeAsync(TcpClient client, CancellationToken stop)
    {
        try
        {
            using (client)
            {
                client.NoDelay = true;
                var peer = (client.Client.RemoteEndPoint as IPEndPoint)?.Address;
                await using var stream = client.GetStream();
                await _endpoint!.HandleConnectionAsync(stream, peer, stop).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is IOException or SocketException or ObjectDisposedException or OperationCanceledException)
        {
            // The connection broke; nothing else to do with it.
        }
        catch (Exception ex)
        {
            _audit.Record(new AgentAuditEvent(AgentAuditKind.RequestRejected, _time.GetUtcNow(), null, null,
                "connection failed: " + ex.GetType().Name));
        }
        finally
        {
            try
            {
                _slots.Release();
            }
            catch (ObjectDisposedException)
            {
            }
        }
    }

    private static async Task RefuseAsync(TcpClient client, CancellationToken stop)
    {
        using (client)
        {
            try
            {
                await using var stream = client.GetStream();
                await LoopbackHttp.WriteResponseAsync(stream, 503, "{\"error\":\"busy\"}", null, stop).ConfigureAwait(false);
            }
            catch (Exception)
            {
                // Busy and broken: either way the client is told nothing more.
            }
        }
    }
}

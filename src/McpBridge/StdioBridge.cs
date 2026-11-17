using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using GUIClient.Agents;

namespace NetRisk.McpBridge;

/// <summary>
/// Proxies MCP's stdio transport (newline-delimited JSON-RPC on stdin/stdout) to the GUI's loopback Streamable HTTP
/// endpoint (T345). It finds the GUI through the discovery file on every message, so it survives GUI restarts, and
/// it holds no port, key or token of its own. It logs to stderr only, and never a message body: one carries the key.
/// </summary>
public sealed class StdioBridge : IDisposable
{
    public const string NotRunningMessage =
        "NetRisk is not running, or its AI agent connection is switched off (Application > AI agents).";

    public const int MaxMessageChars = 1024 * 1024;

    private readonly Func<(AgentDiscoveryInfo? Info, string? Error)> _discover;
    private readonly TextReader _input;
    private readonly Stream _output;
    private readonly TextWriter _log;
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly object _sessionGate = new();
    private string? _sessionUrl;
    private string? _sessionId;
    private string? _protocolVersion;

    public StdioBridge(Func<(AgentDiscoveryInfo? Info, string? Error)> discover, TextReader input, Stream output,
        TextWriter log, HttpMessageHandler? handler = null)
    {
        _discover = discover;
        _input = input;
        _output = output;
        _log = log;
        // Loopback only, so never through a proxy, and never following a redirect somewhere else.
        _http = new HttpClient(handler ?? new SocketsHttpHandler
        {
            UseProxy = false,
            AllowAutoRedirect = false,
            ConnectTimeout = TimeSpan.FromSeconds(5)
        })
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
    }

    /// <summary>A bridge that reads <paramref name="discoveryPath"/> and checks the GUI that wrote it is alive.</summary>
    public static StdioBridge ForDiscoveryFile(string discoveryPath, TextReader input, Stream output, TextWriter log) =>
        new(() =>
        {
            var info = AgentDiscoveryFile.TryRead(discoveryPath, out var error);
            if (info is not null && !AgentDiscoveryFile.IsProcessAlive(info.ProcessId))
                return (null, "the NetRisk process that wrote the discovery file is not running");
            return (info, error);
        }, input, output, log);

    public async Task<int> RunAsync(CancellationToken cancellationToken)
    {
        var inFlight = new List<Task>();
        while (!cancellationToken.IsCancellationRequested)
        {
            string? line;
            try
            {
                line = await _input.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            if (line is null) break;
            if (string.IsNullOrWhiteSpace(line)) continue;

            inFlight.RemoveAll(t => t.IsCompleted);
            if (line.Length > MaxMessageChars)
            {
                inFlight.Add(WriteErrorAsync(null, -32600, "Message too large"));
                continue;
            }
            inFlight.Add(ForwardAsync(line, cancellationToken));
        }

        try
        {
            await Task.WhenAll(inFlight).WaitAsync(TimeSpan.FromSeconds(60)).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Stdin closed: whatever is still waiting on the user is abandoned with the process.
        }
        return 0;
    }

    /// <summary>Forwards one message and writes whatever the GUI answers. Public for tests.</summary>
    public async Task ForwardAsync(string line, CancellationToken cancellationToken)
    {
        JsonNode? id;
        string? method;
        bool isRequest;
        try
        {
            if (JsonNode.Parse(line) is not JsonObject message)
            {
                await WriteErrorAsync(null, -32600, "Invalid Request").ConfigureAwait(false);
                return;
            }
            method = message["method"] is JsonValue m && m.GetValueKind() == JsonValueKind.String ? m.GetValue<string>() : null;
            isRequest = method is not null && message.ContainsKey("id");
            id = message["id"]?.DeepClone();
        }
        catch (JsonException)
        {
            await WriteErrorAsync(null, -32700, "Parse error").ConfigureAwait(false);
            return;
        }

        var (info, error) = _discover();
        if (info is null)
        {
            await _log.WriteLineAsync("netrisk-mcp-bridge: " + error).ConfigureAwait(false);
            if (isRequest) await WriteErrorAsync(id, -32000, NotRunningMessage).ConfigureAwait(false);
            return;
        }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, info.Url)
            {
                Content = new StringContent(line, new UTF8Encoding(false), "application/json")
            };
            request.Headers.TryAddWithoutValidation("Accept", "application/json, text/event-stream");
            request.Headers.TryAddWithoutValidation(LoopbackRequestGuard.LaunchTokenHeader, info.LaunchToken);

            var sentSession = false;
            lock (_sessionGate)
            {
                if (method != "initialize" && _sessionUrl == info.Url && _sessionId is not null)
                {
                    request.Headers.TryAddWithoutValidation("Mcp-Session-Id", _sessionId);
                    sentSession = true;
                }
                if (method != "initialize" && _sessionUrl == info.Url && _protocolVersion is not null)
                    request.Headers.TryAddWithoutValidation("MCP-Protocol-Version", _protocolVersion);
            }

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
            {
                await _log.WriteLineAsync("netrisk-mcp-bridge: cannot reach NetRisk (" + ex.GetType().Name + ")")
                    .ConfigureAwait(false);
                if (isRequest) await WriteErrorAsync(id, -32000, NotRunningMessage).ConfigureAwait(false);
                return;
            }

            using (response)
            {
                if (response.StatusCode == HttpStatusCode.NotFound && sentSession)
                {
                    // The GUI restarted and forgot the session; sessions are optional, so carry on without one.
                    lock (_sessionGate) _sessionId = null;
                    continue;
                }
                if (response.StatusCode == HttpStatusCode.Accepted) return;

                var mediaType = response.Content.Headers.ContentType?.MediaType;
                if (mediaType == "text/event-stream")
                {
                    await RelayEventStreamAsync(response, cancellationToken).ConfigureAwait(false);
                    return;
                }

                var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                if (TryCompactJsonRpc(body, out var compact, out var result))
                {
                    if (method == "initialize" && response.IsSuccessStatusCode) RememberSession(info.Url, response, result);
                    await WriteLineAsync(compact).ConfigureAwait(false);
                    return;
                }

                if (isRequest)
                    await WriteErrorAsync(id, -32000, $"NetRisk refused the request (HTTP {(int)response.StatusCode}).")
                        .ConfigureAwait(false);
                return;
            }
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _writeGate.Dispose();
    }

    private void RememberSession(string url, HttpResponseMessage response, JsonObject? result)
    {
        lock (_sessionGate)
        {
            _sessionUrl = url;
            _sessionId = response.Headers.TryGetValues("Mcp-Session-Id", out var values)
                ? string.Join(",", values)
                : null;
            _protocolVersion = result?["protocolVersion"] is JsonValue v && v.GetValueKind() == JsonValueKind.String
                ? v.GetValue<string>()
                : null;
        }
    }

    private async Task RelayEventStreamAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream, Encoding.UTF8);
        var data = new StringBuilder();
        while (await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false) is { } line)
        {
            if (line.Length == 0)
            {
                if (data.Length > 0 && TryCompactJsonRpc(data.ToString(), out var compact, out _))
                    await WriteLineAsync(compact).ConfigureAwait(false);
                data.Clear();
            }
            else if (line.StartsWith("data:", StringComparison.Ordinal))
            {
                if (data.Length > 0) data.Append('\n');
                data.Append(line.AsSpan(5).TrimStart(' '));
            }
        }
    }

    private Task WriteErrorAsync(JsonNode? id, int code, string message) =>
        WriteLineAsync(new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        }.ToJsonString());

    private async Task WriteLineAsync(string json)
    {
        var bytes = Encoding.UTF8.GetBytes(json + "\n");
        await _writeGate.WaitAsync().ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(bytes).ConfigureAwait(false);
            await _output.FlushAsync().ConfigureAwait(false);
        }
        finally
        {
            _writeGate.Release();
        }
    }

    /// <summary>A JSON-RPC message re-serialized on one line (stdio framing forbids embedded newlines).</summary>
    private static bool TryCompactJsonRpc(string body, out string compact, out JsonObject? result)
    {
        compact = "";
        result = null;
        try
        {
            if (JsonNode.Parse(body) is not JsonObject message || message["jsonrpc"]?.ToString() != "2.0") return false;
            result = message["result"] as JsonObject;
            compact = message.ToJsonString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }
}

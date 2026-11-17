using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Tools;

namespace GUIClient.Agents;

public sealed class McpServerOptions
{
    public string ServerName { get; init; } = "netrisk";
    public string ServerVersion { get; init; } = "0.0.0";

    /// <summary>How long one call waits for the user before answering "pending" (clients time out around a minute).</summary>
    public TimeSpan DecisionWait { get; init; } = TimeSpan.FromSeconds(45);

    public int MaxSessions { get; init; } = 32;
    public TimeSpan SessionIdleTimeout { get; init; } = TimeSpan.FromHours(4);
}

/// <summary>What the transport answers: a status, an optional JSON body and optional headers.</summary>
public sealed record McpHttpResult(int StatusCode, string? Body, IReadOnlyDictionary<string, string>? Headers = null);

/// <summary>The request headers the protocol layer reads. The key may come from here or from a tool argument.</summary>
public sealed record McpRequestContext(string? SessionId, string? Authorization, string? ProtocolVersion);

/// <summary>
/// The MCP subset M61 needs over Streamable HTTP: <c>initialize</c>, <c>ping</c>, <c>tools/list</c>, <c>tools/call</c>,
/// notifications, and optional sessions. Exactly three tools exist: <c>register_agent</c> (the only unauthenticated
/// one), <c>request_access</c> and <c>get_status</c> — the two a key without a lease may call (S55).
/// </summary>
public sealed class McpDispatcher
{
    public const string RegisterAgentTool = "register_agent";
    public const string RequestAccessTool = "request_access";
    public const string GetStatusTool = "get_status";

    public static readonly IReadOnlyList<string> SupportedProtocolVersions = new[] { "2025-06-18", "2025-03-26" };

    private const string Instructions =
        "NetRisk lets you work beside its signed-in user, with that user's permissions and only while the user allows " +
        "it. Call register_agent once; the user approves you in NetRisk and you receive an agent key, shown once. Then " +
        "call request_access with the key, a reason and a number of 10-minute blocks; the user grants or refuses it. " +
        "Both calls may answer 'pending': call again with the returned id to keep waiting.";

    private readonly AgentRegistry _registry;
    private readonly AgentLeaseManager _leases;
    private readonly AgentApprovalCoordinator _approvals;
    private readonly TimeProvider _time;
    private readonly McpServerOptions _options;
    private readonly object _sessionsGate = new();
    private readonly Dictionary<string, Session> _sessions = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IAgentTool> _tools = new(StringComparer.Ordinal);

    public McpDispatcher(AgentRegistry registry, AgentLeaseManager leases, AgentApprovalCoordinator approvals,
        TimeProvider time, McpServerOptions? options = null, IReadOnlyList<IAgentTool>? tools = null)
    {
        _registry = registry;
        _leases = leases;
        _approvals = approvals;
        _time = time;
        _options = options ?? new McpServerOptions();
        foreach (var tool in tools ?? Array.Empty<IAgentTool>())
        {
            if (BootstrapToolNames.Contains(tool.Name) || !_tools.TryAdd(tool.Name, tool))
                throw new ArgumentException($"Tool name '{tool.Name}' is reserved or registered twice.", nameof(tools));
        }
    }

    /// <summary>The only tools a key without a lease reaches (S55). Everything else goes through the lease gate.</summary>
    public static IReadOnlyList<string> BootstrapToolNames { get; } =
        new[] { RegisterAgentTool, RequestAccessTool, GetStatusTool };

    /// <summary>Handles one POSTed JSON-RPC message.</summary>
    public async Task<McpHttpResult> HandlePostAsync(byte[] body, McpRequestContext context, CancellationToken cancellationToken)
    {
        JsonNode? root;
        try
        {
            root = JsonNode.Parse(body, documentOptions: new JsonDocumentOptions { MaxDepth = 32 });
        }
        catch (JsonException)
        {
            return Error(400, null, -32700, "Parse error");
        }

        if (root is JsonArray) return Error(400, null, -32600, "Batched requests are not supported");
        if (root is not JsonObject message || Text(message["jsonrpc"]) != "2.0")
            return Error(400, null, -32600, "Invalid Request");

        var method = Text(message["method"]);
        // A response or a notification from the client: accepted, nothing to answer (Streamable HTTP).
        if (method is null || !message.ContainsKey("id")) return new McpHttpResult(202, null);

        var id = message["id"];
        if (id is not JsonValue idValue || idValue.GetValueKind() is not (JsonValueKind.String or JsonValueKind.Number))
            return Error(400, null, -32600, "Invalid Request");

        if (method != "initialize")
        {
            if (context.SessionId is { } sessionId && !TouchSession(sessionId))
                return Error(404, id, -32001, "Unknown or expired session; initialize again");
            if (context.ProtocolVersion is { } version && !SupportedProtocolVersions.Contains(version))
                return Error(400, id, -32600, "Unsupported MCP-Protocol-Version");
        }

        var parameters = message["params"] as JsonObject;
        switch (method)
        {
            case "initialize":
                return Initialize(id, parameters);
            case "ping":
                return Result(id, new JsonObject());
            case "tools/list":
                var tools = ToolDefinitions();
                foreach (var tool in _tools.Values) tools.Add(LeasedToolDefinition(tool));
                return Result(id, new JsonObject { ["tools"] = tools });
            case "tools/call":
                return await CallToolAsync(id, parameters, context, cancellationToken).ConfigureAwait(false);
            default:
                return Error(200, id, -32601, "Method not found");
        }
    }

    /// <summary>Ends a session (HTTP DELETE). False when it was unknown.</summary>
    public bool EndSession(string? sessionId)
    {
        if (string.IsNullOrEmpty(sessionId)) return false;
        lock (_sessionsGate) return _sessions.Remove(sessionId);
    }

    private McpHttpResult Initialize(JsonNode id, JsonObject? parameters)
    {
        var requested = Text(parameters?["protocolVersion"]);
        var version = requested is not null && SupportedProtocolVersions.Contains(requested)
            ? requested
            : SupportedProtocolVersions[0];
        var clientInfo = parameters?["clientInfo"] as JsonObject;
        var client = new AgentClientInfo(
            AgentText.CleanOrEmpty(Text(clientInfo?["name"]), AgentText.MaxClientNameLength),
            AgentText.CleanOrEmpty(Text(clientInfo?["version"]), AgentText.MaxClientVersionLength));

        var sessionId = RandomGenerator.RandomToken(24);
        lock (_sessionsGate)
        {
            var now = _time.GetUtcNow();
            foreach (var stale in _sessions.Where(s => now - s.Value.LastSeen > _options.SessionIdleTimeout).ToList())
                _sessions.Remove(stale.Key);
            if (_sessions.Count >= _options.MaxSessions)
                _sessions.Remove(_sessions.OrderBy(s => s.Value.LastSeen).First().Key);
            _sessions[sessionId] = new Session(client, now);
        }

        var result = new JsonObject
        {
            ["protocolVersion"] = version,
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } },
            ["serverInfo"] = new JsonObject { ["name"] = _options.ServerName, ["version"] = _options.ServerVersion },
            ["instructions"] = Instructions
        };
        return Result(id, result, new Dictionary<string, string> { ["Mcp-Session-Id"] = sessionId });
    }

    private async Task<McpHttpResult> CallToolAsync(JsonNode id, JsonObject? parameters, McpRequestContext context,
        CancellationToken cancellationToken)
    {
        var name = Text(parameters?["name"]);
        var arguments = parameters?["arguments"] as JsonObject ?? new JsonObject();
        JsonObject? result;
        if (name == RegisterAgentTool)
            result = await RegisterAgentAsync(arguments, context, cancellationToken).ConfigureAwait(false);
        else if (name == RequestAccessTool)
            result = await RequestAccessAsync(arguments, context, cancellationToken).ConfigureAwait(false);
        else if (name == GetStatusTool)
            result = GetStatus(arguments, context);
        else if (name is not null && _tools.TryGetValue(name, out var tool))
            result = await CallLeasedToolAsync(tool, arguments, context, cancellationToken).ConfigureAwait(false);
        else
            result = null;

        return result is null ? Error(200, id, -32602, "Unknown tool") : Result(id, result);
    }

    private async Task<JsonObject> CallLeasedToolAsync(IAgentTool tool, JsonObject arguments, McpRequestContext context,
        CancellationToken cancellationToken)
    {
        // The single gate (S55: a key without a lease reaches only the bootstrap tools): a valid key, then a lease that
        // allows this tool. Nothing below runs otherwise.
        var agent = Authenticate(arguments, context);
        if (agent is null) return Unauthorized();

        var check = _leases.Authorize(agent.Id, tool.RequiresEdit);
        var lease = _leases.GetLease(agent.Id);
        if (check == LeaseCheck.Allowed && lease is null) check = LeaseCheck.NoLease;
        switch (check)
        {
            case LeaseCheck.NoLease:
                return ToolResult(true, Status("access_expired",
                    "No access lease is active. Call request_access to ask the user for access."));
            case LeaseCheck.Paused:
                return ToolResult(true, Status("paused", "The user has paused your access. Wait until they resume it."));
            case LeaseCheck.ReadOnly:
                return ToolResult(true, Status("read_only",
                    "Your lease is read-only and this tool edits. Ask with request_access, mode read_write, if needed."));
        }

        // The tool never sees the key.
        var forwarded = (JsonObject)arguments.DeepClone();
        forwarded.Remove("agent_key");
        try
        {
            var data = await tool.InvokeAsync(new AgentToolCall(agent, lease!), forwarded, cancellationToken)
                .ConfigureAwait(false);
            return ToolResult(false, data);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ToolResult(true, Status("tool_failed", "The tool failed (" + ex.GetType().Name + ")."));
        }
    }

    private async Task<JsonObject> RegisterAgentAsync(JsonObject arguments, McpRequestContext context,
        CancellationToken cancellationToken)
    {
        var pendingId = Text(arguments["pending_id"]);
        if (pendingId is null)
        {
            var outcome = _registry.Begin(Text(arguments["name"]), Text(arguments["description"]), ClientOf(context));
            switch (outcome.Kind)
            {
                case RegistrationOutcomeKind.Created:
                    // Answer at once: the agent must be able to show the fingerprint before the user decides, or the
                    // user has nothing to match the dialog against. Only a call carrying pending_id waits.
                    _approvals.EnqueueRegistration(outcome.Pending!.PendingId);
                    return PendingRegistrationResult(outcome.Pending);
                case RegistrationOutcomeKind.RateLimited:
                    return ToolResult(true, new JsonObject
                    {
                        ["status"] = "rate_limited",
                        ["retry_after_seconds"] = (int)Math.Ceiling((outcome.RetryAfter ?? TimeSpan.FromMinutes(1)).TotalSeconds),
                        ["message"] = "Too many registration requests are waiting for the user. Try again later."
                    });
                case RegistrationOutcomeKind.AlreadyPending:
                    return ToolResult(true, Status("already_pending",
                        "A registration with this name is already waiting for the user. If it is not yours, ask the " +
                        "user to deny the request whose fingerprint you did not show them, then call register_agent again."));
                case RegistrationOutcomeKind.NoUser:
                    return ToolResult(true, Status("unavailable", "Nobody is signed in to NetRisk."));
                default:
                    return ToolResult(true, Status("invalid", outcome.Error ?? "invalid arguments"));
            }
        }

        var waited = await _registry.WaitAsync(pendingId, _options.DecisionWait, cancellationToken).ConfigureAwait(false);
        return waited.Kind switch
        {
            RegistrationResultKind.Pending => PendingRegistrationResult(waited.Pending!),
            RegistrationResultKind.Approved => ToolResult(false, new JsonObject
            {
                ["status"] = "approved",
                ["agent_id"] = waited.Registration!.Id,
                ["agent_key"] = waited.Key!.Reveal(),
                ["fingerprint"] = waited.Registration.Fingerprint,
                ["message"] = "Keep agent_key secret: NetRisk will not show it again. Pass it to request_access " +
                              "(argument agent_key, or header 'Authorization: Bearer <key>') to ask the user for access."
            }),
            RegistrationResultKind.Denied => ToolResult(true, Status("denied", "The user refused this registration.")),
            RegistrationResultKind.Expired => ToolResult(true, Status("expired",
                "The registration was not approved in time. Call register_agent again to start over.")),
            _ => ToolResult(true, Status("not_found", "Unknown pending_id. Call register_agent without it to start over."))
        };
    }

    private static JsonObject PendingRegistrationResult(PendingRegistration pending) => ToolResult(false, new JsonObject
    {
        ["status"] = "pending",
        ["pending_id"] = pending.PendingId,
        ["fingerprint"] = pending.Fingerprint,
        ["retry_after_seconds"] = 2,
        ["message"] = $"Show the user the fingerprint {pending.Fingerprint} now: NetRisk is asking them to approve " +
                      "this agent and they should approve only if the codes match. Then call register_agent again " +
                      "with this pending_id to wait for their decision and receive the key."
    });

    private async Task<JsonObject> RequestAccessAsync(JsonObject arguments, McpRequestContext context,
        CancellationToken cancellationToken)
    {
        var agent = Authenticate(arguments, context);
        if (agent is null) return Unauthorized();

        var requestId = Text(arguments["request_id"]);
        if (requestId is null)
        {
            var blocks = arguments.ContainsKey("blocks") ? Integer(arguments["blocks"]) ?? 0 : 1;
            var modeText = Text(arguments["mode"]) ?? "read";
            AgentAccessMode mode;
            if (modeText == "read") mode = AgentAccessMode.Read;
            else if (modeText == "read_write") mode = AgentAccessMode.ReadWrite;
            else return ToolResult(true, Status("invalid", "mode must be 'read' or 'read_write'"));

            var outcome = _leases.Request(agent, blocks, mode, Text(arguments["reason"]));
            switch (outcome.Kind)
            {
                case AccessRequestOutcomeKind.Created:
                    requestId = outcome.Request!.RequestId;
                    _approvals.EnqueueAccess(requestId);
                    break;
                case AccessRequestOutcomeKind.Collapsed:
                    requestId = outcome.Request!.RequestId;
                    break;
                case AccessRequestOutcomeKind.Busy:
                    return ToolResult(true, Status("busy", outcome.Error ?? "busy"));
                default:
                    return ToolResult(true, Status("invalid", outcome.Error ?? "invalid arguments"));
            }
        }

        var request = await _leases.WaitAsync(requestId, agent.Id, _options.DecisionWait, cancellationToken)
            .ConfigureAwait(false);
        if (request is null)
            return ToolResult(true, Status("not_found", "Unknown request_id. Call request_access without it to ask again."));

        return request.Status switch
        {
            AccessRequestStatus.Pending => ToolResult(false, new JsonObject
            {
                ["status"] = "pending",
                ["request_id"] = request.RequestId,
                ["retry_after_seconds"] = 5,
                ["message"] = "Waiting for the NetRisk user to decide. Call request_access again with this request_id."
            }),
            AccessRequestStatus.Granted when request.Lease is { } lease => ToolResult(false, LeaseJson("granted", lease)),
            AccessRequestStatus.Denied => ToolResult(true, Status("denied", "The user refused access.")),
            _ => ToolResult(true, Status("expired", "The request was not decided in time. Ask again if you still need access."))
        };
    }

    private JsonObject GetStatus(JsonObject arguments, McpRequestContext context)
    {
        var agent = Authenticate(arguments, context);
        if (agent is null) return Unauthorized();

        var lease = _leases.GetLease(agent.Id);
        var pending = _leases.GetPendingFor(agent.Id);
        return ToolResult(false, new JsonObject
        {
            ["status"] = "ok",
            ["agent_id"] = agent.Id,
            ["agent_name"] = agent.Name,
            ["fingerprint"] = agent.Fingerprint,
            ["lease"] = lease is null ? null : LeaseJson("active", lease),
            ["pending_request_id"] = pending?.RequestId,
            ["block_minutes"] = AgentLeaseOptions.BlockMinutes,
            ["max_blocks"] = _leases.MaxBlocks
        });
    }

    private JsonObject LeaseJson(string status, AgentLease lease)
    {
        var now = _time.GetUtcNow();
        return new JsonObject
        {
            ["status"] = status,
            ["mode"] = lease.Mode == AgentAccessMode.ReadWrite ? "read_write" : "read",
            ["minutes"] = lease.Minutes,
            ["expires_at"] = lease.ExpiresAt.UtcDateTime.ToString("O"),
            ["remaining_seconds"] = (int)lease.Remaining(now).TotalSeconds,
            ["paused"] = lease.IsPaused
        };
    }

    private AgentRegistration? Authenticate(JsonObject arguments, McpRequestContext context)
    {
        var fromArgument = Text(arguments["agent_key"]);
        string? fromHeader = null;
        if (context.Authorization is { } authorization)
        {
            const string scheme = "Bearer ";
            if (!authorization.StartsWith(scheme, StringComparison.OrdinalIgnoreCase)) return null;
            fromHeader = authorization[scheme.Length..].Trim();
        }

        // Two different keys in one call is a confused or hostile caller: neither is honoured.
        if (fromHeader is not null && fromArgument is not null && !string.Equals(fromHeader, fromArgument, StringComparison.Ordinal))
            return null;

        return _registry.Authenticate(fromHeader ?? fromArgument);
    }

    private static JsonObject Unauthorized() =>
        ToolResult(true, Status("unauthorized",
            "Missing, unknown or revoked agent key. Call register_agent to register this agent again."));

    private AgentClientInfo ClientOf(McpRequestContext context)
    {
        if (context.SessionId is null) return AgentClientInfo.Unknown;
        lock (_sessionsGate) return _sessions.TryGetValue(context.SessionId, out var s) ? s.Client : AgentClientInfo.Unknown;
    }

    private bool TouchSession(string sessionId)
    {
        lock (_sessionsGate)
        {
            if (!_sessions.TryGetValue(sessionId, out var session)) return false;
            var now = _time.GetUtcNow();
            if (now - session.LastSeen > _options.SessionIdleTimeout)
            {
                _sessions.Remove(sessionId);
                return false;
            }
            _sessions[sessionId] = session with { LastSeen = now };
            return true;
        }
    }

    private static JsonArray ToolDefinitions() => new(
        Tool(RegisterAgentTool,
            "Register this agent with NetRisk. The signed-in user approves it on screen; on approval you receive an " +
            "agent key, shown only once. If the answer is 'pending', call again with pending_id to keep waiting.",
            new JsonObject
            {
                ["name"] = new JsonObject { ["type"] = "string", ["maxLength"] = AgentText.MaxNameLength,
                    ["description"] = "Display name the user sees, e.g. 'Claude Code on my laptop'." },
                ["description"] = new JsonObject { ["type"] = "string", ["maxLength"] = AgentText.MaxDescriptionLength,
                    ["description"] = "What you will use NetRisk for." },
                ["pending_id"] = new JsonObject { ["type"] = "string",
                    ["description"] = "From an earlier 'pending' answer, to keep waiting for the user." }
            }),
        Tool(RequestAccessTool,
            "Ask the user for access in blocks of 10 minutes. The user may grant fewer minutes or read-only access. " +
            "If the answer is 'pending', call again with request_id to keep waiting.",
            new JsonObject
            {
                ["agent_key"] = new JsonObject { ["type"] = "string",
                    ["description"] = "The key from register_agent (or send it as 'Authorization: Bearer <key>')." },
                ["blocks"] = new JsonObject { ["type"] = "integer", ["minimum"] = 1,
                    ["maximum"] = AgentLeaseOptions.AbsoluteMaxBlocks, ["description"] = "Number of 10-minute blocks." },
                ["mode"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("read", "read_write") },
                ["reason"] = new JsonObject { ["type"] = "string", ["maxLength"] = AgentText.MaxReasonLength,
                    ["description"] = "Why you need access; shown to the user." },
                ["request_id"] = new JsonObject { ["type"] = "string",
                    ["description"] = "From an earlier 'pending' answer, to keep waiting for the user." }
            }),
        Tool(GetStatusTool,
            "Report this agent's registration and current access lease, if any.",
            new JsonObject
            {
                ["agent_key"] = new JsonObject { ["type"] = "string",
                    ["description"] = "The key from register_agent (or send it as 'Authorization: Bearer <key>')." }
            }));

    /// <summary>A business tool's definition, with the agent_key argument every leased call accepts.</summary>
    private static JsonObject LeasedToolDefinition(IAgentTool tool)
    {
        var schema = (JsonObject)tool.InputSchema().DeepClone();
        if (schema["properties"] is not JsonObject properties)
        {
            properties = new JsonObject();
            schema["properties"] = properties;
        }
        schema["type"] = "object";
        properties["agent_key"] = new JsonObject { ["type"] = "string",
            ["description"] = "The key from register_agent (or send it as 'Authorization: Bearer <key>')." };
        return new JsonObject { ["name"] = tool.Name, ["description"] = tool.Description, ["inputSchema"] = schema };
    }

    private static JsonObject Tool(string name, string description, JsonObject properties) => new()
    {
        ["name"] = name,
        ["description"] = description,
        ["inputSchema"] = new JsonObject { ["type"] = "object", ["properties"] = properties }
    };

    private static JsonObject Status(string status, string message) =>
        new() { ["status"] = status, ["message"] = message };

    private static JsonObject ToolResult(bool isError, JsonObject data) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = data.ToJsonString() }),
        ["structuredContent"] = data,
        ["isError"] = isError
    };

    private static McpHttpResult Result(JsonNode id, JsonObject result, IReadOnlyDictionary<string, string>? headers = null)
    {
        var envelope = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id.DeepClone(), ["result"] = result };
        return new McpHttpResult(200, envelope.ToJsonString(), headers);
    }

    private static McpHttpResult Error(int status, JsonNode? id, int code, string message)
    {
        var envelope = new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id?.DeepClone(),
            ["error"] = new JsonObject { ["code"] = code, ["message"] = message }
        };
        return new McpHttpResult(status, envelope.ToJsonString());
    }

    private static string? Text(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.String ? value.GetValue<string>() : null;

    private static int? Integer(JsonNode? node) =>
        node is JsonValue value && value.GetValueKind() == JsonValueKind.Number && value.TryGetValue<int>(out var number)
            ? number
            : node is JsonValue v2 && v2.GetValueKind() == JsonValueKind.Number && v2.TryGetValue<double>(out var d)
              && d == Math.Floor(d) && d is >= int.MinValue and <= int.MaxValue
                ? (int)d
                : null;

    private sealed record Session(AgentClientInfo Client, DateTimeOffset LastSeen);
}

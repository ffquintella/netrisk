using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GUIClient.Agents;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>A clock the test moves by hand.</summary>
internal sealed class ManualTimeProvider : TimeProvider
{
    private readonly object _gate = new();
    private DateTimeOffset _now = new(2026, 11, 17, 12, 0, 0, TimeSpan.Zero);

    public override DateTimeOffset GetUtcNow()
    {
        lock (_gate) return _now;
    }

    public void Advance(TimeSpan by)
    {
        lock (_gate) _now += by;
    }
}

/// <summary>Captures every audit record so a test can search them for a secret.</summary>
internal sealed class RecordingAuditSink : IAgentAuditSink
{
    private readonly List<AgentAuditEvent> _events = new();

    public IReadOnlyList<AgentAuditEvent> Events
    {
        get { lock (_events) return _events.ToList(); }
    }

    public void Record(AgentAuditEvent auditEvent)
    {
        lock (_events) _events.Add(auditEvent);
    }
}

/// <summary>Answers approval prompts as the test says, and counts how many were on screen at once.</summary>
internal sealed class ScriptedPrompter : IAgentApprovalPrompter
{
    private int _open;

    public Func<RegistrationPrompt, Task<bool>> OnRegistration { get; set; } = _ => Task.FromResult(false);
    public Func<AccessPrompt, Task<AccessDecision>> OnAccess { get; set; } = _ => Task.FromResult(AccessDecision.Deny);

    public int Shown;
    public int MaxOnScreen;
    public readonly List<RegistrationPrompt> RegistrationPrompts = new();

    public async Task<bool> ConfirmRegistrationAsync(RegistrationPrompt prompt, CancellationToken cancellationToken)
    {
        lock (RegistrationPrompts) RegistrationPrompts.Add(prompt);
        Enter();
        try
        {
            return await OnRegistration(prompt).WaitAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _open);
        }
    }

    public async Task<AccessDecision> ConfirmAccessAsync(AccessPrompt prompt, CancellationToken cancellationToken)
    {
        Enter();
        try
        {
            return await OnAccess(prompt).WaitAsync(cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _open);
        }
    }

    private void Enter()
    {
        Interlocked.Increment(ref Shown);
        var open = Interlocked.Increment(ref _open);
        int seen;
        do
        {
            seen = MaxOnScreen;
        } while (open > seen && Interlocked.CompareExchange(ref MaxOnScreen, open, seen) != seen);
    }
}

/// <summary>The stand-in's declared result shape: what <see cref="StubTool"/> returns unless a test says otherwise.</summary>
internal sealed record StubResult(string Status, string Tool);

/// <summary>A business tool stand-in that records what reached it.</summary>
internal sealed class StubTool : IAgentTool
{
    /// <summary>What a stub tool needs by default; <see cref="AgentRig.DefaultUser"/> holds it.</summary>
    public const string StubPermission = "stub_read";

    public StubTool(string name, bool requiresEdit, AgentToolPermission? permission = null)
    {
        Name = name;
        RequiresEdit = requiresEdit;
        Permission = permission ?? AgentToolPermission.AnyOf(StubPermission);
    }

    public string Name { get; }
    public string Description => "Stub " + Name;
    public bool RequiresEdit { get; }
    public AgentToolPermission Permission { get; }
    public Type ResultType => typeof(StubResult);
    public List<(AgentToolCall Call, System.Text.Json.Nodes.JsonObject Arguments)> Invocations { get; } = new();

    public System.Text.Json.Nodes.JsonObject InputSchema() => new()
    {
        ["type"] = "object",
        ["properties"] = new System.Text.Json.Nodes.JsonObject { ["id"] = new System.Text.Json.Nodes.JsonObject { ["type"] = "integer" } }
    };

    /// <summary>Runs inside the call, before the result is returned: a test signs out, ends the lease, or shapes the result.</summary>
    public Func<AgentToolCall, System.Text.Json.Nodes.JsonObject>? OnInvoke { get; set; }

    public Task<System.Text.Json.Nodes.JsonObject> InvokeAsync(AgentToolCall call,
        System.Text.Json.Nodes.JsonObject arguments, CancellationToken cancellationToken)
    {
        lock (Invocations) Invocations.Add((call, arguments));
        var result = OnInvoke?.Invoke(call) ?? new System.Text.Json.Nodes.JsonObject { ["status"] = "ok", ["tool"] = Name };
        return Task.FromResult(result);
    }
}

/// <summary>Registry, leases, approvals and dispatcher wired the way the runtime wires them, on a manual clock.</summary>
internal sealed class AgentRig
{
    public const int UserId = 7;
    public const int Port = 47123;
    public const string LaunchToken = "launch-token-for-tests_0123456789abcdefABCDEF";

    public AgentRig(AgentRegistryOptions? registry = null, AgentLeaseOptions? leases = null,
        IAgentApprovalPrompter? prompter = null, TimeSpan? decisionWait = null, IReadOnlyList<IAgentTool>? tools = null,
        Model.Authentication.AuthenticatedUserInfo? user = null)
    {
        Prompter = prompter ?? new ScriptedPrompter();
        Registry = new AgentRegistry(Store, Time, Audit, registry);
        Registry.SetUser(UserId);
        Leases = new AgentLeaseManager(Time, Audit, leases);
        Approvals = new AgentApprovalCoordinator(Registry, Leases, Prompter, Audit, Time);
        User = user ?? DefaultUser();
        Dispatcher = new McpDispatcher(Registry, Leases, Approvals, Time,
            new McpServerOptions
            {
                DecisionWait = decisionWait ?? TimeSpan.FromMilliseconds(50),
                ReviewedTools = ReviewedWithStandIns(tools)
            }, tools, () => User, Audit);
        Guard = new LoopbackRequestGuard(Port, LaunchToken);
        Endpoint = new McpEndpoint(Guard, Dispatcher, Audit, Time);
    }

    /// <summary>The production inventory plus the test's own stand-ins.</summary>
    public static IReadOnlyDictionary<string, ReviewedAgentTool> ReviewedWithStandIns(IEnumerable<IAgentTool>? tools)
    {
        var reviewed = AgentToolInventory.Reviewed.ToDictionary(p => p.Key, p => p.Value, StringComparer.Ordinal);
        // Stand-ins (any tool the production inventory does not name) are reviewed as they declare themselves; production
        // tools keep their real review, so a catalog tool that drifted still fails.
        foreach (var tool in tools ?? Enumerable.Empty<IAgentTool>())
            if (!AgentToolInventory.Reviewed.ContainsKey(tool.Name))
                reviewed[tool.Name] = new ReviewedAgentTool(tool.Name, tool.Permission.Description, tool.RequiresEdit);
        return reviewed;
    }

    /// <summary>The signed-in user the dispatcher sees, read on every call; a test may swap it.</summary>
    public Model.Authentication.AuthenticatedUserInfo? User { get; set; }

    /// <summary>The rig's user: <see cref="UserId"/>, not an administrator, holding only the stub tools' permission.</summary>
    public static Model.Authentication.AuthenticatedUserInfo DefaultUser(params string[] permissions) => new()
    {
        UserId = UserId,
        UserName = "Ana Analyst",
        UserAccount = "ana",
        UserEmail = "ana@example.test",
        UserRole = "Analyst",
        IsAdmin = false,
        UserPermissions = permissions.Length > 0 ? permissions.ToList() : new List<string> { StubTool.StubPermission }
    };

    public ManualTimeProvider Time { get; } = new();
    public RecordingAuditSink Audit { get; } = new();
    public InMemoryAgentRegistrationStore Store { get; } = new();
    public IAgentApprovalPrompter Prompter { get; }
    public AgentRegistry Registry { get; }
    public AgentLeaseManager Leases { get; }
    public AgentApprovalCoordinator Approvals { get; }
    public McpDispatcher Dispatcher { get; }
    public LoopbackRequestGuard Guard { get; }
    public McpEndpoint Endpoint { get; }

    /// <summary>Registers an agent and collects its key the way <c>register_agent</c> does.</summary>
    public (AgentRegistration Registration, string Key) RegisterApproved(string name = "Claude Code")
    {
        var outcome = Registry.Begin(name, "Helps triage findings", new AgentClientInfo("claude-code", "2.1.0"));
        Assert.Equal(RegistrationOutcomeKind.Created, outcome.Kind);
        Assert.True(Registry.Decide(outcome.Pending!.PendingId, approve: true));
        var result = Registry.WaitAsync(outcome.Pending.PendingId, TimeSpan.Zero, CancellationToken.None)
            .GetAwaiter().GetResult();
        Assert.Equal(RegistrationResultKind.Approved, result.Kind);
        return (result.Registration!, result.Key!.Reveal());
    }

    public AgentLease Grant(AgentRegistration agent, int blocks, AgentAccessMode mode, int? grantBlocks = null,
        AgentAccessMode? grantMode = null)
    {
        var outcome = Leases.Request(agent, blocks, mode, "Review the open findings");
        Assert.Equal(AccessRequestOutcomeKind.Created, outcome.Kind);
        Assert.True(Leases.Decide(outcome.Request!.RequestId,
            AccessDecision.Grant(grantBlocks ?? blocks, grantMode ?? mode)));
        return Leases.GetLease(agent.Id)!;
    }

    /// <summary>A raw HTTP/1.1 POST to the endpoint, with the guard's expected headers unless overridden.</summary>
    public static string Post(string body, string? host = null, string? origin = null, string? token = LaunchToken,
        string? sessionId = null, string? authorization = null)
    {
        var request = new StringBuilder();
        request.Append("POST /mcp HTTP/1.1\r\n");
        if (host != "") request.Append("Host: ").Append(host ?? "127.0.0.1:" + Port).Append("\r\n");
        if (origin is not null) request.Append("Origin: ").Append(origin).Append("\r\n");
        if (token is not null) request.Append(LoopbackRequestGuard.LaunchTokenHeader).Append(": ").Append(token).Append("\r\n");
        if (sessionId is not null) request.Append("Mcp-Session-Id: ").Append(sessionId).Append("\r\n");
        if (authorization is not null) request.Append("Authorization: ").Append(authorization).Append("\r\n");
        request.Append("Content-Type: application/json\r\n");
        request.Append("Accept: application/json, text/event-stream\r\n");
        request.Append("Content-Length: ").Append(Encoding.UTF8.GetByteCount(body)).Append("\r\n\r\n");
        request.Append(body);
        return request.ToString();
    }

    public static string ToolCall(int id, string tool, string argumentsJson) =>
        $"{{\"jsonrpc\":\"2.0\",\"id\":{id},\"method\":\"tools/call\",\"params\":{{\"name\":\"{tool}\",\"arguments\":{argumentsJson}}}}}";

    /// <summary>Sends <paramref name="raw"/> through the endpoint as a loopback peer and parses the response.</summary>
    public Task<RawResponse> SendAsync(string raw, IPAddress? peer = null) =>
        SendAsync(Endpoint, raw, peer ?? IPAddress.Loopback);

    public static async Task<RawResponse> SendAsync(McpEndpoint endpoint, string raw, IPAddress? peer)
    {
        var stream = new DuplexStream(Encoding.UTF8.GetBytes(raw));
        await endpoint.HandleConnectionAsync(stream, peer, CancellationToken.None);
        return RawResponse.Parse(Encoding.UTF8.GetString(stream.Output.ToArray()));
    }
}

internal sealed record RawResponse(int Status, string Head, string Body)
{
    public static RawResponse Parse(string text)
    {
        var split = text.IndexOf("\r\n\r\n", StringComparison.Ordinal);
        Assert.True(split > 0, "No HTTP response was written.");
        var head = text[..split];
        var status = int.Parse(head.Split(' ')[1]);
        return new RawResponse(status, head, text[(split + 4)..]);
    }

    public string? Header(string name) => Head.Split("\r\n")
        .Where(l => l.StartsWith(name + ":", StringComparison.OrdinalIgnoreCase))
        .Select(l => l[(name.Length + 1)..].Trim())
        .FirstOrDefault();
}

/// <summary>
/// A connection stand-in: reads the request from a buffer, captures what is written back. Like a live socket, a read
/// past the request waits; it returns end-of-stream only once <see cref="CloseFromClient"/> is called.
/// </summary>
internal sealed class DuplexStream : Stream
{
    private readonly MemoryStream _input;
    private readonly TaskCompletionSource _closed = new(TaskCreationOptions.RunContinuationsAsynchronously);

    public DuplexStream(byte[] input) => _input = new MemoryStream(input);

    public MemoryStream Output { get; } = new();

    /// <summary>The client hangs up.</summary>
    public void CloseFromClient() => _closed.TrySetResult();

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _input.ReadAsync(buffer, cancellationToken);
        if (read > 0) return read;
        await _closed.Task.WaitAsync(cancellationToken);
        return 0;
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => true;
    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => _input.Read(buffer, offset, count);

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => Output.Write(buffer, offset, count);
}

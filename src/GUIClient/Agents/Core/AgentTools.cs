using System;
using System.Collections.Generic;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Model.Authentication;

namespace GUIClient.Agents;

/// <summary>
/// A business tool (M62+): a thin wrapper over a ClientServices call. The dispatcher runs one only through its single
/// gate — a valid agent key, a lease that allows the tool, then the signed-in user holding <see cref="Permission"/> —
/// and never hands it the key. What it returns is tagged as untrusted data and scrubbed of vault references by the
/// dispatcher (<see cref="AgentUntrustedData"/>), so no tool can skip either.
/// </summary>
public interface IAgentTool
{
    string Name { get; }

    string Description { get; }

    /// <summary>True for a tool that changes data: refused under a read lease. Every M62 tool reads.</summary>
    bool RequiresEdit { get; }

    /// <summary>
    /// What the signed-in user must hold for the tool to be listed and to run (T350). Defence in depth only: the API
    /// decides every call the tool makes.
    /// </summary>
    AgentToolPermission Permission { get; }

    /// <summary>
    /// The declared shape of a successful result — an allowlist DTO the result is serialized through, so a field the
    /// type does not name cannot reach the agent. <c>AgentToolResultShapeTest</c> scans it for credential-like members.
    /// </summary>
    Type ResultType { get; }

    /// <summary>The tool's own arguments. The dispatcher adds <c>agent_key</c>.</summary>
    JsonObject InputSchema();

    /// <summary>
    /// Runs the tool; returns the structured result the dispatcher wraps as the MCP tool result. A failure is thrown:
    /// <see cref="AgentToolException"/> for one the tool names, anything else (an API refusal through ClientServices)
    /// is mapped by <see cref="AgentToolErrors"/> to a clean error with no data.
    /// </summary>
    Task<JsonObject> InvokeAsync(AgentToolCall call, JsonObject arguments, CancellationToken cancellationToken);
}

/// <summary>A set of tools composed outside the catalog (a module that alone may name its contracts) and handed to it.</summary>
public interface IAgentToolSource
{
    IEnumerable<IAgentTool> Tools { get; }
}

/// <summary>
/// Who is calling, under which lease, for which signed-in user. Carries no key and no session token. A tool records in
/// <see cref="Withheld"/> what it left out (<see cref="AgentDataPolicy"/>); the dispatcher reports the counts.
/// </summary>
public sealed record AgentToolCall(AgentRegistration Agent, AgentLease Lease, AuthenticatedUserInfo? User = null)
{
    private AgentClassificationScope? _classification;

    public AgentWithholding Withheld { get; } = new();

    /// <summary>
    /// Where the data classification comes from and the policy (T362). The dispatcher sets the runtime's; a call built
    /// without one gets <see cref="AgentClassificationOptions.Unconfigured"/>, which withholds every classified link.
    /// </summary>
    public AgentClassificationOptions ClassificationOptions { get; init; } = AgentClassificationOptions.Unconfigured;

    /// <summary>This call's classification view: loaded lazily, once, and counting into <see cref="Withheld"/>.</summary>
    public AgentClassificationScope Classification =>
        LazyInitializer.EnsureInitialized(ref _classification, () => new AgentClassificationScope(ClassificationOptions, Withheld));
}

using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;

namespace GUIClient.Agents;

/// <summary>
/// A business tool (M62+): a thin wrapper over a ClientServices call. M61 registers none. The dispatcher runs one only
/// through its single lease gate — a valid agent key, then a lease that allows the tool — and never hands it the key.
/// </summary>
public interface IAgentTool
{
    string Name { get; }

    string Description { get; }

    /// <summary>True for a tool that changes data: refused under a read lease.</summary>
    bool RequiresEdit { get; }

    /// <summary>The tool's own arguments. The dispatcher adds <c>agent_key</c>.</summary>
    JsonObject InputSchema();

    /// <summary>Runs the tool; returns the structured result the dispatcher wraps as the MCP tool result.</summary>
    Task<JsonObject> InvokeAsync(AgentToolCall call, JsonObject arguments, CancellationToken cancellationToken);
}

/// <summary>Who is calling, under which lease. Carries no key.</summary>
public sealed record AgentToolCall(AgentRegistration Agent, AgentLease Lease);

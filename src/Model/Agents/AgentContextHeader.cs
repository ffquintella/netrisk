using System;
using System.Linq;

namespace Model.Agents;

/// <summary>
/// The agent-context header the desktop client sends on the API calls an agent tool makes (T358, S55):
/// <c>X-NetRisk-Agent: &lt;registration id&gt;</c>. Informational only: the API records it next to the authenticated
/// user and nothing else reads it, so a forged value can never change who the caller is or what they may do
/// (<c>API.Tests/Security/AgentContextHeaderTest</c>). Shared by the client that sends it and the server that records it.
/// </summary>
public static class AgentContextHeader
{
    public const string Name = "X-NetRisk-Agent";

    /// <summary>
    /// A registration id is exactly what the desktop issues: <c>RandomGenerator.RandomToken(16)</c>, 16 random bytes as
    /// unpadded base64url — 22 characters. Any other length is not an id and is ignored.
    /// </summary>
    public const int Length = 22;

    /// <summary>The registration id in <paramref name="value"/>, or null. Anything else is ignored, never cleaned.</summary>
    public static string? Parse(string? value)
    {
        if (value is null || value.Length != Length) return null;
        return value.All(IsTokenChar) ? value : null;
    }

    public static bool IsValid(string? value) => Parse(value) is not null;

    private static bool IsTokenChar(char c) =>
        c is (>= 'A' and <= 'Z') or (>= 'a' and <= 'z') or (>= '0' and <= '9') or '-' or '_';

    /// <summary>
    /// The audit actor of a write made through an agent: <c>"&lt;login&gt; via agent &lt;id&gt;"</c>, at most
    /// <paramref name="maxLength"/> characters. The login is always kept whole (cut only if it alone exceeds the column);
    /// the agent annotation is what gets shortened. Without a valid agent id, the login.
    /// </summary>
    public static string Actor(string login, string? agentHeader, int maxLength = 64)
    {
        ArgumentNullException.ThrowIfNull(login);
        if (login.Length >= maxLength) return login[..maxLength];
        var agent = Parse(agentHeader);
        if (agent is null) return login;

        var actor = login + " via agent " + agent;
        return actor.Length <= maxLength ? actor : actor[..maxLength];
    }
}

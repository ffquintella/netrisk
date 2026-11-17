using System;
using System.Globalization;
using System.Text;

namespace GUIClient.Agents;

/// <summary>
/// An approved agent, as it is persisted (T347). There is deliberately no property that could hold the key: the only
/// trace of it is <see cref="KeyHash"/>.
/// </summary>
public sealed class AgentRegistration
{
    /// <summary>Random, URL-safe identifier. Also what the audit trail names the agent by.</summary>
    public string Id { get; init; } = "";

    /// <summary>The NetRisk user who approved it. A registration authenticates only while that user is signed in.</summary>
    public int UserId { get; init; }

    public string Name { get; set; } = "";
    public string Description { get; init; } = "";
    public string ClientName { get; init; } = "";
    public string ClientVersion { get; init; } = "";

    /// <summary><see cref="AgentKeyIssuer.Hash"/> of the key issued on approval.</summary>
    public string KeyHash { get; init; } = "";

    /// <summary>The fingerprint the user approved; shown again in the Agents window.</summary>
    public string Fingerprint { get; init; } = "";

    public DateTimeOffset CreatedAt { get; init; }
    public DateTimeOffset? LastUsedAt { get; set; }

    internal AgentRegistration Copy() => new()
    {
        Id = Id,
        UserId = UserId,
        Name = Name,
        Description = Description,
        ClientName = ClientName,
        ClientVersion = ClientVersion,
        KeyHash = KeyHash,
        Fingerprint = Fingerprint,
        CreatedAt = CreatedAt,
        LastUsedAt = LastUsedAt
    };
}

/// <summary>What the MCP client said about itself in <c>initialize</c>. Self-reported, so display-only.</summary>
public sealed record AgentClientInfo(string Name, string Version)
{
    public static readonly AgentClientInfo Unknown = new("", "");
}

/// <summary>
/// Bounds and cleans every piece of agent-supplied text before it reaches a dialog or the store (NRM §5.3.5: a maximum
/// size on every input). Control, format and separator characters are removed rather than escaped: a right-to-left
/// override in an agent's name is how "evil" is made to read as "live" in an approval dialog.
/// </summary>
public static class AgentText
{
    public const int MaxNameLength = 64;
    public const int MaxDescriptionLength = 500;
    public const int MaxClientNameLength = 64;
    public const int MaxClientVersionLength = 32;
    public const int MaxReasonLength = 500;
    public const int MaxIdLength = 64;

    /// <summary>
    /// Cleans <paramref name="value"/> and checks it against <paramref name="maxLength"/>. Over-long input is refused,
    /// not truncated, so the user never approves a name the agent did not send.
    /// </summary>
    public static bool TryClean(string? value, int maxLength, bool required, out string cleaned)
    {
        cleaned = "";
        if (value is null) return !required;

        // A first, generous bound before any work is done on the string.
        if (value.Length > maxLength * 4) return false;

        var builder = new StringBuilder(value.Length);
        var lastWasSpace = false;
        foreach (var ch in value)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(ch);
            var drop = category is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned;
            var space = drop || char.IsWhiteSpace(ch);

            if (space)
            {
                if (!lastWasSpace && builder.Length > 0) builder.Append(' ');
                lastWasSpace = true;
                continue;
            }

            builder.Append(ch);
            lastWasSpace = false;
        }

        cleaned = builder.ToString().Trim();
        if (cleaned.Length > maxLength) return false;
        return !required || cleaned.Length > 0;
    }

    /// <summary>Like <see cref="TryClean"/> for optional display text: anything unusable becomes empty.</summary>
    public static string CleanOrEmpty(string? value, int maxLength) =>
        TryClean(value, maxLength, required: false, out var cleaned) ? cleaned : "";
}

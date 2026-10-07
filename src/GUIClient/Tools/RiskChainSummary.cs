using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using Model.Authentication;
using Model.Risks.Chain;

namespace GUIClient.Tools;

/// <summary>
/// The pure half of the linkage-chain screens (Stage 9.1, S41 §7): how a level of a risk's chain
/// reads in the risk detail, how a link reads in the edit dialog's grid, and how the coverage report
/// states its headline.
///
/// No Avalonia types and nothing from <c>Tools</c>, so <c>GUIClient.Tests</c> compiles this file
/// directly — that project deliberately does not reference <c>GUIClient</c>.
/// </summary>
public static class RiskChainSummary
{
    /// <summary>
    /// One level of a risk's chain as a single line: the targets' names joined in the order the server
    /// sent them, a redacted host as <paramref name="hostRedacted"/>, and an empty level as
    /// <paramref name="notInformed"/> — a missing link is shown, never left blank.
    /// </summary>
    public static string Describe(IEnumerable<RiskChainLinkDto>? links, string notInformed, string hostRedacted)
    {
        var names = (links ?? []).Select(link => TargetName(link, hostRedacted)).ToList();
        return names.Count == 0 ? notInformed : string.Join(", ", names);
    }

    /// <summary><see cref="Describe"/> for one <paramref name="level"/> of <paramref name="chain"/>.</summary>
    public static string ForLevel(RiskChainDto? chain, RiskChainLevel level, string notInformed, string hostRedacted) =>
        Describe(chain?.Levels.FirstOrDefault(l => l.Level == level)?.Links, notInformed, hostRedacted);

    /// <summary>
    /// What a link's target is called on screen. A redacted host has no name to show — the server
    /// withheld it — so it reads as <paramref name="hostRedacted"/>; a target whose name property is
    /// empty falls back to its id rather than to nothing.
    /// </summary>
    public static string TargetName(RiskChainLinkDto link, string hostRedacted)
    {
        if (link.IsRedacted) return hostRedacted;
        if (!string.IsNullOrWhiteSpace(link.TargetName)) return link.TargetName!;

        var id = link.EntityId ?? link.HostId;
        return id is null ? string.Empty : "#" + id.Value.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The coverage report's headline: "<paramref name="summaryFormat"/>" filled with covered, total
    /// and the percentage — or <paramref name="notComputable"/> when there is no critical process,
    /// which is neither 0 % nor 100 % (S41 §11, D7).
    /// </summary>
    /// <param name="summaryFormat">A composite format with <c>{0}</c> covered, <c>{1}</c> critical processes
    /// and <c>{2}</c> the percentage.</param>
    public static string Coverage(CriticalProcessCoverageDto? coverage, string summaryFormat, string notComputable,
        CultureInfo? culture = null)
    {
        if (coverage?.CoverageRatio is not { } ratio) return notComputable;

        var format = culture ?? CultureInfo.CurrentCulture;
        var percent = (ratio * 100m).ToString("0.#", format);

        return string.Format(format, summaryFormat, coverage.CoveredCount, coverage.CriticalProcessCount, percent);
    }
}

/// <summary>
/// Who may do what on the chain screens, from the signed-in user's info — the client-side mirror of
/// the server's rules, used only to enable buttons. The server re-checks every one of them.
/// </summary>
public static class RiskChainAccess
{
    /// <summary>
    /// The audience of the <c>RequireRiskmanagement</c> policy the chain endpoints carry: the
    /// <c>riskmanagement</c> permission or the <c>Administrator</c> role. Not <c>IsAdmin</c> — the server
    /// policy does not accept the <c>Admin</c> role, and a button that opens a dialog every call of which
    /// answers 403 is worse than a disabled one.
    /// </summary>
    public static bool CanEditChain(AuthenticatedUserInfo? user) =>
        user is not null
        && ((user.UserPermissions?.Contains("riskmanagement") ?? false)
            || string.Equals(user.UserRole, "Administrator", StringComparison.Ordinal));

    /// <summary>Host targets also need <c>hosts</c>: an administrator, or the permission (S41 §6).</summary>
    public static bool CanReadHosts(AuthenticatedUserInfo? user) =>
        user is not null && (user.IsAdmin || (user.UserPermissions?.Contains("hosts") ?? false));

    /// <summary>
    /// A link can be removed from the chain dialog when it is Declared — a Legacy link is removed from
    /// the risk's Entity field, where it was set — and, for a host link, only by someone who may read
    /// hosts.
    /// </summary>
    public static bool CanRemove(RiskChainLinkDto? link, bool canReadHosts) =>
        link is not null
        && link.Origin == RiskChainLinkOrigin.Declared
        && ((link.HostId is null && !link.IsRedacted) || canReadHosts);
}

/// <summary>One row of the edit dialog's link grid, already in the words the user reads.</summary>
public sealed class RiskChainLinkRow
{
    public RiskChainLinkRow(RiskChainLinkDto link, string levelName, string targetName, string originName)
    {
        Link = link;
        LevelName = levelName;
        TargetName = targetName;
        OriginName = originName;
    }

    public RiskChainLinkDto Link { get; }

    public string LevelName { get; }

    public string TargetName { get; }

    public string OriginName { get; }

    public DateTime LinkedAt => Link.CreatedAt;

    public bool IsLegacy => Link.Origin == RiskChainLinkOrigin.Legacy;

    /// <summary>
    /// Builds the rows of a chain in the order the levels go — objective first — and, within a level,
    /// the order the server sent them.
    /// </summary>
    public static List<RiskChainLinkRow> From(RiskChainDto? chain, Func<RiskChainLevel, string> levelName,
        string hostRedacted, string declared, string legacy) =>
        (chain?.Levels ?? [])
        .OrderBy(l => (int)l.Level)
        .SelectMany(l => l.Links)
        .Select(link => new RiskChainLinkRow(link, levelName(link.Level),
            RiskChainSummary.TargetName(link, hostRedacted),
            link.Origin == RiskChainLinkOrigin.Legacy ? legacy : declared))
        .ToList();
}

/// <summary>One critical process in the coverage report, with "covered" as text rather than colour.</summary>
public sealed class CriticalProcessCoverageRow
{
    public CriticalProcessCoverageRow(CriticalProcessCoverageRowDto row, string yes, string no,
        string? criticalitySource = null)
    {
        CriticalitySourceText = criticalitySource ?? string.Empty;
        ProcessId = row.ProcessId;
        ProcessName = row.ProcessName;
        Criticality = row.Criticality;
        DirectOpenRiskCount = row.DirectOpenRiskCount;
        InferredOpenRiskCount = row.InferredOpenRiskCount;
        Covered = row.Covered;
        CoveredText = row.Covered ? yes : no;
    }

    public int ProcessId { get; }
    public string ProcessName { get; }
    public int Criticality { get; }
    public int DirectOpenRiskCount { get; }
    public int InferredOpenRiskCount { get; }
    public bool Covered { get; }
    public string CoveredText { get; }

    /// <summary>Where the criticality comes from (Stage 9.3): "BIA" or "declared, not BIA".</summary>
    public string CriticalitySourceText { get; }
}

/// <summary>One risk of the process selected in the coverage report, with the node it came through.</summary>
public sealed class RiskChainMatchRow
{
    public RiskChainMatchRow(RiskChainMatchDto match)
    {
        RiskId = match.RiskId;
        Subject = match.Subject;
        Status = match.Status;
        Via = match.Inferred ? match.ViaEntityName ?? (match.ViaEntityId is { } id ? "#" + id : string.Empty)
                             : string.Empty;
    }

    public int RiskId { get; }
    public string Subject { get; }
    public string Status { get; }

    /// <summary>Empty for a direct link; the name of the node below for an inferred one.</summary>
    public string Via { get; }
}

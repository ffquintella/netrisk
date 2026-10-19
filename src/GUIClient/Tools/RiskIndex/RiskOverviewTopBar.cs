using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>One segment of the scope breadcrumb (§5.1): the last one is the current node, shown as text.</summary>
public sealed record BreadcrumbSegment(string Key, string Label, bool IsCurrent);

/// <summary>The module's states before there is a value to show (§5.13).</summary>
public enum RiskOverviewEmptyState
{
    None = 0,

    /// <summary>The publication is <c>shadow</c> and the caller is neither on the committee nor an administrator.</summary>
    Shadow = 1,

    /// <summary>No profile was ever approved: the index is not computed.</summary>
    NoApprovedProfile = 2,

    /// <summary>A profile is approved but the 05:00 UTC job has not written a snapshot yet.</summary>
    AwaitingFirstSnapshot = 3
}

/// <summary>The profile in force was retired with no successor (§5.13, §6.4): the last snapshot shows with this banner.</summary>
public sealed record SuspendedPublication(DateTime? RetiredAtUtc, int Version);

/// <summary>The top bar's pure rules (S40 §5.1, §5.13): chips, breadcrumb, the empty states and the date picker's blackout days.</summary>
public static class RiskOverviewTopBar
{
    public static string ProvenanceKey(RiskIndexProvenance provenance) => "RiskProvenance_" + provenance;

    /// <summary>Partial and preview chips carry the warning class (§5.1); official and derived do not.</summary>
    public static bool ProvenanceIsWarning(RiskIndexProvenance provenance) =>
        provenance is RiskIndexProvenance.PartialRestricted or RiskIndexProvenance.UnofficialPreview;

    public static string ModeKey(RiskIndexMode mode) => "RiskMode_" + mode;

    public static string ModeHintKey(RiskIndexMode mode) => "RiskMode_" + mode + "Hint";

    public static string ProfileStatusKey(RiskIndexProfileStatus status) => "RiskProfileStatus_" + status;

    /// <summary>"Perfil v3 · Equilibrado": the version and the profile's name, or its preset when it has none.</summary>
    public static string ProfileChip(RiskIndexProfileSummaryDto profile, TextLookup text, CultureInfo culture)
    {
        var name = !string.IsNullOrWhiteSpace(profile.Name) ? profile.Name : profile.Preset ?? "";
        return text("RiskOverviewProfileChipFMT") is { } template
            ? string.Format(culture, template, profile.Version, name)
            : "v" + profile.Version.ToString(culture) + " · " + name;
    }

    /// <summary>The shadow-pilot chip shows whenever the publication is still <c>shadow</c> (§5.1).</summary>
    public static bool ShowsPilotChip(RiskIndexPublicationState? publication) => publication == RiskIndexPublicationState.Shadow;

    /// <summary>
    /// The breadcrumb (§5.1): the server's path, then the node itself as the last, non-clickable segment. The path names the
    /// organization only for global scope; a restricted scope with several entities is headed by "Meu escopo"
    /// (<paramref name="myScopeRoot"/>). "org" and "scope" read in the interface's language.
    /// </summary>
    public static IReadOnlyList<BreadcrumbSegment> Breadcrumb(RiskIndexOverviewDto overview, string organizationLabel,
        string myScopeLabel, bool myScopeRoot = false)
    {
        string Label(RiskIndexNodeRef node) => node.Key switch
        {
            RiskIndexNodeKey.Organization => organizationLabel,
            RiskIndexNodeKey.Scope => myScopeLabel,
            _ => node.Label
        };

        var segments = overview.Breadcrumb.Where(n => !string.Equals(n.Key, overview.Node.Key, StringComparison.Ordinal))
            .Select(n => new BreadcrumbSegment(n.Key, Label(n), false)).ToList();
        if (myScopeRoot && overview.Node.Key != RiskIndexNodeKey.Scope &&
            segments.All(s => s.Key is not (RiskIndexNodeKey.Organization or RiskIndexNodeKey.Scope)))
            segments.Insert(0, new BreadcrumbSegment(RiskIndexNodeKey.Scope, myScopeLabel, false));
        segments.Add(new BreadcrumbSegment(overview.Node.Key, Label(overview.Node), true));
        return segments;
    }

    /// <summary>With no snapshot (204): awaiting the first one when a profile is in force, otherwise none was approved.</summary>
    public static RiskOverviewEmptyState WithoutSnapshot(IReadOnlyList<RiskIndexProfileSummaryDto> profiles, DateTime nowUtc) =>
        profiles.Any(p => IsInForce(p, nowUtc))
            ? RiskOverviewEmptyState.AwaitingFirstSnapshot
            : RiskOverviewEmptyState.NoApprovedProfile;

    /// <summary>
    /// "Publicação suspensa em dd/mm (perfil vN aposentado)" (§5.13): the snapshot's profile is retired and no profile is in
    /// force. A retired profile behind a historical date, with a successor in force, is not a suspension.
    /// </summary>
    public static SuspendedPublication? Suspended(RiskIndexProfileSummaryDto snapshotProfile,
        IReadOnlyList<RiskIndexProfileSummaryDto> profiles, DateTime nowUtc) =>
        snapshotProfile.Status == RiskIndexProfileStatus.Retired && !profiles.Any(p => IsInForce(p, nowUtc))
            ? new SuspendedPublication(snapshotProfile.RetiredAt, snapshotProfile.Version)
            : null;

    private static bool IsInForce(RiskIndexProfileSummaryDto profile, DateTime nowUtc) =>
        profile.Status == RiskIndexProfileStatus.Active && (profile.RetiredAt is null || profile.RetiredAt > nowUtc);

    /// <summary>A date earlier than the latest snapshot: the "Histórico: dd/mm" chip and "Voltar ao último" (§5.1).</summary>
    public static bool IsHistory(DateOnly? selected, DateOnly? latest) => selected is { } s && latest is { } l && s < l;

    /// <summary>
    /// The date picker's blackout days (§5.1): every day between the first and the last snapshot that has none, as inclusive
    /// ranges. Days outside the range are closed by <c>DisplayDateStart</c>/<c>DisplayDateEnd</c>.
    /// </summary>
    public static IReadOnlyList<(DateOnly From, DateOnly To)> BlackoutRanges(IReadOnlyList<DateOnly> snapshotDates)
    {
        var days = snapshotDates.Distinct().Order().ToList();
        var gaps = new List<(DateOnly, DateOnly)>();
        for (var i = 1; i < days.Count; i++)
            if (days[i].DayNumber - days[i - 1].DayNumber > 1)
                gaps.Add((days[i - 1].AddDays(1), days[i].AddDays(-1)));
        return gaps;
    }

    /// <summary>
    /// Which cause the partial banner's hint names (§5.6.5): a process or application reached only through an EAV link when
    /// the server marks it (<c>closure_through_eav</c>); otherwise both possible causes, since nothing names the other one.
    /// </summary>
    public static string PartialCauseKey(IReadOnlyList<RiskIndexChipDto> chips) =>
        chips.Any(c => c.Key == "closure_through_eav") ? "RiskState_PartialCauseEav" : "RiskState_PartialCauseEither";
}

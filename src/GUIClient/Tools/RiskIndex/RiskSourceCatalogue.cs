using System;
using System.Collections.Generic;
using System.Linq;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>Availability of a source in the installed version (S39 §3.2; S40 §6.5): [H] usable, [C] with a known defect, [N] not built.</summary>
public enum RiskSourceAvailability
{
    Available = 1,
    Caveat = 2,
    NotAvailable = 3
}

/// <summary>A row of the source catalogue: what it feeds, its availability and the task or defect that qualifies it.</summary>
public sealed record RiskSourceInfo(string Id, string Feeds, RiskSourceAvailability Availability, string? Qualifier,
    bool HasFreshnessWindow);

/// <summary>One line of the Sources and freshness tab: the active profile's and the draft's settings side by side.</summary>
public sealed record RiskSourceLine(RiskSourceInfo Source, bool EnabledActive, bool EnabledDraft, int? WindowActive,
    int? WindowDraft);

/// <summary>
/// The S39 §3.2 catalogue as this version ships it, and the draft edits of the Sources and freshness tab (S40 §6.5): an [N]
/// source cannot be switched on ("Requer ‹tarefa›"), a window is edited only for a source that has one, and every edit is a
/// change of the one draft session the Draft tab also edits.
/// </summary>
public static class RiskSourceCatalogue
{
    public static readonly IReadOnlyList<RiskSourceInfo> All =
    [
        new(RiskIndexSources.RegResidual, "REG.R", RiskSourceAvailability.Available, null, false),
        new(RiskIndexSources.RegImpact, "REG · w_r", RiskSourceAvailability.Available, null, false),
        new(RiskIndexSources.RegRank, "REG · m_rank", RiskSourceAvailability.Available, null, false),
        new(RiskIndexSources.V1Device, "EXP.D", RiskSourceAvailability.Available, null, true),
        new(RiskIndexSources.V1Cve, "EXP.V", RiskSourceAvailability.Available, null, true),
        new(RiskIndexSources.V1InternetFacing, "EXP.X", RiskSourceAvailability.Available, null, true),
        new(RiskIndexSources.V1Accounts, "EXP.I", RiskSourceAvailability.Available, null, true),
        new(RiskIndexSources.V1Cloud, "EXP.N", RiskSourceAvailability.Available, null, true),
        new(RiskIndexSources.V1LocalApps, "EXP.N", RiskSourceAvailability.Available, null, true),
        new(RiskIndexSources.V1SecurityConfiguration, "CTL.C", RiskSourceAvailability.Available, null, true),
        new(RiskIndexSources.SscTotal, "EXP.X", RiskSourceAvailability.Available, null, true),
        new(RiskIndexSources.TenableNessus, "EXP.V", RiskSourceAvailability.Available, null, true),
        new(RiskIndexSources.TenableVm, "EXP.V", RiskSourceAvailability.Available, null, true),
        new(RiskIndexSources.TenableAes, "EXP.D", RiskSourceAvailability.Available, null, true),
        new(RiskIndexSources.TenableAcr, "crit", RiskSourceAvailability.Available, null, true),
        new(RiskIndexSources.Importers, "EXP.V", RiskSourceAvailability.Available, null, true),
        new(RiskIndexSources.Incidents, "AME.I", RiskSourceAvailability.Available, null, false),
        new(RiskIndexSources.NearMisses, "AME.Q", RiskSourceAvailability.NotAvailable, "M40", false),
        new(RiskIndexSources.IncidentResponsePlans, "CTL.P", RiskSourceAvailability.Available, null, false),
        new(RiskIndexSources.Assessments, "CTL.A", RiskSourceAvailability.Caveat, "D-16", true),
        new(RiskIndexSources.CmdbCriticality, "crit", RiskSourceAvailability.Caveat, "D-10", true),
        new(RiskIndexSources.CmdbEnvironment, "crit · env", RiskSourceAvailability.Available, null, true),
        new(RiskIndexSources.CmdbInternetFacing, "m_net", RiskSourceAvailability.NotAvailable, "T291", true),
        new(RiskIndexSources.CmdbRelations, "closure", RiskSourceAvailability.NotAvailable, "T291", true),
        new(RiskIndexSources.EntityCriticality, "crit", RiskSourceAvailability.Available, null, false),
        new(RiskIndexSources.EntityClassification, "crit · Δ", RiskSourceAvailability.Available, null, false),
        new(RiskIndexSources.EntityInternetFacing, "m_net", RiskSourceAvailability.Available, null, false)
    ];

    public static RiskSourceInfo? Find(string id) => All.FirstOrDefault(s => s.Id == id);

    /// <summary>The tab's lines: the catalogue read against the active profile (may be absent) and the draft.</summary>
    public static IReadOnlyList<RiskSourceLine> Lines(RiskIndexParameters? active, RiskIndexParameters draft) =>
        All.Select(s => new RiskSourceLine(s,
            active?.EnabledSources.Contains(s.Id) ?? false,
            draft.EnabledSources.Contains(s.Id),
            active is not null && active.FreshnessDays.TryGetValue(s.Id, out var a) ? a : null,
            draft.FreshnessDays.TryGetValue(s.Id, out var d) ? d : null)).ToList();

    /// <summary>
    /// Whether the user may switch the source; a [N] source that is already on may still be switched off, since leaving a
    /// source enabled that the version cannot read must never be the only way out.
    /// </summary>
    public static Gate Toggle(RiskSourceInfo source, bool enabledNow, bool canEdit) =>
        !canEdit ? Gate.Closed(RiskIndexAdminAccess.RequiresConfigure)
        : source.Availability == RiskSourceAvailability.NotAvailable && !enabledNow ? Gate.Closed("RiskSourceRequiresTask")
        : Gate.Open;

    public static RiskIndexParameters WithEnabled(RiskIndexParameters draft, string sourceId, bool enabled)
    {
        var list = draft.EnabledSources.Where(s => s != sourceId).ToList();
        if (enabled) list.Add(sourceId);
        // Kept in catalogue order, so the same set never reads as a change just because it was toggled in another order.
        var order = All.Select(s => s.Id).ToList();
        list = list.OrderBy(s => order.IndexOf(s) is var i && i < 0 ? int.MaxValue : i).ThenBy(s => s, StringComparer.Ordinal).ToList();
        return draft with { EnabledSources = list };
    }

    public static RiskIndexParameters WithWindow(RiskIndexParameters draft, string sourceId, int days)
    {
        var windows = new Dictionary<string, int>(draft.FreshnessDays, StringComparer.Ordinal) { [sourceId] = days };
        return draft with { FreshnessDays = windows };
    }
}

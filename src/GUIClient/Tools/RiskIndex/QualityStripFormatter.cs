using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// One data-quality item (S40 §5.12): its label and value as text, ⚠ past its threshold, "—" with a hint when not measured
/// at this node, and where its "›" leads — the object list filtered by <see cref="ObjectFilter"/>, or the node <see cref="NodeKey"/>.
/// </summary>
public sealed record QualityItem(string Key, string Label, string Value, bool IsWarning, bool IsUnavailable, string? Hint,
    string? ObjectFilter = null, string? NodeKey = null, string Threshold = "")
{
    public bool CanOpen => NodeKey is not null || ObjectListQuery.IsValidPreset(ObjectFilter);
}

/// <summary>
/// The data-quality strip and its detail (S40 §5.12.1–§5.12.2, S39 §8.5–§8.6; T256). Pure: the same items in the same
/// order whatever the values — a zero stays "0", an unmeasured count reads "—" with why — so nothing disappears silently.
/// </summary>
public static class QualityStripFormatter
{
    private const string Dash = "—";

    public const string Unassigned = "unassigned";
    public const string DefaultCriticality = "default_criticality";
    public const string StaleSource = "stale_source";
    public const string StaleSourcesNone = "stale_sources_none";
    public const string PresumedCritical = "presumed_critical";
    public const string DivergentLinks = "divergent_links";
    public const string OrphanApplications = "orphan_applications";
    public const string SscPseudoHosts = "ssc_pseudo_hosts";
    public const string ExitedBadReading = "exited_bad_reading";
    public const string FindingStatusDisagreements = "finding_status_disagreements";
    public const string ResidualAboveInherent = "residual_above_inherent";
    public const string EvidenceConfidence = "evidence_confidence";

    /// <summary>The strip's items (§5.12.1), in the spec's order; one per stale source, or a single "0" when none.</summary>
    public static IReadOnlyList<QualityItem> Strip(RiskIndexQualityDto quality, bool isOrganization, TextLookup text,
        CultureInfo culture)
    {
        var items = new List<QualityItem>
        {
            UnassignedItem(quality, isOrganization, "RiskQualityStrip_", null, text, culture),
            DefaultCriticalityItem(quality, "RiskQualityStrip_", text, culture)
        };
        items.AddRange(StaleSourceItems(quality, "RiskQualityStrip_", text, culture));
        items.Add(Counted(PresumedCritical, "RiskQualityStrip_", quality.PresumedCritical,
            RiskIndexQualityFilters.Filter(RiskIndexQualityFilters.PresumedCritical), text, culture));
        items.Add(RootOnly(OrphanApplications, "RiskQualityStrip_", quality.OrphanApplications, text, culture));
        items.Add(NotStored(SscPseudoHosts, "RiskQualityStrip_", quality.SscPseudoHosts, text, culture));
        items.Add(NotStored(ExitedBadReading, "RiskQualityStrip_", quality.ExitedWithBadReading, text, culture));
        return items;
    }

    /// <summary>
    /// The detail's rows (§5.12.2): every strip item plus divergent links, legacy-status disagreements, residual above
    /// inherent and the M40 evidence confidence — never mixed into quality. The threshold column shows the Unassigned limit.
    /// Residual above inherent lists scenarios by register data: its "›" only with <paramref name="mayReadRegister"/> (§11.3).
    /// </summary>
    public static IReadOnlyList<QualityItem> Panel(RiskIndexQualityDto quality, bool isOrganization, double? unassignedThreshold,
        TextLookup text, CultureInfo culture, bool mayReadRegister = true)
    {
        var items = new List<QualityItem>
        {
            UnassignedItem(quality, isOrganization, "RiskQualityPanel_", unassignedThreshold, text, culture),
            DefaultCriticalityItem(quality, "RiskQualityPanel_", text, culture)
        };
        items.AddRange(StaleSourceItems(quality, "RiskQualityPanel_", text, culture));
        items.Add(Counted(PresumedCritical, "RiskQualityPanel_", quality.PresumedCritical,
            RiskIndexQualityFilters.Filter(RiskIndexQualityFilters.PresumedCritical), text, culture));
        items.Add(RootOnly(DivergentLinks, "RiskQualityPanel_", quality.DivergentLinks, text, culture));
        items.Add(RootOnly(OrphanApplications, "RiskQualityPanel_", quality.OrphanApplications, text, culture));
        items.Add(NotStored(SscPseudoHosts, "RiskQualityPanel_", quality.SscPseudoHosts, text, culture));
        items.Add(NotStored(ExitedBadReading, "RiskQualityPanel_", quality.ExitedWithBadReading, text, culture));
        items.Add(RootOnly(FindingStatusDisagreements, "RiskQualityPanel_", quality.FindingStatusDisagreements, text, culture));
        items.Add(Counted(ResidualAboveInherent, "RiskQualityPanel_", quality.ResidualAboveInherent,
            mayReadRegister ? RiskIndexQualityFilters.Filter(RiskIndexQualityFilters.ResidualAboveInherent) : null, text, culture));
        items.Add(new QualityItem(EvidenceConfidence, RiskText.Get(text, "RiskQualityPanel_" + EvidenceConfidence),
            RiskText.Get(text, "RiskQuality_Evidence_" + quality.EvidenceConfidenceStateKey), false, true, null));
        return items;
    }

    /// <summary>
    /// "Não atribuído 12%" with ⚠ above <c>limiar_nao_atribuido</c>. Its "›" opens the Unassigned node — only from the
    /// organization, where those objects are, and only when there are any.
    /// </summary>
    private static QualityItem UnassignedItem(RiskIndexQualityDto quality, bool isOrganization, string prefix,
        double? threshold, TextLookup text, CultureInfo culture) =>
        new(Unassigned, RiskText.Get(text, prefix + Unassigned), Percent(quality.UnassignedWeightFraction, culture),
            quality.UnassignedWarning, false, quality.UnassignedWarning ? RiskText.Get(text, "RiskQuality_AboveThreshold") : null,
            NodeKey: isOrganization && quality.UnassignedWeightFraction > 0 ? RiskIndexNodeKey.UnassignedTerm : null,
            Threshold: threshold is { } t ? Percent(t, culture) : "");

    private static QualityItem DefaultCriticalityItem(RiskIndexQualityDto quality, string prefix, TextLookup text,
        CultureInfo culture) =>
        new(DefaultCriticality, RiskText.Get(text, prefix + DefaultCriticality), Percent(quality.DefaultCriticalityWeightFraction, culture),
            false, false, null, RiskIndexQualityFilters.Filter(RiskIndexQualityFilters.DefaultCriticality));

    /// <summary>"Fonte obsoleta: Vision One — dispositivos, 27/09/2026" per source, each opening the objects it reads.</summary>
    private static IEnumerable<QualityItem> StaleSourceItems(RiskIndexQualityDto quality, string prefix, TextLookup text,
        CultureInfo culture)
    {
        if (quality.StaleSources.Count == 0)
        {
            yield return new QualityItem(StaleSourcesNone, RiskText.Get(text, prefix + StaleSourcesNone), 0.ToString(culture), false,
                false, null);
            yield break;
        }

        foreach (var source in quality.StaleSources)
        {
            var date = source.LastReadingUtc is { } at ? at.ToLocalTime().ToString("d", culture) : Dash;
            yield return new QualityItem(StaleSource,
                RiskText.Format(text, prefix + StaleSource + "FMT", culture, NodeKeyDisplay.SourceLabel(source.SourceKey, text), date),
                // The strip's chip names the source and date only (§5.12.1); the detail counts it in its value column.
                prefix == "RiskQualityPanel_" ? RiskText.Get(text, "RiskQualityPanel_OneSource") : "", true, false, null,
                "source=" + source.SourceKey);
        }
    }

    private static QualityItem Counted(string key, string prefix, int count, string? filter, TextLookup text, CultureInfo culture) =>
        new(key, RiskText.Get(text, prefix + key), count.ToString("N0", culture), false, false, null, filter);

    /// <summary>A count of the organization's tree, stored on its headline: "—" elsewhere, with why.</summary>
    private static QualityItem RootOnly(string key, string prefix, int? count, TextLookup text, CultureInfo culture) =>
        count is { } value
            ? new QualityItem(key, RiskText.Get(text, prefix + key), value.ToString("N0", culture), false, false, null)
            : new QualityItem(key, RiskText.Get(text, prefix + key), Dash, false, true, RiskText.Get(text, "RiskQuality_RootOnlyHint"));

    /// <summary>A count the snapshot does not store yet (S39 §8.6): "—" and the reason, never a zero it did not measure.</summary>
    private static QualityItem NotStored(string key, string prefix, int? count, TextLookup text, CultureInfo culture) =>
        count is { } value
            ? new QualityItem(key, RiskText.Get(text, prefix + key), value.ToString("N0", culture), false, false, null)
            : new QualityItem(key, RiskText.Get(text, prefix + key), Dash, false, true, RiskText.Get(text, "RiskQuality_NotStoredHint"));

    private static string Percent(double fraction, CultureInfo culture) => HeadlineFormatter.Percent(fraction, culture) + "%";

    private static readonly string[] StripKeys =
        [Unassigned, DefaultCriticality, StaleSourcesNone, PresumedCritical, OrphanApplications, SscPseudoHosts, ExitedBadReading];

    private static readonly string[] PanelKeys =
    [
        Unassigned, DefaultCriticality, StaleSourcesNone, PresumedCritical, DivergentLinks, OrphanApplications, SscPseudoHosts,
        ExitedBadReading, FindingStatusDisagreements, ResidualAboveInherent, EvidenceConfidence
    ];

    /// <summary>The resource keys built at run time, so a test can require them in every language.</summary>
    public static IEnumerable<string> ComputedKeys() =>
        StripKeys.Select(k => "RiskQualityStrip_" + k)
            .Concat(PanelKeys.Select(k => "RiskQualityPanel_" + k))
            .Concat(["RiskQualityStrip_" + StaleSource + "FMT", "RiskQualityPanel_" + StaleSource + "FMT"])
            .Concat(
            [
                "RiskQualityPanel_OneSource", "RiskQuality_AboveThreshold", "RiskQuality_RootOnlyHint", "RiskQuality_NotStoredHint",
                "RiskQuality_Evidence_" + RiskIndexQualityFilters.EvidenceConfidenceRequiresM40
            ]);
}

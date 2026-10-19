using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using Material.Icons;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>A tab header's level (§5.5): the band of the node ∩ class as text and glyph, or why there is none.</summary>
public sealed record AssetTabLevel(string Text, MaterialIconKind Glyph, string? BandClass, bool IsUnavailable);

/// <summary>One segment of the visibility bar: a state, its count (the label carries it) and its glyph (§10.7).</summary>
public sealed record VisibilitySegment(string Kind, int Count, string Label, MaterialIconKind Glyph);

/// <summary>
/// One source's row of the inventory visibility (§5.5.1; T256): its name, and either the bar of its readings — current,
/// stale, expired, none, each counted and with its glyph — or, when the profile does not enable it, the reason.
/// </summary>
public sealed record SourceVisibilityRow(string SourceKey, string Label, bool IsEnabled, string? DisabledText,
    IReadOnlyList<VisibilitySegment> Segments, string? LastReadingText);

/// <summary>
/// The asset-class tabs' pure half (S40 §5.5–§5.5.1; T253): the five tabs in order, their icons and categories, which are
/// enabled for the node shown, the key of node ∩ tab, the header's level and the attack-surface summary of a tab: the
/// per-source rows (<see cref="Sources"/>, T256) and the general bar (<see cref="Overall"/>) below them.
/// </summary>
public static class AssetTabFormatter
{
    public static IReadOnlyList<RiskIndexTabGroup> Tabs => RiskIndexTabMap.All;

    public static MaterialIconKind Icon(RiskIndexTabGroup tab) => tab switch
    {
        RiskIndexTabGroup.Devices => MaterialIconKind.Laptop,
        RiskIndexTabGroup.InternetFacing => MaterialIconKind.Web,
        RiskIndexTabGroup.Identities => MaterialIconKind.AccountKeyOutline,
        RiskIndexTabGroup.Applications => MaterialIconKind.Application,
        _ => MaterialIconKind.CloudOutline
    };

    public static string NameKey(RiskIndexTabGroup tab) => "RiskTab_" + tab;

    /// <summary>"Categorias: EXP, CTL, GOV" — the header's hint (§5.5), in the fixed order.</summary>
    public static string CategoriesHint(RiskIndexTabGroup tab, TextLookup text, CultureInfo culture) =>
        RiskText.Format(text, "RiskTabCategoriesFMT", culture,
            string.Join(", ", RiskIndexTabMap.Categories(tab).Select(HeadlineFormatter.CategoryCode)));

    /// <summary>The tabs whose source does not exist yet (§5.5): the state card names the task that enables them.</summary>
    public static string? KnownUnavailableKey(RiskIndexTabGroup tab) => tab switch
    {
        RiskIndexTabGroup.Identities => "RiskTab_IdentitiesUnavailable",
        RiskIndexTabGroup.Clouds => "RiskTab_CloudsUnavailable",
        _ => null
    };

    private static RiskIndexNodeKey Parse(string? node) =>
        RiskIndexNodeKey.TryParse(node, out var key) ? key : RiskIndexNodeKey.Org;

    /// <summary>
    /// Whether the tab applies to the node shown (§5.5): a node already cut by a class enables only that class's tab; the
    /// others say "O nó atual já é recortado por ‹classe›". Null reason when enabled.
    /// </summary>
    public static string? DisabledReason(string? node, RiskIndexTabGroup tab, TextLookup text, CultureInfo culture)
    {
        var key = Parse(node);
        var cutBy = key.AssetClass is { } c ? RiskIndexTabMap.Of(c) : key.TabGroup;
        if (cutBy is not { } group || group == tab) return null;
        var label = key.AssetClass is { } cls ? RiskIndexNodeKey.ClassTerm(cls) : RiskText.Get(text, NameKey(group));
        return RiskText.Format(text, "RiskTabCutByFMT", culture, label);
    }

    /// <summary>The key of node ∩ tab; a node already cut by a class of the tab is its own intersection.</summary>
    public static string IntersectionKey(string? node, RiskIndexTabGroup tab)
    {
        var key = Parse(node);
        return key.AssetClass is not null || key.TabGroup is not null ? key.ToString() : key.WithTabGroup(tab).ToString();
    }

    /// <summary>
    /// The header's level (§5.5): "▲ Médio" from the band of node ∩ class, "Indicativo" without a band, "Indisponível" when
    /// the intersection has no object or no value.
    /// </summary>
    public static AssetTabLevel Level(RiskIndexOverviewDto? overview, TextLookup text, CultureInfo ui)
    {
        if (overview?.Headline is not { } headline || overview.ObjectCount == 0 || headline.DisplayState == RiskIndexDisplayState.NoData)
            return new AssetTabLevel(RiskText.Get(text, "RiskTabUnavailable"), MaterialIconKind.CloudOffOutline, null, true);
        if (headline.DisplayState == RiskIndexDisplayState.Indicative || !HeadlineFormatter.ShowsBand(headline, overview.Bands))
            return new AssetTabLevel(RiskText.Get(text, "RiskTabIndicative"), BandGlyph.Indicative, null, false);

        var band = HeadlineFormatter.BandAt(overview.Bands, headline.BandIndex)!;
        return new AssetTabLevel(HeadlineFormatter.BandLabel(band, ui), BandGlyph.ForBand(band.Index, overview.Bands.Count),
            ChartPaletteMap.BandClass(band.Index, overview.Bands.Count), false);
    }

    /// <summary>"Inventário de EXP: 16.831 objetos" — the basis the server counted (§5.5.1).</summary>
    public static string InventoryText(RiskIndexInventoryDto inventory, TextLookup text, CultureInfo culture) =>
        RiskText.Format(text, "RiskVisibility_Basis_" + inventory.InventoryBasisKey + "FMT", culture,
            inventory.InventoryCount.ToString("N0", culture));

    /// <summary>
    /// The general visibility bar (S39 §8.1): assessed, stale, presumed and — when there are any — not assessed, each with
    /// its count and glyph, so colour is never the only information (§10.7, §10.8).
    /// </summary>
    public static IReadOnlyList<VisibilitySegment> Overall(RiskIndexStateCountsDto counts, TextLookup text, CultureInfo culture)
    {
        var segments = new List<VisibilitySegment>
        {
            Segment("assessed", counts.Assessed, "RiskVisibility_AssessedFMT", MaterialIconKind.CheckCircle),
            Segment("stale", counts.Stale, "RiskVisibility_StaleFMT", MaterialIconKind.ClockAlertOutline),
            Segment("presumed", counts.Presumed, "RiskVisibility_PresumedFMT", MaterialIconKind.HelpCircleOutline)
        };
        if (counts.NotAssessed > 0)
            segments.Add(Segment("notAssessed", counts.NotAssessed, "RiskVisibility_NotAssessedFMT", MaterialIconKind.MinusCircleOutline));
        return segments;

        VisibilitySegment Segment(string kind, int count, string key, MaterialIconKind glyph) =>
            new(kind, count, RiskText.Format(text, key, culture, count.ToString("N0", culture)), glyph);
    }

    /// <summary>
    /// Visibility per source (§5.5.1, S39 §8.2–§8.3): for each source of the tab, the inventory objects whose reading from it
    /// is current (<c>v = 1</c>), stale (<c>0 &lt; v &lt; 1</c>), expired (<c>v = 0</c>, kept) or missing. Every state is listed,
    /// zero included, so a source that reads nothing shows that rather than an empty bar. A disabled source gives why.
    /// </summary>
    public static IReadOnlyList<SourceVisibilityRow> Sources(RiskIndexInventoryDto inventory, TextLookup text, CultureInfo culture) =>
        inventory.Sources.Select(source =>
        {
            var label = NodeKeyDisplay.SourceLabel(source.SourceKey, text);
            if (!source.Enabled)
                return new SourceVisibilityRow(source.SourceKey, label, false,
                    (source.DisabledReasonKey is { Length: > 0 } reason ? text("RiskVisibility_Disabled_" + reason) : null) ??
                    RiskText.Get(text, "RiskVisibility_SourceDisabled"), [], null);

            List<VisibilitySegment> segments =
            [
                Reading("current", source.Current, "RiskVisibility_ReadingCurrentFMT", MaterialIconKind.CheckCircle),
                Reading("stale", source.Stale, "RiskVisibility_ReadingStaleFMT", MaterialIconKind.ClockAlertOutline),
                Reading("expired", source.Expired, "RiskVisibility_ReadingExpiredFMT", MaterialIconKind.TimerSandComplete),
                Reading("noReading", source.NoReading, "RiskVisibility_ReadingNoneFMT", MaterialIconKind.CircleOffOutline)
            ];
            var last = source.LastReadingUtc is { } at
                ? RiskText.Format(text, "RiskVisibility_LastReadingFMT", culture, at.ToLocalTime().ToString("d", culture))
                : null;
            return new SourceVisibilityRow(source.SourceKey, label, true, null, segments, last);

            VisibilitySegment Reading(string kind, int count, string key, MaterialIconKind glyph) =>
                new(kind, count, RiskText.Format(text, key, culture, count.ToString("N0", culture)), glyph);
        }).ToList();

    /// <summary>The disabled reasons the server sends today (<c>RiskIndexReadService.GetInventoryAsync</c>).</summary>
    public static readonly IReadOnlyList<string> KnownDisabledReasons = ["source_not_enabled"];

    /// <summary>The resource keys the visibility rows build at run time.</summary>
    public static IEnumerable<string> SourceKeys() =>
        KnownDisabledReasons.Select(r => "RiskVisibility_Disabled_" + r).Concat(
        [
            "RiskVisibility_SourceDisabled", "RiskVisibility_ReadingCurrentFMT", "RiskVisibility_ReadingStaleFMT",
            "RiskVisibility_ReadingExpiredFMT", "RiskVisibility_ReadingNoneFMT", "RiskVisibility_LastReadingFMT"
        ]);

    /// <summary>
    /// "Distribuição de R dos 9.120 avaliados: ● Baixo 1.226 · ▲ Médio 7.858 · ⬣ Alto 36" — the assessed objects by band of
    /// 100·R, in band order; the counts add up to the assessed total (§5.5.1).
    /// </summary>
    public static IReadOnlyList<(string Text, MaterialIconKind Glyph, string BandClass)> Distribution(
        RiskIndexInventoryDto inventory, IReadOnlyList<RiskIndexBandDto> bands, CultureInfo culture, CultureInfo ui) =>
        bands.OrderBy(b => b.Index)
            .Where(b => ChartPaletteMap.IsValidBand(b.Index, bands.Count))
            .Select(b => (HeadlineFormatter.BandLabel(b, ui) + " " +
                          inventory.RDistributionByBand.GetValueOrDefault(b.Key).ToString("N0", culture),
                BandGlyph.ForBand(b.Index, bands.Count), ChartPaletteMap.BandClass(b.Index, bands.Count)))
            .ToList();
}

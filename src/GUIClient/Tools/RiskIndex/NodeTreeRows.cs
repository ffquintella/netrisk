using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using Material.Icons;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>What a line of the Explore tree is: a node, the note between two comparability groups, or a state message.</summary>
public enum NodeTreeEntryKind
{
    Node = 1,
    Separator = 2,
    Message = 3
}

/// <summary>The columns of the Explore tree that sort (§5.6.1). Δ7d, band, type and state never do.</summary>
public enum NodeTreeSortColumn
{
    Node = 1,
    Icr = 2,
    Coverage = 3,
    Objects = 4,
    Shared = 5
}

/// <summary>
/// One line of the Explore tree with every cell already in text (S40 §5.6.1): node, type, ICR, band (label, glyph and the
/// pill's ramp-step class), Δ7d, coverage, objects, shared and state. <see cref="GroupIndex"/> is the comparability group's
/// position; <see cref="IsUnranked"/> marks indicative and no-data nodes, which go to the end of their group unbanded.
/// </summary>
public sealed record NodeTreeEntry(
    NodeTreeEntryKind Kind,
    string Key,
    int GroupIndex,
    string Label,
    string TypeText,
    int? Displayed,
    string IcrText,
    string? BandLabel,
    MaterialIconKind? BandGlyph,
    string? BandClass,
    string DeltaText,
    double? Coverage,
    string CoverageText,
    int ObjectCount,
    string ObjectsText,
    int SharedObjects,
    string SharedText,
    string StateText,
    bool StateWarning,
    bool IsUnranked,
    bool CanExpand)
{
    public bool IsNode => Kind == NodeTreeEntryKind.Node;
}

/// <summary>
/// The Explore tree's pure half (S40 §5.6.1, §5.6.4; T254): the rows of <c>GET /RiskIndex/Nodes</c> with the
/// "não comparáveis" notes between comparability groups, the sort that never crosses a group (partial groups after the
/// official ones, indicative and no-data nodes last in theirs), and the name filter of Ctrl+F (IX-8).
/// </summary>
public static class NodeTreeRows
{
    private const string Dash = "—";

    public static NodeTreeEntry Message(string text) =>
        new(NodeTreeEntryKind.Message, "", int.MaxValue, text, "", null, "", null, null, null, "", null, "", 0, "", 0, "", "",
            false, false, false);

    /// <summary>The children in the server's order (group, then classified before unranked), a note opening every group but the first.</summary>
    public static IReadOnlyList<NodeTreeEntry> Build(RiskIndexNodeChildrenDto children, IReadOnlyList<RiskIndexBandDto> bands,
        NodeLabels labels, TextLookup text, CultureInfo culture, CultureInfo ui)
    {
        var groups = children.Groups.Select((g, i) => (g, i)).ToDictionary(x => x.g.Key, x => x.i, StringComparer.Ordinal);
        var entries = new List<NodeTreeEntry>();
        var previous = -1;
        foreach (var row in children.Rows)
        {
            var group = groups.TryGetValue(row.ComparabilityGroup, out var index) ? index : groups.Count;
            if (previous >= 0 && group != previous && group < children.Groups.Count)
                entries.Add(Separator(group, previous >= 0 && previous < children.Groups.Count ? children.Groups[previous] : null,
                    children.Groups[group], text));
            previous = group;
            entries.Add(Row(row, group, children.Dimension, bands, labels, text, culture, ui));
        }

        return entries;
    }

    /// <summary>
    /// The note between two groups (§5.6.4): a partial group after official ones is "não comparáveis com as linhas acima:
    /// visão parcial"; otherwise the first difference — type, applicable categories, provenance.
    /// </summary>
    public static string SeparatorKey(RiskIndexComparabilityGroupDto? previous, RiskIndexComparabilityGroupDto group)
    {
        if (group.Provenance == RiskIndexProvenance.PartialRestricted && previous?.Provenance != RiskIndexProvenance.PartialRestricted)
            return "RiskExploreNotComparablePartial";
        if (previous is null || !string.Equals(previous.NodeType, group.NodeType, StringComparison.Ordinal))
            return "RiskExploreNotComparableType";
        return previous.ApplicableCategories.SequenceEqual(group.ApplicableCategories)
            ? "RiskExploreNotComparableProvenance"
            : "RiskExploreNotComparableCategories";
    }

    private static NodeTreeEntry Separator(int group, RiskIndexComparabilityGroupDto? previous, RiskIndexComparabilityGroupDto current,
        TextLookup text) =>
        new(NodeTreeEntryKind.Separator, "", group, RiskText.Get(text, SeparatorKey(previous, current)), "", null, "", null, null,
            null, "", null, "", 0, "", 0, "", "", false, false, false);

    public static NodeTreeEntry Row(RiskIndexNodeRowDto row, int group, RiskIndexDimension dimension,
        IReadOnlyList<RiskIndexBandDto> bands, NodeLabels labels, TextLookup text, CultureInfo culture, CultureInfo ui)
    {
        var classified = row.State is RiskIndexDisplayState.Point or RiskIndexDisplayState.PointWithInterval && row.Displayed is not null;
        var band = classified && CategoryRowFormatter.BandIndex(row.BandKey) is { } index && ChartPaletteMap.IsValidBand(index, bands.Count)
            ? HeadlineFormatter.BandAt(bands, index)
            : null;
        var key = RiskIndexNodeKey.TryParse(row.Node.Key, out var parsed) ? parsed : RiskIndexNodeKey.Org;
        var canExpand = row.HasChildren && DrillState.EntityDimensions.Contains(dimension) && key.EntityId is not null;

        return new NodeTreeEntry(NodeTreeEntryKind.Node, row.Node.Key, group,
            NodeKeyDisplay.RowLabel(row, dimension, labels, text, culture), NodeKeyDisplay.RowType(row, dimension, text),
            classified ? row.Displayed : null, classified ? row.Displayed!.Value.ToString(culture) : Dash,
            band is null ? null : HeadlineFormatter.BandLabel(band, ui),
            band is null ? null : BandGlyph.ForBand(band.Index, bands.Count),
            band is null ? null : ChartPaletteMap.BandClass(band.Index, bands.Count),
            row.Delta7d is { } delta ? HeadlineFormatter.SignedPoints(delta, culture) : Dash,
            row.Coverage, row.Coverage is { } coverage ? HeadlineFormatter.Percent(coverage, culture) + "%" : Dash,
            row.ObjectCount, row.ObjectCount.ToString("N0", culture), row.SharedObjects, row.SharedObjects.ToString("N0", culture),
            StateText(row, text, culture), row.UnassignedWeightFraction is not null || row.Provenance == RiskIndexProvenance.PartialRestricted,
            !classified, canExpand);
    }

    /// <summary>
    /// The "Estado" cell (§5.6.1): "sem dados", "indicativo 41 [20–79]", the share of the weight held by "Não atribuído",
    /// or the provenance — "oficial", "derivado", "parcial".
    /// </summary>
    public static string StateText(RiskIndexNodeRowDto row, TextLookup text, CultureInfo culture)
    {
        if (row.State == RiskIndexDisplayState.NoData) return RiskText.Get(text, "RiskExploreState_NoData");
        if (row.State == RiskIndexDisplayState.Indicative)
            return row.IntervalLow is { } low && row.IntervalHigh is { } high
                ? RiskText.Format(text, "RiskExploreState_IndicativeFMT", culture,
                    row.Displayed is { } shown ? shown.ToString(culture) : Dash, HeadlineFormatter.Bound(low, culture),
                    HeadlineFormatter.Bound(high, culture))
                : RiskText.Get(text, "RiskExploreState_Indicative");
        if (row.UnassignedWeightFraction is { } share)
            return RiskText.Format(text, "RiskExploreState_UnassignedWeightFMT", culture, HeadlineFormatter.Percent(share, culture));
        return RiskText.Get(text, "RiskExploreState_" + row.Provenance);
    }

    /// <summary>
    /// The Environment dimension's note (§5.6.1): only hosts have an environment, so "n objetos sem ambiente ficam fora
    /// deste recorte"; null along any other dimension or when every object is a host.
    /// </summary>
    public static string? OutsideNote(RiskIndexNodeChildrenDto children, TextLookup text, CultureInfo culture) =>
        children.Dimension == RiskIndexDimension.Environment && children.ObjectsOutsideDimension > 0
            ? RiskText.Format(text, "RiskExploreOutsideEnvironmentFMT", culture, children.ObjectsOutsideDimension.ToString("N0", culture))
            : null;

    /// <summary>
    /// The tree's order for a sorted column (§5.6.4): never across groups — group first, its note opening it, indicative and
    /// no-data nodes last — and then the column, the direction applying to the column alone. Empty values sort last both ways.
    /// </summary>
    public static int Compare(NodeTreeEntry? a, NodeTreeEntry? b, NodeTreeSortColumn column, bool descending)
    {
        if (ReferenceEquals(a, b)) return 0;
        if (a is null) return 1;
        if (b is null) return -1;

        var byGroup = a.GroupIndex.CompareTo(b.GroupIndex);
        if (byGroup != 0) return byGroup;
        var byKind = Rank(a.Kind).CompareTo(Rank(b.Kind));
        if (byKind != 0 || !a.IsNode) return byKind;
        var byRanking = a.IsUnranked.CompareTo(b.IsUnranked);
        if (byRanking != 0) return byRanking;

        var byColumn = column switch
        {
            NodeTreeSortColumn.Node => string.Compare(a.Label, b.Label, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase),
            NodeTreeSortColumn.Icr => Nullable(a.Displayed, b.Displayed, descending),
            NodeTreeSortColumn.Coverage => Nullable(a.Coverage, b.Coverage, descending),
            NodeTreeSortColumn.Objects => a.ObjectCount.CompareTo(b.ObjectCount),
            _ => a.SharedObjects.CompareTo(b.SharedObjects)
        };
        if (column is not (NodeTreeSortColumn.Icr or NodeTreeSortColumn.Coverage) && descending) byColumn = -byColumn;
        if (byColumn != 0) return byColumn;

        var byLabel = string.Compare(a.Label, b.Label, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
        return byLabel != 0 ? byLabel : string.CompareOrdinal(a.Key, b.Key);

        static int Rank(NodeTreeEntryKind kind) => kind switch
        {
            NodeTreeEntryKind.Separator => 0,
            NodeTreeEntryKind.Node => 1,
            _ => 2
        };
    }

    /// <summary>Compares two optional values in a direction, an empty value last either way.</summary>
    private static int Nullable<T>(T? a, T? b, bool descending) where T : struct, IComparable<T>
    {
        if (a is null && b is null) return 0;
        if (a is null) return 1;
        if (b is null) return -1;
        var result = a.Value.CompareTo(b.Value);
        return descending ? -result : result;
    }

    /// <summary>
    /// Ctrl+F's name filter (IX-8): the nodes whose name contains the text, ignoring case and accents. A group's note stays
    /// only while a node of the group does, and never opens the list.
    /// </summary>
    public static IReadOnlyList<NodeTreeEntry> Filter(IReadOnlyList<NodeTreeEntry> entries, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return entries;

        var text = query.Trim();
        var compare = CultureInfo.InvariantCulture.CompareInfo;
        var kept = entries.Where(e => e.IsNode &&
                                      compare.IndexOf(e.Label, text, CompareOptions.IgnoreCase | CompareOptions.IgnoreNonSpace) >= 0)
            .ToList();
        var groups = kept.Select(e => e.GroupIndex).ToHashSet();

        var result = new List<NodeTreeEntry>();
        foreach (var entry in entries)
        {
            if (entry.IsNode ? kept.Contains(entry) : entry.Kind == NodeTreeEntryKind.Separator && groups.Contains(entry.GroupIndex))
                result.Add(entry);
        }

        while (result.Count > 0 && result[0].Kind == NodeTreeEntryKind.Separator) result.RemoveAt(0);
        return result;
    }

    /// <summary>The resource keys this helper computes, so a test can require them in every language.</summary>
    public static IEnumerable<string> ComputedKeys() =>
        Enum.GetValues<RiskIndexProvenance>().Select(p => "RiskExploreState_" + p)
            .Concat(new[]
            {
                "RiskExploreState_NoData", "RiskExploreState_Indicative", "RiskExploreState_IndicativeFMT",
                "RiskExploreState_UnassignedWeightFMT", "RiskExploreNotComparablePartial", "RiskExploreNotComparableType",
                "RiskExploreNotComparableCategories", "RiskExploreNotComparableProvenance", "RiskExploreOutsideEnvironmentFMT"
            });
}

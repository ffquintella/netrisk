using System;
using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>The four filter terms of a node key (S40 §8.1): asset class or tab group, source, criticality, environment.</summary>
public enum DrillFilterKind
{
    AssetClass = 1,
    Source = 2,
    Criticality = 3,
    Environment = 4
}

/// <summary>One filter term — a chip of the Explore panel or a filter segment of the breadcrumb (S40 §5.6.2).</summary>
public sealed record DrillFilter(DrillFilterKind Kind, string Value)
{
    /// <summary>The term as the node-key grammar writes it: <c>c:server</c>, <c>s:V1-DEV</c>, <c>k:5</c>, <c>a:prod</c>.</summary>
    public string Term => Prefix(Kind) + ":" + Value;

    public static string Prefix(DrillFilterKind kind) => kind switch
    {
        DrillFilterKind.AssetClass => "c",
        DrillFilterKind.Source => "s",
        DrillFilterKind.Criticality => "k",
        _ => "a"
    };

    /// <summary>Parses one <c>c:</c>/<c>s:</c>/<c>k:</c>/<c>a:</c> term through the shared key parser; false for anything else.</summary>
    public static bool TryParse(string? term, out DrillFilter filter)
    {
        filter = new DrillFilter(DrillFilterKind.AssetClass, "");
        if (string.IsNullOrWhiteSpace(term) || term.Contains(';') || !RiskIndexNodeKey.TryParse(term, out var key) ||
            key.IsOrganization || key.EntityId is not null || key.Unassigned || key.IsScopeRoot)
            return false;

        var terms = DrillState.FilterTerms(key);
        if (terms.Count != 1) return false;
        filter = terms[0];
        return true;
    }
}

/// <summary>What a transition asks the page to read again (S40 §5.6.2, "Recarrega").</summary>
[Flags]
public enum DrillReload
{
    None = 0,

    /// <summary>The Explore tree: the dimension or the chips changed.</summary>
    Tree = 1,

    /// <summary>The selected asset-class tab's content.</summary>
    Tab = 2,

    /// <summary>The whole page: the node changed.</summary>
    Page = 4
}

/// <summary>A dimension change and the chip it removed, if any — the toast "Filtro ‹x› removido" (S40 §5.6.2).</summary>
public sealed record DimensionChange(DrillState State, DrillFilter? RemovedFilter);

/// <summary>
/// The page's single drill-down state (S40 §5.6.2; T254): the node shown, the dimension the Explore tree lists children
/// along, the Explore chips (filter terms added to each child, never to the node) and the selected asset-class tab. It is
/// immutable — every action yields a new state — and <c>RiskOverviewViewModel</c> holds the only instance: the panels
/// receive it and derive the key they query. Pure; parsing and formatting go through <see cref="RiskIndexNodeKey"/>,
/// the parser the server uses.
/// </summary>
public sealed record DrillState
{
    private DrillState(RiskIndexNodeKey node, RiskIndexDimension dimension, RiskIndexNodeKey childFilters,
        RiskIndexTabGroup? classTab)
    {
        Node = node;
        Dimension = dimension;
        ChildFilters = childFilters;
        ClassTab = classTab;
    }

    /// <summary>The node shown, normalized (canonical term order).</summary>
    public RiskIndexNodeKey Node { get; }

    /// <summary>The dimension of the Explore tree's children. Changes the children listed, never the node.</summary>
    public RiskIndexDimension Dimension { get; }

    /// <summary>The Explore chips: only <c>c:</c>/<c>s:</c>/<c>k:</c>/<c>a:</c> terms; <c>org</c> when there are none.</summary>
    public RiskIndexNodeKey ChildFilters { get; }

    /// <summary>The asset-class tab selected (§5.5): its content is the node ∩ the tab, and it never changes the tree.</summary>
    public RiskIndexTabGroup? ClassTab { get; }

    /// <summary>The entity dimensions: their children are entities, not intersections with a filter term.</summary>
    public static readonly IReadOnlyList<RiskIndexDimension> EntityDimensions =
    [
        RiskIndexDimension.Unit, RiskIndexDimension.Process, RiskIndexDimension.Activity, RiskIndexDimension.Application
    ];

    /// <summary>Every dimension in the selector's order (§5.6.1).</summary>
    public static readonly IReadOnlyList<RiskIndexDimension> AllDimensions =
    [
        RiskIndexDimension.Unit, RiskIndexDimension.Process, RiskIndexDimension.Activity, RiskIndexDimension.Application,
        RiskIndexDimension.AssetClass, RiskIndexDimension.Source, RiskIndexDimension.Criticality, RiskIndexDimension.Environment
    ];

    /// <summary>A state on <paramref name="node"/> (null or unparsable is the organization), the Unit dimension and no chips.</summary>
    public static DrillState Start(string? node) =>
        new DrillState(Parse(node), RiskIndexDimension.Unit, RiskIndexNodeKey.Org, null).Normalized();

    /// <summary>The node key the page queries.</summary>
    public string NodeText => Node.ToString();

    /// <summary>The <c>filters</c> argument of <c>GET /RiskIndex/Nodes</c>; null without chips, so the server applies none.</summary>
    public string? FiltersText => ChildFilters.HasFilters ? ChildFilters.ToString() : null;

    /// <summary>The chips in canonical order.</summary>
    public IReadOnlyList<DrillFilter> Chips => FilterTerms(ChildFilters);

    /// <summary>The filter kind a dimension lists children by; null for the entity dimensions.</summary>
    public static DrillFilterKind? KindOf(RiskIndexDimension dimension) => dimension switch
    {
        RiskIndexDimension.AssetClass => DrillFilterKind.AssetClass,
        RiskIndexDimension.Source => DrillFilterKind.Source,
        RiskIndexDimension.Criticality => DrillFilterKind.Criticality,
        RiskIndexDimension.Environment => DrillFilterKind.Environment,
        _ => null
    };

    /// <summary>
    /// The dimensions the selector offers for the node. "Meu escopo" lists the claimed entities only. A filter dimension the
    /// node already carries would list the node itself, so it is left out; "Não atribuído" is no entity, so it has no
    /// entity children and offers only the filter dimensions.
    /// </summary>
    public IReadOnlyList<RiskIndexDimension> AvailableDimensions
    {
        get
        {
            if (Node.IsScopeRoot) return [RiskIndexDimension.Unit];
            var carried = FilterTerms(Node).Select(f => f.Kind).ToHashSet();
            return AllDimensions
                .Where(d => !(Node.Unassigned && EntityDimensions.Contains(d)))
                .Where(d => KindOf(d) is not { } kind || !carried.Contains(kind))
                .ToList();
        }
    }

    /// <summary>
    /// The kinds "+ Filtro" offers (§5.6.2): those other than the dimension's and absent from the node. A chip of an offered
    /// kind is replaced by the new one. "Meu escopo" takes no filter.
    /// </summary>
    public IReadOnlyList<DrillFilterKind> AddableFilterKinds
    {
        get
        {
            if (Node.IsScopeRoot) return [];
            var carried = FilterTerms(Node).Select(f => f.Kind).ToHashSet();
            var dimensionKind = KindOf(Dimension);
            return Enum.GetValues<DrillFilterKind>().Where(k => k != dimensionKind && !carried.Contains(k)).ToList();
        }
    }

    /// <summary>Chooses the tree's dimension; a chip of the same kind leaves and is returned for the toast.</summary>
    public DimensionChange WithDimension(RiskIndexDimension dimension)
    {
        if (!AvailableDimensions.Contains(dimension) || dimension == Dimension) return new DimensionChange(this, null);

        var removed = KindOf(dimension) is { } kind ? Chips.FirstOrDefault(c => c.Kind == kind) : null;
        var filters = removed is null ? ChildFilters : FiltersKey(Chips.Where(c => c != removed));
        return new DimensionChange(new DrillState(Node, dimension, filters, ClassTab), removed);
    }

    /// <summary>"+ Filtro": adds a chip of an offered kind, replacing that kind's chip. Anything else leaves the state as is.</summary>
    public DrillState AddFilter(DrillFilter filter)
    {
        if (!AddableFilterKinds.Contains(filter.Kind) || !DrillFilter.TryParse(filter.Term, out var parsed)) return this;
        return new DrillState(Node, Dimension, FiltersKey(Chips.Where(c => c.Kind != parsed.Kind).Append(parsed)), ClassTab);
    }

    /// <summary>The chip's "×".</summary>
    public DrillState RemoveFilter(DrillFilterKind kind) =>
        Chips.All(c => c.Kind != kind) ? this : new DrillState(Node, Dimension, FiltersKey(Chips.Where(c => c.Kind != kind)), ClassTab);

    /// <summary>Selecting a tab changes only the tab: never the node, never the tree (§5.5, §5.6.2).</summary>
    public DrillState SelectTab(RiskIndexTabGroup? tab) => tab == ClassTab ? this : new DrillState(Node, Dimension, ChildFilters, tab);

    /// <summary>
    /// Opening a child of the tree (§5.6.2): the child's key — which already carries the chips — becomes the node; the chips
    /// clear; the dimension and the tab stay unless the new node rules them out (<see cref="Normalized"/>). An unparsable key
    /// leaves the state as is.
    /// </summary>
    public DrillState OpenChild(string? childKey) =>
        !string.IsNullOrWhiteSpace(childKey) && RiskIndexNodeKey.TryParse(childKey, out var key)
            ? new DrillState(key, Dimension, RiskIndexNodeKey.Org, ClassTab).Normalized()
            : this;

    /// <summary>A breadcrumb segment (an entity or a filter prefix of the node) becomes the node; the chips clear.</summary>
    public DrillState OpenBreadcrumb(string? prefixKey) =>
        !string.IsNullOrWhiteSpace(prefixKey) && RiskIndexNodeKey.TryParse(prefixKey, out var key)
            ? new DrillState(key, Dimension, RiskIndexNodeKey.Org, ClassTab).Normalized()
            : this;

    /// <summary>What the page reads again going from <paramref name="before"/> to <paramref name="after"/> (§5.6.2).</summary>
    public static DrillReload Reload(DrillState? before, DrillState after)
    {
        if (before is null || !before.Node.Equals(after.Node)) return DrillReload.Page;
        var reload = DrillReload.None;
        if (before.Dimension != after.Dimension || !before.ChildFilters.Equals(after.ChildFilters)) reload |= DrillReload.Tree;
        if (before.ClassTab != after.ClassTab) reload |= DrillReload.Tab;
        return reload;
    }

    /// <summary>
    /// Keeps the state coherent with its node: a dimension the node rules out falls back to the first offered one, a chip
    /// of a kind the node carries (or of the dimension's kind) leaves, and a node already cut by a class selects that
    /// class's tab — the only one enabled (§5.5).
    /// </summary>
    private DrillState Normalized()
    {
        var dimension = AvailableDimensions.Contains(Dimension) ? Dimension : AvailableDimensions[0];
        var carried = FilterTerms(Node).Select(f => f.Kind).ToHashSet();
        var dimensionKind = KindOf(dimension);
        var chips = Node.IsScopeRoot
            ? new List<DrillFilter>()
            : Chips.Where(c => !carried.Contains(c.Kind) && c.Kind != dimensionKind).ToList();
        var tab = Node.AssetClass is { } cls ? RiskIndexTabMap.Of(cls) : Node.TabGroup ?? ClassTab;
        return new DrillState(Node, dimension, FiltersKey(chips), tab);
    }

    /// <summary>The filter terms of a key in canonical order (<c>c; s; k; a</c>).</summary>
    public static IReadOnlyList<DrillFilter> FilterTerms(RiskIndexNodeKey key)
    {
        var terms = new List<DrillFilter>();
        if (key.AssetClass is { } c) terms.Add(new DrillFilter(DrillFilterKind.AssetClass, RiskIndexNodeKey.ClassTerm(c)));
        else if (key.TabGroup is { } t) terms.Add(new DrillFilter(DrillFilterKind.AssetClass, RiskIndexNodeKey.TabTerm(t)));
        if (key.Source is { } s) terms.Add(new DrillFilter(DrillFilterKind.Source, s));
        if (key.Criticality is { } k)
            terms.Add(new DrillFilter(DrillFilterKind.Criticality, k.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        if (key.Environment is { } a) terms.Add(new DrillFilter(DrillFilterKind.Environment, a));
        return terms;
    }

    /// <summary>The key of the entity part of <paramref name="node"/> followed by <paramref name="filters"/>.</summary>
    public static RiskIndexNodeKey Compose(RiskIndexNodeKey node, IEnumerable<DrillFilter> filters)
    {
        var parts = new List<string>();
        var entity = node.EntityPart;
        if (!entity.IsOrganization && !entity.IsScopeRoot) parts.Add(entity.ToString());
        parts.AddRange(filters.Select(f => f.Term));
        if (parts.Count == 0) return entity.IsScopeRoot ? entity : RiskIndexNodeKey.Org;
        return RiskIndexNodeKey.TryParse(string.Join(';', parts), out var key) ? key : node;
    }

    private static RiskIndexNodeKey FiltersKey(IEnumerable<DrillFilter> filters) => Compose(RiskIndexNodeKey.Org, filters);

    private static RiskIndexNodeKey Parse(string? node) =>
        RiskIndexNodeKey.TryParse(node, out var key) ? key : RiskIndexNodeKey.Org;
}

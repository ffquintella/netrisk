using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>The three roots and pseudo-entities named in the interface's language, never with the server's English.</summary>
public sealed record NodeLabels(string Organization, string MyScope, string Unassigned);

/// <summary>
/// How a node key reads (S40 §5.1, §5.6.1–§5.6.3; T254): the label of each filter term, the breadcrumb with one segment per
/// entity and then one per filter term of the node, the label and type of a row of the Explore tree, and the type seal
/// beside the title. Pure, over the shared <see cref="RiskIndexNodeKey"/> parser.
/// </summary>
public static class NodeKeyDisplay
{
    /// <summary>
    /// The separator the server writes between an entity's name and the labels of the node's filter terms
    /// (<c>RiskIndexReadService.RefAsync</c>: "Unidade X ∩ Server ∩ V1-DEV").
    /// </summary>
    public const string IntersectionSeparator = " ∩ ";

    private static RiskIndexNodeKey Parse(string? key) => RiskIndexNodeKey.TryParse(key, out var parsed) ? parsed : RiskIndexNodeKey.Org;

    /// <summary>
    /// A filter term in the interface's language: "Servidor", "Dispositivos", the source's id (or its <c>RiskSource_‹id›</c>
    /// name when one is declared), "Criticidade 5", "Produção", "Sem ambiente", "Ambiente: dev".
    /// </summary>
    public static string TermLabel(DrillFilter filter, TextLookup text, CultureInfo culture)
    {
        switch (filter.Kind)
        {
            case DrillFilterKind.AssetClass:
                if (RiskIndexNodeKey.ClassTerms.TryGetValue(filter.Value, out var assetClass))
                    return text(AssetClassKey(assetClass)) ?? filter.Value;
                return RiskIndexNodeKey.TabTerms.TryGetValue(filter.Value, out var tab)
                    ? text(AssetTabFormatter.NameKey(tab)) ?? filter.Value
                    : filter.Value;
            case DrillFilterKind.Source:
                return SourceLabel(filter.Value, text);
            case DrillFilterKind.Criticality:
                return RiskText.Format(text, "RiskExploreCriticalityFMT", culture, filter.Value);
            default:
                return filter.Value switch
                {
                    "prod" => RiskText.Get(text, "RiskEnvironment_prod"),
                    "none" => RiskText.Get(text, "RiskEnvironment_none"),
                    _ => RiskText.Format(text, "RiskEnvironmentFMT", culture, filter.Value)
                };
        }
    }

    public static string AssetClassKey(RiskIndexAssetClass assetClass) => "RiskAssetClass_" + assetClass;

    /// <summary>
    /// The reading sources the interface names (S39 §3.2: those with a validity window W, which inventory visibility and
    /// stale sources show). Any other id reads as itself.
    /// </summary>
    public static readonly IReadOnlyList<string> NamedSources =
    [
        RiskIndexSources.V1Device, RiskIndexSources.V1Cve, RiskIndexSources.V1InternetFacing, RiskIndexSources.V1Accounts,
        RiskIndexSources.V1Cloud, RiskIndexSources.V1LocalApps, RiskIndexSources.V1SecurityConfiguration, RiskIndexSources.SscTotal,
        RiskIndexSources.TenableNessus, RiskIndexSources.TenableVm, RiskIndexSources.TenableAes, RiskIndexSources.TenableAcr,
        RiskIndexSources.Importers, RiskIndexSources.Assessments
    ];

    /// <summary>A source's name ("Vision One — dispositivos"), or its S39 id when the interface does not name it.</summary>
    public static string SourceLabel(string source, TextLookup text) => text("RiskSource_" + source) ?? source;

    /// <summary>"Classe de ativo", "Fonte", "Criticidade", "Ambiente".</summary>
    public static string FilterKindLabel(DrillFilterKind kind, TextLookup text) => RiskText.Get(text, "RiskExploreFilterKind_" + kind);

    /// <summary>The dimension selector's entries: "Unidade", "Processo", … "Ambiente".</summary>
    public static string DimensionLabel(RiskIndexDimension dimension, TextLookup text) =>
        RiskText.Get(text, "RiskExploreDimension_" + dimension);

    /// <summary>An entity type ("organizationUnit", "businessProcess"…) as a word; the server's intersection mark is ignored.</summary>
    public static string EntityTypeLabel(string? nodeType, TextLookup text)
    {
        var type = (nodeType ?? "").TrimEnd('∩');
        return type.Length == 0 ? RiskText.Get(text, "RiskNodeType_entity") : text("RiskNodeType_" + type) ?? type;
    }

    /// <summary>
    /// The entity part of a node's name: the organization, "Meu escopo", "Não atribuído" in the interface's language, or the
    /// entity's name without the labels of the node's filter terms that the server appends.
    /// </summary>
    public static string EntityLabel(RiskIndexNodeRef node, NodeLabels labels)
    {
        var key = Parse(node.Key);
        if (key.IsScopeRoot) return labels.MyScope;
        if (key.Unassigned) return labels.Unassigned;
        if (key.EntityId is null) return labels.Organization;

        var filters = DrillState.FilterTerms(key).Count;
        var parts = node.Label.Split(IntersectionSeparator);
        return filters > 0 && parts.Length > filters ? string.Join(IntersectionSeparator, parts[..^filters]) : node.Label;
    }

    /// <summary>
    /// The scope breadcrumb (§5.1, §5.6.2): the server's path of entities (the organization only for global scope, "Meu
    /// escopo" heading a several-entity scope), the node's entity, and then one segment per filter term of the node, each
    /// the prefix of the node up to that term. The last segment is the node itself, shown as text.
    /// </summary>
    public static IReadOnlyList<BreadcrumbSegment> Breadcrumb(RiskIndexOverviewDto overview, NodeLabels labels, bool myScopeRoot,
        TextLookup text, CultureInfo culture)
    {
        var key = Parse(overview.Node.Key);
        var filters = DrillState.FilterTerms(key);
        var entityKey = key.EntityPart;
        var entity = new RiskIndexNodeRef(entityKey.ToString(), EntityLabel(overview.Node, labels), overview.Node.NodeType);

        var path = overview with
        {
            Node = entity,
            Breadcrumb = overview.Breadcrumb.Where(n => !string.Equals(n.Key, overview.Node.Key, StringComparison.Ordinal)).ToList()
        };
        var segments = RiskOverviewTopBar.Breadcrumb(path, labels.Organization, labels.MyScope, myScopeRoot).ToList();
        if (filters.Count == 0) return segments;

        segments[^1] = segments[^1] with { IsCurrent = false };
        for (var i = 0; i < filters.Count; i++)
            segments.Add(new BreadcrumbSegment(DrillState.Compose(entityKey, filters.Take(i + 1)).ToString(),
                TermLabel(filters[i], text, culture), i == filters.Count - 1));
        return segments;
    }

    /// <summary>Alt+← (§5.6.1, §10.8): the segment before the node, or null at the root.</summary>
    public static string? ParentKey(IReadOnlyList<BreadcrumbSegment> segments) => segments.Count >= 2 ? segments[^2].Key : null;

    /// <summary>
    /// The "Nó" cell of a tree row: along an entity dimension the entity's name; along a filter dimension the label of the
    /// term the row adds ("Servidor", "V1-DEV", "Criticidade 5", "Produção").
    /// </summary>
    public static string RowLabel(RiskIndexNodeRowDto row, RiskIndexDimension dimension, NodeLabels labels, TextLookup text,
        CultureInfo culture)
    {
        if (DrillState.KindOf(dimension) is { } kind &&
            DrillState.FilterTerms(Parse(row.Node.Key)).FirstOrDefault(f => f.Kind == kind) is { } term)
            return TermLabel(term, text, culture);
        return EntityLabel(row.Node, labels);
    }

    /// <summary>The "Tipo" cell: the entity's type along an entity dimension, the dimension's kind along a filter dimension.</summary>
    public static string RowType(RiskIndexNodeRowDto row, RiskIndexDimension dimension, TextLookup text) =>
        DrillState.KindOf(dimension) is { } kind && !Parse(row.Node.Key).Unassigned
            ? FilterKindLabel(kind, text)
            : Parse(row.Node.Key).Unassigned
                ? RiskText.Get(text, "RiskNodeType_unassigned")
                : EntityTypeLabel(row.Node.NodeType, text);

    /// <summary>
    /// The type seal beside the title (§5.6.3): "Processo", "Classe de ativo: Dispositivos", "Unidade · Fonte: V1-DEV".
    /// Null for the organization and "Meu escopo", which need none.
    /// </summary>
    public static string? TypeSeal(RiskIndexNodeRef node, TextLookup text, CultureInfo culture)
    {
        var key = Parse(node.Key);
        if (key.IsScopeRoot) return null;

        var parts = new List<string>();
        if (key.Unassigned) parts.Add(RiskText.Get(text, "RiskNodeType_unassigned"));
        else if (key.EntityId is not null) parts.Add(EntityTypeLabel(node.NodeType, text));
        parts.AddRange(DrillState.FilterTerms(key)
            .Select(f => RiskText.Format(text, "RiskExploreSealTermFMT", culture, FilterKindLabel(f.Kind, text), TermLabel(f, text, culture))));
        return parts.Count == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>The resource keys the display computes for a value set, so a test can require them in every language.</summary>
    public static IEnumerable<string> ComputedKeys() =>
        Enum.GetValues<RiskIndexAssetClass>().Select(AssetClassKey)
            .Concat(Enum.GetValues<DrillFilterKind>().Select(k => "RiskExploreFilterKind_" + k))
            .Concat(Enum.GetValues<RiskIndexDimension>().Select(d => "RiskExploreDimension_" + d))
            .Concat(NamedSources.Select(s => "RiskSource_" + s))
            .Concat(new[]
            {
                "RiskNodeType_organization", "RiskNodeType_organizationUnit", "RiskNodeType_subOrganizationUnit",
                "RiskNodeType_businessProcess", "RiskNodeType_activity", "RiskNodeType_application", "RiskNodeType_unassigned",
                "RiskNodeType_entity", "RiskEnvironment_prod", "RiskEnvironment_none", "RiskEnvironmentFMT",
                "RiskExploreCriticalityFMT", "RiskExploreSealTermFMT"
            });
}

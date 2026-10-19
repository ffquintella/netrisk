using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using DAL.Enums;
using GUIClient.Tools.Hosts;
using Model.RiskIndex;

namespace GUIClient.Tools.RiskIndex;

/// <summary>
/// What the object list asks <c>GET /RiskIndex/Objects</c> for (S40 §5.9.1, §8.2, §10.9; T254): the node — the page's,
/// optionally intersected with the asset-class tab a factor line came from — the filters, the server-side sort and the page.
/// Pure and immutable: every change returns a new query, and every change but paging goes back to the first page.
/// </summary>
public sealed record ObjectListQuery
{
    /// <summary>Page sizes offered: 50 by default, up to the server's limit of 500 (§8.2, §10.9).</summary>
    public static readonly IReadOnlyList<int> PageSizes = [50, 100, 250, 500];

    public const int DefaultPageSize = 50;
    public const int MaxPageSize = 500;

    /// <summary>The longest name search sent; a label is a few dozen characters.</summary>
    public const int MaxLabelLength = 100;

    /// <summary>The columns the server sorts by (§8.2): there is deliberately no sort by sensitivity or by Δ (S39 §1.3).</summary>
    public static readonly IReadOnlySet<string> SortableColumns =
        new HashSet<string>(StringComparer.Ordinal) { "label", "r", "contribution", "criticality", "lastReading" };

    /// <summary>
    /// The filters a preset (a factor line's, a seal's, a chip's or a data-quality item's <c>ObjectFilter</c>) may carry (§8.2):
    /// a stale source's item lists the objects that source reads (T256).
    /// </summary>
    public static readonly IReadOnlySet<string> PresetKeys =
        new HashSet<string>(StringComparer.Ordinal) { "factor", "seal", "chip", "isPresumed", "kind", "environment", "source" };

    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = DefaultPageSize;

    /// <summary>The tab a factor line came from: the list is then the node ∩ that tab, like the line (§5.5.1).</summary>
    public RiskIndexTabGroup? TabScope { get; init; }

    public RiskIndexCategory? Category { get; init; }
    public RiskIndexObjectState? State { get; init; }
    public RiskIndexAssetClass? AssetClass { get; init; }
    public string? Source { get; init; }
    public int? Criticality { get; init; }
    public string? Label { get; init; }

    /// <summary>One <c>name=value</c> filter from a factor line, seal or chip (<c>factor=EXP.V</c>).</summary>
    public string? Preset { get; init; }

    /// <summary>What the preset's chip reads ("31 achados com exploração conhecida").</summary>
    public string? PresetLabel { get; init; }

    public string? SortKey { get; init; }
    public bool SortDescending { get; init; }

    /// <summary>The key the list queries: the page's node, or the node ∩ the tab of the factor it was opened from.</summary>
    public string NodeKey(RiskIndexNodeKey node) =>
        TabScope is { } tab ? AssetTabFormatter.IntersectionKey(node.ToString(), tab) : node.ToString();

    /// <summary>The <c>filters</c> argument: comma-separated <c>name=value</c>; null when nothing filters.</summary>
    public string? FiltersText
    {
        get
        {
            var terms = new List<string>();
            if (Category is { } category) terms.Add("category=" + category);
            if (State is { } state) terms.Add("state=" + state);
            if (AssetClass is { } assetClass) terms.Add("assetClass=" + assetClass);
            if (Source is { Length: > 0 } source) terms.Add("source=" + source);
            if (Criticality is { } criticality) terms.Add("criticality=" + criticality.ToString(CultureInfo.InvariantCulture));
            if (Label is { Length: > 0 } label) terms.Add("label=" + label);
            if (Preset is { Length: > 0 } preset) terms.Add(preset);
            return terms.Count == 0 ? null : string.Join(',', terms);
        }
    }

    /// <summary>The <c>sorts</c> argument (<c>-r</c>, <c>label</c>); null keeps the server's order — contribution, largest first.</summary>
    public string? SortsText => SortKey is null ? null : (SortDescending ? "-" : "") + SortKey;

    /// <summary>
    /// A click on a column header (§5.9.1): the sort is the server's, so the page is read again from the first page. The
    /// same column toggles its direction; a new one starts ascending for the name and descending for the numbers. A column
    /// the server does not sort by changes nothing.
    /// </summary>
    public ObjectListQuery SortBy(string? column)
    {
        if (column is null || !SortableColumns.Contains(column)) return this;
        var descending = column == SortKey ? !SortDescending : column != "label";
        return this with { SortKey = column, SortDescending = descending, Page = 1 };
    }

    /// <summary>The category detail's list (§5.4, §5.9.1): that category pre-selected, the other filters cleared (the name search stays).</summary>
    public ObjectListQuery ForCategory(RiskIndexCategory category) => new() { PageSize = PageSize, Category = category, Label = Label };

    /// <summary>
    /// A factor line's list (§5.5.1): the node ∩ the line's tab, filtered by the line's <c>ObjectFilter</c>. A filter that is
    /// not one <c>name=value</c> of the accepted names is ignored rather than sent.
    /// </summary>
    public ObjectListQuery ForPreset(string? objectFilter, string? label, RiskIndexTabGroup? tab) =>
        IsValidPreset(objectFilter)
            ? new ObjectListQuery { PageSize = PageSize, Preset = objectFilter!.Trim(), PresetLabel = label, TabScope = tab, Label = Label }
            : new ObjectListQuery { PageSize = PageSize, TabScope = tab, Label = Label };

    public static bool IsValidPreset(string? objectFilter)
    {
        if (string.IsNullOrWhiteSpace(objectFilter)) return false;
        var text = objectFilter.Trim();
        var eq = text.IndexOf('=');
        if (eq <= 0 || eq == text.Length - 1 || text.IndexOf(',') >= 0 || text.IndexOf('=', eq + 1) >= 0) return false;
        var value = text[(eq + 1)..];
        return PresetKeys.Contains(text[..eq]) && value.Length <= 64 &&
               value.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '_' or '-');
    }

    public ObjectListQuery WithoutPreset() => this with { Preset = null, PresetLabel = null, TabScope = null, Page = 1 };

    /// <summary>
    /// The name search as sent: the filter syntax's separators (<c>,</c> and <c>=</c>) and control characters removed,
    /// trimmed and capped. Empty is no search.
    /// </summary>
    public static string? CleanLabel(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        var builder = new StringBuilder(text.Length);
        foreach (var ch in text)
            if (ch is not (',' or '=') && !char.IsControl(ch)) builder.Append(ch);
        var cleaned = builder.ToString().Trim();
        if (cleaned.Length > MaxLabelLength) cleaned = cleaned[..MaxLabelLength].TrimEnd();
        return cleaned.Length == 0 ? null : cleaned;
    }

    /// <summary>A page size outside the offered ones falls back to the default.</summary>
    public ObjectListQuery WithPageSize(int size) => this with { PageSize = PageSizes.Contains(size) ? size : DefaultPageSize, Page = 1 };

    public ObjectListQuery Next() => this with { Page = Page + 1 };

    public ObjectListQuery Previous() => this with { Page = Math.Max(1, Page - 1) };

    /// <summary>"1–50 de 1.204" and whether either side has a page (S38's footer, the same rule as the Hosts list).</summary>
    public PageRange Range(int total, int rowsOnPage) => PageRange.Of(Page, PageSize, total, rowsOnPage);

    /// <summary>"Ordenado por R (decrescente)", or the server's default order — contribution, largest first.</summary>
    public static string SortText(ObjectListQuery query, TextLookup text, CultureInfo culture) =>
        query.SortKey is { } key
            ? RiskText.Format(text, query.SortDescending ? "RiskObjectsSortedDescFMT" : "RiskObjectsSortedAscFMT", culture,
                RiskText.Get(text, "RiskObjectsSort_" + key))
            : RiskText.Get(text, "RiskObjectsSortedDefault");

    public static string RangeText(PageRange range, TextLookup text, CultureInfo culture) =>
        RiskText.Format(text, "RiskObjectsRangeFMT", culture, range.First.ToString("N0", culture), range.Last.ToString("N0", culture),
            range.Total.ToString("N0", culture));
}

/// <summary>One row of the object list as text (S40 §5.9.1), with the redaction hint when the label was withheld (§11.3).</summary>
public sealed record ObjectRowText(string Label, string? LabelHint, string Kind, string AssetClass, string Category, string R,
    string Criticality, string State, string Reading, string Points, string Shared);

/// <summary>The object list's cells (S40 §5.9.1; T254). Pure.</summary>
public static class ObjectRowFormatter
{
    private const string Dash = "—";

    public static ObjectRowText Format(RiskIndexObjectRowDto row, TextLookup text, CultureInfo culture)
    {
        var assetClass = row.AssetClass is { Length: > 0 } cls
            ? Enum.TryParse<RiskIndexAssetClass>(cls, out var parsed) ? text(NodeKeyDisplay.AssetClassKey(parsed)) ?? cls : cls
            : Dash;
        var label = ObjectLabelDisplay.Of(row.Object, text, culture);
        return new ObjectRowText(label.Label, label.Hint,
            RiskText.Get(text, "RiskObjectKind_" + row.Object.Object.Kind), assetClass, HeadlineFormatter.CategoryCode(row.Category),
            R(row.R, culture), Criticality(row, text, culture), RiskText.Get(text, "RiskObjectState_" + row.State),
            row.LastReadingUtc is { } reading ? reading.ToLocalTime().ToString("d", culture) : Dash,
            row.ContributionPoints.ToString("0.#", culture), row.SharedWith.ToString("N0", culture));
    }

    /// <summary>R on the 0–100 scale of the column header, as a whole number.</summary>
    public static string R(double r, CultureInfo culture) =>
        ((int)Math.Round(Math.Clamp(r, 0, 1) * 100, MidpointRounding.AwayFromZero)).ToString(culture);

    /// <summary>"5 (CMDB)": the effective criticality and where it came from.</summary>
    public static string Criticality(RiskIndexObjectRowDto row, TextLookup text, CultureInfo culture) =>
        row.CriticalitySource is { Length: > 0 } source
            ? RiskText.Format(text, "RiskObjectCriticalityFMT", culture, row.Criticality,
                text("RiskCriticalitySource_" + source) ?? source)
            : row.Criticality.ToString(culture);

    /// <summary>The criticality sources the server writes (<c>RiskIndexObjectRow.CriticalitySource</c>).</summary>
    public static readonly IReadOnlyList<string> CriticalitySources =
        ["manual", "cmdb", "visionone", "entity", "impact", "external", "default", "unknown"];

    /// <summary>The resource keys this formatter computes, so a test can require them in every language.</summary>
    public static IEnumerable<string> ComputedKeys() =>
        Enum.GetValues<RiskIndexObjectKind>().Select(k => "RiskObjectKind_" + k)
            .Concat(Enum.GetValues<RiskIndexObjectState>().Select(s => "RiskObjectState_" + s))
            .Concat(CriticalitySources.Select(s => "RiskCriticalitySource_" + s))
            .Concat(ObjectListQuery.SortableColumns.Select(c => "RiskObjectsSort_" + c))
            .Concat(new[]
            {
                "RiskObjectCriticalityFMT", "RiskObjectsRangeFMT", "RiskCategoryRequiresPermissionFMT", "RiskObjectsSortedAscFMT",
                "RiskObjectsSortedDescFMT", "RiskObjectsSortedDefault"
            });
}

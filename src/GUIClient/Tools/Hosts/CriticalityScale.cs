using System.Collections.Generic;
using System.Globalization;

namespace GUIClient.Tools.Hosts;

/// <summary>
/// A host's business criticality, the existing <c>hosts.criticality</c> 1–5 column (S38 §3.5):
/// which label it reads as, which pill class paints it, and the short form the host list shows.
///
/// Avalonia-free so <c>GUIClient.Tests</c> can compile it directly; the converters are thin
/// wrappers. A value outside 1–5 (an import wrote something the scale does not define) reads as
/// "Not set" everywhere, so the pill, the label and the facet can never disagree about it.
/// </summary>
public static class CriticalityScale
{
    public const int Lowest = 1;
    public const int Highest = 5;

    /// <summary>The pill class for a host with no criticality, or one outside the scale.</summary>
    public const string UnsetClass = "unset";

    /// <summary>What the list pill shows when there is no level: a dash, not an empty pill.</summary>
    public const string UnsetGlyph = "–";

    /// <summary>The edit dialog's choices, in the order it lists them: Not set, then 1 to 5.</summary>
    public static IReadOnlyList<int?> EditableLevels { get; } = [null, 1, 2, 3, 4, 5];

    /// <summary>The level if it is on the scale, otherwise null.</summary>
    public static int? Normalize(int? level) => level is >= Lowest and <= Highest ? level : null;

    /// <summary>The resource key of the level's name. Every key is declared in all three resx files.</summary>
    public static string LabelKey(int? level) => Normalize(level) switch
    {
        1 => "CriticalityVeryLow",
        2 => "CriticalityLow",
        3 => "CriticalityMedium",
        4 => "CriticalityHigh",
        5 => "CriticalityCritical",
        _ => "CriticalityNotSet"
    };

    /// <summary>Every key <see cref="LabelKey"/> can return.</summary>
    public static IReadOnlyList<string> AllLabelKeys { get; } =
    [
        "CriticalityVeryLow", "CriticalityLow", "CriticalityMedium", "CriticalityHigh",
        "CriticalityCritical", "CriticalityNotSet"
    ];

    /// <summary>The modifier class on <c>Border.criticality</c>: <c>c1</c>…<c>c5</c> or <c>unset</c>.</summary>
    public static string ClassFor(int? level) =>
        Normalize(level) is { } on ? "c" + on.ToString(CultureInfo.InvariantCulture) : UnsetClass;

    /// <summary>The list pill's text: the digit, or a dash when unset.</summary>
    public static string ShortLabel(int? level) =>
        Normalize(level) is { } on ? on.ToString(CultureInfo.InvariantCulture) : UnsetGlyph;

    /// <summary>
    /// The header pill's text, <c>Critical 5</c>; just the name (<c>Not set</c>) when there is no
    /// level, since a number would claim one.
    /// </summary>
    public static string LongLabel(int? level, string localizedName) =>
        Normalize(level) is { } on
            ? localizedName + " " + on.ToString(CultureInfo.InvariantCulture)
            : localizedName;
}

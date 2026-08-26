using System.Globalization;

namespace GUIClient.Tools;

/// <summary>
/// Renders the anchor text shown beside a likelihood or impact choice at rating time (Track 8
/// milestone 8.7.1).
///
/// A five-point scale labelled only "Low/Medium/High" is read differently by different raters — the
/// finding behind Budescu's work on verbal probability expressions and Cox's 2008 critique of risk
/// matrices — and two raters who mean different things by "Medium" produce a register that cannot be
/// aggregated or compared across teams. The anchors themselves live on the scale rows
/// (<c>likelihood.definition</c>, <c>impact.definition</c>, seeded in db_version 80), so an
/// installation that rewrites them for its own appetite gets its own wording with no code change.
///
/// This is a free function in its own file, with no Avalonia types, so <c>GUIClient.Tests</c> can
/// compile it directly — that project deliberately does not reference <c>GUIClient</c>, because doing
/// so would pull Avalonia into a headless run.
/// </summary>
public static class ScaleAnchorFormatter
{
    /// <summary>
    /// The definition plus its quantitative range, when the installation configured one. The range is
    /// what makes the anchor checkable: "unlikely" is an opinion, "1% – 5%" is a claim somebody can
    /// disagree with.
    /// </summary>
    /// <param name="definition">The prose anchor, or null/blank when the level has none.</param>
    /// <param name="min">Lower bound — annual probability (0–1) or monetary loss.</param>
    /// <param name="max">Upper bound, in the same units.</param>
    /// <param name="isProbability">Formats the bounds as percentages rather than currency.</param>
    /// <param name="culture">Defaults to the current UI culture.</param>
    public static string Describe(string? definition, double? min, double? max, bool isProbability,
        CultureInfo? culture = null)
    {
        var text = definition?.Trim() ?? "";

        // A half-open range is not a range. Rendering "5% – " would read as a bound the installation
        // never set, so the prose stands on its own instead.
        if (min is null || max is null) return text;

        var format = culture ?? CultureInfo.CurrentCulture;

        var range = isProbability
            ? string.Format(format, "{0:P1} – {1:P1}", min.Value, max.Value)
            : string.Format(format, "{0:C0} – {1:C0}", min.Value, max.Value);

        return text.Length == 0 ? range : $"{text} ({range})";
    }
}

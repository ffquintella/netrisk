using System.Globalization;

namespace GUIClient.Tools;

/// <summary>
/// The one-line inherent → residual summary shown beside a risk in the register list
/// (Track 8 milestone 8.2.3), with the business rank from a review campaign (8.6.5) when there is
/// one.
///
/// A free function in its own file, with no Avalonia types, so <c>GUIClient.Tests</c> can compile it
/// directly — that project deliberately does not reference <c>GUIClient</c>. The Avalonia side is a
/// thin <c>IMultiValueConverter</c> that calls in here.
/// </summary>
public static class RiskScoreSummary
{
    /// <summary>
    /// Renders the scores for one row. Empty when there is nothing to say, so the caller can hide the
    /// line rather than reserve space for a dash.
    /// </summary>
    /// <param name="inherent">The untreated score.</param>
    /// <param name="residual">The post-treatment score, or null when nothing has been credited yet.</param>
    /// <param name="businessRank">Rank the business reviewers gave it, or null.</param>
    /// <param name="culture">Defaults to the current culture.</param>
    public static string Describe(double? inherent, double? residual, int? businessRank,
        CultureInfo? culture = null)
    {
        var format = culture ?? CultureInfo.CurrentCulture;

        var parts = new System.Collections.Generic.List<string>(2);

        if (inherent is not null)
        {
            // "8.0" on its own for an untreated risk rather than "8.0 → —": an em dash in the
            // residual position reads as a residual of nothing, when what is true is that nobody has
            // computed one yet.
            parts.Add(residual is null
                ? inherent.Value.ToString("0.0", format)
                // The sign is the *change in the score*, so treatment that took 8 down to 2 reads
                // "−6.0". Note this is the inverse of RiskScorePair.Delta, which is inherent minus
                // residual and so is positive when treatment helped: that is the right convention for
                // "how much did we buy" in a report, and the wrong one for a number sitting between
                // two scores where a reader will take it as an arrow.
                : string.Format(format, "{0:0.0} → {1:0.0} ({2})",
                    inherent.Value, residual.Value, SignedDelta(residual.Value - inherent.Value, format)));
        }

        if (businessRank is not null) parts.Add("#" + businessRank.Value.ToString(format));

        return string.Join("  ·  ", parts);
    }

    /// <summary>
    /// The delta always carries its sign, because an unsigned "6.0" beside two other numbers is
    /// ambiguous, and because a *negative* delta — treatment that made the score worse, or a residual
    /// computed against a since-raised inherent — is exactly the row somebody should look at.
    /// </summary>
    private static string SignedDelta(double delta, CultureInfo format)
    {
        // U+2212 for the minus sign rather than a hyphen: it aligns with the digits at the font sizes
        // the list uses, where a hyphen sits noticeably high.
        var sign = delta < 0 ? "−" : delta > 0 ? "+" : "";

        return sign + System.Math.Abs(delta).ToString("0.0", format);
    }
}

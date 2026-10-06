using System.Globalization;
using System.Text;

namespace Tools.Risks;

/// <summary>
/// The duplicate rule of Stage 9.2 (T154, S42 §5.3): two scenarios are duplicates when their
/// <b>central event</b> and their <b>consequences</b> are the same text once normalized — both,
/// never one of them. Two scenarios with the same event and different consequences are distinct by
/// the methodology's own rule ("dividir cenários com donos ou tratamentos distintos"), and must not be
/// flagged.
///
/// Normalization is deliberately shallow and deterministic: case, accents, runs of whitespace and
/// trailing punctuation are ignored; words are not. A rephrased duplicate is not detected — fuzzy
/// similarity was rejected because a threshold that flags rephrasings also flags distinct scenarios
/// that share vocabulary, and a warning that is usually wrong stops being read (S42 §11, D7).
///
/// An empty or missing field never matches anything — in particular two legacy risks, whose four
/// fields are all NULL, are not duplicates of each other.
/// </summary>
public static class RiskScenarioMatcher
{
    /// <summary>Trimmed from the end together with spaces, so "lost . ." loses all of it, not the last dot.</summary>
    private static readonly char[] TrailingPunctuation = ['.', ';', ':', '!', '?', ',', ' '];

    /// <summary>
    /// The comparable form of one scenario field, or <c>null</c> when there is nothing to compare:
    /// decomposed and stripped of combining marks, lower-cased invariantly, whitespace runs collapsed
    /// to one space, and trailing punctuation removed.
    /// </summary>
    public static string? Normalize(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;

        var decomposed = text.Normalize(NormalizationForm.FormKD);
        var builder = new StringBuilder(decomposed.Length);
        var pendingSpace = false;

        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;

            if (char.IsWhiteSpace(c))
            {
                pendingSpace = builder.Length > 0;
                continue;
            }

            if (pendingSpace)
            {
                builder.Append(' ');
                pendingSpace = false;
            }

            builder.Append(char.ToLowerInvariant(c));
        }

        var normalized = builder.ToString().TrimEnd(TrailingPunctuation);
        return normalized.Length == 0 ? null : normalized;
    }

    /// <summary>
    /// The key two duplicate scenarios share, or <c>null</c> when either half of the pair is empty —
    /// half a pair matches nothing.
    /// </summary>
    public static (string CentralEvent, string Consequences)? Key(string? centralEvent, string? consequences)
    {
        var e = Normalize(centralEvent);
        var c = Normalize(consequences);
        if (e is null || c is null) return null;

        // A pair, not a joined string: no separator can make "a|b" + "c" collide with "a" + "b|c".
        return (e, c);
    }

    /// <summary>Whether scenario A and scenario B are duplicates under the rule above.</summary>
    public static bool IsDuplicate(string? centralEventA, string? consequencesA,
        string? centralEventB, string? consequencesB)
    {
        var a = Key(centralEventA, consequencesA);
        return a is not null && a == Key(centralEventB, consequencesB);
    }
}

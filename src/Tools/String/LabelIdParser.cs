using System.Globalization;

namespace Tools.String;

/// <summary>
/// Helpers for the "Name (123)" labels used throughout the GUI combo boxes,
/// where a display name is suffixed with a numeric id in parentheses.
/// </summary>
public static class LabelIdParser
{
    /// <summary>
    /// Extracts the trailing numeric id encoded as "Name (123)".
    /// Uses the last parenthesis pair so names that themselves contain
    /// parentheses are handled correctly. Returns false (and id = 0) for
    /// null/empty/malformed input instead of throwing.
    /// </summary>
    public static bool TryParseTrailingId(string? label, out int id)
    {
        id = 0;

        if (string.IsNullOrWhiteSpace(label)) return false;

        var open = label.LastIndexOf('(');
        var close = label.LastIndexOf(')');

        if (open < 0 || close < 0 || close <= open) return false;

        var strId = label.Substring(open + 1, close - open - 1).Trim();

        return int.TryParse(strId, NumberStyles.Integer, CultureInfo.InvariantCulture, out id);
    }

    /// <summary>
    /// Extracts the text inside the last parenthesis pair (e.g. "Definition(host)" -> "host").
    /// Returns null for null/empty/malformed input instead of throwing.
    /// </summary>
    public static string? ExtractParenthesizedValue(string? label)
    {
        if (string.IsNullOrWhiteSpace(label)) return null;

        var open = label.LastIndexOf('(');
        var close = label.LastIndexOf(')');

        if (open < 0 || close < 0 || close <= open) return null;

        return label.Substring(open + 1, close - open - 1).Trim();
    }
}

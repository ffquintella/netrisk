using System;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GUIClient.Agents;

/// <summary>
/// What the per-write dialog shows (T357, T359 review #5). The item label and the "before" values are item text anyone
/// may have written, and "after" is the agent's: none may steer the user. Labels are one clean line; values lose every
/// invisible or bidirectional character, runs of blank lines collapse into a counted marker, and the dialog shows each
/// value's size and the changed span, so a change cannot sit unseen below the fold or inside an unchanged paragraph.
/// </summary>
public static class AgentWriteDisplay
{
    public const int LabelLength = 300;
    public const int SpanContext = 40;
    public const int SpanLength = 400;

    /// <summary>One line: invisible characters dropped, every run of whitespace or line breaks a single space, clipped.</summary>
    public static string Line(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var builder = new StringBuilder(Math.Min(text.Length, LabelLength + 1));
        var space = false;
        foreach (var rune in text.EnumerateRunes())
        {
            if (Hidden(rune.Value)) continue;
            if (Rune.IsWhiteSpace(rune) || Breaks(rune.Value))
            {
                space = builder.Length > 0;
                continue;
            }
            if (space) builder.Append(' ');
            space = false;
            builder.Append(rune.ToString());
            if (builder.Length > LabelLength) break;
        }
        var line = builder.ToString();
        return line.Length > LabelLength ? line[..LabelLength] + "…" : line;
    }

    /// <summary>
    /// A value as the dialog shows it: invisible characters dropped, line and paragraph separators made line breaks, and
    /// two or more blank lines in a row replaced by one line saying how many there were.
    /// </summary>
    public static string Block(string? text)
    {
        if (string.IsNullOrEmpty(text)) return text ?? "";
        var clean = new StringBuilder(text.Length);
        foreach (var rune in text.Replace("\r\n", "\n").EnumerateRunes())
        {
            if (Breaks(rune.Value)) clean.Append('\n');
            else if (!Hidden(rune.Value)) clean.Append(rune.ToString());
        }

        var output = new StringBuilder(clean.Length);
        var blank = 0;
        foreach (var line in clean.ToString().Split('\n'))
        {
            if (line.Trim().Length == 0)
            {
                blank++;
                continue;
            }
            Flush(output, blank);
            blank = 0;
            if (output.Length > 0) output.Append('\n');
            output.Append(line);
        }
        Flush(output, blank);
        return output.ToString();
    }

    /// <summary>The marker for <paramref name="count"/> blank lines; one blank line stays a blank line.</summary>
    public static string BlankLines(int count) => $"[{count.ToString(CultureInfo.InvariantCulture)} blank lines]";

    /// <summary>Lines and characters of the value as stored (before any cleaning).</summary>
    public static (int Lines, int Characters) Size(string? text) =>
        string.IsNullOrEmpty(text) ? (0, 0) : (text.Replace("\r\n", "\n").Count(c => c == '\n') + 1, text.Length);

    /// <summary>
    /// The changed span: the end of the common beginning, what was removed and what was added, the start of the common
    /// end — "…context «−removed−» «+added+» context…". Line breaks show as ⏎.
    /// </summary>
    public static string ChangedSpan(string? before, string? after)
    {
        var b = Block(before);
        var a = Block(after);
        if (string.Equals(a, b, StringComparison.Ordinal)) return "";
        var prefix = 0;
        while (prefix < a.Length && prefix < b.Length && a[prefix] == b[prefix]) prefix++;
        var suffix = 0;
        while (suffix < a.Length - prefix && suffix < b.Length - prefix && a[^(suffix + 1)] == b[^(suffix + 1)]) suffix++;

        var lead = b[..prefix];
        var tail = b[(b.Length - suffix)..];
        var removed = b[prefix..(b.Length - suffix)];
        var added = a[prefix..(a.Length - suffix)];
        var text = new StringBuilder();
        text.Append(lead.Length > SpanContext ? "…" + lead[^SpanContext..] : lead);
        if (removed.Length > 0) text.Append("«−").Append(Clip(removed)).Append("−»");
        if (added.Length > 0) text.Append("«+").Append(Clip(added)).Append("+»");
        text.Append(tail.Length > SpanContext ? tail[..SpanContext] + "…" : tail);
        return text.ToString().Replace("\n", "⏎");
    }

    private static string Clip(string value) =>
        value.Length <= SpanLength ? value : value[..SpanLength] + $"… [{(value.Length - SpanLength).ToString(CultureInfo.InvariantCulture)} more characters]";

    private static void Flush(StringBuilder output, int blank)
    {
        if (blank == 0 || output.Length == 0) return;
        output.Append('\n');
        if (blank >= 2) output.Append(BlankLines(blank));
    }

    private static bool Breaks(int codePoint) => codePoint is '\n' or 0x2028 or 0x2029 or 0x85;

    /// <summary>Format (bidi, zero-width), private use, unassigned, surrogate and control characters other than tab.</summary>
    private static bool Hidden(int codePoint)
    {
        if (codePoint is '\t' or '\n') return false;
        var category = CharUnicodeInfo.GetUnicodeCategory(codePoint);
        return category is UnicodeCategory.Format or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned
            or UnicodeCategory.Surrogate or UnicodeCategory.Control;
    }
}

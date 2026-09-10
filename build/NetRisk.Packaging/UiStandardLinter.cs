using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace NetRisk.Packaging;

/// <summary>
/// One rule breach found in an AXAML file.
/// </summary>
/// <param name="Rule">Rule id (<c>R1</c>, <c>R4</c>, <c>R5</c>, <c>R6</c>, or <c>R0</c> for a malformed waiver).</param>
/// <param name="File">Path as handed to the linter — the Nuke target passes a repo-relative path.</param>
/// <param name="Line">1-based line of the offending element or attribute.</param>
/// <param name="Message">Human-readable description, without the rule id or location.</param>
public sealed record UiStandardViolation(string Rule, string File, int Line, string Message)
{
    public override string ToString() => $"[{Rule} Violation] {File}:{Line} - {Message}";
}

/// <summary>
/// A waiver a view declares for itself, e.g.
/// <c>&lt;!-- ui-lint-waive R6: third-party control template, no Classes support --&gt;</c>.
/// </summary>
/// <param name="Rules">Rule ids the waiver suppresses on the element that follows it.</param>
/// <param name="Reason">Justification. A waiver without one is itself a violation (<c>R0</c>).</param>
/// <param name="Line">1-based line the waiver comment starts on.</param>
public sealed record UiStandardWaiver(IReadOnlyCollection<string> Rules, string Reason, int Line);

/// <summary>
/// The UI standard linter behind the <c>LintUi</c> Nuke target. Pure text in, findings out — no
/// filesystem, no process, so <c>Packaging.Tests</c> can exercise every rule directly.
///
/// The rules are element-aware rather than line-aware. The original line-by-line implementation
/// reported a <c>&lt;Button</c> whose <c>Classes</c> attribute sat on the following line as an R6
/// breach, which is how a 58-violation report was mostly noise. Everything here works on the whole
/// start tag, wherever its attributes are wrapped.
///
/// Rules (see docs/ui-standard.md):
///   R1 — hard-coded hex color in a brush attribute (§2.6).
///   R4 — named Avalonia brush used as a color/status indicator (§2.6).
///   R5 — literal user-facing string instead of a <c>Str*</c> resource binding (§3.2).
///   R6 — <c>Button</c> without a style class (§4).
///   R0 — a <c>ui-lint-waive</c> comment with no reason; waivers must be justified.
/// </summary>
public static class UiStandardLinter
{
    /// <summary>Marker that opens a waiver comment.</summary>
    public const string WaiverMarker = "ui-lint-waive";

    /// <summary>Attributes that paint a brush, and so must reference a token/class, not a literal.</summary>
    private static readonly string[] BrushAttributes =
    [
        "Background", "Foreground", "BorderBrush"
    ];

    /// <summary>
    /// Named brushes the standard forbids as status indicators (§2.6: NetRisk encodes status by
    /// shape, not color).
    /// </summary>
    private static readonly string[] ForbiddenNamedBrushes =
    [
        "Azure", "Green", "Red", "Blue", "Orange", "Yellow", "Purple", "Pink",
        "LightGreen", "DarkRed", "DarkGreen", "Crimson", "Lime", "Magenta", "Cyan"
    ];

    /// <summary>
    /// Attributes that carry user-facing copy, mapped to the elements they matter on. An empty
    /// element list means "any element".
    /// </summary>
    private static readonly (string Attribute, string[] Elements)[] LocalizableAttributes =
    [
        ("Text", ["TextBlock", "Run", "SelectableTextBlock"]),
        ("Content", ["Button", "CheckBox", "RadioButton", "Label", "ToggleButton", "MenuItem", "TabItem"]),
        ("Title", ["Window"]),
        ("Header", ["TabItem", "GroupBox", "HeaderedContentControl", "Expander", "MenuItem",
                    "NativeMenuItem"]),
        ("ToolTip.Tip", []),
        ("Watermark", [])
    ];

    private static readonly Regex AttributeRegex = new(
        @"(?<name>[A-Za-z_][\w:.\-]*)\s*=\s*""(?<value>[^""]*)""",
        RegexOptions.Compiled);

    private static readonly Regex WaiverRegex = new(
        WaiverMarker + @"\s+(?<rules>R\d+(?:\s*,\s*R\d+)*)\s*(?::\s*(?<reason>.*?))?\s*$",
        RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>Lints a single AXAML document.</summary>
    /// <param name="filePath">Path reported in the findings.</param>
    /// <param name="content">Full file text.</param>
    public static IReadOnlyList<UiStandardViolation> Lint(string filePath, string content)
    {
        var violations = new List<UiStandardViolation>();

        if (string.IsNullOrEmpty(content))
            return violations;

        var lineStarts = ComputeLineStarts(content);

        // A waiver comment applies to the next element that opens after it.
        UiStandardWaiver? pendingWaiver = null;

        foreach (var node in Scan(content, lineStarts))
        {
            if (node is CommentNode comment)
            {
                var waiver = ParseWaiver(comment.Text, comment.Line);

                if (waiver is null)
                    continue;

                if (string.IsNullOrWhiteSpace(waiver.Reason))
                {
                    violations.Add(new UiStandardViolation(
                        "R0", filePath, waiver.Line,
                        $"Waiver for {string.Join(", ", waiver.Rules)} carries no reason; " +
                        $"write '{WaiverMarker} {waiver.Rules.First()}: <why this cannot comply>'"));
                    continue;
                }

                pendingWaiver = waiver;
                continue;
            }

            var element = (ElementNode)node;
            var waived = pendingWaiver;
            pendingWaiver = null;

            foreach (var violation in Inspect(filePath, element))
            {
                if (waived is not null && waived.Rules.Contains(violation.Rule))
                    continue;

                violations.Add(violation);
            }
        }

        return violations
            .OrderBy(v => v.Line)
            .ThenBy(v => v.Rule, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>Lints many documents, preserving the order they were supplied in.</summary>
    public static IReadOnlyList<UiStandardViolation> Lint(
        IEnumerable<(string FilePath, string Content)> documents) =>
        documents.SelectMany(d => Lint(d.FilePath, d.Content)).ToList();

    /// <summary>Groups findings by rule id — what the Nuke target reports as its summary.</summary>
    public static IReadOnlyDictionary<string, int> CountByRule(
        IEnumerable<UiStandardViolation> violations) =>
        violations
            .GroupBy(v => v.Rule)
            .OrderBy(g => g.Key, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.Ordinal);

    private static IEnumerable<UiStandardViolation> Inspect(string filePath, ElementNode element)
    {
        var attributes = element.Attributes;

        // R1 / R4 — brush literals.
        foreach (var attribute in attributes)
        {
            if (!BrushAttributes.Contains(attribute.Name, StringComparer.Ordinal))
                continue;

            var value = attribute.Value.Trim();

            if (value.StartsWith('#') && value.Length >= 7 && IsHex(value.AsSpan(1)))
            {
                yield return new UiStandardViolation(
                    "R1", filePath, attribute.Line,
                    $"Hardcoded hex color: {attribute.Name}=\"{attribute.Value}\"");
            }
            else if (ForbiddenNamedBrushes.Contains(value, StringComparer.Ordinal))
            {
                yield return new UiStandardViolation(
                    "R4", filePath, attribute.Line,
                    $"Legacy named brush: {attribute.Name}=\"{attribute.Value}\"");
            }
        }

        // R5 — user-facing copy that is not a resource binding.
        foreach (var (name, elements) in LocalizableAttributes)
        {
            if (elements.Length > 0 && !elements.Contains(element.LocalName, StringComparer.Ordinal))
                continue;

            foreach (var attribute in attributes.Where(a => a.Name == name))
            {
                if (!IsUserFacingLiteral(attribute.Value))
                    continue;

                yield return new UiStandardViolation(
                    "R5", filePath, attribute.Line,
                    $"Hardcoded user-facing string: {attribute.Name}=\"{attribute.Value}\"");
            }
        }

        // R6 — buttons must carry a style class.
        if (element.LocalName == "Button" &&
            !attributes.Any(a => a.Name == "Classes") &&
            !attributes.Any(a => a.Name == "Theme"))
        {
            yield return new UiStandardViolation(
                "R6", filePath, element.Line,
                "Unclassed Button element detected");
        }
    }

    /// <summary>
    /// True when a value is literal copy a user would read. Bindings, resource lookups, numbers,
    /// single glyphs and format placeholders are not.
    /// </summary>
    private static bool IsUserFacingLiteral(string value)
    {
        var trimmed = value.Trim();

        if (trimmed.Length < 2)
            return false;

        // {Binding …}, {DynamicResource …}, {x:Static …}, {}{escaped}
        if (trimmed.StartsWith('{'))
            return false;

        // Needs at least two letters to be a word rather than a symbol or a number.
        if (trimmed.Count(char.IsLetter) < 2)
            return false;

        // Digits-and-punctuation values ("0.00", "1 / 2") are not copy.
        if (!trimmed.Any(char.IsLetter))
            return false;

        return true;
    }

    private static bool IsHex(ReadOnlySpan<char> span)
    {
        foreach (var c in span)
        {
            if (!Uri.IsHexDigit(c))
                return false;
        }

        return span.Length > 0;
    }

    private static UiStandardWaiver? ParseWaiver(string commentText, int line)
    {
        if (!commentText.Contains(WaiverMarker, StringComparison.Ordinal))
            return null;

        var match = WaiverRegex.Match(commentText.Trim());

        if (!match.Success)
        {
            return new UiStandardWaiver(
                new[] { "R?" }, string.Empty, line);
        }

        var rules = match.Groups["rules"].Value
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .ToArray();

        var reason = match.Groups["reason"].Success ? match.Groups["reason"].Value.Trim() : string.Empty;

        return new UiStandardWaiver(rules, reason, line);
    }

    // ---------------------------------------------------------------------------------------
    // Minimal AXAML scanner. Enough to walk start tags and comments without pulling in an XML
    // reader (views contain markup extensions and namespaces an XML reader would fuss over, and
    // we need byte offsets to report line numbers anyway).
    // ---------------------------------------------------------------------------------------

    private abstract record Node(int Line);

    private sealed record CommentNode(string Text, int Line) : Node(Line);

    private sealed record ElementNode(
        string LocalName,
        int Line,
        IReadOnlyList<AttributeOccurrence> Attributes) : Node(Line);

    private sealed record AttributeOccurrence(string Name, string Value, int Line);

    private static IEnumerable<Node> Scan(string content, int[] lineStarts)
    {
        var i = 0;

        while (i < content.Length)
        {
            var open = content.IndexOf('<', i);

            if (open < 0)
                break;

            if (Matches(content, open, "<!--"))
            {
                var end = content.IndexOf("-->", open + 4, StringComparison.Ordinal);
                var stop = end < 0 ? content.Length : end;

                yield return new CommentNode(
                    content[(open + 4)..stop],
                    LineOf(lineStarts, open));

                i = end < 0 ? content.Length : end + 3;
                continue;
            }

            if (open + 1 >= content.Length)
                break;

            var next = content[open + 1];

            // Closing tag, declaration or processing instruction — skip to its '>'.
            if (next is '/' or '!' or '?')
            {
                var close = content.IndexOf('>', open + 1);
                i = close < 0 ? content.Length : close + 1;
                continue;
            }

            if (!char.IsLetter(next) && next != '_')
            {
                i = open + 1;
                continue;
            }

            var tagEnd = FindTagEnd(content, open);

            if (tagEnd < 0)
                break;

            var tag = content[open..tagEnd];
            var name = ReadElementName(tag);

            // Property elements (`<Button.Styles>`, `<Grid.ColumnDefinitions>`) are not controls.
            if (!name.Contains('.'))
            {
                yield return new ElementNode(
                    LocalName(name),
                    LineOf(lineStarts, open),
                    ReadAttributes(tag, open, lineStarts));
            }

            i = tagEnd;
        }
    }

    /// <summary>Index just past the '&gt;' that closes the start tag beginning at <paramref name="open"/>.</summary>
    private static int FindTagEnd(string content, int open)
    {
        var inQuote = false;
        var quote = '"';

        for (var i = open + 1; i < content.Length; i++)
        {
            var c = content[i];

            if (inQuote)
            {
                if (c == quote)
                    inQuote = false;
                continue;
            }

            if (c is '"' or '\'')
            {
                inQuote = true;
                quote = c;
                continue;
            }

            if (c == '>')
                return i + 1;
        }

        return -1;
    }

    private static string ReadElementName(string tag)
    {
        var i = 1;

        while (i < tag.Length && !char.IsWhiteSpace(tag[i]) && tag[i] is not ('>' or '/'))
            i++;

        return tag[1..i];
    }

    /// <summary>Strips an <c>xmlns</c> prefix so <c>ui:Button</c> is still a Button.</summary>
    private static string LocalName(string qualifiedName)
    {
        var colon = qualifiedName.IndexOf(':');
        return colon < 0 ? qualifiedName : qualifiedName[(colon + 1)..];
    }

    private static IReadOnlyList<AttributeOccurrence> ReadAttributes(
        string tag, int tagOffset, int[] lineStarts)
    {
        var attributes = new List<AttributeOccurrence>();
        var nameStart = tag.IndexOf(' ');

        if (nameStart < 0)
            return attributes;

        foreach (Match match in AttributeRegex.Matches(tag, nameStart))
        {
            attributes.Add(new AttributeOccurrence(
                LocalName(match.Groups["name"].Value),
                match.Groups["value"].Value,
                LineOf(lineStarts, tagOffset + match.Index)));
        }

        return attributes;
    }

    private static bool Matches(string content, int index, string token) =>
        index + token.Length <= content.Length &&
        string.CompareOrdinal(content, index, token, 0, token.Length) == 0;

    private static int[] ComputeLineStarts(string content)
    {
        var starts = new List<int> { 0 };

        for (var i = 0; i < content.Length; i++)
        {
            if (content[i] == '\n')
                starts.Add(i + 1);
        }

        return starts.ToArray();
    }

    private static int LineOf(int[] lineStarts, int index)
    {
        var low = 0;
        var high = lineStarts.Length - 1;

        while (low < high)
        {
            var mid = (low + high + 1) / 2;

            if (lineStarts[mid] <= index)
                low = mid;
            else
                high = mid - 1;
        }

        return low + 1;
    }

    /// <summary>Renders the per-rule summary line the build logs.</summary>
    public static string DescribeCounts(IEnumerable<UiStandardViolation> violations)
    {
        var counts = CountByRule(violations);

        return counts.Count == 0
            ? "none"
            : string.Join(", ", counts.Select(c =>
                string.Create(CultureInfo.InvariantCulture, $"{c.Key}={c.Value}")));
    }
}

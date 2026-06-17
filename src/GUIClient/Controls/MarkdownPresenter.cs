using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace GUIClient.Controls;

/// <summary>
/// A small, dependency-free Markdown presenter used to render the rich-text
/// <c>ExplanationMarkdown</c> help field on assessment questions. It supports a
/// pragmatic subset of Markdown: ATX headings (<c>#</c>…<c>######</c>), unordered
/// bullet lists (<c>-</c>/<c>*</c>/<c>+</c>), blank-line separated paragraphs and the
/// inline spans <c>**bold**</c>, <c>*italic*</c>/<c>_italic_</c> and <c>`code`</c>.
/// Anything it does not recognise is rendered verbatim, so unknown markup never throws.
/// </summary>
public class MarkdownPresenter : ContentControl
{
    public static readonly StyledProperty<string?> MarkdownProperty =
        AvaloniaProperty.Register<MarkdownPresenter, string?>(nameof(Markdown));

    public string? Markdown
    {
        get => GetValue(MarkdownProperty);
        set => SetValue(MarkdownProperty, value);
    }

    static MarkdownPresenter()
    {
        MarkdownProperty.Changed.AddClassHandler<MarkdownPresenter>((c, _) => c.Rebuild());
    }

    private void Rebuild()
    {
        var markdown = Markdown;
        if (string.IsNullOrWhiteSpace(markdown))
        {
            Content = null;
            return;
        }

        var panel = new StackPanel { Orientation = Orientation.Vertical, Spacing = 4 };

        // Normalise newlines and walk the document line-by-line, grouping consecutive
        // non-blank, non-heading, non-bullet lines into a single paragraph.
        var lines = markdown.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var paragraph = new List<string>();

        void FlushParagraph()
        {
            if (paragraph.Count == 0) return;
            var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
            AddInlines(block.Inlines!, string.Join(" ", paragraph));
            panel.Children.Add(block);
            paragraph.Clear();
        }

        foreach (var raw in lines)
        {
            var line = raw.TrimEnd();

            if (string.IsNullOrWhiteSpace(line))
            {
                FlushParagraph();
                continue;
            }

            var heading = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
            if (heading.Success)
            {
                FlushParagraph();
                var level = heading.Groups[1].Value.Length;
                var block = new SelectableTextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    FontWeight = FontWeight.Bold,
                    FontSize = Math.Max(13, 22 - (level - 1) * 2),
                    Margin = new Thickness(0, level == 1 ? 0 : 4, 0, 2)
                };
                AddInlines(block.Inlines!, heading.Groups[2].Value);
                panel.Children.Add(block);
                continue;
            }

            var bullet = Regex.Match(line, @"^\s*[-*+]\s+(.*)$");
            if (bullet.Success)
            {
                FlushParagraph();
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(8, 0, 0, 0) };
                row.Children.Add(new SelectableTextBlock { Text = "•", Margin = new Thickness(0, 0, 6, 0) });
                var block = new SelectableTextBlock { TextWrapping = TextWrapping.Wrap };
                AddInlines(block.Inlines!, bullet.Groups[1].Value);
                row.Children.Add(block);
                panel.Children.Add(row);
                continue;
            }

            paragraph.Add(line);
        }

        FlushParagraph();

        Content = panel;
    }

    // Matches **bold**, *italic*, _italic_ and `code` spans.
    private static readonly Regex InlineRegex = new(
        @"(\*\*(?<bold>.+?)\*\*)|(\*(?<italic>.+?)\*)|(_(?<italic2>.+?)_)|(`(?<code>.+?)`)",
        RegexOptions.Compiled);

    private static void AddInlines(InlineCollection inlines, string text)
    {
        var lastIndex = 0;

        foreach (Match m in InlineRegex.Matches(text))
        {
            if (m.Index > lastIndex)
                inlines.Add(new Run(text.Substring(lastIndex, m.Index - lastIndex)));

            if (m.Groups["bold"].Success)
            {
                inlines.Add(new Run(m.Groups["bold"].Value) { FontWeight = FontWeight.Bold });
            }
            else if (m.Groups["italic"].Success || m.Groups["italic2"].Success)
            {
                var value = m.Groups["italic"].Success ? m.Groups["italic"].Value : m.Groups["italic2"].Value;
                inlines.Add(new Run(value) { FontStyle = FontStyle.Italic });
            }
            else if (m.Groups["code"].Success)
            {
                inlines.Add(new Run(m.Groups["code"].Value) { FontFamily = new FontFamily("Consolas, Menlo, monospace") });
            }

            lastIndex = m.Index + m.Length;
        }

        if (lastIndex < text.Length)
            inlines.Add(new Run(text.Substring(lastIndex)));
    }
}

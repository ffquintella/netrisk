using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Localization;
using UglyToad.PdfPig;

namespace ServerServices.Tests.Reports;

/// <summary>
/// What a rendered report PDF says, read back with PdfPig (Apache-2.0).
///
/// The text is compared with all whitespace removed. Where a renderer puts a space glyph, a line break
/// or a page break is layout, not content, and an assertion that depended on it would fail on a
/// re-flow that dropped nothing. Words still have to appear with their letters in order.
/// </summary>
internal sealed class PdfText
{
    private PdfText(string text, int pages, string? title, string? author, string? subject,
        IReadOnlySet<string> fonts, int images)
    {
        Images = images;
        Text = text;
        Pages = pages;
        Title = title;
        Author = author;
        Subject = subject;
        Fonts = fonts;
    }

    /// <summary>Every page's text in content order, whitespace removed.</summary>
    public string Text { get; }

    public int Pages { get; }
    public string? Title { get; }
    public string? Author { get; }
    public string? Subject { get; }

    /// <summary>The font names the letters were drawn with, subset prefixes ("ABCDEF+") removed.</summary>
    public IReadOnlySet<string> Fonts { get; }

    /// <summary>How many images the pages draw.</summary>
    public int Images { get; }

    public static bool IsPdf(byte[] data) =>
        data.Length > 4 && Encoding.ASCII.GetString(data, 0, 4) == "%PDF";

    public static PdfText Read(byte[] data)
    {
        using var document = PdfDocument.Open(data);

        var text = new StringBuilder();
        var fonts = new HashSet<string>(StringComparer.Ordinal);
        var images = 0;

        foreach (var page in document.GetPages())
        {
            images += page.GetImages().Count();

            foreach (var letter in page.Letters)
            {
                text.Append(letter.Value);

                var font = letter.FontName ?? "";
                var plus = font.IndexOf('+');
                fonts.Add(plus >= 0 ? font[(plus + 1)..] : font);
            }
        }

        return new PdfText(Squash(text.ToString()), document.NumberOfPages,
            document.Information.Title, document.Information.Author, document.Information.Subject, fonts, images);
    }

    public static string Squash(string value) =>
        new(value.Where(c => !char.IsWhiteSpace(c)).ToArray());

    /// <summary>Whether <paramref name="expected"/> appears, ignoring whitespace on both sides.</summary>
    public bool Contains(string expected) => Text.Contains(Squash(expected), StringComparison.Ordinal);

    /// <summary>Where <paramref name="expected"/> first appears at or after <paramref name="from"/>; -1 if it does not.</summary>
    public int IndexOf(string expected, int from = 0) => Text.IndexOf(Squash(expected), from, StringComparison.Ordinal);

    public int Count(char c) => Text.Count(x => x == c);
}

/// <summary>
/// Returns the key for any lookup, which is what the production localizer does for a key with no
/// resource entry, so a report's labels read back as their resource keys.
/// </summary>
internal sealed class KeyEchoLocalizer : IStringLocalizer
{
    public LocalizedString this[string name] => new(name, name, resourceNotFound: false);

    public LocalizedString this[string name, params object[] arguments] =>
        new(name, string.Format(name, arguments), resourceNotFound: false);

    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures) => [];
}

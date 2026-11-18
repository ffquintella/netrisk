using System.Text.RegularExpressions;

namespace Model.Integrations;

/// <summary>
/// The canonical spelling of a vendor tag (T263, S40 §6.6): <c>key: value</c> with single spaces, or the bare value when
/// the tag has no key. The sync stores tags in this form, a rule's value is saved in it, and the two are compared
/// case-insensitively — so a rule typed as <c>bu:Law</c> matches the device tag Vision One reports as <c>BU</c> / <c>Law</c>.
/// </summary>
public static partial class VendorTagText
{
    /// <summary>The longest tag a rule or a host row keeps (<c>vendor_entity_mappings.match_value</c>).</summary>
    public const int MaxLength = 255;

    /// <summary>The canonical form of free text, or null when nothing is left after trimming.</summary>
    public static string? Canonical(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return null;
        var text = Spaces().Replace(raw.Trim(), " ");
        var colon = text.IndexOf(':');
        if (colon < 0) return text;
        var key = text[..colon].Trim();
        var value = text[(colon + 1)..].Trim();
        if (key.Length == 0) return value.Length == 0 ? null : value;
        return value.Length == 0 ? key : key + ": " + value;
    }

    /// <summary>The canonical form of a tag the vendor reports as a key and a value.</summary>
    public static string? FromKeyValue(string? key, string? value) =>
        string.IsNullOrWhiteSpace(key) ? Canonical(value) : Canonical(key.Trim() + ":" + (value ?? ""));

    /// <summary>Whether two tags are the same tag: canonical forms equal, ignoring case.</summary>
    public static bool Same(string? a, string? b) =>
        Canonical(a) is { } x && Canonical(b) is { } y && string.Equals(x, y, StringComparison.OrdinalIgnoreCase);

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}

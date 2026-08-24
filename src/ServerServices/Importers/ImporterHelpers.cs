using System.Text;
using System.Text.Json;
using Contracts.Importers;

namespace ServerServices.Importers;

/// <summary>
/// Shared plumbing for the built-in importers: content sniffing and the small string/date coercions
/// every scanner format needs.
/// </summary>
public static class ImporterHelpers
{
    /// <summary>How much of a report is read for <c>CanHandle</c> sniffing.</summary>
    public const int SniffWindowBytes = 64 * 1024;

    /// <summary>
    /// Reads a prefix of <paramref name="stream"/> and restores the position, so sniffing never
    /// consumes the report. Returns an empty string for an unseekable stream — the caller then
    /// cannot sniff and falls back to an explicitly-named importer.
    /// </summary>
    public static string PeekText(Stream stream, int maxBytes = SniffWindowBytes)
    {
        if (!stream.CanSeek) return string.Empty;

        var origin = stream.Position;
        try
        {
            var buffer = new byte[Math.Min(maxBytes, 8 * 1024 * 1024)];
            var read = 0;
            while (read < buffer.Length)
            {
                var n = stream.Read(buffer, read, buffer.Length - read);
                if (n == 0) break;
                read += n;
            }

            return Encoding.UTF8.GetString(buffer, 0, read);
        }
        finally
        {
            stream.Position = origin;
        }
    }

    /// <summary>
    /// True when every one of <paramref name="markers"/> appears in the report's leading bytes.
    /// Sniffing on a conjunction of markers rather than one keeps formats that share a root
    /// element (all the JSON scanners, all the XML ones) from claiming each other's files.
    /// </summary>
    public static bool Sniff(Stream stream, params string[] markers)
    {
        var text = PeekText(stream);
        if (text.Length == 0) return false;
        return markers.All(m => text.Contains(m, StringComparison.OrdinalIgnoreCase));
    }

    public static async Task<string> ReadAllTextAsync(Stream stream, CancellationToken ct)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            leaveOpen: true);
        return await reader.ReadToEndAsync(ct);
    }

    public static async Task<JsonDocument> ReadJsonAsync(Stream stream, CancellationToken ct)
    {
        // Comments and trailing commas are tolerated: several scanners emit them, and rejecting an
        // otherwise-valid report over a trailing comma is a support ticket, not a safety feature.
        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        };

        return await JsonDocument.ParseAsync(stream, options, ct);
    }

    public static string? Text(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            if (!parent.TryGetProperty(name, out var prop)) continue;
            switch (prop.ValueKind)
            {
                case JsonValueKind.String:
                    var s = prop.GetString();
                    if (!string.IsNullOrWhiteSpace(s)) return s;
                    break;
                case JsonValueKind.Number:
                    return prop.GetRawText();
                case JsonValueKind.True:
                case JsonValueKind.False:
                    return prop.GetRawText();
            }
        }

        return null;
    }

    public static double? Number(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            if (!parent.TryGetProperty(name, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.Number && prop.TryGetDouble(out var d)) return d;
            if (prop.ValueKind == JsonValueKind.String &&
                double.TryParse(prop.GetString(), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }

        return null;
    }

    public static bool? Bool(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            if (!parent.TryGetProperty(name, out var prop)) continue;
            if (prop.ValueKind == JsonValueKind.True) return true;
            if (prop.ValueKind == JsonValueKind.False) return false;
            if (prop.ValueKind == JsonValueKind.String && bool.TryParse(prop.GetString(), out var b)) return b;
        }

        return null;
    }

    public static DateTime? Date(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            var raw = Text(parent, name);
            if (raw == null) continue;
            if (DateTime.TryParse(raw, System.Globalization.CultureInfo.InvariantCulture,
                    System.Globalization.DateTimeStyles.AdjustToUniversal |
                    System.Globalization.DateTimeStyles.AssumeUniversal, out var parsed))
                return parsed;
        }

        return null;
    }

    public static JsonElement? Child(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
            if (parent.TryGetProperty(name, out var prop) && prop.ValueKind != JsonValueKind.Null)
                return prop;

        return null;
    }

    public static IEnumerable<JsonElement> Array(JsonElement parent, params string[] names)
    {
        foreach (var name in names)
        {
            if (!parent.TryGetProperty(name, out var prop)) continue;
            if (prop.ValueKind != JsonValueKind.Array) continue;
            foreach (var item in prop.EnumerateArray()) yield return item;
            yield break;
        }
    }

    /// <summary>
    /// Pulls CVE identifiers out of arbitrary text — references, aliases, titles. Tools scatter
    /// them across all three, and a missing CVE costs a dedup match and a KEV lookup.
    /// </summary>
    public static IEnumerable<string> ExtractCves(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        foreach (var match in System.Text.RegularExpressions.Regex.Matches(text,
                     @"CVE-\d{4}-\d{4,7}",
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase).Cast<System.Text.RegularExpressions.Match>())
            yield return match.Value.ToUpperInvariant();
    }

    public static IEnumerable<string> ExtractCwes(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) yield break;

        foreach (var match in System.Text.RegularExpressions.Regex.Matches(text,
                     @"CWE-(\d{1,5})",
                     System.Text.RegularExpressions.RegexOptions.IgnoreCase).Cast<System.Text.RegularExpressions.Match>())
        {
            // Leading zeros are stripped: SARIF tags write CWE-089 where the advisory databases
            // write CWE-89, and treating those as different identifiers would break every lookup
            // and every dedup key built from the CWE list.
            var number = match.Groups[1].Value.TrimStart('0');
            if (number.Length == 0) number = "0";

            yield return $"CWE-{number}";
        }
    }

    /// <summary>
    /// Truncates to the column width NetRisk stores, appending an ellipsis so a reader can tell.
    /// </summary>
    public static string? Clip(string? value, int max)
    {
        if (string.IsNullOrEmpty(value)) return value;
        return value.Length <= max ? value : value.Substring(0, Math.Max(0, max - 1)) + "…";
    }
}

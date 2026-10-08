using System.Text;
using System.Text.Json;
using DAL.Enums;
using Model.Exceptions;
using Model.ThirdParties;
using Tools.Security;

namespace Tools.ThirdParties;

/// <summary>One component of a parsed SBOM, sanitized and cut to the stored column widths.</summary>
public sealed record SbomComponentFacts(string Name, string? Version, string? Purl, string? License);

/// <summary>What <see cref="SbomParser.Parse"/> read from a document.</summary>
public sealed record SbomParseResult(
    SbomFormat Format,
    string? SpecVersion,
    string? SerialNumber,
    string? DescribedName,
    string? DescribedVersion,
    IReadOnlyList<SbomComponentFacts> Components,
    string Sha256,
    int SizeBytes);

/// <summary>
/// Parses an SBOM a supplier handed over (Stage 9.10, S51 §4.6, T202). The document is <b>untrusted input</b> — a vendor
/// file, possibly crafted — and the parser is written as such (S51 D9):
/// <list type="bullet">
/// <item>CycloneDX JSON and SPDX JSON only. XML (XXE, entity expansion) and SPDX tag-value are refused, not parsed.</item>
/// <item>At most <see cref="ThirdPartyLimits.MaxSbomDocumentBytes"/> of UTF-8, measured before parsing; at most
/// <see cref="ThirdPartyLimits.MaxSbomJsonDepth"/> levels of nesting (<see cref="JsonDocumentOptions.MaxDepth"/>); at most
/// <see cref="ThirdPartyLimits.MaxSbomComponents"/> components, nested ones included, walked with an explicit stack —
/// a larger document is refused, never silently truncated.</item>
/// <item>Every string kept is stripped of control characters and cut to its column width.</item>
/// <item>Nothing is fetched and nothing is written to disk: there is no URL to follow and no path to build.</item>
/// </list>
/// A component without a name is a malformed document (both formats require one) and refuses it, naming the position.
/// </summary>
public static class SbomParser
{
    private const string Parameter = "Document";

    private static readonly JsonDocumentOptions Options = new()
    {
        MaxDepth = ThirdPartyLimits.MaxSbomJsonDepth,
        CommentHandling = JsonCommentHandling.Disallow,
        AllowTrailingCommas = false
    };

    public static SbomParseResult Parse(string? document)
    {
        if (string.IsNullOrWhiteSpace(document))
            throw Refuse("The SBOM document is required: the CycloneDX or SPDX JSON text itself.");

        var size = Encoding.UTF8.GetByteCount(document);
        if (size > ThirdPartyLimits.MaxSbomDocumentBytes)
            throw Refuse($"The SBOM document is {size} bytes; at most {ThirdPartyLimits.MaxSbomDocumentBytes} are accepted.");

        if (document.TrimStart().StartsWith('<'))
            throw Refuse("XML SBOMs are not accepted. Export the CycloneDX or SPDX document as JSON.");

        JsonDocument json;
        try
        {
            json = JsonDocument.Parse(document, Options);
        }
        catch (JsonException ex)
        {
            throw Refuse($"The SBOM document is not valid JSON (line {(ex.LineNumber ?? 0) + 1}), or nests deeper than " +
                         $"{ThirdPartyLimits.MaxSbomJsonDepth} levels.");
        }

        using (json)
        {
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw Refuse("The SBOM document must be a JSON object.");

            var sha256 = HashTool.CreateSha256(document).ToLowerInvariant();

            if (Text(root, "bomFormat") is { } bomFormat && bomFormat.Equals("CycloneDX", StringComparison.OrdinalIgnoreCase))
                return ParseCycloneDx(root, sha256, size);

            if (Text(root, "spdxVersion") is { } spdx && spdx.StartsWith("SPDX-", StringComparison.OrdinalIgnoreCase))
                return ParseSpdx(root, sha256, size);

            throw Refuse("Only CycloneDX JSON (bomFormat \"CycloneDX\") and SPDX JSON (spdxVersion \"SPDX-2.x\") are accepted.");
        }
    }

    // --- CycloneDX ---------------------------------------------------------------------------------------------------

    private static SbomParseResult ParseCycloneDx(JsonElement root, string sha256, int size)
    {
        var components = new Collector();

        // Nested components are walked with an explicit stack: the depth is already capped by the reader, but recursion
        // is not needed to bound it.
        var stack = new Stack<(JsonElement Array, string Path)>();
        if (ArrayOf(root, "components", "components") is { } top) stack.Push((top, "components"));

        while (stack.Count > 0)
        {
            var (array, path) = stack.Pop();
            var index = 0;
            foreach (var item in array.EnumerateArray())
            {
                var here = $"{path}[{index++}]";
                if (item.ValueKind != JsonValueKind.Object) throw Refuse($"{here} is not an object.");

                var name = Clean(Text(item, "name", here), ThirdPartyLimits.MaxStoredComponentNameLength)
                           ?? throw Refuse($"{here} has no name — a CycloneDX component requires one.");

                components.Add(new SbomComponentFacts(name,
                    Clean(Text(item, "version", here), ThirdPartyLimits.MaxStoredComponentVersionLength),
                    Clean(Text(item, "purl", here), ThirdPartyLimits.MaxStoredPurlLength),
                    Clean(CycloneDxLicenses(item, here), ThirdPartyLimits.MaxStoredLicenseLength)));

                if (ArrayOf(item, "components", $"{here}.components") is { } nested) stack.Push((nested, $"{here}.components"));
            }
        }

        string? describedName = null, describedVersion = null;
        if (ObjectOf(root, "metadata", "metadata") is { } metadata && ObjectOf(metadata, "component", "metadata.component") is { } product)
        {
            describedName = Clean(Text(product, "name", "metadata.component"), ThirdPartyLimits.MaxSbomComponentNameLength);
            describedVersion = Clean(Text(product, "version", "metadata.component"),
                ThirdPartyLimits.MaxSbomComponentVersionLength);
        }

        return new SbomParseResult(SbomFormat.CycloneDxJson,
            Clean(Text(root, "specVersion"), ThirdPartyLimits.MaxSpecVersionLength),
            Clean(Text(root, "serialNumber"), ThirdPartyLimits.MaxSerialNumberLength),
            describedName, describedVersion, components.Items, sha256, size);
    }

    private static string? CycloneDxLicenses(JsonElement component, string path)
    {
        if (ArrayOf(component, "licenses", $"{path}.licenses") is not { } licenses) return null;

        var names = new List<string>();
        foreach (var choice in licenses.EnumerateArray())
        {
            if (choice.ValueKind != JsonValueKind.Object) continue;

            if (Text(choice, "expression") is { } expression) names.Add(expression);
            else if (choice.TryGetProperty("license", out var license) && license.ValueKind == JsonValueKind.Object)
            {
                if ((Text(license, "id") ?? Text(license, "name")) is { } id) names.Add(id);
            }
        }

        return names.Count == 0 ? null : string.Join("; ", names.Distinct(StringComparer.Ordinal));
    }

    // --- SPDX --------------------------------------------------------------------------------------------------------

    private static SbomParseResult ParseSpdx(JsonElement root, string sha256, int size)
    {
        var components = new Collector();
        var bySpdxId = new Dictionary<string, (string Name, string? Version)>(StringComparer.Ordinal);

        if (ArrayOf(root, "packages", "packages") is { } packages)
        {
            var index = 0;
            foreach (var item in packages.EnumerateArray())
            {
                var here = $"packages[{index++}]";
                if (item.ValueKind != JsonValueKind.Object) throw Refuse($"{here} is not an object.");

                var name = Clean(Text(item, "name", here), ThirdPartyLimits.MaxStoredComponentNameLength)
                           ?? throw Refuse($"{here} has no name — an SPDX package requires one.");
                var version = Clean(Text(item, "versionInfo", here), ThirdPartyLimits.MaxStoredComponentVersionLength);

                string? purl = null;
                if (ArrayOf(item, "externalRefs", $"{here}.externalRefs") is { } refs)
                    foreach (var reference in refs.EnumerateArray())
                        if (reference.ValueKind == JsonValueKind.Object
                            && string.Equals(Text(reference, "referenceType"), "purl", StringComparison.OrdinalIgnoreCase))
                        {
                            purl = Clean(Text(reference, "referenceLocator"), ThirdPartyLimits.MaxStoredPurlLength);
                            break;
                        }

                var license = Asserted(Text(item, "licenseConcluded")) ?? Asserted(Text(item, "licenseDeclared"));

                components.Add(new SbomComponentFacts(name, version, purl,
                    Clean(license, ThirdPartyLimits.MaxStoredLicenseLength)));

                if (Text(item, "SPDXID") is { } spdxId) bySpdxId.TryAdd(spdxId, (name, version));
            }
        }

        // The described product: the package documentDescribes names, else the document's name.
        string? describedName = null, describedVersion = null;
        if (ArrayOf(root, "documentDescribes", "documentDescribes") is { } describes)
            foreach (var described in describes.EnumerateArray())
                if (described.ValueKind == JsonValueKind.String && described.GetString() is { } id
                    && bySpdxId.TryGetValue(id, out var package))
                {
                    (describedName, describedVersion) = package;
                    break;
                }

        describedName ??= Clean(Text(root, "name"), ThirdPartyLimits.MaxSbomComponentNameLength);

        return new SbomParseResult(SbomFormat.SpdxJson,
            Clean(Text(root, "spdxVersion"), ThirdPartyLimits.MaxSpecVersionLength),
            Clean(Text(root, "documentNamespace"), ThirdPartyLimits.MaxSerialNumberLength),
            Clean(describedName, ThirdPartyLimits.MaxSbomComponentNameLength),
            Clean(describedVersion, ThirdPartyLimits.MaxSbomComponentVersionLength),
            components.Items, sha256, size);
    }

    private static string? Asserted(string? spdxLicense) =>
        spdxLicense is null || spdxLicense.Equals("NOASSERTION", StringComparison.OrdinalIgnoreCase)
                            || spdxLicense.Equals("NONE", StringComparison.OrdinalIgnoreCase)
            ? null
            : spdxLicense;

    // --- shared ------------------------------------------------------------------------------------------------------

    /// <summary>Collects distinct components and refuses the document past the cap.</summary>
    private sealed class Collector
    {
        private readonly HashSet<(string, string?, string?)> _seen = new();
        private int _total;

        public List<SbomComponentFacts> Items { get; } = new();

        public void Add(SbomComponentFacts component)
        {
            if (++_total > ThirdPartyLimits.MaxSbomComponents)
                throw Refuse($"The SBOM lists more than {ThirdPartyLimits.MaxSbomComponents} components; split it by " +
                             "product, or ask the supplier for the SBOM of the component it delivers.");

            if (_seen.Add((component.Name, component.Version, component.Purl))) Items.Add(component);
        }
    }

    /// <summary>A string property; absent or null is null; any other type refuses the document, naming where.</summary>
    private static string? Text(JsonElement element, string property, string? path = null)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.String)
            throw Refuse($"{(path is null ? property : $"{path}.{property}")} must be a string.");

        return value.GetString();
    }

    private static JsonElement? ArrayOf(JsonElement element, string property, string path)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Array) throw Refuse($"{path} must be an array.");

        return value;
    }

    private static JsonElement? ObjectOf(JsonElement element, string property, string path)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind == JsonValueKind.Null) return null;
        if (value.ValueKind != JsonValueKind.Object) throw Refuse($"{path} must be an object.");

        return value;
    }

    /// <summary>Control characters removed, trimmed, cut to <paramref name="max"/>; empty is null.</summary>
    public static string? Clean(string? text, int max)
    {
        if (text is null) return null;

        var builder = new StringBuilder(System.Math.Min(text.Length, max));
        foreach (var c in text)
        {
            if (char.IsControl(c)) continue;
            builder.Append(c);
        }

        var cleaned = builder.ToString().Trim();
        if (cleaned.Length == 0) return null;

        if (cleaned.Length > max)
        {
            cleaned = cleaned[..max];
            // Never leave half of a surrogate pair at the cut.
            if (char.IsHighSurrogate(cleaned[^1])) cleaned = cleaned[..^1];
        }

        return cleaned;
    }

    /// <summary>
    /// The file name a client sent, reduced to metadata (S51 D9): the last segment after any <c>/</c> or <c>\</c>,
    /// control characters removed, cut to <see cref="ThirdPartyLimits.MaxSbomFileNameLength"/>; <c>.</c>, <c>..</c> and
    /// empty are null. It is never combined into a path — nothing is written to disk — so this keeps a traversal string
    /// from even being stored as if it named a file.
    /// </summary>
    public static string? SafeFileName(string? name)
    {
        if (string.IsNullOrWhiteSpace(name)) return null;

        var last = name.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        var cleaned = Clean(last, ThirdPartyLimits.MaxSbomFileNameLength);

        return cleaned is null or "." or ".." ? null : cleaned;
    }

    private static InvalidParameterException Refuse(string message) => new(Parameter, message);
}

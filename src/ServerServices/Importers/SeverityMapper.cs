using Contracts.Importers;

namespace ServerServices.Importers;

/// <summary>
/// Maps a scanner's severity vocabulary onto <see cref="NormalizedSeverity"/>.
///
/// Every importer needs this and every importer's table is different, so the table is data rather
/// than a switch statement: an operator can override a single mapping through the importer's
/// options (<c>severity.moderate=high</c>) without a code change, which is what the spec's
/// "configurable severity-mapping table" asks for.
/// </summary>
public class SeverityMapper
{
    public const string OptionPrefix = "severity.";

    private readonly Dictionary<string, NormalizedSeverity> _map;

    public SeverityMapper(IReadOnlyDictionary<string, NormalizedSeverity> defaults)
    {
        _map = new Dictionary<string, NormalizedSeverity>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in defaults) _map[key] = value;
    }

    /// <summary>
    /// Applies <c>severity.&lt;raw&gt;=&lt;level&gt;</c> options over the defaults. Returns a new
    /// mapper so the defaults stay shared and immutable across imports.
    /// </summary>
    public SeverityMapper WithOverrides(IReadOnlyDictionary<string, string>? options)
    {
        if (options == null || options.Count == 0) return this;

        var merged = new Dictionary<string, NormalizedSeverity>(_map, StringComparer.OrdinalIgnoreCase);

        foreach (var (key, value) in options)
        {
            if (!key.StartsWith(OptionPrefix, StringComparison.OrdinalIgnoreCase)) continue;
            var raw = key.Substring(OptionPrefix.Length);
            if (string.IsNullOrWhiteSpace(raw)) continue;
            if (Enum.TryParse<NormalizedSeverity>(value, ignoreCase: true, out var parsed))
                merged[raw] = parsed;
        }

        return new SeverityMapper(merged);
    }

    /// <summary>
    /// The mapped level, or <paramref name="fallback"/> when the tool used a value the table does
    /// not know. Unknown values fall back rather than throwing: a scanner adding a severity word
    /// in a point release should not fail the whole import.
    /// </summary>
    public NormalizedSeverity Map(string? raw, NormalizedSeverity fallback = NormalizedSeverity.None)
    {
        if (string.IsNullOrWhiteSpace(raw)) return fallback;
        return _map.TryGetValue(raw.Trim(), out var mapped) ? mapped : fallback;
    }

    public IReadOnlyDictionary<string, NormalizedSeverity> Entries => _map;

    /// <summary>
    /// CVSS base score to a severity band, per the CVSS v3.1 qualitative rating scale. The
    /// fallback for tools that report a score but no severity word.
    /// </summary>
    public static NormalizedSeverity FromCvssScore(double? score) => score switch
    {
        null => NormalizedSeverity.None,
        >= 9.0 => NormalizedSeverity.Critical,
        >= 7.0 => NormalizedSeverity.High,
        >= 4.0 => NormalizedSeverity.Medium,
        > 0.0 => NormalizedSeverity.Low,
        _ => NormalizedSeverity.None
    };

    /// <summary>
    /// The vocabulary shared by most tools — the CVSS band words. Individual importers extend or
    /// replace it.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, NormalizedSeverity> CvssWords =
        new Dictionary<string, NormalizedSeverity>(StringComparer.OrdinalIgnoreCase)
        {
            ["critical"] = NormalizedSeverity.Critical,
            ["high"] = NormalizedSeverity.High,
            ["medium"] = NormalizedSeverity.Medium,
            ["moderate"] = NormalizedSeverity.Medium,
            ["low"] = NormalizedSeverity.Low,
            ["none"] = NormalizedSeverity.None,
            ["info"] = NormalizedSeverity.None,
            ["informational"] = NormalizedSeverity.None,
            ["unknown"] = NormalizedSeverity.None,
            ["negligible"] = NormalizedSeverity.None
        };
}

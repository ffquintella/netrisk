namespace Model.Findings;

/// <summary>
/// What <c>GET /vulnerabilities/importers</c> returns for one importer (Track 3 milestone 3.1.4).
///
/// Built-ins and plugin importers produce the same shape on purpose: a client picking an importer
/// should not need to know, or care, which is which.
/// </summary>
public class ImporterDescriptor
{
    /// <summary>The identifier used in the import URL. Stable; renaming it breaks callers.</summary>
    public string Name { get; set; } = string.Empty;

    public string DisplayName { get; set; } = string.Empty;

    public string Version { get; set; } = string.Empty;

    /// <summary>
    /// The importer-contract version the implementation was built against. Surfaced so an operator
    /// can see at a glance that a plugin is behind the host, rather than discovering it from a
    /// failed import.
    /// </summary>
    public int ContractVersion { get; set; }

    /// <summary>Extensions including the dot, lower-case.</summary>
    public List<string> SupportedFileExtensions { get; set; } = new();

    public List<string> SupportedMimeTypes { get; set; } = new();

    /// <summary>False for a built-in, true for one contributed by a plugin.</summary>
    public bool IsPlugin { get; set; }

    /// <summary>
    /// The deduplication strategy chain currently configured for this importer, so a client can
    /// show what will happen to a re-import without a second round trip.
    /// </summary>
    public string? DedupStrategyChain { get; set; }
}

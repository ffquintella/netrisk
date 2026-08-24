namespace DAL.Entities;

/// <summary>
/// Per-scanner deduplication settings (Track 3 milestone 3.3.3).
///
/// Held in its own table rather than the general settings store because it is edited as a unit,
/// needs a change history, and the admin preview panel wants to read a whole configuration at once.
/// </summary>
public class ScannerDedupConfiguration
{
    public int Id { get; set; }

    /// <summary>The importer name this applies to. One row per importer at most.</summary>
    public string Importer { get; set; } = null!;

    /// <summary>
    /// The ordered strategy chain, comma-separated (<c>UniqueIdFromTool,HashBased</c>). First
    /// strategy to produce a key wins; the order is the whole point, so it is stored as a list
    /// rather than a set of flags.
    /// </summary>
    public string StrategyChain { get; set; } = null!;

    /// <summary>
    /// Which fields the <c>HashBased</c> strategy hashes, comma-separated and ordered. Empty means
    /// the strategy's own default set.
    /// </summary>
    public string? HashFields { get; set; }

    /// <summary>
    /// Whether a full scan of this scanner's scope may close findings it no longer reports.
    /// Defaults to false: a partial scan that is mistaken for a full one closes everything outside
    /// its slice, and that is far worse than a stale open finding.
    /// </summary>
    public bool AutoCloseMissing { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime? UpdatedAt { get; set; }

    public int? UpdatedById { get; set; }

    public virtual User? UpdatedBy { get; set; }
}

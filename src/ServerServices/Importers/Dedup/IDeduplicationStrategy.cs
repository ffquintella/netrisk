namespace ServerServices.Importers.Dedup;

/// <summary>
/// Computes the key two findings must share to be considered the same finding
/// (Track 3 milestone 3.3.1).
///
/// A strategy is a pure function of its input. That is the one property a dedup engine cannot
/// compromise on: a key that depends on the clock, the database, or anything else mutable stops
/// matching keys it produced earlier, and the register silently fills with duplicates.
/// </summary>
public interface IDeduplicationStrategy
{
    /// <summary>Identifier used in the per-scanner strategy chain configuration.</summary>
    string Name { get; }

    /// <summary>
    /// The key, or null when this strategy has no opinion about the finding — the engine then falls
    /// through to the next strategy in the chain. Returning a weak key rather than null is the
    /// mistake to avoid: it merges findings that are not the same.
    /// </summary>
    string? ComputeKey(DedupContext context, DedupFieldSet fields);

    /// <summary>
    /// True when the produced key should also be compared against the legacy
    /// <c>vulnerabilities.import_hash</c> column rather than only <c>dedup_key</c>. Only the
    /// backward-compatibility strategy sets this.
    /// </summary>
    bool MatchesLegacyImportHash => false;
}

namespace DAL.Entities;

/// <summary>
/// Append-only record of a change to a scanner's dedup configuration (Track 3 milestone 3.3.3:
/// "change history recorded").
///
/// Worth keeping because a dedup heuristic change silently alters what counts as "the same
/// finding" from that point on; when the register's numbers jump, this is the table that explains
/// why.
/// </summary>
public class ScannerDedupConfigurationHistory
{
    public int Id { get; set; }

    public string Importer { get; set; } = null!;

    public string? OldStrategyChain { get; set; }

    public string NewStrategyChain { get; set; } = null!;

    public string? OldHashFields { get; set; }

    public string? NewHashFields { get; set; }

    public bool? OldAutoCloseMissing { get; set; }

    public bool NewAutoCloseMissing { get; set; }

    public int? UserId { get; set; }

    public DateTime ChangedAt { get; set; }

    public virtual User? User { get; set; }
}

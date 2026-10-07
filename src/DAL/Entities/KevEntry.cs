namespace DAL.Entities;

/// <summary>
/// One entry of the CISA Known Exploited Vulnerabilities catalogue (Stage 9.4, T163, S45 §4.3), table
/// <c>kev_entries</c>.
///
/// A row is never deleted. Leaving the catalogue is recorded, not erased: <see cref="DelistedAt"/> when a
/// valid catalogue drops it under the shrink guard, <see cref="DelistingHeldSince"/> when the guard held
/// the delisting (S45 §4.8). An unavailable or malformed catalogue changes neither.
/// </summary>
public class KevEntry
{
    public int Id { get; set; }

    public string CveId { get; set; } = null!;

    public string? VendorProject { get; set; }

    public string? Product { get; set; }

    public string? VulnerabilityName { get; set; }

    public string? ShortDescription { get; set; }

    public string? RequiredAction { get; set; }

    public string? Notes { get; set; }

    /// <summary>CWE identifiers, comma-separated.</summary>
    public string? Cwes { get; set; }

    public DateTime DateAdded { get; set; }

    public DateTime? DueDate { get; set; }

    /// <summary>CISA's <c>knownRansomwareCampaignUse</c> is "Known".</summary>
    public bool KnownRansomwareUse { get; set; }

    public DateTime? DelistedAt { get; set; }

    public DateTime? DelistingHeldSince { get; set; }

    /// <summary>The first synchronization that saw the entry.</summary>
    public DateTime CreatedAt { get; set; }

    /// <summary>Only when a catalogue field or one of the two states changed.</summary>
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Listed: not delisted. A held delisting is still listed.</summary>
    public bool IsListed => DelistedAt == null;
}

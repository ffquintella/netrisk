namespace DAL.Entities;

/// <summary>
/// The columns Track 4's posture integrations add to <c>hosts</c> — external identity (4.4.2),
/// operating-system detail, business criticality, and the vendor risk score (4.4.4).
///
/// In a partial rather than edited into the generated entity so that file stays regenerable from the
/// database.
/// </summary>
public partial class Host
{
    /// <summary>
    /// The provider's own id for this asset — a Vision One <c>agentGuid</c>, a SecurityScorecard
    /// domain. Together with <see cref="ExternalProvider"/> this is what makes a resync update the
    /// same host instead of creating a second one, in the cases where hostname and MAC both moved.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>Which integration owns <see cref="ExternalId"/> — <c>TrendMicroVisionOne</c>, <c>SecurityScorecard</c>.</summary>
    public string? ExternalProvider { get; set; }

    /// <summary>OS version/build, kept apart from the free-text <c>Os</c> the scanners fill in.</summary>
    public string? OsVersion { get; set; }

    /// <summary>
    /// Business criticality on a 1–5 scale, synced from the provider's asset classification. Not an
    /// enum: providers disagree about how many bands they have, and clamping a five-band vendor
    /// scale into a three-value enum loses the distinction the customer configured.
    /// </summary>
    public int? Criticality { get; set; }

    /// <summary>Vendor cyber-risk score, 0–100. Higher is worse, matching Vision One's convention.</summary>
    public int? RiskScore { get; set; }

    /// <summary>Where <see cref="RiskScore"/> came from, so two providers cannot silently overwrite each other.</summary>
    public string? RiskScoreSource { get; set; }

    public DateTime? RiskScoreUpdatedAt { get; set; }
}

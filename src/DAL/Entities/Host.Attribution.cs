using System;

namespace DAL.Entities;

/// <summary>
/// M58 (S39 §8.2, D-18, D-20): when a scan last assessed the host, and who attributed it to its entity.
/// </summary>
public partial class Host
{
    /// <summary>
    /// When a scan last assessed this host, in UTC (T281, D-18). Written <em>only</em> by finding ingestion from a
    /// scanner or code importer (<c>FindingIngestionService</c>, see <c>HostAssessmentPolicy</c>) — never by the Jira
    /// Assets import, the Vision One inventory or CVE feed, the SecurityScorecard pseudo-host or a person, all of which
    /// stamp <see cref="LastVerificationDate"/>. That is why the risk index can measure a scan reading's age from it: the
    /// verification date says the host exists, this says a scanner looked at it. The hosts API ignores it on the way in.
    /// </summary>
    public DateTime? LastAssessedAt { get; set; }

    /// <summary>
    /// Who wrote <see cref="EntityId"/> (T282, D-20): an <see cref="EntitySources"/> value; null = unknown (written before
    /// the column existed) or unassigned. The hosts API stamps it; a body's own value is never trusted.
    /// </summary>
    public string? EntitySource { get; set; }
}

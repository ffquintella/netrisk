using DAL.Entities;

namespace ServerServices.Interfaces;

/// <summary>
/// Reads and prunes the field-level governance trail (Track 8 milestone 8.4).
///
/// Writing it is <c>GovernanceAuditInterceptor</c>'s job, in the DAL, so no service can forget.
/// This is the read side: the per-record trail an operator opens, the per-entity/period evidence
/// pack an auditor asks for, and the retention job.
/// </summary>
public interface IAuditTrailService
{
    /// <summary>Every recorded change to one governance record, newest first.</summary>
    Task<List<AuditLog>> GetForRecordAsync(string entityType, int entityId, int limit = 500);

    /// <summary>
    /// The whole trail around one risk: the risk row itself plus its scoring, mitigations,
    /// mitigation tasks, reviews and acceptances. This is what "who changed what on this risk" means
    /// to somebody who does not know how the aggregate is split across tables.
    /// </summary>
    Task<List<AuditLog>> GetForRiskAsync(int riskId, int limit = 1000);

    /// <summary>The trail for a business entity over a period, for the evidence export.</summary>
    Task<List<AuditLog>> GetForEntityPeriodAsync(int? entityId, DateTime fromUtc, DateTime toUtc,
        int limit = 20000);

    /// <summary>
    /// The full auditor evidence pack for one entity and period (8.4.2): the acceptances, the
    /// management reviews and their counter-signatures, the business review campaign decisions
    /// (8.6.5), and the field-level trail underneath as corroboration.
    ///
    /// Assembled once and rendered by both the CSV and the PDF path, so the two cannot disagree
    /// about what the evidence is.
    /// </summary>
    Task<Model.Governance.GovernanceEvidencePack> GetEvidencePackAsync(int? entityId, DateTime fromUtc,
        DateTime toUtc, string requestedBy, int changeLimit = 20000);

    /// <summary>
    /// Deletes rows older than the retention window. Returns how many went.
    /// A retention policy that is documented and not implemented is worse than none: it tells an
    /// operator the data is gone when it is not.
    /// </summary>
    Task<int> ApplyRetentionAsync(DateTime asOfUtc);
}

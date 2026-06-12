namespace DAL.Enums;

/// <summary>
/// Persisted risk status (Track 6 Phase 5: <c>risks.status_id</c> int column).
/// Mirrors <c>Model.Risks.RiskStatus</c> member-for-member and in the same ordinal order so the
/// int values match — DAL cannot reference Model (Model -> DAL is the project dependency direction),
/// so the entity-side enum is duplicated here, following the <see cref="TransactionResult"/> convention.
/// During the create-copy-coexist cycle the legacy free-text <c>risks.status</c> column remains the
/// source of truth; <c>status_id</c> is the backfilled, type-safe replacement (old column dropped in a
/// later release, never in the same one that introduces this).
/// </summary>
public enum RiskStatus
{
    New = 0,
    MitigationPlanned = 1,
    ManagementReview = 2,
    Closed = 3,
}

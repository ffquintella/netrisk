namespace DAL.Enums;

/// <summary>
/// The triage lifecycle of a single finding (Track 3 milestone 3.2.1), persisted in
/// <c>vulnerabilities.status_id</c>.
///
/// Deliberately separate from the general-purpose <c>Model.IntStatus</c> that the register's legacy
/// workflow column uses. <c>IntStatus</c> is a fifty-value set shared by jobs, messages, incidents
/// and risks; a finding lifecycle needs a closed set with an enforced transition matrix, and mixing
/// the two would let any of those fifty values appear where the state machine expects one of seven.
///
/// The value set follows DefectDojo's, which is the de-facto standard for scanner triage and what
/// anyone migrating from it will expect. Unlike <see cref="RiskStatus"/> this enum is <em>not</em>
/// duplicated into <c>Model</c>: Model references DAL, so one definition serves every tier and
/// there is nothing to drift.
/// </summary>
public enum FindingStatus
{
    /// <summary>Open and untriaged, or triaged and confirmed outstanding.</summary>
    Active = 1,

    /// <summary>A human confirmed the finding is real. Still open work.</summary>
    Verified = 2,

    /// <summary>Not a real defect. Suppressed: a re-import will not reactivate it.</summary>
    FalsePositive = 3,

    /// <summary>Real, but outside the assessment's scope (a test host, a third-party asset).
    /// Suppressed like <see cref="FalsePositive"/>.</summary>
    OutOfScope = 4,

    /// <summary>The same defect as another finding, which is the canonical one.</summary>
    Duplicate = 5,

    /// <summary>Covered by an authorized, expiring risk acceptance. Suppressed until it lapses.</summary>
    RiskAccepted = 6,

    /// <summary>Remediated. A scanner seeing it again is a regression and reactivates it.</summary>
    Mitigated = 7
}

/// <summary>
/// The semantics of <see cref="FindingStatus"/> — which states are open, suppressed, or accrue SLA.
///
/// Beside the enum rather than in <c>Model</c> so that the entity itself can use them (DAL cannot
/// reference Model) and every tier above still gets one definition. Duplicating these predicates
/// per tier is what lets a dashboard and an SLA report disagree about the same finding.
/// </summary>
public static class FindingStatusExtensions
{
    /// <summary>
    /// States in which a re-import must not reactivate the finding. This is the "sticky triage"
    /// property: a scanner reports the same thing every run, and a false positive that comes back
    /// as Active on every scan is how a register becomes unusable.
    /// </summary>
    public static bool IsSuppressed(this FindingStatus status) =>
        status is FindingStatus.FalsePositive or FindingStatus.OutOfScope or FindingStatus.RiskAccepted;

    /// <summary>States that count as outstanding work — what dashboards and SLAs measure.</summary>
    public static bool IsOpen(this FindingStatus status) =>
        status is FindingStatus.Active or FindingStatus.Verified;

    /// <summary>
    /// Transitions into these states require a stated reason. Suppressing a finding is a decision
    /// someone has to defend to an auditor, and an unexplained suppression is indistinguishable
    /// from a mistake.
    /// </summary>
    public static bool RequiresJustification(this FindingStatus status) =>
        status.IsSuppressed() || status == FindingStatus.Duplicate;

    /// <summary>
    /// Whether the SLA clock runs in this state. A finding nobody is allowed to work on — because
    /// it was accepted, or ruled out of scope — should not accrue overdue days.
    /// </summary>
    public static bool AccruesSla(this FindingStatus status) => status.IsOpen();
}

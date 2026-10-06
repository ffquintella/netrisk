namespace DAL.Enums;

/// <summary>
/// How well the evidence behind a risk scenario is established (Stage 9.2, S42 §4.1) — the
/// "qualidade da evidência" the MIGR-TI/IA Phase 2 quality rules ask every scenario to declare.
///
/// Persisted as <c>risks.evidence_confidence</c>, nullable: a legacy risk has no declared confidence,
/// and NULL means exactly that — "not declared" — rather than being read as any of the three levels.
/// <see cref="Hypothesis"/> is the default a promoted pending risk receives (S42 §11, D5).
///
/// Not <c>AssessmentScoring.CvssReportConfidence</c>, which is the CVSS temporal metric of one
/// vulnerability report and says nothing about the scenario.
/// </summary>
public enum EvidenceConfidence
{
    /// <summary>The scenario is supported by direct evidence: an incident, a test, an audit finding.</summary>
    Confirmed = 1,

    /// <summary>Indirect evidence points at it: a near miss, a threat report, an analogous case.</summary>
    Indicative = 2,

    /// <summary>A plausible scenario nobody has evidence for yet.</summary>
    Hypothesis = 3
}

/// <summary>
/// Where a pending risk — the register's hypothesis record — came from (Stage 9.2, S42 §4.2).
/// Persisted as <c>pending_risks.origin</c>, <c>NOT NULL DEFAULT 1</c>, so every row that predates
/// the column reads as <see cref="Assessment"/>, which is the only origin that existed.
/// </summary>
public enum PendingRiskOrigin
{
    /// <summary>Raised by an assessment answer; carries <c>assessment_id</c> and <c>assessment_answer_id</c>.</summary>
    Assessment = 1,

    /// <summary>Registered directly through <c>POST /Risks/Pending</c>; carries no assessment, and records its author.</summary>
    Standalone = 2
}

/// <summary>
/// Whether an <c>incidents</c> row is an incident or a near miss (Stage 9.2, S42 §4.3).
///
/// A near miss is an event that could have caused harm and did not. It is recorded on the same table,
/// numbered in the same yearly sequence, because the methodology asks for both to feed the same
/// learning loop (Phase 7 triggers, Phase 4 backtesting) — but it is not an incident, and the one
/// aggregate that calls rows "open incidents" (the Master Dashboard posture) leaves it out.
/// Orthogonal to <c>incidents.Category</c>, which is the threat type (phishing, malware…): a phishing
/// near miss and a phishing incident share the category and differ here.
/// </summary>
public enum IncidentKind
{
    Incident = 1,
    NearMiss = 2
}

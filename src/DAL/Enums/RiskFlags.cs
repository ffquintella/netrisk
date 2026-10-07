namespace DAL.Enums;

/// <summary>
/// The eleven mandatory flags of the MIGR-TI/IA methodology (Phase 2), numbered as the methodology
/// numbers them, plus the one Gate A condition that is not a flag (Stage 9.5, S46 §4.1).
///
/// Stored as <c>risk_flags.flag</c> (int, <c>ck_risk_flags_flag</c> 1–12). The values are the
/// methodology's numbers on purpose: "flag 3" in a report, a test and a query mean the same row.
/// </summary>
public enum RiskFlagCode
{
    /// <summary>1 — life, health or human safety. Declared. Gate A.</summary>
    HumanSafety = 1,

    /// <summary>2 — legal/regulatory obligation or LGPD. Declared (derivable from the LGPD catalogue in 9.11). Gate A.</summary>
    LegalRegulatory = 2,

    /// <summary>3 — known exploitation (CISA KEV) or active attack. Derived from KEV, also declarable. Gate A.</summary>
    KnownExploitation = 3,

    /// <summary>4 — critical process with RTO/RPO threatened. Derived from the BIA, also declarable. Not Gate A (S46 D5).</summary>
    CriticalProcessContinuity = 4,

    /// <summary>5 — sensitive personal data, large volume or strategic research. Derived from data classification, also declarable.</summary>
    SensitiveData = 5,

    /// <summary>6 — systemic risk or single point of failure. Declared.</summary>
    SystemicSinglePointOfFailure = 6,

    /// <summary>7 — concentration in a third party, cloud or identity. Declared (third-party register in 9.10).</summary>
    ThirdPartyConcentration = 7,

    /// <summary>8 — low probability, catastrophic impact. Declared (tail statistics in 9.7).</summary>
    LowProbabilityCatastrophic = 8,

    /// <summary>9 — emerging risk or rapid growth. Declared (KRIs in 9.8).</summary>
    EmergingRapidGrowth = 9,

    /// <summary>10 — high uncertainty or weak evidence. Declared; evidence confidence (M40) sits beside it.</summary>
    HighUncertainty = 10,

    /// <summary>11 — AI risk: discrimination, hallucination, prompt injection, drift. Declarable only until 9.12.</summary>
    ArtificialIntelligence = 11,

    /// <summary>
    /// "Risk without legitimate acceptance" — a Gate A condition of Phase 4 that is <b>not</b> one of the
    /// eleven flags (S46 D6). Declared only.
    /// </summary>
    NoLegitimateAcceptance = 12
}

/// <summary>The four Phase 4 decisions (<c>risk_decisions.decision</c>, S46 §4.3).</summary>
public enum RiskDecisionKind
{
    /// <summary>🔴 Act immediately — immediate escalation, notified. Forced by Gate A; never derived from severity.</summary>
    ActImmediately = 1,

    /// <summary>🟡 Treat within the cycle.</summary>
    TreatInCycle = 2,

    /// <summary>🔵 Monitor / accept. A classification only — the acceptance itself is a <c>risk_acceptances</c> row.</summary>
    MonitorAccept = 3,

    /// <summary>⚪ Archive. A classification only — closing is <c>POST /Risks/{id}/Closure</c>.</summary>
    Archive = 4
}

/// <summary>Who recorded a decision (<c>risk_decisions.source</c>).</summary>
public enum RiskDecisionSource
{
    /// <summary>A person, through <c>POST /RiskFlags/Risks/{id}/Decisions</c>.</summary>
    Declared = 1,

    /// <summary>The system, at the onset of a Gate A condition.</summary>
    GateA = 2
}

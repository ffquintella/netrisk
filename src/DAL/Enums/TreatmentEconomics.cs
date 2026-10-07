namespace DAL.Enums;

/// <summary>
/// The four treatment options of MIGR-TI/IA Phase 5 — avoid, reduce, transfer/share, accept (Stage 9.6,
/// S47 §4.5). Stored as <c>mitigation_economics.treatment_option</c> (int,
/// <c>ck_mitigation_economics_treatment_option</c> 1–4).
///
/// Typed, beside the editable <c>planning_strategy</c> label table rather than read from it: that table is
/// renamed by administrators, so no code can rely on its value 5 meaning "transfer" (S47 D2).
/// </summary>
public enum TreatmentOption
{
    /// <summary>Stop the activity that produces the risk. Modelled as 100 % effectiveness.</summary>
    Avoid = 1,

    /// <summary>Controls that lower frequency or magnitude — the mitigation's declared effectiveness.</summary>
    Reduce = 2,

    /// <summary>Insurance, contract or SLA; the effectiveness declares the share transferred. Needs a counterparty.</summary>
    TransferShare = 3,

    /// <summary>
    /// Retain the risk. A planning declaration only: it creates no <c>RiskAcceptance</c>, is not applicable to
    /// Gate C, and is refused while Gate A holds.
    /// </summary>
    Accept = 4
}

namespace DAL.Enums;

/// <summary>
/// The seven forms of loss of MIGR-TI/IA Phase 3 — "response, recovery, productivity, revenue, liability, fines when
/// legally applicable, estimated reputation" (Stage 9.7, S48 §4.1). Stored as <c>risk_loss_components.component</c> and
/// <c>risk_tail_components.component</c> (int, CHECK 1–7).
/// </summary>
public enum LossComponent
{
    /// <summary>Incident response: investigation, containment, external responders, notification.</summary>
    Response = 1,

    /// <summary>Recovery: restoring systems and data, replacement of assets.</summary>
    Recovery = 2,

    /// <summary>Lost productivity while the service or the people are unavailable.</summary>
    Productivity = 3,

    /// <summary>Revenue lost or deferred.</summary>
    Revenue = 4,

    /// <summary>Liability: compensation, settlements, contractual penalties.</summary>
    Liability = 5,

    /// <summary>Regulatory fines — only with a written legal basis (<c>ck_risk_loss_components_fine_basis</c>).</summary>
    Fine = 6,

    /// <summary>Estimated reputational loss.</summary>
    Reputation = 7
}

/// <summary>
/// The <c>settings</c> keys of the flag 8 derivation (S48 §4.8). Not seeded — the code defaults apply until an
/// administrator writes the row — and audited by key, like the continuity parameters, because they change a
/// governance result.
/// </summary>
public static class TailRiskSettingKeys
{
    /// <summary>Annual probability of a loss year at or below which a scenario is "low probability". Default 0.10, range (0, 0.5].</summary>
    public const string TailFlagMaxAnnualProbability = "tail_flag_max_annual_probability";

    /// <summary>Mean loss of a loss year at or above which the impact is "catastrophic". Default: the top quantitative band threshold.</summary>
    public const string TailFlagCatastrophicLoss = "tail_flag_catastrophic_loss";
}

/// <summary>Which Monte Carlo run a tail-statistics row describes (S48 §4.2). CHECK 1–2.</summary>
public enum TailRun
{
    /// <summary>Before treatment — the run the mapped score and flag 8 read.</summary>
    Inherent = 1,

    /// <summary>With the latest mitigation's effectiveness applied; absent when the effectiveness is zero.</summary>
    Residual = 2
}

/// <summary>Where the per-event loss magnitude of a run came from (S48 §4.4). CHECK 1–2.</summary>
public enum MagnitudeSource
{
    /// <summary>The single calibrated range of <c>risk_scoring.quant_loss_*</c>.</summary>
    SingleRange = 1,

    /// <summary>The sum of the declared loss components, each sampled independently per event.</summary>
    Components = 2
}

namespace DAL.Enums;

/// <summary>
/// The Track 8 <c>settings</c> rows that move the risk index (S39 D-22, §9.5, T283). Declared in DAL because the
/// governance audit interceptor, which lives here, records exactly the keys it is given; the services that read them
/// alias these constants, so the key read and the key audited cannot drift apart.
/// </summary>
public static class GovernanceSettingKeys
{
    /// <summary>Whether the review cadence keys off the inherent or the residual score (<c>MgmtReviewsService</c>).</summary>
    public const string NextReviewDateUses = "next_review_date_uses";

    /// <summary>The three ascending monetary thresholds of the quantitative bands (<c>QuantitativeRiskService</c>).</summary>
    public const string QuantitativeBandThresholds = "quantitative_band_thresholds";

    /// <summary>The strategy that computes the classic residual (<c>RiskCalculationService</c>).</summary>
    public const string RiskWorkflowResidualStrategy = "risk_workflow_residual_strategy";
}

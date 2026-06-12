namespace Model.Risks;

public class RiskHelper
{
    public static string GetRiskStatusName(RiskStatus riskStatus)
    {
        return riskStatus switch
        {
            RiskStatus.New => "New",
            RiskStatus.MitigationPlanned => "Mitigation Planned",
            RiskStatus.ManagementReview => "Mgmt Reviewed",
            RiskStatus.Closed => "Closed",
            _ => throw new ArgumentOutOfRangeException(nameof(riskStatus), riskStatus, null)
        };
    }

    /// <summary>
    /// Inverse of <see cref="GetRiskStatusName"/>: maps a legacy free-text <c>risks.status</c> value to its
    /// <see cref="RiskStatus"/>, or <c>null</c> for any value outside the known set. This is the documented,
    /// testable mirror of the Track 6 Phase 5 SQL backfill of <c>risks.status_id</c> (New=0, Mitigation
    /// Planned=1, Mgmt Reviewed=2, Closed=3); unknown legacy values stay NULL rather than defaulting to New.
    /// </summary>
    public static RiskStatus? GetRiskStatusFromName(string? statusName)
    {
        return statusName switch
        {
            "New" => RiskStatus.New,
            "Mitigation Planned" => RiskStatus.MitigationPlanned,
            "Mgmt Reviewed" => RiskStatus.ManagementReview,
            "Closed" => RiskStatus.Closed,
            _ => null
        };
    }
}
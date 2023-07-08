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
}
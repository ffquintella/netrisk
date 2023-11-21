namespace Tools.Risks;

public class RiskCalculationTool
{
    public static float CalculateTotalRiskScore(float calculatedRiskScore, float? contributingRiskScore)
    {

        if(contributingRiskScore == null) return calculatedRiskScore;
        
        return (calculatedRiskScore + (2 * contributingRiskScore.Value)) / 3;

    }
}
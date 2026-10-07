using DAL.Entities;
using Model.TailRisk;
using Tools.TailRisk;

namespace ServerServices.Governance;

/// <summary>
/// The one place a stored tail-statistics row becomes a DTO or the pure <see cref="TailStatistics"/> Gate B compares
/// (Stage 9.7, S48 §4.2, §4.7), so the quantitative result, the risk's tail view, the appetite and the portfolio read
/// the same numbers.
/// </summary>
public static class TailStatisticsMapping
{
    public static TailStatisticsDto ToDto(RiskTailStatistics row) => new()
    {
        Run = row.Run,
        Iterations = row.Iterations,
        Seed = row.Seed,
        ConfidenceLevel = row.ConfidenceLevel,
        ExpectedLoss = row.ExpectedLoss,
        ExpectedLossCiLow = row.ExpectedLossCiLow,
        ExpectedLossCiHigh = row.ExpectedLossCiHigh,
        P95 = row.P95,
        P95CiLow = row.P95CiLow,
        P95CiHigh = row.P95CiHigh,
        Cvar95 = row.Cvar95,
        Cvar95CiLow = row.Cvar95CiLow,
        Cvar95CiHigh = row.Cvar95CiHigh,
        ProbabilityOfLoss = row.ProbabilityOfLoss,
        ConditionalLoss = row.ConditionalLoss,
        MagnitudeSource = row.MagnitudeSource,
        MitigationEffectiveness = row.MitigationEffectiveness,
        ComputedAt = row.ComputedAt,
        Components = row.Components
            .OrderBy(c => c.Component)
            .Select(c => new LossComponentContributionDto
            {
                Component = c.Component,
                Min = c.LossMin,
                MostLikely = c.LossMostLikely,
                Max = c.LossMax,
                ExpectedLoss = c.ExpectedLoss,
                Cvar95 = c.Cvar95
            })
            .ToList()
    };

    public static TailStatistics ToStatistics(RiskTailStatistics row) => new()
    {
        Iterations = row.Iterations,
        ExpectedLoss = row.ExpectedLoss,
        ExpectedLossCiLow = row.ExpectedLossCiLow,
        ExpectedLossCiHigh = row.ExpectedLossCiHigh,
        P95 = row.P95,
        P95CiLow = row.P95CiLow,
        P95CiHigh = row.P95CiHigh,
        Cvar95 = row.Cvar95,
        Cvar95CiLow = row.Cvar95CiLow,
        Cvar95CiHigh = row.Cvar95CiHigh,
        ProbabilityOfLoss = row.ProbabilityOfLoss,
        ConditionalLoss = row.ConditionalLoss
    };

    /// <summary>The scenario tolerances of an appetite's tail limits, or null when it has none.</summary>
    public static TailLimits? ScenarioLimits(RiskAppetiteTailLimit? limits) => limits is null
        ? null
        : new TailLimits((double?)limits.MaxScenarioExpectedLoss, (double?)limits.MaxScenarioP95,
            (double?)limits.MaxScenarioCvar95);

    /// <summary>The portfolio tolerances of an appetite's tail limits, or null when it has none.</summary>
    public static TailLimits? PortfolioLimits(RiskAppetiteTailLimit? limits) => limits is null
        ? null
        : new TailLimits((double?)limits.MaxPortfolioExpectedLoss, (double?)limits.MaxPortfolioP95,
            (double?)limits.MaxPortfolioCvar95);

    /// <summary>The run Gate B reads: the residual where it exists, the inherent otherwise — the appetite's rule.</summary>
    public static RiskTailStatistics? Governing(IEnumerable<RiskTailStatistics> rows)
    {
        var list = rows.ToList();
        return list.FirstOrDefault(r => r.Run == DAL.Enums.TailRun.Residual)
               ?? list.FirstOrDefault(r => r.Run == DAL.Enums.TailRun.Inherent);
    }
}

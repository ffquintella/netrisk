using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model;
using Serilog;
using ServerServices.Events;
using ServerServices.Governance;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class RiskCalculationService(
    ILogger logger,
    IDalService dalService,
    IEnumerable<IResidualRiskStrategy> residualStrategies): ServiceBase(logger, dalService), IRiskCalculationService
{
    /// <summary>Which residual formula is in force. Names an <see cref="IResidualRiskStrategy"/>.</summary>
    public const string ResidualStrategySetting = "risk_workflow_residual_strategy";

    
    public event EventHandler<RiskCalculationEventArgs> RiskScoreCalculated = delegate { };
    protected virtual void OnRiskScoreCalculated(RiskCalculationEventArgs e)
    {
        RiskScoreCalculated.Invoke(this, e);
    }
    
    public event EventHandler<RiskCalculationEventArgs> RiskContributingImpactCalculated = delegate { };
    
    protected virtual void OnRiskContributingImpactCalculated(RiskCalculationEventArgs e)
    {
        RiskContributingImpactCalculated.Invoke(this, e);
    }
    
    public async Task CalculateRiskScoreAsync()
    {
        await using var context = DalService.GetContext();
        
        // Get Risks with vulnerabilities
        var risks = context.Risks
            .ToList();
        
        var riskModelValues = context.CustomRiskModelValues.ToList();


        foreach (var risk in risks)
        {
            var scoring = context.RiskScorings.FirstOrDefault(rs => rs.Id == risk.Id);

            if (scoring == null)
                scoring = new RiskScoring()
                {
                    Id = risk.Id,
                    ScoringMethod = 1,
                    ClassicImpact = 2,
                    ClassicLikelihood = 2,
                };
            

            scoring.CalculatedRisk =  Convert.ToSingle(riskModelValues.FirstOrDefault(rmv => rmv.Impact == scoring.ClassicImpact && rmv.Likelihood == scoring.ClassicLikelihood)?.Value ?? 0.0);
            await context.SaveChangesAsync();
            
            OnRiskScoreCalculated( new RiskCalculationEventArgs()
            {
                RiskScoring = scoring,
            });

        }
        
        Log.Information("Risk scores calculated");
    }

    public async Task CalculateContributingImpactAsync()
    {
        await using var context = DalService.GetContext();
        
        Console.WriteLine("Calculating contributing impacts");

        try
        {
            // Get Risks with vulnerabilities
            var risks = context.Risks
                .Include(risk => risk.Vulnerabilities.Where(v => 
                    v.Status != (int)IntStatus.Closed &&
                    v.Status != (int)IntStatus.Solved &&
                    v.Status != (int)IntStatus.Rejected &&
                    v.Status != (int)IntStatus.Retired &&
                    v.Status != (int)IntStatus.Fixed
                ))
                .Where(r => r.Vulnerabilities.Count > 0)
                .ToList();

            const int NMaxVulDiv = 10;

            // Get all vulnerabilities for each risk
            foreach (var risk in risks)
            {
                var scoring = context.RiskScorings.FirstOrDefault(rs => rs.Id == risk.Id);
                if (scoring == null) continue;
                
                if(risk.Vulnerabilities.Count == 0) 
                {
                    scoring.ContributingScore = 0;
                    await context.SaveChangesAsync();
                    continue;
                }
                
                var topScore = risk.Vulnerabilities.Max(v => v.Score)!.Value;

                var deltaConst = 10 - topScore;

                var totalSum = risk.Vulnerabilities.Sum(v => v.Score)!.Value;

                var contributingRiskScore = 0 + topScore;
                foreach (var vul in risk.Vulnerabilities)
                {
                    var vulcontrib = (deltaConst / (topScore * NMaxVulDiv)) * vul.Score!.Value;
                    contributingRiskScore += vulcontrib;
                }

                if (contributingRiskScore > 10) contributingRiskScore = 10;
                scoring.ContributingScore = contributingRiskScore;
                await context.SaveChangesAsync();
                
                OnRiskScoreCalculated( new RiskCalculationEventArgs()
                {
                    RiskScoring = scoring,
                });
            }
            Log.Information("Contributing impacts calculated");
        }
        catch (Exception e)
        {
            Log.Error("Error calculating contributing impacts: {Message}", e.Message);
            Console.WriteLine(e);
            throw;
        }
    }

    /// <summary>
    /// Track 8 milestone 8.2.1 — the residual pass.
    ///
    /// The formula itself lives in a strategy so the organization can change it without changing
    /// this loop, and so 8.7's quantitative method can supply its own. A risk with no treatment gets
    /// a null residual rather than a residual equal to its inherent score: the first says nobody has
    /// assessed the treatment, the second says the treatment achieves nothing, and those are
    /// different statements to put in front of an auditor.
    ///
    /// A history row is written only when the value actually moves. The job runs on a schedule, and
    /// writing an identical row every run would bury the changes that matter under thousands that
    /// do not.
    /// </summary>
    public async Task<int> CalculateResidualRiskAsync()
    {
        await using var context = DalService.GetContext();

        var strategy = await ResolveStrategyAsync(context);

        var risks = await context.Risks.ToListAsync();
        var mitigations = await context.Mitigations.ToListAsync();
        var controls = await context.MitigationToControls.ToListAsync();

        var changed = 0;

        foreach (var risk in risks)
        {
            var scoring = await context.RiskScorings.FirstOrDefaultAsync(rs => rs.Id == risk.Id);
            if (scoring == null) continue;

            var mitigation = mitigations
                .Where(m => m.RiskId == risk.Id)
                .OrderByDescending(m => m.LastUpdate)
                .FirstOrDefault();

            var attached = mitigation == null
                ? new List<MitigationToControl>()
                : controls.Where(c => c.MitigationId == mitigation.Id).ToList();

            var residual = strategy.Compute(new ResidualRiskContext(risk, scoring, mitigation, attached));

            if (Nearly(scoring.ResidualRisk, residual)) continue;

            scoring.ResidualRisk = residual;
            scoring.ResidualUpdatedAt = DateTime.UtcNow;

            context.RiskScoringHistories.Add(new RiskScoringHistory
            {
                RiskId = risk.Id,
                CalculatedRisk = scoring.CalculatedRisk,
                ResidualRisk = residual,
                LastUpdate = DateTime.UtcNow
            });

            changed++;
        }

        await context.SaveChangesAsync();

        Log.Information("Residual risk recalculated for {Count} risks using the {Strategy} strategy",
            changed, strategy.Name);

        return changed;
    }

    /// <summary>
    /// The configured strategy, or the first registered one when the setting names something that is
    /// not registered. Falling back rather than throwing is deliberate: a typo in a setting must not
    /// stop the whole calculation job, and the warning says which strategy actually ran.
    /// </summary>
    private async Task<IResidualRiskStrategy> ResolveStrategyAsync(DAL.Context.AuditableContext context)
    {
        var strategies = residualStrategies.ToList();
        if (strategies.Count == 0)
            throw new InvalidOperationException(
                "No residual-risk strategy is registered, so the residual score cannot be derived.");

        var setting = await context.Settings.FirstOrDefaultAsync(s => s.Name == ResidualStrategySetting);
        var name = setting?.Value?.Trim();

        if (string.IsNullOrWhiteSpace(name)) return strategies[0];

        var match = strategies.FirstOrDefault(s =>
            string.Equals(s.Name, name, StringComparison.OrdinalIgnoreCase));

        if (match != null) return match;

        Log.Warning("Residual-risk strategy '{Configured}' is not registered; falling back to {Fallback}",
            name, strategies[0].Name);

        return strategies[0];
    }

    /// <summary>Float comparison with a tolerance, so a rounding wobble is not a change.</summary>
    private static bool Nearly(float? a, float? b)
    {
        if (a is null && b is null) return true;
        if (a is null || b is null) return false;
        return System.Math.Abs(a.Value - b.Value) < 0.0001f;
    }
}

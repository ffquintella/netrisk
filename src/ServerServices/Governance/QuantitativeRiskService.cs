using System.Text.Json;
using DAL.Entities;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Governance;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.Risks;

namespace ServerServices.Governance;

/// <summary>
/// Track 8 milestone 8.7.2 — the FAIR-lite scoring method.
///
/// Inputs are calibrated three-point ranges for loss-event frequency and loss magnitude; the
/// simulation (in <see cref="MonteCarloRiskSimulator"/>, which is pure and lives in
/// <c>Tools</c> so it is testable without a database) produces an annualized-loss distribution; the
/// percentiles and the loss-exceedance curve are cached on the scoring row.
///
/// The bridge back to the rest of the product is <see cref="MapToScore"/>: the median annualized loss
/// is mapped onto the existing 0–10 scale by configurable monetary thresholds, so a quantitatively
/// scored risk still sorts in the same list, colours in the same heatmap, and is gated by the same
/// appetite rules as a matrix-scored one. Without that bridge a second scoring method would be a
/// second product.
/// </summary>
public class QuantitativeRiskService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), IQuantitativeRiskService
{
    /// <summary>The scoring method id this service owns. 1 is Classic; 2 is CVSS in the seeded table.</summary>
    public const int QuantitativeScoringMethod = 3;

    public const string BandThresholdsSetting = "quantitative_band_thresholds";
    public const string IterationsSetting = "quantitative_iterations";

    /// <summary>
    /// The monetary boundaries between Low/Medium/High/Very High, ascending. Defaults chosen to
    /// match the seeded impact-scale anchors so the two scales tell the same story.
    /// </summary>
    public static readonly double[] DefaultBandThresholds = [10_000, 100_000, 1_000_000];

    public async Task<QuantitativeRiskResult> ComputeAndSaveAsync(int riskId, QuantitativeRiskInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var frequency = new CalibratedRange(input.LossEventFrequencyMin, input.LossEventFrequencyMostLikely,
            input.LossEventFrequencyMax);
        var magnitude = new CalibratedRange(input.LossMagnitudeMin, input.LossMagnitudeMostLikely,
            input.LossMagnitudeMax);

        if (!frequency.IsValid)
            throw new InvalidParameterException(nameof(input.LossEventFrequencyMostLikely),
                "The loss-event frequency range has to run minimum ≤ most likely ≤ maximum, with no " +
                "negative values. An unordered range is not an estimate, it is a typo.");

        if (!magnitude.IsValid)
            throw new InvalidParameterException(nameof(input.LossMagnitudeMostLikely),
                "The loss-magnitude range has to run minimum ≤ most likely ≤ maximum, with no negative " +
                "values.");

        await using var db = DalService.GetContext();

        var risk = await db.Risks.FirstOrDefaultAsync(r => r.Id == riskId)
                   ?? throw new DataNotFoundException("local", "risks",
                       new Exception($"Risk with id {riskId} not found"));

        var scoring = await db.RiskScorings.FirstOrDefaultAsync(s => s.Id == riskId);
        if (scoring is null)
        {
            scoring = new RiskScoring { Id = riskId, ScoringMethod = QuantitativeScoringMethod };
            db.RiskScorings.Add(scoring);
        }

        var iterations = input.Iterations ?? await ReadIntSettingAsync(db, IterationsSetting,
            MonteCarloRiskSimulator.DefaultIterations);
        var seed = input.Seed ?? 20260826;

        var inherent = MonteCarloRiskSimulator.Run(frequency, magnitude, iterations, seed);

        // The residual run differs only in the mitigation's effectiveness, so the two numbers are
        // comparable by construction — which is what makes the before/after a control-ROI statement
        // rather than two unrelated simulations.
        var effectiveness = await EffectiveMitigationAsync(db, risk, scoring);
        LossExposureResult? residual = null;
        if (effectiveness > 0)
            residual = MonteCarloRiskSimulator.Run(frequency, magnitude, iterations, seed, effectiveness);

        scoring.ScoringMethod = QuantitativeScoringMethod;
        scoring.QuantLefMin = frequency.Min;
        scoring.QuantLefMostLikely = frequency.MostLikely;
        scoring.QuantLefMax = frequency.Max;
        scoring.QuantLossMin = magnitude.Min;
        scoring.QuantLossMostLikely = magnitude.MostLikely;
        scoring.QuantLossMax = magnitude.Max;
        scoring.QuantAleP10 = inherent.P10;
        scoring.QuantAleP50 = inherent.P50;
        scoring.QuantAleP90 = inherent.P90;
        scoring.QuantAleMean = inherent.Mean;
        scoring.QuantResidualAleP10 = residual?.P10;
        scoring.QuantResidualAleP50 = residual?.P50;
        scoring.QuantResidualAleP90 = residual?.P90;
        scoring.QuantSeed = seed;
        scoring.QuantComputedAt = DateTime.UtcNow;
        scoring.QuantLossExceedanceCurve = JsonSerializer.Serialize(
            inherent.LossExceedanceCurve.Select(p => new LossExceedancePointDto
                { Loss = p.Loss, Probability = p.Probability }));

        var thresholds = await ReadBandThresholdsAsync(db);

        // The 0–10 score the rest of the product reads. Written onto CalculatedRisk so lists,
        // heatmaps, review cadence and appetite all keep working without knowing which method
        // produced it.
        scoring.CalculatedRisk = MapToScore(inherent.P50, thresholds);
        if (residual is not null)
        {
            scoring.ResidualRisk = MapToScore(residual.P50, thresholds);
            scoring.ResidualUpdatedAt = DateTime.UtcNow;
        }

        db.RiskScoringHistories.Add(new RiskScoringHistory
        {
            RiskId = riskId,
            CalculatedRisk = scoring.CalculatedRisk,
            ResidualRisk = scoring.ResidualRisk,
            LastUpdate = DateTime.UtcNow
        });

        await db.SaveChangesAsync();

        Logger.Information(
            "Risk {RiskId} scored quantitatively: ALE P50 {P50:N0}, P90 {P90:N0}, mapped to {Score:F2}",
            riskId, inherent.P50, inherent.P90, scoring.CalculatedRisk);

        return Build(riskId, scoring, inherent, residual, thresholds);
    }

    public async Task<QuantitativeRiskResult?> GetAsync(int riskId)
    {
        await using var db = DalService.GetContext();

        var scoring = await db.RiskScorings.FirstOrDefaultAsync(s => s.Id == riskId);
        if (scoring?.QuantComputedAt is null) return null;

        var thresholds = await ReadBandThresholdsAsync(db);

        var curve = string.IsNullOrWhiteSpace(scoring.QuantLossExceedanceCurve)
            ? []
            : JsonSerializer.Deserialize<List<LossExceedancePointDto>>(scoring.QuantLossExceedanceCurve)
              ?? [];

        return new QuantitativeRiskResult
        {
            RiskId = riskId,
            InherentP10 = scoring.QuantAleP10 ?? 0,
            InherentP50 = scoring.QuantAleP50 ?? 0,
            InherentP90 = scoring.QuantAleP90 ?? 0,
            InherentMean = scoring.QuantAleMean ?? 0,
            ResidualP10 = scoring.QuantResidualAleP10,
            ResidualP50 = scoring.QuantResidualAleP50,
            ResidualP90 = scoring.QuantResidualAleP90,
            LossExceedanceCurve = curve,
            MappedScore = scoring.CalculatedRisk,
            MappedRiskLevel = BandName(scoring.QuantAleP50 ?? 0, thresholds),
            Seed = scoring.QuantSeed ?? 0,
            Iterations = MonteCarloRiskSimulator.DefaultIterations
        };
    }

    public async Task<int> RecomputeAllAsync()
    {
        await using var db = DalService.GetContext();

        var ids = await db.RiskScorings
            .Where(s => s.ScoringMethod == QuantitativeScoringMethod && s.QuantLefMostLikely != null)
            .Select(s => s.Id)
            .ToListAsync();

        var recomputed = 0;

        foreach (var id in ids)
        {
            var scoring = await db.RiskScorings.FirstOrDefaultAsync(s => s.Id == id);
            if (scoring?.QuantLefMostLikely is null) continue;

            await ComputeAndSaveAsync(id, new QuantitativeRiskInput
            {
                LossEventFrequencyMin = scoring.QuantLefMin ?? 0,
                LossEventFrequencyMostLikely = scoring.QuantLefMostLikely.Value,
                LossEventFrequencyMax = scoring.QuantLefMax ?? scoring.QuantLefMostLikely.Value,
                LossMagnitudeMin = scoring.QuantLossMin ?? 0,
                LossMagnitudeMostLikely = scoring.QuantLossMostLikely ?? 0,
                LossMagnitudeMax = scoring.QuantLossMax ?? scoring.QuantLossMostLikely ?? 0,
                Seed = scoring.QuantSeed
            });

            recomputed++;
        }

        return recomputed;
    }

    // --- mapping --------------------------------------------------------------------------------

    /// <summary>
    /// Maps an annualized loss onto the 0–10 scale the register uses.
    ///
    /// Piecewise-linear inside each band rather than one number per band, so two risks in the same
    /// band still order sensibly against each other. Above the top threshold the score approaches 10
    /// logarithmically and never exceeds it — a loss ten times the top threshold is worse than one at
    /// the threshold, and both are "as bad as this scale can say".
    /// </summary>
    public static float MapToScore(double annualizedLoss, IReadOnlyList<double> thresholds)
    {
        if (annualizedLoss <= 0 || thresholds.Count == 0) return 0;

        // Band edges on the 0–10 scale, matching the seeded risk_levels: Low 0, Medium 4, High 7,
        // Very High 10.
        double[] scoreEdges = [0, 4, 7, 10];

        if (annualizedLoss < thresholds[0])
            return (float)(scoreEdges[1] * (annualizedLoss / thresholds[0]));

        for (var i = 0; i < thresholds.Count - 1; i++)
        {
            if (annualizedLoss >= thresholds[i + 1]) continue;

            var span = thresholds[i + 1] - thresholds[i];
            var within = span <= 0 ? 0 : (annualizedLoss - thresholds[i]) / span;
            var lower = scoreEdges[System.Math.Min(i + 1, scoreEdges.Length - 1)];
            var upper = scoreEdges[System.Math.Min(i + 2, scoreEdges.Length - 1)];

            return (float)(lower + within * (upper - lower));
        }

        var top = thresholds[^1];
        var ratio = annualizedLoss / top;
        var score = scoreEdges[^1] - 3.0 / (1 + System.Math.Log10(ratio + 1) * 3);

        return (float)System.Math.Min(10.0, System.Math.Max(scoreEdges[^2], score));
    }

    private static string BandName(double annualizedLoss, IReadOnlyList<double> thresholds)
    {
        if (thresholds.Count < 3) return "Unknown";
        if (annualizedLoss < thresholds[0]) return "Low";
        if (annualizedLoss < thresholds[1]) return "Medium";
        if (annualizedLoss < thresholds[2]) return "High";
        return "Very High";
    }

    private static QuantitativeRiskResult Build(int riskId, RiskScoring scoring, LossExposureResult inherent,
        LossExposureResult? residual, IReadOnlyList<double> thresholds) => new()
    {
        RiskId = riskId,
        InherentP10 = inherent.P10,
        InherentP50 = inherent.P50,
        InherentP90 = inherent.P90,
        InherentMean = inherent.Mean,
        ResidualP10 = residual?.P10,
        ResidualP50 = residual?.P50,
        ResidualP90 = residual?.P90,
        LossExceedanceCurve = inherent.LossExceedanceCurve
            .Select(p => new LossExceedancePointDto { Loss = p.Loss, Probability = p.Probability })
            .ToList(),
        MappedScore = scoring.CalculatedRisk,
        MappedRiskLevel = BandName(inherent.P50, thresholds),
        Seed = inherent.Seed,
        Iterations = inherent.Iterations
    };

    private async Task<double> EffectiveMitigationAsync(DAL.Context.AuditableContext db, Risk risk,
        RiskScoring scoring)
    {
        var mitigation = await db.Mitigations.Where(m => m.RiskId == risk.Id)
            .OrderByDescending(m => m.LastUpdate).FirstOrDefaultAsync();
        if (mitigation is null) return 0;

        var controls = await db.MitigationToControls.Where(c => c.MitigationId == mitigation.Id)
            .ToListAsync();

        return MitigationPercentResidualStrategy.EffectiveMitigation(
            new ResidualRiskContext(risk, scoring, mitigation, controls));
    }

    private static async Task<int> ReadIntSettingAsync(DAL.Context.AuditableContext db, string key,
        int fallback)
    {
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Name == key);
        return setting?.Value is not null && int.TryParse(setting.Value, out var value) && value > 0
            ? value
            : fallback;
    }

    private static async Task<double[]> ReadBandThresholdsAsync(DAL.Context.AuditableContext db)
    {
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Name == BandThresholdsSetting);
        if (setting?.Value is null) return DefaultBandThresholds;

        var parsed = setting.Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(part => double.TryParse(part, System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var value)
                ? value
                : (double?)null)
            .Where(v => v is not null)
            .Select(v => v!.Value)
            .OrderBy(v => v)
            .ToArray();

        // A malformed setting falls back rather than producing a silently wrong band: three
        // ascending numbers is the contract, and two of them would map High onto Very High.
        return parsed.Length == 3 ? parsed : DefaultBandThresholds;
    }
}

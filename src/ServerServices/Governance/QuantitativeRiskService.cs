using System.Text.Json;
using DAL.Entities;
using DAL.Enums;
using Microsoft.EntityFrameworkCore;
using Model.Exceptions;
using Model.Governance;
using Model.TailRisk;
using Serilog;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.Risks;
using Tools.TailRisk;

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

        // Stage 9.7 (S48 §4.3): a ceiling on the iterations. There was none, and the bootstrap of the tail intervals
        // multiplies the cost of every iteration; below the floor the simulator still raises the count, as it did.
        if (input.Iterations > TailRiskLimits.MaxIterations)
            throw new InvalidParameterException(nameof(input.Iterations),
                $"At most {TailRiskLimits.MaxIterations:N0} iterations; the tail statistics are already stable at " +
                $"the default {MonteCarloRiskSimulator.DefaultIterations:N0}.");

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

        var iterations = Math.Clamp(input.Iterations ?? await ReadIterationsSettingAsync(db),
            TailRiskLimits.MinIterations, TailRiskLimits.MaxIterations);
        var seed = input.Seed ?? 20260826;

        // Stage 9.7 (S48 §4.4): with loss components declared, each event's loss is the sum of the components, and
        // the single range has to be their envelope — a magnitude declared two ways that disagree is refused, never
        // silently overridden by either.
        var components = await db.RiskLossComponents.Where(c => c.RiskId == riskId)
            .OrderBy(c => c.Component).ToListAsync();

        IReadOnlyList<CalibratedRange> ranges = [magnitude];
        if (components.Count > 0)
        {
            var envelope = Envelope(components);
            if (!SameRange(envelope, magnitude))
                throw new InvalidParameterException(nameof(input.LossMagnitudeMostLikely),
                    "This risk's loss magnitude is declared by component, so the single range has to be their " +
                    $"envelope ({envelope.Min:N0} / {envelope.MostLikely:N0} / {envelope.Max:N0}). Edit the components " +
                    "through /TailRisk/Risks/{id}/LossComponents, or delete them to go back to a single range.");

            ranges = components.Select(c => new CalibratedRange(c.LossMin, c.LossMostLikely, c.LossMax)).ToList();
        }

        var inherent = MonteCarloRiskSimulator.Run(frequency, ranges, iterations, seed);

        // The residual run differs only in the mitigation's effectiveness, so the two numbers are
        // comparable by construction — which is what makes the before/after a control-ROI statement
        // rather than two unrelated simulations.
        var effectiveness = await EffectiveMitigationAsync(db, risk, scoring);
        LossExposureResult? residual = null;
        if (effectiveness > 0)
            residual = MonteCarloRiskSimulator.Run(frequency, ranges, iterations, seed, effectiveness);

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
        // Stage 9.6 (S47 §4.4): the residual *mean* — Gate C's E[L] after. The median above is no substitute: the
        // median year of a low-frequency risk has no loss, so a median benefit is zero exactly where it matters.
        scoring.QuantResidualAleMean = residual?.Mean;
        scoring.QuantSeed = seed;
        scoring.QuantComputedAt = DateTime.UtcNow;
        scoring.QuantLossExceedanceCurve = JsonSerializer.Serialize(
            inherent.LossExceedanceCurve.Select(p => new LossExceedancePointDto
                { Loss = p.Loss, Probability = p.Probability }));

        var thresholds = await ReadBandThresholdsAsync(db);

        // The 0–10 score the rest of the product reads. Written onto CalculatedRisk so lists,
        // heatmaps, review cadence and appetite all keep working without knowing which method
        // produced it.
        //
        // Mapped from the *mean* annualized loss, not the median. For a low-frequency risk the median
        // year has no loss event at all, so the P50 is legitimately zero — mapping from it would score
        // "once a decade, ten million" as harmless, which is precisely the class of risk a
        // quantitative method exists to surface. The mean is the conventional FAIR summary (ALE) and
        // is the statistic that stays meaningful across the frequency range.
        scoring.CalculatedRisk = MapToScore(inherent.Mean, thresholds);
        if (residual is not null)
        {
            scoring.ResidualRisk = MapToScore(residual.Mean, thresholds);
            scoring.ResidualUpdatedAt = DateTime.UtcNow;
        }

        db.RiskScoringHistories.Add(new RiskScoringHistory
        {
            RiskId = riskId,
            CalculatedRisk = scoring.CalculatedRisk,
            ResidualRisk = scoring.ResidualRisk,
            LastUpdate = DateTime.UtcNow
        });

        // Stage 9.7 (S48 §4.2): the tail statistics, in their own table — never in risk_scoring, whose whole payload a
        // PUT /Risks/{id}/Scoring copies (S48 D1). Same save, so a stored statistic always describes stored inputs.
        var computedAt = scoring.QuantComputedAt!.Value;
        var tailRows = await db.RiskTailStatistics.Include(t => t.Components).Where(t => t.RiskId == riskId)
            .ToListAsync();

        WriteTail(db, tailRows, riskId, TailRun.Inherent, inherent, frequency, magnitude, components, 0, computedAt);

        if (residual is not null)
            WriteTail(db, tailRows, riskId, TailRun.Residual, residual, frequency, magnitude, components, effectiveness,
                computedAt);
        else if (tailRows.FirstOrDefault(t => t.Run == TailRun.Residual) is { } stale)
            // No residual run any more (the effectiveness went to zero): Gate B must not read a residual that no
            // longer exists, so it falls back to the inherent run, as the score does.
            db.RiskTailStatistics.Remove(stale);

        await db.SaveChangesAsync();

        Logger.Information(
            "Risk {RiskId} scored quantitatively: mean ALE {Mean:N0}, P50 {P50:N0}, P90 {P90:N0}, " +
            "mapped to {Score:F2}", riskId, inherent.Mean, inherent.P50, inherent.P90,
            scoring.CalculatedRisk);

        var stored = await db.RiskTailStatistics.AsNoTracking().Include(t => t.Components)
            .Where(t => t.RiskId == riskId).ToListAsync();

        return Build(riskId, scoring, inherent, residual, thresholds, stored);
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

        var tails = await db.RiskTailStatistics.AsNoTracking().Include(t => t.Components)
            .Where(t => t.RiskId == riskId).ToListAsync();
        var inherentTail = tails.FirstOrDefault(t => t.Run == TailRun.Inherent);

        return new QuantitativeRiskResult
        {
            RiskId = riskId,
            InherentTail = inherentTail is null ? null : TailStatisticsMapping.ToDto(inherentTail),
            ResidualTail = tails.FirstOrDefault(t => t.Run == TailRun.Residual) is { } residualTail
                ? TailStatisticsMapping.ToDto(residualTail)
                : null,
            InherentP10 = scoring.QuantAleP10 ?? 0,
            InherentP50 = scoring.QuantAleP50 ?? 0,
            InherentP90 = scoring.QuantAleP90 ?? 0,
            InherentMean = scoring.QuantAleMean ?? 0,
            ResidualP10 = scoring.QuantResidualAleP10,
            ResidualP50 = scoring.QuantResidualAleP50,
            ResidualP90 = scoring.QuantResidualAleP90,
            ResidualMean = scoring.QuantResidualAleMean,
            LossExceedanceCurve = curve,
            MappedScore = scoring.CalculatedRisk,
            MappedRiskLevel = BandName(scoring.QuantAleMean ?? 0, thresholds),
            Seed = scoring.QuantSeed ?? 0,
            Iterations = MonteCarloRiskSimulator.DefaultIterations
        };
    }

    public async Task<QuantitativeRiskResult?> RecomputeAsync(int riskId)
    {
        await using var db = DalService.GetContext();

        var scoring = await db.RiskScorings.AsNoTracking().FirstOrDefaultAsync(s => s.Id == riskId);
        if (scoring?.QuantLefMostLikely is null) return null;

        // With components declared the magnitude is their envelope (S48 §4.4) — which is what keeps a recomputation
        // after the components changed from being refused as a disagreement with the previous envelope.
        var components = await db.RiskLossComponents.AsNoTracking().Where(c => c.RiskId == riskId).ToListAsync();
        var magnitude = components.Count > 0
            ? Envelope(components)
            : new CalibratedRange(scoring.QuantLossMin ?? 0, scoring.QuantLossMostLikely ?? 0,
                scoring.QuantLossMax ?? scoring.QuantLossMostLikely ?? 0);

        return await ComputeAndSaveAsync(riskId, new QuantitativeRiskInput
        {
            LossEventFrequencyMin = scoring.QuantLefMin ?? 0,
            LossEventFrequencyMostLikely = scoring.QuantLefMostLikely.Value,
            LossEventFrequencyMax = scoring.QuantLefMax ?? scoring.QuantLefMostLikely.Value,
            LossMagnitudeMin = magnitude.Min,
            LossMagnitudeMostLikely = magnitude.MostLikely,
            LossMagnitudeMax = magnitude.Max,
            Seed = scoring.QuantSeed
        });
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
            if (await RecomputeAsync(id) is not null) recomputed++;
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
        LossExposureResult? residual, IReadOnlyList<double> thresholds, IReadOnlyList<RiskTailStatistics> tails) => new()
    {
        RiskId = riskId,
        InherentTail = tails.FirstOrDefault(t => t.Run == TailRun.Inherent) is { } inherentTail
            ? TailStatisticsMapping.ToDto(inherentTail)
            : null,
        ResidualTail = tails.FirstOrDefault(t => t.Run == TailRun.Residual) is { } residualTail
            ? TailStatisticsMapping.ToDto(residualTail)
            : null,
        InherentP10 = inherent.P10,
        InherentP50 = inherent.P50,
        InherentP90 = inherent.P90,
        InherentMean = inherent.Mean,
        ResidualP10 = residual?.P10,
        ResidualP50 = residual?.P50,
        ResidualP90 = residual?.P90,
        ResidualMean = residual?.Mean,
        LossExceedanceCurve = inherent.LossExceedanceCurve
            .Select(p => new LossExceedancePointDto { Loss = p.Loss, Probability = p.Probability })
            .ToList(),
        MappedScore = scoring.CalculatedRisk,
        MappedRiskLevel = BandName(inherent.Mean, thresholds),
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

    /// <summary>
    /// The configured iteration count, clamped to the range the tail statistics accept (S48 §4.3) — an
    /// administrator's 10⁶ would otherwise turn the nightly recomputation into hours of bootstrap.
    /// </summary>
    public static async Task<int> ReadIterationsSettingAsync(DAL.Context.AuditableContext db)
    {
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Name == IterationsSetting);
        var value = setting?.Value is not null && int.TryParse(setting.Value, out var parsed) && parsed > 0
            ? parsed
            : MonteCarloRiskSimulator.DefaultIterations;

        return Math.Clamp(value, TailRiskLimits.MinIterations, TailRiskLimits.MaxIterations);
    }

    private static async Task<double[]> ReadBandThresholdsAsync(DAL.Context.AuditableContext db)
    {
        var setting = await db.Settings.FirstOrDefaultAsync(s => s.Name == BandThresholdsSetting);
        return ParseBandThresholds(setting?.Value);
    }

    /// <summary>
    /// The three ascending band thresholds of a <c>quantitative_band_thresholds</c> value, or the defaults when it is
    /// missing or malformed. Public so the flag 8 derivation reads the same top threshold the score mapping does.
    /// </summary>
    public static double[] ParseBandThresholds(string? value)
    {
        if (value is null) return DefaultBandThresholds;

        var parsed = value
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

    // --- Stage 9.7: the tail statistics -------------------------------------------------------------

    /// <summary>The envelope of the components: Σ minimum, Σ most likely, Σ maximum (S48 §4.4).</summary>
    public static CalibratedRange Envelope(IEnumerable<RiskLossComponent> components)
    {
        var list = components.ToList();
        return new CalibratedRange(list.Sum(c => c.LossMin), list.Sum(c => c.LossMostLikely), list.Sum(c => c.LossMax));
    }

    /// <summary>Equal within a relative 10⁻⁹ — a range read back from a double column and re-sent is the same range.</summary>
    private static bool SameRange(CalibratedRange a, CalibratedRange b)
    {
        static bool Close(double x, double y) => Math.Abs(x - y) <= 1e-9 * Math.Max(1, Math.Max(Math.Abs(x), Math.Abs(y)));
        return Close(a.Min, b.Min) && Close(a.MostLikely, b.MostLikely) && Close(a.Max, b.Max);
    }

    /// <summary>Upserts one run's tail row and replaces its component rows (S48 §4.2, §4.4.1).</summary>
    private static void WriteTail(DAL.Context.AuditableContext db, List<RiskTailStatistics> rows, int riskId, TailRun run,
        LossExposureResult result, CalibratedRange frequency, CalibratedRange magnitude,
        IReadOnlyList<RiskLossComponent> components, double effectiveness, DateTime computedAt)
    {
        var row = rows.FirstOrDefault(t => t.Run == run);
        if (row is null)
        {
            row = new RiskTailStatistics { RiskId = riskId, Run = run, CreatedAt = computedAt };
            db.RiskTailStatistics.Add(row);
            rows.Add(row);
        }
        else
        {
            row.UpdatedAt = computedAt;
            foreach (var old in row.Components.ToList()) db.RiskTailComponents.Remove(old);
            row.Components.Clear();
        }

        var envelope = components.Count > 0 ? Envelope(components) : magnitude;
        var tail = result.Tail;

        row.Iterations = result.Iterations;
        row.Seed = result.Seed;
        row.ConfidenceLevel = (decimal)TailStatisticsCalculator.ConfidenceLevel;
        row.LefMin = frequency.Min;
        row.LefMostLikely = frequency.MostLikely;
        row.LefMax = frequency.Max;
        row.MagnitudeMin = envelope.Min;
        row.MagnitudeMostLikely = envelope.MostLikely;
        row.MagnitudeMax = envelope.Max;
        row.MagnitudeSource = components.Count > 0 ? MagnitudeSource.Components : MagnitudeSource.SingleRange;
        row.MitigationEffectiveness = effectiveness;
        row.ExpectedLoss = tail.ExpectedLoss;
        row.ExpectedLossCiLow = tail.ExpectedLossCiLow;
        row.ExpectedLossCiHigh = tail.ExpectedLossCiHigh;
        row.P95 = tail.P95;
        row.P95CiLow = tail.P95CiLow;
        row.P95CiHigh = tail.P95CiHigh;
        row.Cvar95 = tail.Cvar95;
        row.Cvar95CiLow = tail.Cvar95CiLow;
        row.Cvar95CiHigh = tail.Cvar95CiHigh;
        row.ProbabilityOfLoss = tail.ProbabilityOfLoss;
        row.ConditionalLoss = tail.ConditionalLoss;
        row.ComputedAt = computedAt;

        if (components.Count == 0) return;

        foreach (var share in result.ComponentContributions)
        {
            var component = components[share.Index];
            row.Components.Add(new RiskTailComponent
            {
                Component = component.Component,
                LossMin = component.LossMin,
                LossMostLikely = component.LossMostLikely,
                LossMax = component.LossMax,
                ExpectedLoss = share.ExpectedLoss,
                Cvar95 = share.Cvar95,
                CreatedAt = computedAt
            });
        }
    }
}

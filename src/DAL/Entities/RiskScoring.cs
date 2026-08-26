using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class RiskScoring
{
    public int Id { get; set; }

    public int ScoringMethod { get; set; }

    public float CalculatedRisk { get; set; }

    public float ClassicLikelihood { get; set; }

    public float ClassicImpact { get; set; }

    public float? Custom { get; set; }

    public double? ContributingScore { get; set; }

    /// <summary>
    /// Post-treatment score (Track 8 milestone 8.2.1). <c>null</c> until the calculation job has run
    /// over the risk, which is distinguishable from "residual equals inherent" on purpose — the
    /// former means nobody has assessed the treatment, the latter that the treatment achieves
    /// nothing, and an auditor cares which.
    /// </summary>
    public float? ResidualRisk { get; set; }

    /// <summary>When <see cref="ResidualRisk"/> was last derived.</summary>
    public DateTime? ResidualUpdatedAt { get; set; }

    // --- Track 8 milestone 8.7.2: FAIR-lite quantitative inputs and cached outputs -------------
    // Ranges, not point estimates: the whole reason to reach for FAIR is that a single number
    // conceals the uncertainty the decision actually turns on.

    /// <summary>Loss-event frequency, minimum, events per year.</summary>
    public double? QuantLefMin { get; set; }

    /// <summary>Loss-event frequency, most likely, events per year.</summary>
    public double? QuantLefMostLikely { get; set; }

    /// <summary>Loss-event frequency, maximum, events per year.</summary>
    public double? QuantLefMax { get; set; }

    /// <summary>Loss magnitude per event, minimum, currency.</summary>
    public double? QuantLossMin { get; set; }

    /// <summary>Loss magnitude per event, most likely, currency.</summary>
    public double? QuantLossMostLikely { get; set; }

    /// <summary>Loss magnitude per event, maximum, currency.</summary>
    public double? QuantLossMax { get; set; }

    /// <summary>Annualized loss exposure, 10th percentile of the simulation.</summary>
    public double? QuantAleP10 { get; set; }

    public double? QuantAleP50 { get; set; }

    public double? QuantAleP90 { get; set; }

    public double? QuantAleMean { get; set; }

    /// <summary>The same percentiles re-run with the mitigation's effectiveness applied — the
    /// control-ROI comparison 8.7.2 asks for.</summary>
    public double? QuantResidualAleP10 { get; set; }

    public double? QuantResidualAleP50 { get; set; }

    public double? QuantResidualAleP90 { get; set; }

    /// <summary>
    /// The loss-exceedance curve as JSON — an array of <c>{ loss, probability }</c> points. Cached
    /// rather than recomputed per request: the simulation is ten thousand iterations and the curve
    /// changes only when an input does.
    /// </summary>
    public string? QuantLossExceedanceCurve { get; set; }

    /// <summary>The seed the cached results were produced with, so a run is reproducible.</summary>
    public int? QuantSeed { get; set; }

    public DateTime? QuantComputedAt { get; set; }
}

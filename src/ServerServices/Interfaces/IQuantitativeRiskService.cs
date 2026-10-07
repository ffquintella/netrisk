using Model.Governance;

namespace ServerServices.Interfaces;

/// <summary>
/// FAIR-lite quantitative scoring (Track 8 milestone 8.7.2).
///
/// A second scoring method beside the classic matrix, not a replacement for it. The matrix stays
/// valid for triage and communication; what it cannot do — and what the peer-reviewed criticism of
/// risk matrices is about — is express magnitude or uncertainty, which is what a board needs to
/// decide whether a control is worth its cost.
/// </summary>
public interface IQuantitativeRiskService
{
    /// <summary>
    /// Stores the calibrated ranges, runs the simulation, caches the percentiles and the
    /// loss-exceedance curve on the scoring row, and switches the risk to the quantitative method.
    /// </summary>
    Task<QuantitativeRiskResult> ComputeAndSaveAsync(int riskId, QuantitativeRiskInput input);

    /// <summary>The cached result, or null when the risk has never been scored quantitatively.</summary>
    Task<QuantitativeRiskResult?> GetAsync(int riskId);

    /// <summary>
    /// Re-runs one risk's analysis from its stored inputs — the frequency, the seed and, when loss components are
    /// declared, their envelope (Stage 9.7, S48 §4.4) — or returns null when it has none.
    /// </summary>
    Task<QuantitativeRiskResult?> RecomputeAsync(int riskId);

    /// <summary>Re-runs every quantitatively scored risk. Used by the calculation job.</summary>
    Task<int> RecomputeAllAsync();
}

using DAL.Entities;

namespace GUIClient.ViewModels;

/// <summary>
/// Strongly-typed pairing of a <see cref="Risk"/> with its calculated <see cref="RiskScoring"/>.
/// Replaces the previous <c>Tuple&lt;Risk, RiskScoring&gt;</c> so views can bind with compiled bindings.
/// </summary>
public record RiskScoringPair(Risk Risk, RiskScoring Scoring);

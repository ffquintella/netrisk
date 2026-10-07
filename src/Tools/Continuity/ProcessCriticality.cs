using Model.Continuity;
using Tools.Risks;

namespace Tools.Continuity;

/// <summary>
/// The effective criticality of a business process (Stage 9.3, S43 §4.6, T157): derived from the BIA's
/// MTPD by the mapping S39 §5.2 proposed for this stage, computed on read and never stored.
///
/// <list type="bullet">
/// <item>MTPD declared ⇒ it wins, source <see cref="CriticalitySource.Bia"/>, and the declared
/// <c>criticality</c> entity property is ignored.</item>
/// <item>No MTPD ⇒ the declared property, through <see cref="CriticalProcessCoverageCalculator.ParseCriticality"/>,
/// source <see cref="CriticalitySource.Declared"/> — or nothing.</item>
/// <item>An absent MTPD is never read as 0 (criticality 5) nor as infinite (criticality 1).</item>
/// </list>
///
/// The critical threshold stays <see cref="RiskChainSchema.CriticalThreshold"/> = 4, which is MTPD ≤ 24 h.
/// </summary>
public static class ProcessCriticality
{
    /// <summary>The upper bound, inclusive, of each criticality from 5 down to 2; above the last is 1.</summary>
    private static readonly (int MaxMinutes, int Criticality)[] Bands =
    [
        (240, 5),     // ≤ 4 h
        (1_440, 4),   // ≤ 24 h
        (4_320, 3),   // ≤ 72 h
        (10_080, 2)   // ≤ 7 d
    ];

    public static int FromMtpd(int mtpdMinutes)
    {
        foreach (var (max, criticality) in Bands)
            if (mtpdMinutes <= max) return criticality;

        return 1;
    }

    public static (int? Value, CriticalitySource? Source) Resolve(int? biaMtpdMinutes, string? declaredRaw)
    {
        if (biaMtpdMinutes is { } mtpd) return (FromMtpd(mtpd), CriticalitySource.Bia);

        var declared = CriticalProcessCoverageCalculator.ParseCriticality(declaredRaw);
        return declared is null ? (null, null) : (declared, CriticalitySource.Declared);
    }

    /// <summary>
    /// Whether the BIA owns the process's criticality — the predicate T241/M52 uses to answer
    /// <c>409 criticality_owned_by_bia</c> on the declared property (S43 §3).
    /// </summary>
    public static bool IsOwnedByBia(int? biaMtpdMinutes) => biaMtpdMinutes is not null;
}

using System;
using System.Collections.Generic;

namespace Tools.Risks;

/// <summary>
/// The one place a score becomes a band (S39 §4.5, T238).
///
/// Before this, five code paths classified the same 0–10 register score three different ways:
/// <c>&gt;</c> against the lower bound (<c>MgmtReviewsService</c>, so a score of exactly 7.0 was
/// Medium and exactly 0 had no band), <c>&gt;=</c> (<c>RiskAcceptancesService</c>), and fixed
/// <c>&gt; 7</c> / <c>&gt; 4</c> cut-offs (<c>StatisticsService</c>, <c>MasterDashboardService</c>).
/// The same risk could sit in different bands on the dashboard, in its review cadence and in the
/// acceptance permission check.
///
/// Pure and neutral on purpose: no rounding, no ICR state, no data access, so the ICR (S39 §1.3)
/// and the register can both call it.
/// </summary>
public static class RiskBand
{
    /// <summary>
    /// The register limits the methodology approves (S39 §4.5, <c>limites_registro</c>): Low 0,
    /// Medium 4.0, High 7.0, Very High 9.0. Very High is 9.0, not the seeded 10.1, because the
    /// register score tops out at 10 and a band that starts above it can never be reached.
    /// </summary>
    public static IReadOnlyList<double> DefaultRegisterLimits { get; } = [0.0, 4.0, 7.0, 9.0];

    /// <summary>Index of Low in <see cref="DefaultRegisterLimits"/>.</summary>
    public const int Low = 0;

    /// <summary>Index of Medium in <see cref="DefaultRegisterLimits"/>.</summary>
    public const int Medium = 1;

    /// <summary>Index of High in <see cref="DefaultRegisterLimits"/>.</summary>
    public const int High = 2;

    /// <summary>Index of Very High in <see cref="DefaultRegisterLimits"/>.</summary>
    public const int VeryHigh = 3;

    /// <summary>
    /// The index of the band <paramref name="x"/> falls in: the highest band whose lower bound is
    /// less than or equal to <paramref name="x"/>. Lower bounds are inclusive, so a score exactly on
    /// a bound belongs to the band that bound opens, and the top score belongs to the top band.
    ///
    /// <paramref name="lowerBounds"/> must be ascending, with the first bound the floor of the scale
    /// (0 for every approved profile). Returns -1 when no band applies: <paramref name="x"/> is NaN,
    /// below the first bound, or no bounds were given. Callers decide what an unclassifiable score
    /// means for them; this function does not invent a band.
    /// </summary>
    public static int Faixa(double x, IReadOnlyList<double> lowerBounds)
    {
        ArgumentNullException.ThrowIfNull(lowerBounds);
        if (double.IsNaN(x)) return -1;

        for (var i = lowerBounds.Count - 1; i >= 0; i--)
        {
            if (x >= lowerBounds[i]) return i;
        }

        return -1;
    }
}

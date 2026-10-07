using Model.RiskFlags;

namespace Tools.RiskFlags;

/// <summary>
/// The trend column of the Top Risks list (Stage 9.5, S46 §4.9, D13).
///
/// Over the score history (residual where there is one, inherent where not): <c>current</c> is the most
/// recent point; <c>baseline</c> is the most recent point at or before <c>now − window</c>, or the oldest
/// point when the series is shorter than the window; Δ = current − baseline. Rising at Δ ≥ threshold,
/// Falling at Δ ≤ −threshold, Stable between, Unknown with fewer than two points. The constants are
/// declared (0.5 on the ordinal scale, 90 days), like S45 D6's.
/// </summary>
public static class RiskTrendCalculator
{
    public const int DefaultWindowDays = 90;

    public const double DefaultThreshold = 0.5;

    public static RiskTrendDto Compute(IEnumerable<(DateTime At, double Value)> history, DateTime nowUtc,
        int windowDays = DefaultWindowDays, double threshold = DefaultThreshold)
    {
        ArgumentNullException.ThrowIfNull(history);
        if (windowDays < 1) throw new ArgumentOutOfRangeException(nameof(windowDays));
        if (threshold <= 0) throw new ArgumentOutOfRangeException(nameof(threshold));

        // Order of input is irrelevant: ties on the date keep the later entry, as the history appends.
        var points = history
            .Select((p, index) => (p.At, p.Value, index))
            .Where(p => p.At <= nowUtc)
            .OrderBy(p => p.At)
            .ThenBy(p => p.index)
            .ToList();

        var dto = new RiskTrendDto { WindowDays = windowDays, Points = points.Count };
        if (points.Count == 0) return dto;

        var current = points[^1];
        dto.Current = current.Value;

        if (points.Count < 2) return dto;

        var cutoff = nowUtc.AddDays(-windowDays);
        var before = points.Take(points.Count - 1).Where(p => p.At <= cutoff).ToList();
        var baseline = before.Count > 0 ? before[^1] : points[0];

        dto.Baseline = baseline.Value;
        dto.BaselineAt = baseline.At;
        dto.Delta = System.Math.Round(current.Value - baseline.Value, 6);

        dto.Direction = dto.Delta >= threshold
            ? RiskTrendDirection.Rising
            : dto.Delta <= -threshold
                ? RiskTrendDirection.Falling
                : RiskTrendDirection.Stable;

        return dto;
    }
}

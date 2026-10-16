using Model.RiskIndex;

namespace ServerServices.RiskIndex.Engine;

/// <summary>What the finding anchor reads from one finding (S39 §3.1). Scores are on their native 0–10 scales.</summary>
public readonly record struct FindingReading
{
    public double? Vpr { get; init; }
    public double? Cvss3 { get; init; }
    public double? Cvss2 { get; init; }

    /// <summary>The tolerant-parser severity, 0–4 (<c>SlaService.ParseSeverity</c>); 0 is excluded when nothing else scores the finding.</summary>
    public int Severity { get; init; }

    /// <summary><c>ExploitAvaliable</c>: an exploit exists (and, interim, the Vision One flag — S39 §3.1).</summary>
    public bool ExploitAvailable { get; init; }

    public double? Epss { get; init; }

    /// <summary>Exploitation known or observed: <c>ExploitedByScanner</c> (interim); KEV too in full mode.</summary>
    public bool ExploitationKnown { get; init; }

    /// <summary>The object is internet-facing (S39 §5.3); applies <c>m_net</c>.</summary>
    public bool InternetFacing { get; init; }
}

/// <summary>Where a finding signal's base came from — the multiplier <c>m_x</c> applies only to CVSS and severity.</summary>
public enum FindingBaseSource
{
    Vpr = 1,
    Cvss3 = 2,
    Cvss2 = 3,
    Severity = 4
}

/// <summary>One normalized finding signal and how it was obtained, for the object row's signal list.</summary>
public readonly record struct FindingSignal(double Value, FindingBaseSource BaseSource, double Base, bool ExploitMultiplier,
    bool ExploitationFloor, bool InternetMultiplier);

/// <summary>
/// The fixed normalization anchors of S39 §3.1: every signal becomes <c>s ∈ [0, 1]</c> (1 = worst) by an anchor of its
/// own source, never by min-max or a population percentile, so an object's value never moves because another object
/// moved. Each function reads its constants from the profile (<see cref="RiskIndexParameters"/>).
/// </summary>
public static class IcrAnchors
{
    /// <summary>The severity anchor <c>a_sev</c> for a 1–4 severity, or 0 for none (S39 §3.1, §6.3 SLA).</summary>
    public static double SeverityAnchor(RiskIndexParameters p, int severity) =>
        p.SeverityAnchors.TryGetValue(severity, out var a) ? IcrMath.Clamp01(a) : 0;

    /// <summary>
    /// The finding signal (S39 §3.1): base <c>b</c> from VPR › CVSS3 › CVSS2 › the severity anchor; without exploitation
    /// <c>s = min(ceiling, b·m_x·m_net)</c>, with exploitation known or observed <c>s = max(floor, min(1, b·m_net))</c>,
    /// whatever the base came from. <c>m_x</c> applies only when the base is CVSS or severity — VPR already carries
    /// threat. Returns null for a finding nothing scores (severity 0 and no score).
    /// </summary>
    public static FindingSignal? Finding(RiskIndexParameters p, FindingReading reading)
    {
        double b;
        FindingBaseSource source;

        if (reading.Vpr is > 0) (b, source) = (reading.Vpr.Value / 10, FindingBaseSource.Vpr);
        else if (reading.Cvss3 is > 0) (b, source) = (reading.Cvss3.Value / 10, FindingBaseSource.Cvss3);
        else if (reading.Cvss2 is > 0) (b, source) = (reading.Cvss2.Value / 10, FindingBaseSource.Cvss2);
        else if (reading.Severity is >= 1 and <= 4) (b, source) = (SeverityAnchor(p, reading.Severity), FindingBaseSource.Severity);
        else return null;

        b = IcrMath.Clamp01(b);
        var mNet = reading.InternetFacing ? p.InternetMultiplier : 1.0;

        if (reading.ExploitationKnown)
        {
            var floored = Math.Max(p.ExploitationFloor, Math.Min(1, b * mNet));
            return new FindingSignal(IcrMath.Clamp01(floored), source, b, false, true, reading.InternetFacing);
        }

        var exploitMultiplier = source != FindingBaseSource.Vpr &&
                                (reading.ExploitAvailable || reading.Epss is { } epss && epss >= p.EpssThreshold);
        var mx = exploitMultiplier ? p.ExploitAvailableMultiplier : 1.0;
        var value = Math.Min(p.NoExploitationCeiling, b * mx * mNet);
        return new FindingSignal(IcrMath.Clamp01(value), source, b, exploitMultiplier, false, reading.InternetFacing);
    }

    /// <summary>
    /// The SecurityScorecard rating anchor (S39 §3.1): the inverted, piecewise-linear curve of <c>ancoras_ssc</c>
    /// (100 → 0 … 0 → 1). A score outside 0–100 is clamped.
    /// </summary>
    public static double Ssc(RiskIndexParameters p, double score)
    {
        var anchors = p.SscAnchors.OrderBy(a => a.Key).ToArray();
        if (anchors.Length == 0) return IcrMath.Clamp01(1 - score / 100);

        var x = Math.Clamp(score, anchors[0].Key, anchors[^1].Key);
        for (var i = 1; i < anchors.Length; i++)
        {
            var (lo, hi) = (anchors[i - 1], anchors[i]);
            if (x > hi.Key) continue;
            if (hi.Key == lo.Key) return IcrMath.Clamp01(hi.Value);
            var t = (x - lo.Key) / (hi.Key - lo.Key);
            return IcrMath.Clamp01(lo.Value + t * (hi.Value - lo.Value));
        }

        return IcrMath.Clamp01(anchors[^1].Value);
    }

    /// <summary>
    /// The incident anchor (S39 §3.1): the severity anchor when the incident has a severity the profile knows, otherwise
    /// the category anchor; an unknown category reads as <c>not_specified</c> — unknown is not light.
    /// </summary>
    public static double Incident(RiskIndexParameters p, string? category, string? severity)
    {
        if (!string.IsNullOrWhiteSpace(severity) &&
            p.IncidentSeverityAnchors.TryGetValue(severity.Trim().ToLowerInvariant(), out var bySeverity))
            return IcrMath.Clamp01(bySeverity);

        var key = string.IsNullOrWhiteSpace(category) ? "not_specified" : category.Trim().ToLowerInvariant();
        if (p.IncidentCategoryAnchors.TryGetValue(key, out var byCategory)) return IcrMath.Clamp01(byCategory);
        return p.IncidentCategoryAnchors.TryGetValue("not_specified", out var unknown) ? IcrMath.Clamp01(unknown) : 0.5;
    }

    /// <summary>
    /// The event decay <c>d</c> (S39 §3.1, §9.2): 1 while open; <c>2^(−age/half-life)</c> after closure, with age measured
    /// from the closure date; null once the incident has left the window (it no longer enters the index).
    /// </summary>
    public static double? IncidentDecay(RiskIndexParameters p, bool open, DateTime? closedAt, DateTime now)
    {
        if (open) return 1;
        if (closedAt is null) return 1;

        var age = Math.Max(0, IcrMath.Days(closedAt.Value, now));
        if (age > p.IncidentWindowDays) return null;
        return Math.Pow(2, -age / Math.Max(1, p.IncidentHalfLifeDays));
    }

    /// <summary>
    /// The response-readiness signal <c>s_IRP</c> of an open scenario (S39 §3.1): 0 with an approved plan tested within
    /// <c>irp_teste_validade</c>, 0.5 approved without a recent test, 1 with no plan or none approved.
    /// </summary>
    public static double Irp(RiskIndexParameters p, bool approvedPlan, DateTime? lastTest, DateTime now)
    {
        if (!approvedPlan) return 1.0;
        if (lastTest is { } tested && IcrMath.Days(tested, now) <= p.IrpTestValidityDays) return 0;
        return 0.5;
    }

    /// <summary><c>rampa(σ_ref) = clamp((σ_ref − (θ − h))/(2h), 0, 1)</c> (S39 §3.1): 0 at 0.60, 1 at 0.80 by default.</summary>
    public static double IrpRamp(RiskIndexParameters p, double sigmaRef)
    {
        var h = p.IrpRamp;
        if (h <= 0) return sigmaRef >= p.IrpSigmaThreshold ? 1 : 0;
        return Math.Clamp((sigmaRef - (p.IrpSigmaThreshold - h)) / (2 * h), 0, 1);
    }

    /// <summary>
    /// The campaign-rank weight multiplier (S39 §6.5): <c>1 + β·(1 − (rank − 1)/(n − 1))</c>, <c>1 + β</c> when the
    /// campaign ranked one item, and the neutral <c>1 + β/2</c> without a valid rank.
    /// </summary>
    public static double RankMultiplier(RiskIndexParameters p, int? rank, int n)
    {
        if (rank is null || n < 1) return 1 + p.BetaRank / 2;
        if (n == 1) return 1 + p.BetaRank;
        var r = Math.Clamp(rank.Value, 1, n);
        return 1 + p.BetaRank * (1 - (double)(r - 1) / (n - 1));
    }
}

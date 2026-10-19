using DAL.Enums;
using Model.RiskIndex;
using ServerServices.RiskIndex.Engine;

namespace ServerServices.RiskIndex.Signals;

/// <summary>A governance signal id of S39 §6.3.</summary>
public static class GovernanceSignalIds
{
    public const string AppetiteBreach = "APT-BRK";
    public const string AcceptanceBeforeAppetite = "ACC-APT";
    public const string AcceptanceExpired = "ACC-EXP";
    public const string AcceptanceDrift = "ACC-DRIFT";
    public const string CounterSignature = "APT-DUAL";
    public const string CampaignPending = "CMP-PEND";
    public const string CampaignEscalated = "CMP-ESC";
    public const string CampaignMitigation = "CMP-MIT";
    public const string TaskOverdue = "TSK-OVD";
    public const string ReviewRequested = "REV-REQ";
    public const string ReviewNoCredit = "REV-NOCREDIT";
    public const string Divergence = "DIV";
}

/// <summary>One lit governance signal: its id, factor and value in [0, 1].</summary>
public sealed record GovernanceSignal(string Id, string Factor, double Value);

/// <summary>The governance reading of one scenario (S39 §6.3): the factor values x_f and the signals behind them.</summary>
public sealed record ScenarioGovernance
{
    /// <summary>x_f per GOV factor (<see cref="RiskIndexFactors"/>): MAX inside GOV.A and GOV.C, MRA inside GOV.R.</summary>
    public IReadOnlyDictionary<string, double> Factors { get; init; } = new Dictionary<string, double>();

    public IReadOnlyList<GovernanceSignal> Signals { get; init; } = [];

    /// <summary><c>R_{r,GOV} = σ_ref·MRA(u_f·x_f)</c>.</summary>
    public double R { get; init; }

    /// <summary>An appetite ceiling applies (entity row or global): without one APT-BRK and ACC-APT are inactive and the
    /// appetite part leaves GOV's coverage.</summary>
    public bool AppetiteConfigured { get; init; }

    /// <summary>The stored residual (or inherent) is above the ceiling — the "above the appetite ceiling" seal.</summary>
    public bool AboveCeiling { get; init; }

    /// <summary>A valid acceptance holds the scenario above the current ceiling (the seal's "accepted before" count).</summary>
    public bool AcceptedAboveCeiling { get; init; }

    public bool HasValidAcceptance { get; init; }

    /// <summary>A valid acceptance expires within <c>janela_aviso_expiracao</c> — display only.</summary>
    public bool AcceptanceExpiringSoon { get; init; }
}

/// <summary>
/// The governance signals of S39 §6.3 from the register's reliable fields, and the SLA reading of an asset. Each fact
/// enters once (§6.4): a late review with a residual only through REG's credit; an expired acceptance only through
/// ACC-EXP (the review flag written with it is ignored). Pure: facts in, values out.
/// </summary>
public static class GovernanceSignals
{
    /// <summary>A valid acceptance: <c>Status = Active ∧ ExpiresAt &gt; now</c>, by date (S39 §3.3, §6.2).</summary>
    public static bool IsValid(AcceptanceFact a, DateTime now) =>
        a.Status == RiskAcceptanceStatus.Active && a.RevokedAt is null && a.ExpiresAt > now;

    /// <param name="divergenceLit">GOV.G's state today, latch included (the caller knows yesterday's).</param>
    public static ScenarioGovernance Compute(ScenarioFacts facts, ReviewCreditResult credit, RiskIndexParameters p,
        DateTime now, bool divergenceLit = false)
    {
        var s = p.Signals;
        var signals = new List<GovernanceSignal>();
        var score = credit.SigmaRef * 10;
        var ceiling = facts.AppetiteCeiling;
        var above = ceiling is { } c && score > c;
        var valid = facts.Acceptances.Where(a => IsValid(a, now)).ToList();
        var qualifiedDates = credit.Reviews.Where(r => r.Qualification == ReviewQualification.Qualified)
            .Select(r => r.EffectiveDate).ToList();

        void Lit(string id, string factor, double value)
        {
            if (value > 0) signals.Add(new GovernanceSignal(id, factor, IcrMath.Clamp01(value)));
        }

        // ---- GOV.A: one state (acceptance and appetite), by MAX ----
        if (above && valid.Count == 0)
        {
            var residualAt = facts.ResidualUpdatedAt ?? DateTime.MinValue;
            var treated = facts.Tasks.Any(t => t.Status is MitigationTaskStatus.Open or MitigationTaskStatus.InProgress) ||
                          facts.Tasks.Any(t => t.Status == MitigationTaskStatus.Completed && t.CompletedAt > residualAt);
            Lit(GovernanceSignalIds.AppetiteBreach, RiskIndexFactors.GovAppetite,
                treated ? s.AppetiteBreachTreated : s.AppetiteBreach);
        }

        if (above && valid.Count > 0)
            Lit(GovernanceSignalIds.AcceptanceBeforeAppetite, RiskIndexFactors.GovAppetite, s.AcceptanceBeforeAppetite);

        var last = facts.Acceptances.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).FirstOrDefault();
        if (last is not null && !IsValid(last, now) && facts.Acceptances.All(a => a.RenewedFromId != last.Id))
        {
            var revoked = last.RevokedAt is not null || last.Status == RiskAcceptanceStatus.Revoked;
            var end = last.End;
            var lapsed = revoked || last.ExpiresAt <= now;
            var afterGrace = IcrMath.Days(end, now) > p.AcceptanceGraceDays;
            var decidedSince = qualifiedDates.Any(d => d > end) ||
                               facts.CampaignItems.Any(i => i.Decision != RiskReviewDecision.Pending && i.DecidedAt > end);
            var stillNeeded = !(ceiling is { } cap && score <= cap);

            if (lapsed && afterGrace && !decidedSince && stillNeeded)
                Lit(GovernanceSignalIds.AcceptanceExpired, RiskIndexFactors.GovAppetite,
                    revoked ? s.AcceptanceRevoked : s.AcceptanceExpired);
        }

        var current = valid.OrderByDescending(a => a.CreatedAt).ThenByDescending(a => a.Id).FirstOrDefault();
        if (current?.ResidualScoreSnapshot is { } snapshot && p.AcceptanceDriftAmplitude > 0)
            Lit(GovernanceSignalIds.AcceptanceDrift, RiskIndexFactors.GovAppetite,
                Math.Clamp((score - snapshot - p.AcceptanceDriftStart) / p.AcceptanceDriftAmplitude, 0, 1));

        if (valid.Count > 0)
        {
            var acceptanceReviews = credit.Reviews.Where(r => r.Qualification == ReviewQualification.DerivedFromAcceptance)
                .Select(r => r.Id).ToHashSet();
            var pending = facts.Reviews.Any(r => acceptanceReviews.Contains(r.Id) && r.RequiresCountersignature &&
                                                 r.SecondReviewerId is null &&
                                                 IcrMath.Days(r.SubmissionDate, now) > p.CounterSignatureDays);
            if (pending) Lit(GovernanceSignalIds.CounterSignature, RiskIndexFactors.GovAppetite, s.CounterSignaturePending);
        }

        // ---- GOV.C: the campaign decision — mutually exclusive, by MAX ----
        var overduePending = facts.CampaignItems.Any(i => i.Decision == RiskReviewDecision.Pending &&
            (i.CampaignStatus == RiskReviewCampaignStatus.Overdue ||
             i.CampaignStatus == RiskReviewCampaignStatus.Open && i.CampaignDueDate < now));
        if (overduePending) Lit(GovernanceSignalIds.CampaignPending, RiskIndexFactors.GovCampaign, s.CampaignPending);

        var lastDecision = facts.CampaignItems.Where(i => i.Decision != RiskReviewDecision.Pending && i.DecidedAt is not null)
            .OrderByDescending(i => i.DecidedAt).ThenByDescending(i => i.Id).FirstOrDefault();
        if (lastDecision is { Decision: RiskReviewDecision.Escalated, DecidedAt: { } escalatedAt })
        {
            var followed = qualifiedDates.Any(d => d > escalatedAt) ||
                           facts.Acceptances.Any(a => a.CreatedAt > escalatedAt) ||
                           facts.Tasks.Any(t => t.CreatedAt > escalatedAt);
            if (!followed && IcrMath.Days(escalatedAt, now) > p.DecisionFollowUpDays)
                Lit(GovernanceSignalIds.CampaignEscalated, RiskIndexFactors.GovCampaign, s.CampaignEscalated);
        }

        if (lastDecision is { Decision: RiskReviewDecision.MitigationRequested, DecidedAt: { } requestedAt })
        {
            var window = TimeSpan.FromSeconds(Math.Max(1, p.DerivedReviewWindowSeconds));
            var created = facts.Tasks.Where(t => (t.CreatedAt - requestedAt).Duration() <= window).ToList();
            if (created.Count > 0 && created.All(t => t.Status == MitigationTaskStatus.Cancelled))
                Lit(GovernanceSignalIds.CampaignMitigation, RiskIndexFactors.GovCampaign, s.CampaignMitigate);
        }

        // ---- GOV.T: overdue tasks, no denominator ----
        var overdue = facts.Tasks
            .Where(t => t.Status is MitigationTaskStatus.Open or MitigationTaskStatus.InProgress && t.DueDate < now)
            .Select(t => Math.Min(1, IcrMath.Days(t.DueDate!.Value, now) / Math.Max(1, p.TaskReferenceDays)))
            .DefaultIfEmpty(0).Max();
        Lit(GovernanceSignalIds.TaskOverdue, RiskIndexFactors.GovTreatment, overdue);

        // ---- GOV.R: review integrity, by MRA ----
        if (facts.ReviewRequestedAt is { } requested && !facts.ReviewRequestFromAcceptance &&
            requested > credit.ReferenceDate && IcrMath.Days(requested, now) > p.RequestedReviewDays)
            Lit(GovernanceSignalIds.ReviewRequested, RiskIndexFactors.GovReview, s.ReviewRequested);

        if (!credit.TreatmentAssessed && credit.DelayDays > 0)
            Lit(GovernanceSignalIds.ReviewNoCredit, RiskIndexFactors.GovReview, Math.Min(1, credit.DelayDays / credit.CadenceDays));

        // ---- GOV.G: technical evidence diverging from the residual (the caller latches it) ----
        if (divergenceLit) Lit(GovernanceSignalIds.Divergence, RiskIndexFactors.GovDivergence, s.Divergence);

        var factors = new Dictionary<string, double>
        {
            [RiskIndexFactors.GovAppetite] = Max(signals, RiskIndexFactors.GovAppetite),
            [RiskIndexFactors.GovCampaign] = Max(signals, RiskIndexFactors.GovCampaign),
            [RiskIndexFactors.GovTreatment] = Max(signals, RiskIndexFactors.GovTreatment),
            [RiskIndexFactors.GovReview] = IcrMath.Mra(signals.Where(g => g.Factor == RiskIndexFactors.GovReview)
                .Select(g => g.Value), p.Rho, p.KSignals),
            [RiskIndexFactors.GovDivergence] = Max(signals, RiskIndexFactors.GovDivergence)
        };

        var r = credit.SigmaRef * IcrMath.Mra(factors.Select(f => Ceiling(p, f.Key) * f.Value), p.Rho, p.KSignals);

        return new ScenarioGovernance
        {
            Factors = factors,
            Signals = signals,
            R = IcrMath.Clamp01(r),
            AppetiteConfigured = ceiling is not null,
            AboveCeiling = above,
            AcceptedAboveCeiling = above && valid.Count > 0,
            HasValidAcceptance = valid.Count > 0,
            AcceptanceExpiringSoon = valid.Any(a => IcrMath.Days(now, a.ExpiresAt) <= p.ExpiryWarningDays)
        };
    }

    /// <summary>
    /// <c>e_o</c> of a GOV scenario (S39 §8.4): 1, or <c>1 − u_A/Σu</c> with no applicable ceiling — the appetite part is
    /// not assessable.
    /// </summary>
    public static double CoverageEffect(RiskIndexParameters p, bool appetiteConfigured)
    {
        if (appetiteConfigured) return 1;
        string[] factors = [RiskIndexFactors.GovAppetite, RiskIndexFactors.GovCampaign, RiskIndexFactors.GovTreatment,
            RiskIndexFactors.GovReview, RiskIndexFactors.GovDivergence];
        var total = factors.Sum(f => Ceiling(p, f));
        return total > 0 ? 1 - Ceiling(p, RiskIndexFactors.GovAppetite) / total : 1;
    }

    /// <summary>One finding open for SLA, as the SLA signal reads it.</summary>
    public readonly record struct SlaFinding(int Severity, DateTime FirstDetection, DateTime DueDate);

    /// <summary>
    /// <c>SLA_h = max_f a_sev(f)·min(1, overdue(f)/period(f))</c> over the asset's findings open for SLA (S39 §6.3), the
    /// period being the remediation time the policy granted (<c>sla_due_date − first detection</c>). Nothing overdue is 0.
    /// </summary>
    public static double Sla(RiskIndexParameters p, IEnumerable<SlaFinding> findings, DateTime now) =>
        findings.Select(f =>
        {
            var overdueDays = (now.Date - f.DueDate.Date).TotalDays;
            if (overdueDays <= 0) return 0;
            var period = Math.Max(1, (f.DueDate.Date - f.FirstDetection.Date).TotalDays);
            return IcrAnchors.SeverityAnchor(p, f.Severity) * Math.Min(1, overdueDays / period);
        }).DefaultIfEmpty(0).Max();

    public static double Ceiling(RiskIndexParameters p, string factor) =>
        p.FactorCeilings.TryGetValue(factor, out var u) ? IcrMath.Clamp01(u) : 1;

    private static double Max(IEnumerable<GovernanceSignal> signals, string factor) =>
        signals.Where(g => g.Factor == factor).Select(g => g.Value).DefaultIfEmpty(0).Max();
}

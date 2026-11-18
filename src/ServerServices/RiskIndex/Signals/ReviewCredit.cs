using Model.RiskIndex;
using ServerServices.Governance;
using ServerServices.RiskIndex.Engine;
using Tools.Risks;

namespace ServerServices.RiskIndex.Signals;

/// <summary>The treatment credit of one scenario and everything the object row stores about it (S39 §6.1, §9.1).</summary>
public sealed record ReviewCreditResult
{
    /// <summary><c>d_ref</c>: the latest qualified review's effective date, or the submission date.</summary>
    public DateTime ReferenceDate { get; init; }

    public bool ReferenceIsSubmission { get; init; }

    /// <summary><c>T</c>, days, from the profile's approved cadence copy, and the register band index it came from.</summary>
    public int CadenceDays { get; init; }

    public int CadenceBand { get; init; }

    /// <summary><c>due = min(d_ref + T, fim)</c>; <see cref="DueByAcceptance"/> when an acceptance's end set it.</summary>
    public DateTime Due { get; init; }

    public bool DueByAcceptance { get; init; }

    /// <summary>Days past <see cref="Due"/>, 0 when not overdue.</summary>
    public double DelayDays { get; init; }

    public double Credit { get; init; } = 1;

    /// <summary><c>r_eff</c>, 0–10, never written back (I9); <c>σ_r = r_eff/10</c> and <c>σ_ref = (r_res ?? r_inh)/10</c>.</summary>
    public double EffectiveResidual { get; init; }

    public double SigmaR { get; init; }
    public double SigmaRef { get; init; }

    /// <summary>A residual is stored: false reads "treatment not assessed" and enters by the inherent score.</summary>
    public bool TreatmentAssessed { get; init; }

    /// <summary>The stored residual exceeds the inherent score (quality chip "residual &gt; inherent").</summary>
    public bool ResidualAboveInherent { get; init; }

    public IReadOnlyList<ClassifiedReview> Reviews { get; init; } = [];
}

/// <summary>
/// The review as the expiry date of a residual (S39 §6.1): a residual nobody confirms within the cadence slides back
/// to the inherent score. Acceptances and campaign decisions never restore credit (I10); an acceptance's end only
/// brings the due date forward. Pure: the reader supplies the facts, the snapshot instant is explicit.
/// </summary>
public static class ReviewCredit
{
    /// <summary>The register score that sets the cadence (<c>base_cadencia</c>): inherent, or residual falling back to it.</summary>
    public static double CadenceScore(ScenarioFacts facts, RiskIndexParameters p) =>
        p.CadenceBasis == RiskIndexCadenceBasis.ResidualRisk ? facts.Residual ?? facts.Inherent : facts.Inherent;

    /// <summary><c>cadencia_revisao[Faixa(score; limites_registro)]</c>, on the raw 0–10 score (S39 §4.5, §6.1).</summary>
    public static (int Days, int Band) Cadence(double score, RiskIndexParameters p)
    {
        var limits = p.RegisterLimits;
        var band = RiskBand.Faixa(score, [limits.Low, limits.Medium, limits.High, limits.VeryHigh]);
        var cadence = p.ReviewCadence;
        var days = band switch
        {
            RiskBand.VeryHigh => cadence.VeryHigh,
            RiskBand.High => cadence.High,
            RiskBand.Medium => cadence.Medium,
            _ => cadence.Low
        };
        return (Math.Max(1, days), Math.Max(RiskBand.Low, band));
    }

    /// <summary>
    /// Which reviews qualify (S39 §3.3) and their effective dates: the product's rule (<see cref="ReviewDueRule"/>), with
    /// the profile's <c>janela_revisao_derivada</c>. The index reads the rule; the decision path never reads the index.
    /// </summary>
    public static IReadOnlyList<ClassifiedReview> Classify(ScenarioFacts facts, RiskIndexParameters p, DateTime now) =>
        ReviewDueRule.Classify(History(facts), DerivedReviewWindow(p), now);

    /// <summary><c>janela_revisao_derivada</c> of <paramref name="p"/>.</summary>
    public static TimeSpan DerivedReviewWindow(RiskIndexParameters p) =>
        TimeSpan.FromSeconds(Math.Max(0, p.DerivedReviewWindowSeconds));

    /// <summary><c>d_ref</c> and <c>due = min(d_ref + T, fim)</c> of one scenario, by <see cref="ReviewDueRule.ResolveDue"/>.</summary>
    public static ReviewDue ResolveDue(ScenarioFacts facts, int cadenceDays, TimeSpan derivedWindow, DateTime now) =>
        ReviewDueRule.ResolveDue(History(facts), cadenceDays, derivedWindow, now);

    /// <summary>The scenario's facts as the review rule reads them.</summary>
    private static ReviewHistory History(ScenarioFacts facts) => new()
    {
        SubmissionDate = facts.SubmissionDate,
        SubmittedBy = facts.SubmittedBy,
        Owner = facts.Owner,
        Manager = facts.Manager,
        Reviews = facts.Reviews.Select(r => new ReviewEvidence
        {
            Id = r.Id, ReviewerId = r.ReviewerId, SubmissionDate = r.SubmissionDate, AuditedAt = r.AuditedAt,
            CorrelationId = r.CorrelationId, SavedWithAcceptance = r.SavedWithAcceptance,
            SavedWithCampaignDecision = r.SavedWithCampaignDecision
        }).ToList(),
        Acceptances = facts.Acceptances.Select(a => new AcceptanceEvidence
        {
            CreatedAt = a.CreatedAt, End = a.End, CreatedById = a.CreatedById, RequestedById = a.RequestedById,
            AuthorizingManagerId = a.AuthorizingManagerId
        }).ToList(),
        Decisions = facts.CampaignItems.Where(c => c.DecidedAt != null)
            .Select(c => new CampaignDecisionEvidence(c.DecidedAt!.Value, c.DecidedById)).ToList()
    };

    public static ReviewCreditResult Compute(ScenarioFacts facts, RiskIndexParameters p, DateTime now)
    {
        var (cadence, band) = Cadence(CadenceScore(facts, p), p);
        var (reference, referenceReview, due, byAcceptance, reviews) = ResolveDue(facts, cadence, DerivedReviewWindow(p), now);

        var delay = Math.Max(0, IcrMath.Days(due, now));
        var credit = delay <= 0 ? 1 : Math.Max(p.MinimumCredit, 1 - p.CreditLambda * delay / cadence);

        var inherent = Math.Clamp(facts.Inherent, 0, 10);
        double? residual = facts.Residual is { } r ? Math.Clamp(r, 0, 10) : null;
        var effective = residual is { } res ? res + (1 - credit) * Math.Max(0, inherent - res) : inherent;

        return new ReviewCreditResult
        {
            ReferenceDate = reference,
            ReferenceIsSubmission = referenceReview is null,
            CadenceDays = cadence,
            CadenceBand = band,
            Due = due,
            DueByAcceptance = byAcceptance,
            DelayDays = delay,
            Credit = credit,
            EffectiveResidual = effective,
            SigmaR = effective / 10,
            SigmaRef = (residual ?? inherent) / 10,
            TreatmentAssessed = residual is not null,
            ResidualAboveInherent = residual > inherent,
            Reviews = reviews
        };
    }
}

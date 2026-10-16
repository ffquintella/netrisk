using Model.RiskIndex;
using ServerServices.RiskIndex.Engine;
using Tools.Risks;

namespace ServerServices.RiskIndex.Signals;

/// <summary>Why a management review does or does not restore treatment credit (S39 §3.3, §6.1).</summary>
public enum ReviewQualification
{
    Qualified = 1,

    /// <summary>Written by an acceptance (same save, or the same risk and user within <c>janela_revisao_derivada</c>).</summary>
    DerivedFromAcceptance = 2,

    /// <summary>Written by a campaign decision, likewise.</summary>
    DerivedFromCampaign = 3,

    /// <summary>The reviewer is the risk's submitter, owner or manager (Track 8 segregation, D-07).</summary>
    SelfReview = 4
}

/// <summary>One review as the credit reads it: whether it qualifies, and its effective date.</summary>
public sealed record ClassifiedReview(int Id, ReviewQualification Qualification, DateTime EffectiveDate);

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
    /// Which reviews qualify (S39 §3.3): not written by an acceptance or a campaign decision — the same save's
    /// correlation id, or without an audit row the same risk and user within the window — and not a self-review.
    /// Effective date <c>min(SubmissionDate, audited creation, now)</c>: a future-dated review does not keep credit.
    /// </summary>
    public static IReadOnlyList<ClassifiedReview> Classify(ScenarioFacts facts, RiskIndexParameters p, DateTime now)
    {
        var window = TimeSpan.FromSeconds(Math.Max(0, p.DerivedReviewWindowSeconds));
        var owners = new[] { facts.SubmittedBy, facts.Owner, facts.Manager };

        return facts.Reviews.Select(r =>
        {
            var effective = Min(Min(r.SubmissionDate, r.AuditedAt ?? r.SubmissionDate), now);

            ReviewQualification qualification;
            if (r.SavedWithAcceptance)
                qualification = ReviewQualification.DerivedFromAcceptance;
            else if (r.SavedWithCampaignDecision)
                qualification = ReviewQualification.DerivedFromCampaign;
            else if (r.CorrelationId is null && facts.Acceptances.Any(a =>
                         Near(r.SubmissionDate, a.CreatedAt, window) &&
                         (r.ReviewerId == a.CreatedById || r.ReviewerId == a.RequestedById ||
                          r.ReviewerId == a.AuthorizingManagerId)))
                qualification = ReviewQualification.DerivedFromAcceptance;
            else if (r.CorrelationId is null && facts.CampaignItems.Any(c =>
                         c.DecidedAt is { } decided && Near(r.SubmissionDate, decided, window) &&
                         r.ReviewerId == c.DecidedById))
                qualification = ReviewQualification.DerivedFromCampaign;
            else if (owners.Contains(r.ReviewerId))
                qualification = ReviewQualification.SelfReview;
            else
                qualification = ReviewQualification.Qualified;

            return new ClassifiedReview(r.Id, qualification, effective);
        }).ToList();
    }

    public static ReviewCreditResult Compute(ScenarioFacts facts, RiskIndexParameters p, DateTime now)
    {
        var reviews = Classify(facts, p, now);
        var qualified = reviews.Where(r => r.Qualification == ReviewQualification.Qualified).ToList();

        var reference = qualified.Count > 0 ? qualified.Max(r => r.EffectiveDate) : facts.SubmissionDate;
        var (cadence, band) = Cadence(CadenceScore(facts, p), p);

        var due = reference.AddDays(cadence);
        var byAcceptance = false;
        var ends = facts.Acceptances.Where(a => a.End >= reference).Select(a => a.End).ToList();
        if (ends.Count > 0 && ends.Min() < due)
        {
            due = ends.Min();
            byAcceptance = true;
        }

        var delay = Math.Max(0, IcrMath.Days(due, now));
        var credit = delay <= 0 ? 1 : Math.Max(p.MinimumCredit, 1 - p.CreditLambda * delay / cadence);

        var inherent = Math.Clamp(facts.Inherent, 0, 10);
        double? residual = facts.Residual is { } r ? Math.Clamp(r, 0, 10) : null;
        var effective = residual is { } res ? res + (1 - credit) * Math.Max(0, inherent - res) : inherent;

        return new ReviewCreditResult
        {
            ReferenceDate = reference,
            ReferenceIsSubmission = qualified.Count == 0,
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

    private static DateTime Min(DateTime a, DateTime b) => a <= b ? a : b;

    private static bool Near(DateTime a, DateTime b, TimeSpan window) => (a - b).Duration() <= window;
}

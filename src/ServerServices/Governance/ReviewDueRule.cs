namespace ServerServices.Governance;

/// <summary>Why a management review does or does not restart the review cadence (S39 §3.3, §6.1).</summary>
public enum ReviewQualification
{
    Qualified = 1,

    /// <summary>Written by an acceptance (same save, or the same risk and user within the derived-review window).</summary>
    DerivedFromAcceptance = 2,

    /// <summary>Written by a campaign decision, likewise.</summary>
    DerivedFromCampaign = 3,

    /// <summary>The reviewer is the risk's submitter, owner or manager (Track 8 segregation, D-07).</summary>
    SelfReview = 4
}

/// <summary>One review as the rule reads it: whether it qualifies, and its effective date.</summary>
public sealed record ClassifiedReview(int Id, ReviewQualification Qualification, DateTime EffectiveDate);

/// <summary><c>d_ref</c> and <c>due</c> of one risk (S39 §6.1) for a cadence the caller resolved.</summary>
/// <param name="ReferenceDate"><c>d_ref</c>: the latest qualified review's effective date, or the submission date.</param>
/// <param name="ReferenceReview">The review that set <c>d_ref</c>; null when none qualifies.</param>
/// <param name="Due"><c>min(d_ref + T, fim)</c>.</param>
/// <param name="DueByAcceptance">An acceptance's end set <see cref="Due"/>.</param>
public sealed record ReviewDue(DateTime ReferenceDate, ClassifiedReview? ReferenceReview, DateTime Due,
    bool DueByAcceptance, IReadOnlyList<ClassifiedReview> Reviews);

/// <summary>A management review, with what the audit trail says about its creation.</summary>
public sealed record ReviewEvidence
{
    public int Id { get; init; }
    public int ReviewerId { get; init; }
    public DateTime SubmissionDate { get; init; }

    /// <summary>When <c>audit_logs</c> recorded the row's creation, and that save's correlation id; null without a row.</summary>
    public DateTime? AuditedAt { get; init; }

    public string? CorrelationId { get; init; }

    /// <summary>The same save created a risk acceptance, or recorded a campaign decision (shared correlation id).</summary>
    public bool SavedWithAcceptance { get; init; }

    public bool SavedWithCampaignDecision { get; init; }
}

/// <summary>A risk acceptance, any status; <see cref="End"/> is <c>fim(a) = RevokedAt ?? ExpiresAt</c>.</summary>
public sealed record AcceptanceEvidence
{
    public DateTime CreatedAt { get; init; }
    public DateTime End { get; init; }
    public int? CreatedById { get; init; }
    public int? RequestedById { get; init; }
    public int AuthorizingManagerId { get; init; }
}

/// <summary>A decided campaign item of the risk.</summary>
public sealed record CampaignDecisionEvidence(DateTime DecidedAt, int? DecidedById);

/// <summary>Everything the rule reads about one risk's reviews.</summary>
public sealed record ReviewHistory
{
    public DateTime SubmissionDate { get; init; }
    public int? SubmittedBy { get; init; }
    public int? Owner { get; init; }
    public int? Manager { get; init; }
    public IReadOnlyList<ReviewEvidence> Reviews { get; init; } = [];
    public IReadOnlyList<AcceptanceEvidence> Acceptances { get; init; } = [];
    public IReadOnlyList<CampaignDecisionEvidence> Decisions { get; init; } = [];
}

/// <summary>
/// When a risk's management review falls due (S39 §6.1). The one implementation: the overdue-review list
/// (<c>MgmtReviewsService</c>, T278) calls it, and the risk index's treatment credit maps its facts onto it — the index
/// reads the product's rule, never the other way round (S39 §1.3, R-1). Pure; the instant is explicit.
/// </summary>
public static class ReviewDueRule
{
    /// <summary><c>janela_revisao_derivada</c>'s default (S39 §3.3), the window the product list uses.</summary>
    public static readonly TimeSpan DefaultDerivedWindow = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Which reviews qualify (S39 §3.3): not written by an acceptance or a campaign decision — the same save's correlation
    /// id, or without an audit row the same risk and user within <paramref name="window"/> — and not a self-review.
    /// Effective date <c>min(SubmissionDate, audited creation, now)</c>: a future-dated review does not hold the date.
    /// </summary>
    public static IReadOnlyList<ClassifiedReview> Classify(ReviewHistory history, TimeSpan window, DateTime now)
    {
        var owners = new[] { history.SubmittedBy, history.Owner, history.Manager };

        return history.Reviews.Select(r =>
        {
            var effective = Min(Min(r.SubmissionDate, r.AuditedAt ?? r.SubmissionDate), now);

            ReviewQualification qualification;
            if (r.SavedWithAcceptance)
                qualification = ReviewQualification.DerivedFromAcceptance;
            else if (r.SavedWithCampaignDecision)
                qualification = ReviewQualification.DerivedFromCampaign;
            else if (r.CorrelationId is null && history.Acceptances.Any(a =>
                         Near(r.SubmissionDate, a.CreatedAt, window) &&
                         (r.ReviewerId == a.CreatedById || r.ReviewerId == a.RequestedById ||
                          r.ReviewerId == a.AuthorizingManagerId)))
                qualification = ReviewQualification.DerivedFromAcceptance;
            else if (r.CorrelationId is null && history.Decisions.Any(c =>
                         Near(r.SubmissionDate, c.DecidedAt, window) && r.ReviewerId == c.DecidedById))
                qualification = ReviewQualification.DerivedFromCampaign;
            else if (owners.Contains(r.ReviewerId))
                qualification = ReviewQualification.SelfReview;
            else
                qualification = ReviewQualification.Qualified;

            return new ClassifiedReview(r.Id, qualification, effective);
        }).ToList();
    }

    /// <summary>
    /// <c>d_ref</c> and <c>due = min(d_ref + T, fim)</c>, with <c>fim</c> the earliest end of the acceptances — any status,
    /// renewed and superseded included — that end on or after <c>d_ref</c>: an acceptance only brings the date forward,
    /// and renewing one does not push it back (S39 §6.1, I10).
    /// </summary>
    public static ReviewDue ResolveDue(ReviewHistory history, int cadenceDays, TimeSpan window, DateTime now)
    {
        var reviews = Classify(history, window, now);
        var latest = reviews.Where(r => r.Qualification == ReviewQualification.Qualified)
            .OrderByDescending(r => r.EffectiveDate).ThenByDescending(r => r.Id).FirstOrDefault();

        var reference = latest?.EffectiveDate ?? history.SubmissionDate;

        var due = reference.AddDays(cadenceDays);
        var byAcceptance = false;
        var ends = history.Acceptances.Where(a => a.End >= reference).Select(a => a.End).ToList();
        if (ends.Count > 0 && ends.Min() < due)
        {
            due = ends.Min();
            byAcceptance = true;
        }

        return new ReviewDue(reference, latest, due, byAcceptance, reviews);
    }

    private static DateTime Min(DateTime a, DateTime b) => a <= b ? a : b;

    private static bool Near(DateTime a, DateTime b, TimeSpan window) => (a - b).Duration() <= window;
}

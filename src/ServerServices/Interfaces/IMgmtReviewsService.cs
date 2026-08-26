using DAL.Entities;
using Model.DTO;

namespace ServerServices.Interfaces;

public interface IMgmtReviewsService
{
    
    /// <summary>
    /// Gets a list of risk reviews 
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public List<MgmtReview> GetRiskReviews(int riskId);
    
    /// <summary>
    /// Gets a the review level of a risk
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public ReviewLevel GetRiskReviewLevel(int riskId);
    
    /// <summary>
    /// Gets the last review of a risk
    /// </summary>
    /// <param name="riskId"></param>
    /// <returns></returns>
    public MgmtReview? GetRiskLastReview(int riskId);
    
    /// <summary>
    /// Gets a list of review types
    /// </summary>
    /// <returns></returns>
    public List<Review> GetReviewTypes();
    
    /// <summary>
    ///  Gets a list of next steps
    /// </summary>
    /// <returns></returns>
    public List<NextStep> GetNextSteps();
    
    /// <summary>
    ///  Creates a new review
    /// </summary>
    /// <param name="review"></param>
    /// <returns></returns>
    public MgmtReview Create(MgmtReview review);

    /// <summary>
    ///  Updates a review
    /// </summary>
    /// <param name="review"></param>
    /// <returns></returns>
    public MgmtReview Update(MgmtReviewDto review);
    
    /// <summary>
    ///  Gets a review
    /// </summary>
    /// <param name="mgmtReviewId"></param>
    /// <returns></returns>
    public MgmtReview GetOne(int mgmtReviewId);

    /// <summary>
    /// Records a review as an identified user, with the Track 8 milestone 8.3 rules applied: the
    /// state machine, segregation of duties, and the appetite's dual-approval threshold — which sets
    /// <see cref="MgmtReview.RequiresCountersignature"/> rather than refusing the review.
    ///
    /// The parameterless <see cref="Create(MgmtReview)"/> remains for callers that have already
    /// applied those rules themselves (the acceptance service writes its own review row).
    /// </summary>
    Task<MgmtReview> CreateReviewAsync(MgmtReview review, int actingUserId,
        string? segregationOverrideReason = null);

    /// <summary>
    /// The second signature on a review that crossed the dual-approval threshold (8.3.4).
    ///
    /// Refuses a counter-signature from the first reviewer, from anyone too close to the risk, and
    /// from anyone without the top review band — a second approver who is not more senior is not an
    /// escalation.
    /// </summary>
    Task<MgmtReview> CountersignAsync(int reviewId, int actingUserId,
        string? segregationOverrideReason = null);

    /// <summary>
    /// The review level (cadence) for a risk, honouring the <c>next_review_date_uses</c> setting so
    /// the cadence can key off the residual score instead of the inherent one (8.2.2).
    /// </summary>
    Task<ReviewLevel> GetRiskReviewLevelAsync(int riskId);

    /// <summary>
    /// Every open risk whose management review is past its severity band's cadence, including the
    /// ones never reviewed at all (Track 8 milestone 8.5.1).
    ///
    /// Resolved here rather than in the notification job: the cadence comes from two lookup tables
    /// plus a setting, and a job that worked it out itself would be a second implementation of the
    /// rule <see cref="GetRiskReviewLevelAsync"/> already owns.
    /// </summary>
    Task<List<Model.Governance.OverdueReview>> GetOverdueReviewsAsync(DateTime asOfUtc);
}
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
    /// Updates the decision fields of an existing review after applying third-line and segregation
    /// controls to <paramref name="actingUserId"/>. Risk, reviewer and submission time are immutable.
    /// </summary>
    /// <param name="review"></param>
    /// <param name="actingUserId">The authenticated caller; never taken from the payload.</param>
    /// <returns>The persisted review.</returns>
    public Task<MgmtReview> UpdateAsync(MgmtReviewDto review, int actingUserId);
    
    /// <summary>
    ///  Gets a review
    /// </summary>
    /// <param name="mgmtReviewId"></param>
    /// <returns></returns>
    public MgmtReview GetOne(int mgmtReviewId);

    /// <summary>
    /// Records a review as an identified user, with the Track 8 milestone 8.3 rules applied: the
    /// third-line guard, segregation of duties (the caller must not have submitted, own or manage
    /// the risk — administrators included), and the appetite's dual-approval threshold — which sets
    /// <see cref="MgmtReview.RequiresCountersignature"/> rather than refusing the review. The
    /// reviewer recorded is always <paramref name="actingUserId"/>, whatever the review names.
    ///
    /// This is the only way to create a review through this service. The legacy
    /// <c>Create(MgmtReview)</c> carried no acting user, so it could check nothing, and
    /// <c>POST /MgmtReviews</c> reached it without segregation of duties (S53 §11, defect 2); it was
    /// removed rather than kept beside this method, and
    /// <c>MgmtReviewSegregationInMemoryTest.TestMR5_EveryReviewWritingMethodNamesTheActingUser</c>
    /// fails if a review-writing method without the acting user comes back. The acceptance service
    /// writes its own review row and does not go through here.
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

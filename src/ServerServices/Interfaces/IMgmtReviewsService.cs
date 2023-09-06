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
}
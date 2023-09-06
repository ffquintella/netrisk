using DAL.Entities;
using Model.DTO;

namespace ClientServices.Interfaces;

public interface IMgmtReviewsService
{
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
    /// Creates a new MgmtReview
    /// </summary>
    /// <param name="mgmtReviewDto"></param>
    /// <returns></returns>
    public MgmtReview Create(MgmtReviewDto mgmtReviewDto);
}
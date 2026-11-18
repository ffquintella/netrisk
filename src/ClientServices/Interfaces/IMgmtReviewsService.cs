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
    /// Records a review in the caller's name; the server dates it. A refusal naming a rule (segregation of duties) is a
    /// <see cref="Model.Exceptions.RuleBrokenException"/>.
    /// </summary>
    public MgmtReview Create(MgmtReviewDto mgmtReviewDto);

    /// <summary>The open risks in the caller's scope whose management review is due (<c>GET /MgmtReviews/Overdue</c>, T278).</summary>
    Task<List<Model.Governance.OverdueReview>> GetOverdueReviewsAsync();
}
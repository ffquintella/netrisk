using DAL.Entities;

namespace ClientServices.Interfaces;

public interface IMgmtReviewsService
{
    /// <summary>
    /// Gets a list of review types
    /// </summary>
    /// <returns></returns>
    public List<Review> GetReviewTypes();
}
using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IReportsService
{
    /// <summary>
    /// Get all reports
    /// </summary>
    /// <returns></returns>
    public List<Report> GetAll();
    
    /// <summary>
    /// Create a report
    /// </summary>
    /// <param name="report"></param>
    /// <returns></returns>
    public Task<Report> CreateAsync(Report report, User user);
    
    /// <summary>
    /// Delete a report
    /// </summary>
    /// <param name="reportId"></param>
    public void Delete(int reportId);
}
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
    public Report Create(Report report);
    
    /// <summary>
    /// Delete a report
    /// </summary>
    /// <param name="reportId"></param>
    public void Delete(int reportId);
}
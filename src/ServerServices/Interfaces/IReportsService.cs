using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IReportsService
{
    /// <summary>
    /// Get all reports
    /// </summary>
    /// <returns></returns>
    public List<Report> GetAll();
}
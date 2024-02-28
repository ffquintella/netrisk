using System.Collections.ObjectModel;
using DAL.Entities;
using DAL.EntitiesDto;

namespace ClientServices.Interfaces;

public interface IReportsService
{
    public Task<ObservableCollection<Report>> GetReportsAsync();
    public Task<Report> CreateReportAsync(ReportDto report);
    public Task DeleteReportAsync(int id);
}
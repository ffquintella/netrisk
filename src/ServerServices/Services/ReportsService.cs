using DAL.Entities;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

public class ReportsService: ServiceBase, IReportsService
{
    public ReportsService(ILogger logger, DALService dalService) : base(logger, dalService)
    {
    }

    public List<Report> GetAll()
    {
        using var dbContext = DalService.GetContext();
        
        var reports = dbContext.Reports.ToList();

        return reports;
    }
}
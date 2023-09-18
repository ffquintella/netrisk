using DAL;
using Microsoft.EntityFrameworkCore;
using Model.Statistics;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class StatisticsService: ServiceBase, IStatisticsService
{
    public StatisticsService(ILogger logger, DALManager dalManager) : base(logger, dalManager)
    {
    }

    public List<LabeledPoints> GetRisksVsCosts()
    {
        using var dbContext = DALManager.GetContext();
        
        //var risks = dbContext.Risks.Include(r => r.mi)

        throw new NotImplementedException("");
    }
}
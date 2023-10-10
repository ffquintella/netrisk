using DAL;
using DAL.Entities;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class TechnologiesService: ServiceBase, ITechnologiesService
{
    public TechnologiesService(ILogger logger, DALService dalService) : base(logger, dalService)
    {
    }

    public List<Technology> GetAll()
    {
        using var context = DalService.GetContext();
        
        return context.Technologies.ToList();
    }
}
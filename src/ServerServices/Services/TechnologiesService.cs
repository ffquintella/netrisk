using DAL;
using DAL.Entities;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class TechnologiesService: ServiceBase, ITechnologiesService
{
    public TechnologiesService(ILogger logger, DALManager dalManager) : base(logger, dalManager)
    {
    }

    public List<Technology> GetAll()
    {
        using var context = DalManager.GetContext();
        
        return context.Technologies.ToList();
    }
}
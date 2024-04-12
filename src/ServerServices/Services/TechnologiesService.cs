using DAL;
using DAL.Entities;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class TechnologiesService(ILogger logger, IDalService dalService)
    : ServiceBase(logger, dalService), ITechnologiesService
{
    public List<Technology> GetAll()
    {
        using var context = DalService.GetContext();
        
        return context.Technologies.ToList();
    }
}
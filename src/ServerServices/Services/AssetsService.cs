using DAL;
using DAL.Entities;
using Serilog;
using ServerServices.Interfaces;

namespace ServerServices.Services;

public class AssetsService: ServiceBase, IAssetsService
{

    public AssetsService(ILogger logger, DALManager dalManager): base(logger, dalManager)
    {
        
    }
    
    
    public List<Asset> GetAssets()
    {
        var dbContext = DALManager.GetContext();
        var assets = dbContext.Assets.ToList();
        //if (assets == null) return new List<Asset>();
        return assets;
        
    }
}
using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IAssetManagementService
{
    public List<Asset> GetAssets();
}
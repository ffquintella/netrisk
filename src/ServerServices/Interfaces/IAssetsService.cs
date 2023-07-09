using DAL.Entities;

namespace ServerServices.Interfaces;

public interface IAssetsService
{
    public List<Asset> GetAssets();
}
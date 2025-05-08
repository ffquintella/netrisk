using Model.Services;

namespace ServerServices.Interfaces;

public interface IFaceIDService
{
    public Task<ServiceInformation> GetInfoAsync();
}
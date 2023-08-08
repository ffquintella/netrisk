using Model.Entities;

namespace ClientServices.Interfaces;

public interface IEntitiesService
{
    public Task<EntitiesConfiguration> GetEntitiesConfigurationAsync();
}
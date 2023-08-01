using Model.Entities;

namespace ServerServices.Interfaces;

public interface IEntitiesService
{
    /// <summary>
    /// Loads the entities configuration from the disk
    /// </summary>
    /// <returns></returns>
    public Task<EntitiesConfiguration> GetEntitiesConfigurationAsync();
}
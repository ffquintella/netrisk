using DAL.Entities;
using Model.Entities;

namespace ClientServices.Interfaces;

public interface IEntitiesService
{
    /// <summary>
    /// Gets the configuration
    /// </summary>
    /// <returns></returns>
    public Task<EntitiesConfiguration> GetEntitiesConfigurationAsync();
    
    /// <summary>
    /// Gets all entities
    /// </summary>
    /// <returns></returns>
    public List<Entity> GetAll(string? definitionName = null);
    
    /// <summary>
    /// Creates a new entity
    /// </summary>
    /// <param name="entityDto"></param>
    /// <returns></returns>
    public Entity? CreateEntity(EntityDto entityDto);
}
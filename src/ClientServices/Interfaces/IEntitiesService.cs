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
    
    /// <summary>
    /// Saves an entity
    /// </summary>
    /// <param name="entityDto"></param>
    /// <returns></returns>
    public Entity? SaveEntity(EntityDto entityDto);
    
    /// <summary>
    /// Gets the mandatory properties for a definition
    /// </summary>
    /// <param name="definitionName"></param>
    /// <returns></returns>
    public Task<List<EntitiesPropertyDto>> GetMandatoryPropertiesAsync(string definitionName);
}
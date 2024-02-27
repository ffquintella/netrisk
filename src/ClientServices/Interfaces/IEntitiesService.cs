using System.Collections.Generic;
using System.Threading.Tasks;
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
    /// Gets the entity contiguration
    /// </summary>
    /// <returns></returns>
    public EntitiesConfiguration GetEntitiesConfiguration();
    
    
    /// <summary>
    /// Gets the entity by it´s ID
    /// </summary>
    /// <param name="entityId"></param>
    /// <returns></returns>
    public Entity GetEntity(int entityId, bool loadProperties = true);
    
    /// <summary>
    /// Gets all entities
    /// </summary>
    /// <returns></returns>
    public List<Entity> GetAll(string? definitionName = null, bool loadProperties = true);
        
    /// <summary>
     /// Loads all entities
     /// </summary>
     /// <param name="definitionName"></param>
     /// <param name="loadProperties"></param>
     /// <returns></returns>
    public Task<List<Entity>> GetAllAsync(string? definitionName = null, bool loadProperties = true);
    
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
    /// Deletes one entity
    /// </summary>
    /// <param name="entityId"></param>
    public void Delete(int entityId);
    
    /// <summary>
    /// Gets the mandatory properties for a definition
    /// </summary>
    /// <param name="definitionName"></param>
    /// <returns></returns>
    public Task<List<EntitiesPropertyDto>> GetMandatoryPropertiesAsync(string definitionName);
}
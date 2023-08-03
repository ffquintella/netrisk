using DAL.Entities;
using Model.Entities;

namespace ServerServices.Interfaces;

public interface IEntitiesService
{
    /// <summary>
    /// Loads the entities configuration from the disk
    /// </summary>
    /// <returns></returns>
    public Task<EntitiesConfiguration> GetEntitiesConfigurationAsync();
    
    /// <summary>
    /// Creates a new entity object
    /// </summary>
    /// <param name="entityName"></param>
    /// <param name="entityDefinitionName"></param>
    /// <returns></returns>
    public Entity CreateInstance(int userId, string entityDefinitionName);
    
    /// <summary>
    /// Validates the property list
    /// </summary>
    /// <param name="entityDefinitionName"></param>
    /// <param name="properties"></param>
    public void ValidatePropertyList(string entityDefinitionName, List<EntitiesPropertyDto> properties);
    
    /// <summary>
    /// Creates a new EntityProperty
    /// </summary>
    /// <param name="entityDefinitionName"></param>
    /// <param name="entity"></param>
    /// <param name="property"></param>
    /// <returns></returns>
    public EntitiesProperty CreateProperty(string entityDefinitionName, ref Entity entity, EntitiesPropertyDto property);
    
    /// <summary>
    /// List all Entities
    /// </summary>
    /// <returns></returns>
    public List<Entity> GetEntities();

    public Entity GetEntity(int id);
}
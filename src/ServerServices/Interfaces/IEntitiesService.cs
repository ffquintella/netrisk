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
    public Entity CreateInstance(int userId, string entityDefinitionName, int parentEntityId = 0);
    
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
    /// Updates an EntityProperty
    /// </summary>
    /// <param name="entityDefinitionName"></param>
    /// <param name="entity"></param>
    /// <param name="property"></param>
    /// <returns></returns>
    public EntitiesProperty UpdateProperty(ref Entity entity, EntitiesPropertyDto property, bool save=true);

    /// <summary>
    /// Reconciles an entity's property bag against the complete set of properties it should have,
    /// matching on property type and value rather than on caller-supplied row ids.
    /// </summary>
    /// <param name="entity">The entity to reconcile; its EntitiesProperties is replaced with the result.</param>
    /// <param name="properties">The complete set of properties the entity should end up with.</param>
    /// <returns>The persisted rows, in definition order.</returns>
    public List<EntitiesProperty> ReplaceProperties(Entity entity, List<EntitiesPropertyDto> properties);
    
    
    /// <summary>
    /// Updates an Entity
    /// </summary>
    /// <param name="entity"></param>
    public void UpdateEntity(Entity entity);
    
    /// <summary>
    /// Updates an Entity Property
    /// </summary>
    /// <param name="property"></param>
    public void UpdateEntitiesProperty(EntitiesProperty property);
    
    
    
    /// <summary>
    /// Deletes the entity property with the given id
    /// </summary>
    /// <param name="propertyId"></param>
    public void DeleteEntitiesProperty(int propertyId);
    
    /// <summary>
    /// Deletes the properties based on the type and entities id
    /// </summary>
    /// <param name="type"></param>
    /// <param name="entityId"></param>
    public void TryDeleteEntitiesProperty(string type, int entityId);
    
    /// <summary>
    /// Tries to delete the entity property with the given id if not found does nothing
    /// </summary>
    /// <param name="propertyId"></param>
    public void TryDeleteEntitiesProperty(int propertyId);
    
    /// <summary>
    /// List all Entities
    /// </summary>
    /// <returns></returns>
    public List<Entity> GetEntities(string? entityDefinitionName = null, bool propertyLoad = false);

    /// <summary>
    /// Gets one identy
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Entity GetEntity(int id);
    
    /// <summary>
    /// Deletes one entity
    /// </summary>
    /// <param name="id"></param>
    /// <returns></returns>
    public Entity DeleteEntity(int id);
}
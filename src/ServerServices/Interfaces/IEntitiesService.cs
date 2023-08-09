﻿using DAL.Entities;
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
    /// Updates an EntityProperty
    /// </summary>
    /// <param name="entityDefinitionName"></param>
    /// <param name="entity"></param>
    /// <param name="property"></param>
    /// <returns></returns>
    public EntitiesProperty UpdateProperty(ref Entity entity, EntitiesPropertyDto property, bool save=true);
    
    
    /// <summary>
    /// Updates an Entity
    /// </summary>
    /// <param name="entity"></param>
    public void UpdateEntity(Entity entity);
    
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
﻿using DAL.Entities;
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
    /// Gets all entities
    /// </summary>
    /// <returns></returns>
    public List<Entity> GetAll(string? definitionName = null, bool loadProperties = true);
    
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
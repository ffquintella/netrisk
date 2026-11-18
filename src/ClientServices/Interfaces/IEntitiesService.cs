using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using DAL.Entities;
using Model.Entities;
using Model.Exceptions;

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
    /// Clears the cache
    /// </summary>
    public void ClearCache();
    
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
    /// Every entity, always read from the API with the current session: neither reads nor writes the cache. For callers
    /// that must not be answered from data an earlier read (or an earlier session) left behind — the agent tools (S55).
    /// </summary>
    public Task<List<Entity>> GetAllFromServerAsync(bool loadProperties = true);
    
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
    [Obsolete("Please use SaveEntityAsync")]
    public Entity? SaveEntity(EntityDto entityDto);
    
    /// <summary>
    /// Saves an entity
    /// </summary>
    /// <param name="entityDto"></param>
    /// <returns></returns>
    public Task<Entity?> SaveEntityAsync(EntityDto entityDto);

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

    /// <summary>
    /// The Risk context tab's rows (<c>GET /Entities/RiskContext</c>, S40 §8.5): the entities of <paramref name="types"/>
    /// (null or empty: the server's default types) with their criticality, internet exposure and classification.
    /// </summary>
    /// <exception cref="InvalidHttpRequestException">Refused: <c>StatusCode</c>, <c>ErrorCode</c> and the server's message.</exception>
    /// <exception cref="RestComunicationException">The server did not answer.</exception>
    public Task<IReadOnlyList<EntityRiskContextRowDto>> GetRiskContextAsync(IEnumerable<string>? types = null,
        CancellationToken ct = default);

    /// <summary>
    /// Applies a risk-context batch (<c>PUT /Entities/RiskContext</c>), all or nothing. Clears the entity cache on success,
    /// since the cached entities carry the old values.
    /// </summary>
    /// <exception cref="InvalidHttpRequestException">Refused (400/403/404): <c>StatusCode</c>, <c>ErrorCode</c>, message.</exception>
    /// <exception cref="RestComunicationException">The server did not answer.</exception>
    public Task<EntityRiskContextBatchResultDto> SaveRiskContextAsync(EntityRiskContextBatchRequest request,
        CancellationToken ct = default);

    /// <summary>One entity's change history, newest first, with each batch's justification (<c>GET /Entities/{id}/History</c>).</summary>
    /// <exception cref="InvalidHttpRequestException">Refused (400/403/404): <c>StatusCode</c>, <c>ErrorCode</c>, message.</exception>
    /// <exception cref="RestComunicationException">The server did not answer.</exception>
    public Task<IReadOnlyList<EntityHistoryEntryDto>> GetHistoryAsync(int entityId, int limit = 200,
        CancellationToken ct = default);
}
using System.Security.Claims;
using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Entities;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class EntitiesController: ApiBaseController
{
    private IEntitiesService _entitiesService;
    
    public EntitiesController(ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IEntitiesService entitiesService,
        IUsersService usersService) : base(logger, httpContextAccessor, usersService)
    {
        _entitiesService = entitiesService;
    }

    [HttpGet]
    [Route("Configuration")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(EntitiesConfiguration))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<EntitiesConfiguration>> GetConfiguration()
    {

        var user = GetUser();

        try
        {
            Logger.Information("User:{User} got entities configuration", user.Value);
            var configs = await _entitiesService.GetEntitiesConfigurationAsync();

            return Ok(configs);
        }

        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting configuration: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }
    
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(EntitiesConfiguration))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Entity>> ListAll([FromQuery] string? entityDefinition = null, [FromQuery] bool propertyLoad = false)
    {

        var user = GetUser();

        try
        {
            Logger.Information("User:{User} got entities", user.Value);
            var entities = _entitiesService.GetEntities(entityDefinition, propertyLoad);

            return Ok(entities);
        }
        catch(EntityDefinitionNotFoundException ex)
        {
            Logger.Warning("Entity definition not found: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting entites: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }

    /// <summary>
    /// Whether the caller may set risk-context properties: the permission, or the Admin role that
    /// <c>PermissionAuthorizationHandler</c> admits for every permission.
    /// </summary>
    private bool HoldsRiskContextPermission()
    {
        var principal = _httpContextAccessor.HttpContext?.User;
        return principal is not null &&
               (principal.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == "Admin") ||
                principal.HasClaim(c => c.Type == "Permission" && c.Value == EntityPermissions.RiskContext));
    }

    /// <summary>
    /// The refusal as the API answers it: the status the guard chose and <c>{ error, message }</c>, plus
    /// <c>details.entityId</c> when the refusal names the row refused (S40 §8.5).
    /// </summary>
    private ObjectResult Refused(EntityRequestRefusedException ex, int userId, string operation)
    {
        Logger.Warning("User:{User} was refused {Operation}: {Error} {Message}", userId, operation, ex.Error, ex.Message);
        return ex.EntityId is { } entityId
            ? StatusCode(ex.StatusCode, new { error = ex.Error, message = ex.Message, details = new { entityId } })
            : StatusCode(ex.StatusCode, new { error = ex.Error, message = ex.Message });
    }

    /// <summary>
    /// The Risk context tab's rows (S40 §6.7, §8.5): the entities of <paramref name="types"/> (comma-separated; default
    /// <c>EntityRiskContextRules.DefaultTypes</c>) with their criticality, internet exposure and classification. Every
    /// authenticated user reads the entity map, so this has no permission beyond the class's <c>RequireValidUser</c>.
    /// </summary>
    [HttpGet]
    [Route("RiskContext")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<EntityRiskContextRowDto>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<EntityRiskContextRowDto>>> GetRiskContext([FromQuery] string? types = null,
        CancellationToken ct = default)
    {
        var user = GetUser();

        try
        {
            var requested = types?.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var rows = await _entitiesService.GetRiskContextAsync(requested, ct);
            Logger.Information("User:{User} read the risk context of {Count} entities", user.Value, rows.Count);
            return Ok(rows);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error while reading the risk context: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Applies a risk-context batch all or nothing (S40 §6.7, §8.5): one justification, one save, one correlation id.
    /// The attribute requires <c>entity_risk_context</c>; the service also requires global scope
    /// (<c>RiskContextWritePolicy</c>) and validates every line before writing any.
    /// </summary>
    [HttpPut]
    [PermissionAuthorize(EntityPermissions.RiskContext)]
    [Route("RiskContext")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(EntityRiskContextBatchResultDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<EntityRiskContextBatchResultDto>> SaveRiskContext(
        [FromBody] EntityRiskContextBatchRequest request, CancellationToken ct = default)
    {
        var user = GetUser();

        try
        {
            var result = await _entitiesService.SaveRiskContextAsync(request, user.Value, HoldsRiskContextPermission(), ct);
            Logger.Information("User:{User} applied risk-context batch {Batch} ({Count} entities changed)", user.Value,
                result.BatchId, result.UpdatedEntities);
            return Ok(result);
        }
        catch (EntityRequestRefusedException ex)
        {
            return Refused(ex, user.Value, "a risk-context batch");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error while applying a risk-context batch: {Message}", ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// One entity's change history with each batch's justification (S40 §8.5); never <c>AuditLog</c> nor its user.
    /// "entity_risk_context or entities_manage" is not expressible as one <c>[PermissionAuthorize]</c>, so the service
    /// enforces it — before reading the trail, with the entity's scope (403, or 404 when missing or out of scope).
    /// </summary>
    [HttpGet]
    [Route("{id}/History")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<EntityHistoryEntryDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<EntityHistoryEntryDto>>> GetHistory(int id, [FromQuery] int limit = 200,
        CancellationToken ct = default)
    {
        var user = GetUser();
        var principal = _httpContextAccessor.HttpContext?.User;
        var isAdmin = principal?.HasClaim(c => c.Type == ClaimTypes.Role && c.Value == "Admin") == true;
        var permissions = principal?.Claims.Where(c => c.Type == "Permission").Select(c => c.Value)
            .ToHashSet(StringComparer.Ordinal) ?? [];

        try
        {
            var history = await _entitiesService.GetHistoryAsync(id, limit, isAdmin, permissions, ct);
            Logger.Information("User:{User} read the change history of entity {Id}", user.Value, id);
            return Ok(history);
        }
        catch (EntityRequestRefusedException ex)
        {
            return Refused(ex, user.Value, $"the history of entity {id}");
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error while reading the history of entity {Id}: {Message}", id, ex.Message);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpDelete]
    [PermissionAuthorize(EntityPermissions.Manage)]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(EntitiesConfiguration))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult DeleteOne(int id)
    {
        var user = GetUser();

        try
        {
            // T292: a scoped caller deletes only inside their subtree.
            _entitiesService.AuthorizeEntityWrite(EntityWriteKind.Delete, id, null, HoldsRiskContextPermission());

            Logger.Information("User:{User} delete entity: {Id}", user.Value, id);
            _entitiesService.DeleteEntity(id);

            return Ok();
        }
        catch (EntityWriteRefusedException ex)
        {
            return Refused(ex, user.Value, $"deleting entity {id}");
        }
        catch(DataNotFoundException ex)
        {
            Logger.Warning("Entity not found: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status404NotFound);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while deleting entity {Id} : {Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpGet]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(EntitiesConfiguration))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Entity> GetOne(int id)
    {

        var user = GetUser();

        try
        {
            Logger.Information("User:{User} got entity id:{Id}", user.Value, id);
            var entity = _entitiesService.GetEntity(id);

            return Ok(entity);
        }

        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting entity: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }

   
    [HttpPut]
    [PermissionAuthorize(EntityPermissions.Manage)]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(EntitiesConfiguration))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<Entity> Update(int id, [FromBody] EntityDto entityDto)
    {

        var user = GetUser();

        Entity entity;

        try
        {
            entity = _entitiesService.GetEntity(id);
        }
        catch (DataNotFoundException)
        {
            Logger.Warning("User:{User} tried to update entity id:{Id}, which does not exist", user.Value, id);
            return NotFound();
        }

        // A PUT carries the entity's complete property set, so validate it as one: this rejects a
        // payload that is missing a required property instead of letting the reconcile below read
        // the omission as "the user cleared it".
        try
        {
            // The stored definition, not the one the request claims: the payload does not get to
            // pick which rule set it is validated against.
            _entitiesService.ValidatePropertyList(entity.DefinitionName, entityDto.EntitiesProperties);
        }
        catch (Exception ex)
        {
            Logger.Warning("User:{User} sent an invalid property set for entity id:{Id}: {Message}",
                user.Value, id, ex.Message);
            return BadRequest(ex.Message);
        }

        // T241/T292, before anything is written: the entity and any new parent inside the caller's subtree, no
        // cycle, and a change to a risk-context property only with entity_risk_context and global scope.
        try
        {
            _entitiesService.AuthorizeEntityWrite(EntityWriteKind.Update, id, entityDto, HoldsRiskContextPermission());
        }
        catch (EntityWriteRefusedException ex)
        {
            return Refused(ex, user.Value, $"updating entity {id}");
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }

        try
        {
            Logger.Information("User:{User} updated entity id:{Id}", user.Value, id);

            entity.Updated = DateTime.Now;
            entity.UpdatedBy = user.Value;
            entity.Status = entityDto.Status;
            entity.Parent = entityDto.Parent;

            // The bag is reconciled by type and value, so the ids the client echoes back are not
            // consulted at all — they are stale the moment a multi-valued property is re-saved.
            _entitiesService.ReplaceProperties(entity, entityDto.EntitiesProperties);

            _entitiesService.UpdateEntity(entity);
            
            return Ok(entity);
        }

        catch (Exception ex)
        {
            // Log the exception, not just its message: the message alone ("EntityProperty not
            // found") named neither the property nor the statement that failed.
            Logger.Warning(ex, "Unknown error while updating entity id:{Id} : {Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }
    
    [HttpPost]
    [PermissionAuthorize(EntityPermissions.Manage)]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(EntitiesConfiguration))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public ActionResult<Entity> Create([FromBody] EntityDto entity)
    {

        var user = GetUser();

        try
        {
            
            _entitiesService.ValidatePropertyList(entity.DefinitionName, entity.EntitiesProperties);

            // T241/T292, before the instance is created: the parent inside the caller's subtree, and a
            // risk-context value only with entity_risk_context and global scope.
            _entitiesService.AuthorizeEntityWrite(EntityWriteKind.Create, null, entity, HoldsRiskContextPermission());
            
            Logger.Information("User:{User} created entity of definition {Type}", user.Value, entity.DefinitionName);
            Entity newObj;
            if(entity.Parent != null) newObj = _entitiesService.CreateInstance(user.Value, entity.DefinitionName, entity.Parent.Value);
            else newObj = _entitiesService.CreateInstance(user.Value, entity.DefinitionName);
            
            foreach (var property in entity.EntitiesProperties)
            {
                var propres = _entitiesService.CreateProperty(entity.DefinitionName, ref newObj, property);
            }   

            //newObj.Parent = entity.Parent;
            
            _entitiesService.UpdateEntity(newObj);

            return Ok(newObj);
        }
        catch (EntityWriteRefusedException ex)
        {
            return Refused(ex, user.Value, "creating an entity");
        }
        catch (Exception ex)
        {
            Logger.Warning(ex, "Unknown error while creating entites: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }
    
}
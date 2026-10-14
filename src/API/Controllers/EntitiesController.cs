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

    /// <summary>The refusal as the API answers it: the status the guard chose and <c>{ error, message }</c>.</summary>
    private ObjectResult Refused(EntityWriteRefusedException ex, int userId, string operation)
    {
        Logger.Warning("User:{User} was refused {Operation}: {Error} {Message}", userId, operation, ex.Error, ex.Message);
        return StatusCode(ex.StatusCode, new { error = ex.Error, message = ex.Message });
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
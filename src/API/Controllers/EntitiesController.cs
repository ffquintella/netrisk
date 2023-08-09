using AutoMapper;
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
    private IMapper _mapper;
    
    public EntitiesController(ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IEntitiesService entitiesService,
        IMapper mapper,
        IUsersService usersService) : base(logger, httpContextAccessor, usersService)
    {
        _entitiesService = entitiesService;
        _mapper = mapper;
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

    [HttpDelete]
    [Authorize(Policy = "RequireValidUser")]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(EntitiesConfiguration))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult DeleteOne(int id)
    {
        var user = GetUser();

        try
        {
            Logger.Information("User:{User} delete entity: {Id}", user.Value, id);
            _entitiesService.DeleteEntity(id);

            return Ok();
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
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(EntitiesConfiguration))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Entity> Update(int id, [FromBody] List<EntitiesPropertyDto> properties)
    {

        var user = GetUser();

        try
        {
            Logger.Information("User:{User} updated entity id:{Id}", user.Value, id);
            var entity = _entitiesService.GetEntity(id);
            
            entity.Updated = DateTime.Now;
            entity.UpdatedBy = user.Value;
            
            entity.EntitiesProperties.Clear();

            foreach (var property in properties)
            {
                 var prop = _entitiesService.UpdateProperty( ref entity, property, false);
                 entity.EntitiesProperties.Add(prop);
            }   
            
            _entitiesService.UpdateEntity(entity);
            
            return Ok(entity);
        }

        catch (Exception ex)
        {
            Logger.Warning("Unknown error while updating entity id:{Id} : {Message}", ex.Message, id);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }
    
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(EntitiesConfiguration))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Entity> Create([FromBody] List<EntitiesPropertyDto> properties, [FromQuery] string entityDefinition)
    {

        var user = GetUser();

        try
        {
            _entitiesService.ValidatePropertyList(entityDefinition, properties);
            
            Logger.Information("User:{User} created entity of definition {Type}", user.Value, entityDefinition);
            
            var newObj = _entitiesService.CreateInstance(user.Value, entityDefinition);

            foreach (var property in properties)
            {
                var propres = _entitiesService.CreateProperty(entityDefinition, ref newObj, property);
            }   


            return Ok(newObj);
        }

        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting entites: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }
    
}
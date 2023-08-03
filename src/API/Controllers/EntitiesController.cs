using AutoMapper;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Entities;
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
    public ActionResult<List<Entity>> ListAll()
    {

        var user = GetUser();

        try
        {
            Logger.Information("User:{User} got entities", user.Value);
            var entities = _entitiesService.GetEntities();

            return Ok(entities);
        }

        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting entites: {Message}", ex.Message);
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
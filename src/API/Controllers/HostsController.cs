using API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;
using Host = DAL.Entities.Host;

namespace API.Controllers;


[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class HostsController: ApiBaseController
{
    private IHostsService HostsService { get; }
    public HostsController(ILogger logger, IHttpContextAccessor httpContextAccessor, 
        IUsersService usersService, IHostsService hostsService) 
        : base(logger, httpContextAccessor, usersService)
    {
        HostsService = hostsService;
    }
    
    [PermissionAuthorize("hosts")]
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Host>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Host>> GetAll()
    {

        var user = GetUser();

        try
        {
            Logger.Information("User:{User} listed all hosts", user.Value);
            var hosts = HostsService.GetAll();

            return Ok(hosts);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while listing hosts: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Host))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Host> GetOne(int id)
    {

        var user = GetUser();

        try
        {
            Logger.Information("User:{User} got host: {Id}", user.Value, id);
            var host = HostsService.GetById(id);

            return Ok(host);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Host not found Id{Id} message: {Message}", id, ex.Message);
            return NotFound();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting host:{Id} message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpDelete]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult DeleteOne(int id)
    {
        var user = GetUser();
        try
        {
            HostsService.Delete(id);
            Logger.Information("User:{User} deleted a host: {Id}", user.Value, id);
            return Ok();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Host not found Id{Id} message: {Message}", id, ex.Message);
            return NotFound();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while deleting a host:{Id} message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Host))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Host> Create([FromBody] Host newHost)
    {

        var user = GetUser();

        try
        {
            newHost.Id = 0;
            var host = HostsService.Create(newHost);

            Logger.Information("User:{User} created a new host: {Id}", user.Value, host.Id);
            
            return Created($"/Hosts/{host.Id}",host);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while creating a new host message:{Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPut]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Host))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Host> Update(int id, [FromBody] Host host)
    {

        if(host == null) throw new ArgumentNullException(nameof(host));
        if (host.Id != id) throw new ArgumentException("Id mismatch");
        
        var user = GetUser();

        try
        {
            
            HostsService.Update(host);

            Logger.Information("User:{User} updated a new host: {Id}", user.Value, host.Id);
            
            return Ok();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while updating a host message:{Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
}
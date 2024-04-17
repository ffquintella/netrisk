using API.Security;
using AutoMapper;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Model.DTO;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;
using Host = DAL.Entities.Host;
using Sieve.Exceptions;
using Sieve.Models;

namespace API.Controllers;


[PermissionAuthorize("hosts")]
[ApiController]
[Route("[controller]")]
public class HostsController: ApiBaseController
{
    private IMapper Mapper { get; }
    private IHostsService HostsService { get; }
    public HostsController(ILogger logger, IHttpContextAccessor httpContextAccessor, 
        IUsersService usersService, IHostsService hostsService, IMapper mapper) 
        : base(logger, httpContextAccessor, usersService)
    {
        HostsService = hostsService;
        Mapper = mapper;
    }
    
    
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
    [Route("Filtered")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Vulnerability>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<Vulnerability>>> GetFiltered([FromQuery] SieveModel sieveModel, [FromQuery] string culture = "en-US")
    {

        SetLocalization(culture);
        var user = GetUser();

        try
        {
            var data = await HostsService.GetFiltredAsync(sieveModel);
            Response.Headers.Append("X-Total-Count", data.Item2.ToString());

            Logger.Information("User:{User} listed hosts with filters", user.Value);
            return Ok(data.Item1);
        }
        catch (SieveMethodNotFoundException ex)
        {
            Logger.Warning("Invalid filter: {Message}", ex.Message);
            return this.StatusCode(409, ex.Message);
        }
        catch (SieveException ex)
        {
            Logger.Warning("Filter error while listing hosts with filters: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status400BadRequest, ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Error("Unknown error while listing hosts with filters: {Message}", ex.Message);
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
    
    [HttpGet]
    [Route("Find")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Host))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Host> GetByIp([FromQuery] string? ip)
    {

        var user = GetUser();

        if(ip == null) return BadRequest("A parameter must be provided");
        
        try
        {

            Host host = new Host();
            if(ip != null) host = HostsService.GetByIp(ip);
            
            Logger.Information("User:{User} got host: {Id}", user.Value, host.Id);

            return Ok(host);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Host not found Ip:{Id} message: {Message}", ip, ex.Message);
            return NotFound();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting host:{Ip} message:{Message}", ip, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [PermissionAuthorize("hosts_delete")]
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
    [PermissionAuthorize("hosts_create")]
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
    [PermissionAuthorize("hosts_create")]
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
    
    [HttpGet]
    [Route("{id}/Services")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<DAL.Entities.HostsService>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<DAL.Entities.HostsService>> GetServices(int id)
    {

        var user = GetUser();

        try
        {
            var services = HostsService.GetHostServices(id);
            Logger.Information("User:{User} got host: {Id} services", user.Value, id);

            return Ok(services);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Host not found Id{Id} message: {Message}", id, ex.Message);
            return NotFound();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting host:{Id} services message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}/Vulnerabilities")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<DAL.Entities.HostsService>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Vulnerability>> GetVulnerabilities(int id)
    {

        var user = GetUser();

        try
        {
            var vulnerabilities = HostsService.GetVulnerabilities(id);
            Logger.Information("User:{User} got host: {Id} vulnerabilities", user.Value, id);

            return Ok(vulnerabilities);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Host not found Id{Id} message: {Message}", id, ex.Message);
            return NotFound();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting host:{Id} vulnerabilities message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}/Services/Exists")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(bool))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<bool> HostHasServices(int id, [FromQuery]string name,[FromQuery]string protocol,[FromQuery]int? port = null )
    {

        try
        {
            var hasService = HostsService.HostHasService(id, name, port, protocol);

            return Ok(hasService);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Host not found Id{Id} message: {Message}", id, ex.Message);
            return NotFound();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while verifyng if host:{Id} has a service message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}/Services/Find")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(HostsService))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<HostsService> FindService(int id, [FromQuery]string name,[FromQuery]string protocol,[FromQuery]int? port = null )
    {

        try
        {
            var service = HostsService.FindService(id, s => s.Name == name && s.Port == port && s.Protocol == protocol);

            return Ok(service);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Host not found Id{Id} message: {Message}", id, ex.Message);
            return NotFound();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while verifying if host:{Id} has a service message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}/Services/{serviceId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(HostsService))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<HostsService> GetService(int id, int serviceId)
    {

        var user = GetUser();

        try
        {
            var service = HostsService.GetHostService(id,serviceId);
            Logger.Information("User:{User} got host: {Id} services", user.Value, id);

            return Ok(service);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Host not found Id{Id} message: {Message}", id, ex.Message);
            return NotFound();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting host:{Id} services message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    
    [HttpPost]
    [PermissionAuthorize("hosts_create")]
    [Route("{id}/Services")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(HostsService))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<HostsService> CreateService(int id, HostsServiceDto service)
    {

        var user = GetUser();

        try
        {
            var hservice = new HostsService();
            
            Mapper.Map(service, hservice);
            
            hservice.HostId = id;
            
            if(HostsService.HostHasService(id, service.Name, service.Port, service.Protocol))
                return BadRequest("Service already exists");
            var newService = HostsService.CreateAndAddService(id, hservice);
            Logger.Information("User:{User} created host: {Id} service", user.Value, id);

            return Created($"{id}/Services/{newService.Id}",newService);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Host not found Id{Id} message: {Message}", id, ex.Message);
            return NotFound();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while creating host:{Id} service message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpDelete]
    [PermissionAuthorize("hosts_delete")]
    [Route("{id}/Services/{serviceId}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult DeleteService(int id, int serviceId)
    {

        var user = GetUser();

        try
        {
            HostsService.DeleteService(id, serviceId);
            Logger.Information("User:{User} deleted host: {Id} service", user.Value, id);

            return Ok();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Host not found Id{Id} message: {Message}", id, ex.Message);
            return NotFound();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while deleting host:{Id} service message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    [HttpPut]
    [PermissionAuthorize("hosts_create")]
    [Route("{id}/Services/{serviceId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Host))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult UpdateService(int id,int serviceId, HostsServiceDto service)
    {
            
            var user = GetUser();
    
            try
            {
                var hservice = new HostsService();
                Mapper.Map(service, hservice);
                hservice.HostId = id;
                hservice.Id = serviceId;
                
                HostsService.UpdateService(id, hservice);
                Logger.Information("User:{User} updated host: {Id} service", user.Value, id);
    
                return Ok();
            }
            catch (DataNotFoundException ex)
            {
                Logger.Warning("Host not found Id:{Id} message: {Message}", id, ex.Message);
                return NotFound();
            }
            
            catch (Exception ex)
            {
                Logger.Warning("Unknown error while updating host:{Id} service message:{Message}", id, ex.Message);
                return this.StatusCode(StatusCodes.Status500InternalServerError);
            }
    }
}
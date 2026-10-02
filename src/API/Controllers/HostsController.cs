using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Mvc;
using Model.DTO;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;
using Host = DAL.Entities.Host;
using Mapster;

using Gridify;
using ServerServices.Filtering;

namespace API.Controllers;


[PermissionAuthorize("hosts")]
[ApiController]
[Route("[controller]")]
public class HostsController: ApiBaseController
{
    private IHostsService HostsService { get; }
    private IAuditTrailService AuditTrail { get; }

    /// <summary>The largest <c>limit</c> <see cref="GetHistory"/> accepts.</summary>
    public const int MaxHistoryLimit = 5000;

    public HostsController(ILogger logger, IHttpContextAccessor httpContextAccessor,
        IUsersService usersService, IHostsService hostsService, IAuditTrailService auditTrail)
        : base(logger, httpContextAccessor, usersService)
    {
        HostsService = hostsService;
        AuditTrail = auditTrail;
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
    public async Task<ActionResult<List<Vulnerability>>> GetFiltered([FromQuery] ListQuery listQuery, [FromQuery] string culture = "en-US")
    {

        SetLocalization(culture);
        var user = GetUser();

        try
        {
            var data = await HostsService.GetFiltredAsync(listQuery);
            Response.Headers.Append("X-Total-Count", data.Item2.ToString());

            Logger.Information("User:{User} listed hosts with filters", user.Value);
            return Ok(data.Item1);
        }
        catch (GridifyMapperException ex)
        {
            Logger.Warning("Invalid filter: {Message}", ex.Message);
            return this.StatusCode(409, ex.Message);
        }
        catch (Exception ex) when (ex is GridifyFilteringException
                                   or GridifyOrderingException
                                   or GridifyQueryException)
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
    
    /// <summary>
    /// The distinct, non-blank environments of the hosts the caller can see, ordered — the Hosts
    /// view's environment facet (S38 §5.2).
    /// </summary>
    [HttpGet]
    [Route("Environments")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<string>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<string>>> GetEnvironments()
    {
        var user = GetUser();

        try
        {
            var environments = await HostsService.GetEnvironmentsAsync();
            Logger.Information("User:{User} listed host environments", user.Value);
            return Ok(environments);
        }
        catch (Exception ex)
        {
            Logger.Error("Unknown error while listing host environments: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Open-vulnerability counts by severity for several hosts (S38 §5.3), one grouped query.
    /// <c>ids</c> is pipe-separated (<c>1|2|3</c>), at most
    /// <see cref="IHostsService.MaxSummaryBatchSize"/> distinct ids. Hosts that do not exist or are
    /// outside the caller's entity scope are left out of the answer.
    /// </summary>
    [HttpGet]
    [Route("VulnerabilitySummary")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<HostVulnerabilitySummaryDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<HostVulnerabilitySummaryDto>>> GetVulnerabilitySummaries(
        [FromQuery] string? ids)
    {
        var user = GetUser();

        if (!TryParseIds(ids, out var hostIds, out var error)) return BadRequest(error);

        try
        {
            var summaries = await HostsService.GetVulnerabilitySummariesAsync(hostIds);
            Logger.Information("User:{User} read the vulnerability summary of {Count} host(s)",
                user.Value, hostIds.Count);
            return Ok(summaries);
        }
        catch (Exception ex)
        {
            Logger.Error("Unknown error while summarizing host vulnerabilities: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Open-vulnerability counts by severity for one host (S38 §5.3).</summary>
    [HttpGet]
    [Route("{id}/VulnerabilitySummary")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(HostVulnerabilitySummaryDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<HostVulnerabilitySummaryDto>> GetVulnerabilitySummary(int id)
    {
        var user = GetUser();

        try
        {
            var summary = await HostsService.GetVulnerabilitySummaryAsync(id);
            Logger.Information("User:{User} read the vulnerability summary of host {Id}", user.Value, id);
            return Ok(summary);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Host not found Id{Id} message: {Message}", id, ex.Message);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error("Unknown error while summarizing host:{Id} vulnerabilities: {Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// The field-level change history of one host, newest first (S38 §5.4) — the
    /// <c>audit_logs</c> rows <c>GovernanceAuditInterceptor</c> writes for <see cref="Host"/>.
    ///
    /// The host is looked up first, through the entity-scoped hosts set: <c>audit_logs</c> carries
    /// no entity id of its own, so that lookup is the only thing that stops a scoped caller reading
    /// another entity's host values out of the trail. A host that is gone or out of scope is a 404.
    /// The rows go out without their <see cref="AuditLog.User"/> navigation — the actor name and user
    /// id identify who acted, and a whole user record is not this endpoint's to disclose.
    /// </summary>
    [HttpGet]
    [Route("{id}/History")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AuditLog>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<AuditLog>>> GetHistory(int id, [FromQuery] int limit = 500)
    {
        var user = GetUser();

        if (limit is < 1 or > MaxHistoryLimit)
            return BadRequest($"limit must be between 1 and {MaxHistoryLimit}.");

        try
        {
            HostsService.GetById(id);

            var rows = await AuditTrail.GetForRecordAsync(nameof(Host), id, limit);
            Logger.Information("User:{User} read the change history of host {Id}", user.Value, id);

            return Ok(rows.Select(WithoutUser).ToList());
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Host not found Id{Id} message: {Message}", id, ex.Message);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error("Unknown error while reading host:{Id} history: {Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    private static AuditLog WithoutUser(AuditLog row) => new()
    {
        Id = row.Id,
        EntityType = row.EntityType,
        EntityId = row.EntityId,
        Field = row.Field,
        OldValue = row.OldValue,
        NewValue = row.NewValue,
        Action = row.Action,
        UserId = row.UserId,
        Actor = row.Actor,
        OccurredAt = row.OccurredAt,
        CorrelationId = row.CorrelationId
    };

    /// <summary>
    /// Parses <c>1|2|3</c>. Every token must be a positive integer: a typo is reported rather than
    /// skipped, because a summary that silently drops a host reads as a host with no findings.
    /// </summary>
    private static bool TryParseIds(string? raw, out List<int> ids, out string error)
    {
        ids = [];
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(raw))
        {
            error = "ids is required, pipe-separated (ids=1|2|3).";
            return false;
        }

        var seen = new HashSet<int>();

        foreach (var token in raw.Split('|'))
        {
            if (!int.TryParse(token.Trim(), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var id) || id < 1)
            {
                error = $"'{token.Trim()}' is not a host id.";
                return false;
            }

            if (seen.Add(id)) ids.Add(id);
        }

        if (ids.Count > IHostsService.MaxSummaryBatchSize)
        {
            error = $"At most {IHostsService.MaxSummaryBatchSize} host ids per request.";
            return false;
        }

        return true;
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
            
            service.Adapt(hservice);
            
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
                service.Adapt(hservice);
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
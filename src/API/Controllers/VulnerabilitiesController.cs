using System.Net;
using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices.Interfaces;
using ServerServices.Services;
using Sieve.Exceptions;
using Sieve.Models;
using Host = Microsoft.Extensions.Hosting.Host;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[PermissionAuthorize("vulnerabilities")]
[ApiController]
[Route("[controller]")]
public class VulnerabilitiesController: ApiBaseController
{
    IVulnerabilitiesService VulnerabilitiesService { get; }
    IRisksService RisksService { get; }
    
    public VulnerabilitiesController(ILogger logger, IHttpContextAccessor httpContextAccessor,
        IUsersService usersService,
        IRisksService risksService,
        IVulnerabilitiesService vulnerabilitiesService) 
        : base(logger, httpContextAccessor, usersService)
    {
        VulnerabilitiesService = vulnerabilitiesService;
        RisksService = risksService;
    }
    
    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Vulnerability>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Vulnerability>> GetAll()
    {

        var user = GetUser();

        try
        {
            var vulnerabilities = VulnerabilitiesService.GetAll();
            Logger.Information("User:{User} listed all vulnerabilities", user.Value);
            return Ok(vulnerabilities);
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while listing vulnerabilities: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("Filtered")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Vulnerability>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<Vulnerability>> GetFiltered([FromQuery] SieveModel sieveModel)
    {

        var user = GetUser();

        try
        {
            var vulnerabilities = VulnerabilitiesService.GetFiltred(sieveModel, out var totalItems);
            Response.Headers.Add("X-Total-Count", totalItems.ToString());

            Logger.Information("User:{User} listed vulnerabilities with filters", user.Value);
            return Ok(vulnerabilities);
        }
        catch (SieveMethodNotFoundException ex)
        {
            Logger.Warning("Invalid filter: {Message}", ex.Message);
            return this.StatusCode(409, ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while listing vulnerabilities with filters: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    
    
    [HttpGet]
    [Route("Find")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Vulnerability>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Vulnerability> FindVulnerability(string hash)
    {

        var user = GetUser();

        try
        {
            var vulnerabilities = VulnerabilitiesService.Find(hash);
            Logger.Information("User:{User} searched vulnerabilities", user.Value);
            return Ok(vulnerabilities);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Vulnerability not found hash:{Hash} Message:{Message}", hash, ex.Message);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while searching vulnerabilities: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vulnerability))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Vulnerability> GetOne(int id, [FromQuery] bool includeDetails = false)
    {

        var user = GetUser();

        try
        {
            Logger.Information("User:{User} got Vulnerability: {Id}", user.Value, id);
            var vulnerability = VulnerabilitiesService.GetById(id, includeDetails);

            return Ok(vulnerability);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Vulnerability not found Id:{Id} Message:{Message}", id, ex.Message);
            return NotFound();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting Vulnerability:{Id} message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    [PermissionAuthorize("vulnerabilities_delete")]
    [HttpDelete]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult DeleteOne(int id)
    {
        var user = GetUser();
        try
        {
            VulnerabilitiesService.Delete(id);
            Logger.Information("User:{User} deleted a vulnerability: {Id}", user.Value, id);
            return Ok();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("vulnerability not found Id:{Id} message:{Message}", id, ex.Message);
            return NotFound();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while deleting a vulnerability:{Id} message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    [PermissionAuthorize("vulnerabilities_create")]
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vulnerability))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Vulnerability> Create([FromBody] Vulnerability newVulnerability)
    {
        var user = GetUser();
        try
        {
            newVulnerability.Id = 0;
            var vulnerability = VulnerabilitiesService.Create(newVulnerability);

            Logger.Information("User:{User} created a new vulnerability: {Id}", user.Value, vulnerability.Id);
            
            return Created($"/Vulnerabilities/{vulnerability.Id}",vulnerability);
        }
        
        catch (Exception ex)
        {
            if(ex.InnerException != null)
                Logger.Error("Unknown error while creating a new vulnerability message:{Message} ieMessage:{IEMessage}", 
                    ex.Message, ex.InnerException.Message);
            else Logger.Error("Unknown error while creating a new vulnerability message:{Message} ", ex.Message );
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    [PermissionAuthorize("vulnerabilities_create")]
    [HttpPut]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vulnerability))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Vulnerability> Update(int id, [FromBody] Vulnerability vulnerability)
    {

        if(vulnerability == null) throw new ArgumentNullException(nameof(vulnerability));
        if (vulnerability.Id != id) throw new ArgumentException("Id mismatch");

        vulnerability.FixTeam = null;
        vulnerability.Host = null;
        
        var user = GetUser();

        try
        {
            
            
            VulnerabilitiesService.Update(vulnerability);

            Logger.Information("User:{User} updated a new vulnerability: {Id}", user.Value, vulnerability.Id);
            
            return Ok();
        }
        
        catch (Exception ex)
        {
            if (ex.InnerException != null)
                Logger.Warning("Unknown error while updating a vulnerability message:{Message} inner:{InnerMessage}", 
                    ex.Message,
                    ex.InnerException.Message);
            else Logger.Warning("Unknown error while updating a vulnerability message:{Message} ", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}/RisksScores")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vulnerability))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<RiskScoring> GetRisksScoring(int id)
    {
        var user = GetUser();
        try
        {
           
            var vulnerability = VulnerabilitiesService.GetById(id, true);
            
            var ids = vulnerability.Risks.Select(r => r.Id).ToList();
            var scores = RisksService.GetRisksScoring(ids);

            Logger.Information("User:{User} got Vulnerability risks scorings id: {Id}", user.Value, id);
            return Ok(scores);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Vulnerability not found Id:{Id} Message:{Message}", id, ex.Message);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting Vulnerability risks scorings id:{Id} message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}/Status")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vulnerability))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<RiskScoring> GetStatus(int id)
    {
        var user = GetUser();
        try
        {
           var vulnerability = VulnerabilitiesService.GetById(id, true);

           var status = vulnerability.Status;

            Logger.Information("User:{User} got Vulnerability status id: {Id}", user.Value, id);
            return Ok(status);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Vulnerability not found Id:{Id} Message:{Message}", id, ex.Message);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting Vulnerability status id:{Id} message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpGet]
    [Route("{id}/Actions")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vulnerability))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<ICollection<NrAction>> GetActions(int id)
    {
        var user = GetUser();
        try
        {
            var vulnerability = VulnerabilitiesService.GetById(id, true);

            Logger.Information("User:{User} got Vulnerability actions id: {Id}", user.Value, id);
            return Ok(vulnerability.Actions);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Vulnerability not found Id:{Id} Message:{Message}", id, ex.Message);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting Vulnerability actions id:{Id} message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    [HttpPost]
    [Route("{id}/Actions")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vulnerability))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<NrAction> AddAction(int id, [FromBody] NrAction action)
    {
        var user = GetUser();
        try
        {
            var resultingAction = VulnerabilitiesService.AddAction(id, user.Value, action);

            Logger.Information("User:{User} added Vulnerability action id: {Id}", user.Value, id);
            return Created("/Vulnerabilities/{id}/Actions", resultingAction);
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Vulnerability not found Id:{Id} Message:{Message}", id, ex.Message);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while adding Vulnerability action id:{Id} message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    
    [HttpPut]
    [Route("{id}/Status")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vulnerability))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<RiskScoring> UpdateStatus(int id, [FromBody] ushort status)
    {
        var user = GetUser();
        try
        {
            VulnerabilitiesService.UpdateStatus(id, status);

            Logger.Information("User:{User} updated Vulnerability status id: {Id}", user.Value, id);
            return Ok();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Vulnerability not found Id:{Id} Message:{Message}", id, ex.Message);
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while updating Vulnerability status id:{Id} message:{Message}", id, ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    
    [PermissionAuthorize("vulnerabilities_create")]
    [HttpPost]
    [Route("{id}/RisksAssociate")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vulnerability))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult AssociateRisks(int id, [FromBody] List<int> riskIds)
    {
        var user = GetUser();
        try
        {
            VulnerabilitiesService.AssociateRisks(id, riskIds);
            Logger.Information("User:{User} associated risks to vulnerability id: {Id}", user.Value, id);
            return Ok();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Vulnerability not found Id:{Id} Message:{Message}", id, ex.Message);
            return NotFound();
        }
        catch (Exception ex)
        {
            if (ex.InnerException != null)
                Logger.Warning(
                    "Unknown error while associating risks to vulnerability id:{Id} message:{Message} ieMessage:{IEMessage}",
                    id, ex.Message, ex.InnerException.Message);
            else
                Logger.Warning("Unknown error while associating risks to vulnerability id:{Id} message:{Message}", id,
                    ex.Message);
            
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
    
    
}
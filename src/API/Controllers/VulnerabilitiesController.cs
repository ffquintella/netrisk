﻿using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices.Interfaces;
using Host = Microsoft.Extensions.Hosting.Host;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[PermissionAuthorize("vulnerabilities")]
[ApiController]
[Route("[controller]")]
public class VulnerabilitiesController: ApiBaseController
{
    IVulnerabilitiesService VulnerabilitiesService { get; }
    
    public VulnerabilitiesController(ILogger logger, IHttpContextAccessor httpContextAccessor,
        IUsersService usersService,
        IVulnerabilitiesService vulnerabilitiesService) 
        : base(logger, httpContextAccessor, usersService)
    {
        VulnerabilitiesService = vulnerabilitiesService;
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
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vulnerability))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<Vulnerability> GetOne(int id)
    {

        var user = GetUser();

        try
        {
            Logger.Information("User:{User} got Vulnerability: {Id}", user.Value, id);
            var vulnerability = VulnerabilitiesService.GetById(id);

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
            Logger.Warning("Unknown error while creating a new vulnerability message:{Message}", ex.Message);
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
        
        var user = GetUser();

        try
        {
            
            VulnerabilitiesService.Update(vulnerability);

            Logger.Information("User:{User} updated a new vulnerability: {Id}", user.Value, vulnerability.Id);
            
            return Ok();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while updating a vulnerability message:{Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}
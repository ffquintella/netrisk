using System.Collections.Generic;
using AutoMapper;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model;
using ServerServices;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireAdminOnly")]
[ApiController]
[Route("[controller]")]
public class ClientsController : ControllerBase
{

    private IClientRegistrationService _clientRegistrationService;
    private ILogger _logger;
    private IMapper _mapper;
    
    public ClientsController(
        IClientRegistrationService clientRegistrationService, 
        ILogger logger,
        IMapper mapper)
    {
        _clientRegistrationService = clientRegistrationService;
        _logger = logger;
        _mapper = mapper;
    }

    [HttpGet]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Client>))]
    public ActionResult<List<Client>> GetAll()
    {
        var clientsRegs = _clientRegistrationService.GetAll();
        var clients = _mapper.Map<List<Client>>(clientsRegs);
        
        
        return clients;
    }

    [HttpDelete]
    [Route("{id}")]
    [ProducesResponseType(StatusCodes.Status202Accepted, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(string))]
    public ActionResult<string> Delete(int id)
    {
        var result = _clientRegistrationService.DeleteById(id);
        if (result == 0) return Ok("Deleted OK");
        if (result == 1) return NotFound("Client not found");
        if (result == -1) return StatusCode(500, "Internal error");
        return StatusCode(500, "Internal error");
    }
    
    [HttpGet]
    [Route("{id}/approve")]
    [ProducesResponseType(StatusCodes.Status202Accepted, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(string))]
    public ActionResult<string> Approve(int id)
    {
        var result = _clientRegistrationService.Approve(id);
        if (result == 0) return Ok("Approved OK");
        if (result == 1) return NotFound("Client not found");
        if(result == 2) return StatusCode(403, "Already approved");
        if (result == -1) return StatusCode(500, "Internal error");
        return StatusCode(500, "Internal error");
    }
    
    [HttpGet]
    [Route("{id}/reject")]
    [ProducesResponseType(StatusCodes.Status202Accepted, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status403Forbidden, Type = typeof(string))]
    public ActionResult<string> Reject(int id)
    {
        var result = _clientRegistrationService.Reject(id);
        if (result == 0) return Ok("Rejected OK");
        if (result == 1) return NotFound("Client not found");
        if(result == 2) return StatusCode(403, "Already rejected");
        if (result == -1) return StatusCode(500, "Internal error");
        return StatusCode(500, "Internal error");
    }
}
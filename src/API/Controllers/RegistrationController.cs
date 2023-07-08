using System;
using DAL;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Registration;
using Serilog;
using ServerServices;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;


[ApiController]
[Route("[controller]")]
public class RegistrationController : ControllerBase
{
    private IClientRegistrationService _clientRegistrationService;
    private ILogger _logger;
    
    public RegistrationController(IClientRegistrationService clientRegistrationService, ILogger logger)
    {
        _clientRegistrationService = clientRegistrationService;
        _logger = logger;
    }

    
    [AllowAnonymous]
    [HttpGet]
    [Route("IsAccepted")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(bool))]
    [ProducesResponseType(StatusCodes.Status404NotFound, Type = typeof(bool))]
    public ActionResult<bool> IsAccepted([FromHeader] string clientId)
    {
        try
        {
            var result = _clientRegistrationService.IsAccepted(clientId);
            if (result == 1) return Ok(true);
            if (result == 0)
            {
                _logger.Information("Registration verified for {ClientId}", clientId);
                return Ok(false);
            }
            if (result == -1) return NotFound(false);
        }
        catch (Exception ex)
        {
            StatusCode(StatusCodes.Status503ServiceUnavailable, "Internal Error msg: " + ex.Message);
        }

        return NotFound(false);
    }
    
    
    
    
    [AllowAnonymous]
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed, Type = typeof(string))]
    [ProducesResponseType(StatusCodes.Status400BadRequest, Type = typeof(string))]
    public ActionResult<string> Register([FromBody] RegistrationRequest request)
    {
        if(request.Id == null) return BadRequest("Request Id is null");
        
        string hashCode = String.Format("{0:X}", request.Id!.GetHashCode());

        var newRequest = new AddonsClientRegistration
        {
            Hostname = request.Hostname,
            ExternalId = request.Id,
            LastVerificationDate = DateTime.Now,
            LoggedAccount = request.LoggedAccount,
            Name = hashCode,
            RegistrationDate = DateTime.Now,
            Status = "requested"
        };
        

        try
        {
            var result = _clientRegistrationService.Add(newRequest);
            if(result == 0) return Ok(hashCode);
            if(result == 1) return StatusCode(StatusCodes.Status412PreconditionFailed, "Already exists");
        }
        catch (Exception ex)
        {
            StatusCode(StatusCodes.Status503ServiceUnavailable, "Internal Error msg: " + ex.Message);
        }
        Log.Error("Unknown error detected on request. This point should not be reached - rc.01");
        return StatusCode(StatusCodes.Status500InternalServerError, "Unknown error");
        
        
    }
    
  
    
    
}
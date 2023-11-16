using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.DTO;
using ServerServices.Interfaces;

namespace API.Controllers;

[Authorize(Policy = "RequireAdminOnly")]
[ApiController]
[Route("[controller]")]
public class ConfigurationsController : ControllerBase
{
    private IConfigurationsService _configurationsService;
    public ConfigurationsController(IConfigurationsService configurationsService)
    {
        _configurationsService = configurationsService;
    }
    
    [HttpGet]
    [Route("BackupPassword")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    public ActionResult<string> VerifyBackupPassword()
    {
        var pwd = _configurationsService.GetBackupPassword();
        if (string.IsNullOrEmpty(pwd)) return NotFound("Backup password not found");
        return Ok("Backup password already set");
    }
    
    [HttpPut]
    [Route("BackupPassword")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    public ActionResult<string> UpdateBackupPassword([FromBody] PasswordDto password)
    {
        _configurationsService.UpdateBackupPassword(password.Password);
        return Ok("Backup password updated");
    }
}
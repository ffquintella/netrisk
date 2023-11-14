using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
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
    
    [HttpPut]
    [Route("BackupPassword")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(string))]
    public ActionResult<string> UpdateBackupPassword([FromBody] string password)
    {
        _configurationsService.UpdateBackupPassword(password);
        return Ok("Backup password updated");
    }
}
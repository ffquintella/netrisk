using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;


[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class FaceIDController: ControllerBase
{
    [HttpGet]
    [Route("info")]
    public ActionResult<string> GetInfo()
    {
        
        return Ok("FaceID controller version 1.0");

    }
}
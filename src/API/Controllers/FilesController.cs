using ILogger = Serilog.ILogger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.Exceptions;
using ServerServices;
using ServerServices.Interfaces;
using File = DAL.Entities.File;


namespace API.Controllers;


[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class FilesController: ApiBaseController
{

    private IFilesService _filesService;
    public FilesController(ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IFilesService filesService,
        IUsersService usersService) : base(logger, httpContextAccessor, usersService)
    {
        _filesService = filesService;
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireAdminOnly")]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<File>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<File>> GetAll([FromQuery] string? status = null)
    {

        var user = GetUser();
        if(!user.Admin) return Unauthorized("Only admins can list all files");
        
        var files = new List<File>();

        try
        {
            Logger.Information("User:{User} listed all files", user.Value);
            files = _filesService.GetAll();

            return Ok(files);
        }
        catch (UserNotAuthorizedException ex)
        {
            Logger.Warning("The user {UserName} is not authorized to see files message: {Message}", user.Name, ex.Message);
            return this.Unauthorized();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while listing files: {Message}", ex.Message);
            return this.Unauthorized();
        }
        
        
    }
    
}
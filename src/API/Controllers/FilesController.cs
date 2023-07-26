using ILogger = Serilog.ILogger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.DTO;
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
    public ActionResult<List<FileListing>> GetAll()
    {

        var user = GetUser();
        if(!user.Admin) return Unauthorized("Only admins can list all files");
        
        //var files = new List<FileListing>();

        try
        {
            Logger.Information("User:{User} listed all files", user.Value);
            var files = _filesService.GetAll();

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
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }

    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<File>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<File> CreateFile([FromBody] File file)
    {

        var user = GetUser();
        //if(!user.Admin) return Unauthorized("Only admins can list all files");

        try
        {
            
            var newFile = _filesService.Create(file, user);
            Logger.Information("User:{User} created a new file", user.Value);
            
            return Created("Files/" + newFile.UniqueName, newFile);
        }
        catch (UserNotAuthorizedException ex)
        {
            Logger.Warning("The user {UserName} is not authorized to create files message: {Message}", user.Name, ex.Message);
            return this.Unauthorized();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while creating files: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }
    
    [HttpPut]
    [Route("{name}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<File>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<File> SaveFile(string name, [FromBody] File file)
    {

        var user = GetUser();
        if(!user.Admin && file.User != user.Value) return Unauthorized("Only admins and owners can update files");

        try
        {
            
            _filesService.Save(file);
            Logger.Information("User:{User} updated file:{FileId}", user.Value, file.Id);
            
            return Ok();
        }
        catch (UserNotAuthorizedException ex)
        {
            Logger.Warning("The user {UserName} is not authorized to update this file: {FileId} message: {Message}", 
                user.Name, file.Id, ex.Message);
            return this.Unauthorized();
        }
        catch (InvalidOperationException ex)
        {
            Logger.Warning("The user {UserName} did an invalid operation while updating this file: {FileId} message: {Message}", 
                user.Name, file.Id, ex.Message);
            return this.BadRequest();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while saving files: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }
    
    
    [HttpDelete]
    [Route("{name}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<File>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult DeleteFile(string name)
    {
        var user = GetUser();
        try
        {
            var file = _filesService.GetByUniqueName(name);
            
            if(!user.Admin && file.User != user.Value) return Unauthorized("Only admins and owners can delete files");
            
            _filesService.DeleteByUniqueName(name);
            Logger.Information("User:{User} deleted file:{FileId}", user.Value, file.Id);
            
            return Ok();
        }
        catch (UserNotAuthorizedException ex)
        {
            Logger.Warning("The user {UserName} is not authorized to delete this file with uniqueName: {FileUniqueName} message: {Message}", 
                user.Name, name, ex.Message);
            return this.Unauthorized();
        }
        catch (InvalidOperationException ex)
        {
            Logger.Warning("The user {UserName} did an invalid operation while updating this with uniqueName: {FileUniqueName} message: {Message}", 
                user.Name, name, ex.Message);
            return this.BadRequest();
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while saving files: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }
    
    [HttpGet]
    [Route("{name}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<File>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<File> GetByUniqueName(string name)
    {
        var user = GetUser();

        try
        {
            Logger.Information("User:{User} downloaded file:{FileUniqueName}", user.Value, name);
            
            var file = _filesService.GetByUniqueName(name);

            return Ok(file);
        }
        catch (UserNotAuthorizedException ex)
        {
            Logger.Warning("The user {UserName} is not authorized to see that file message: {Message}", user.Name, ex.Message);
            return this.Unauthorized();
        }
        
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The file {FileUniqueName} could not be found message: {Message}", name, ex.Message);
            return this.Unauthorized();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting file: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

}
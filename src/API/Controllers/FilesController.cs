﻿using ILogger = Serilog.ILogger;
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
            return this.Unauthorized();
        }
        
        
    }

    [HttpGet]
    [Authorize(Policy = "RequireAdminOnly")]
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
            return this.Unauthorized();
        }
    }

}
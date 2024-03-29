﻿using DAL.Entities;
using ILogger = Serilog.ILogger;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Model.DTO;
using Model.Exceptions;
using Model.File;
using ServerServices;
using ServerServices.Interfaces;



namespace API.Controllers;


[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class FilesController: ApiBaseController
{

    private IFilesService _filesService;
    private readonly IWebHostEnvironment _env;
    public FilesController(ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IFilesService filesService,
        IUsersService usersService,
        IWebHostEnvironment env) : base(logger, httpContextAccessor, usersService)
    {
        _filesService = filesService;
        _env = env;
    }
    
    [HttpGet]
    [Authorize(Policy = "RequireAdminOnly")]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NrFile>))]
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

    [HttpGet]
    [Route("Types")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NrFile>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<List<FileType>> GetFileTypes()
    {

        var user = GetUser();

        try
        {
            Logger.Information("User:{User} listed file types", user.Value);
            var types = _filesService.GetFileTypes();

            return Ok(types);
        }

        catch (Exception ex)
        {
            Logger.Warning("Unknown error while listing files types: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
        
        
    }
    
    [HttpPost]
    [Route("")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NrFile>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<FileListing> CreateFile([FromBody] NrFile file)
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

    [HttpGet]
    [Route("local/id")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NrFile>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<string> GetUniqueFileId()
    {
        //var user = GetUser();
        return Ok(Guid.NewGuid().ToString());
    }

    [HttpPost]
    [Route("local/chunk")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NrFile>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<FileListing> CreateLocalFileChunk([FromBody] FileChunk chunk)
    {

        var user = GetUser();
        
        try
        {

            var totalChunks = chunk.TotalChunks;
            var fileId = chunk.FileId;
            
            _filesService.SaveChunk(chunk);

            if (_filesService.CountChunks(chunk.FileId) == totalChunks)
            {
                // Combine all chunks to create the final file
                _filesService.CombineChunks( fileId, totalChunks);
                _filesService.DeleteChunks(chunk.FileId, totalChunks);
            }

            return Ok("Chunk uploaded successfully.");
        }
        catch (Exception ex)
        {
            // Log the error and provide an informative response
            Logger.Error(ex, "Error uploading chunk.");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
        
    }
    

    
    [HttpPut]
    [Route("{name}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NrFile>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<NrFile> SaveFile(string name, [FromBody] NrFile file)
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
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NrFile>))]
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
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NrFile>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<NrFile> GetByUniqueName(string name)
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
    
    [HttpGet]
    [Route("id/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NrFile>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<NrFile> GetById(int id)
    {
        var user = GetUser();

        try
        {
            Logger.Information("User:{User} downloaded file:{Id}", user.Value, id);
            
            var file = _filesService.GetById(id);

            file.Content = Array.Empty<byte>();
            
            return Ok(file);
        }
        catch (UserNotAuthorizedException ex)
        {
            Logger.Warning("The user {UserName} is not authorized to see that file message: {Message}", user.Name, ex.Message);
            return this.Unauthorized();
        }
        
        catch (DataNotFoundException ex)
        {
            Logger.Warning("The file {Id} could not be found message: {Message}", id, ex.Message);
            return this.Unauthorized();
        }
        
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while getting file: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

}
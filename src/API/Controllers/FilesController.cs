using DAL.Entities;
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

    /// <summary>
    /// Per-file access control (security finding NR-2026-017). Before this, knowing a file's
    /// unique name was the whole authorization on <c>GET /Files/{name}</c>, and
    /// <c>GET /Files/id/{id}</c> was enumerable by integer.
    /// </summary>
    private readonly IFileAccessAuthorizer _fileAccess;

    public FilesController(ILogger logger,
        IHttpContextAccessor httpContextAccessor,
        IFilesService filesService,
        IUsersService usersService,
        IWebHostEnvironment env,
        IFileAccessAuthorizer fileAccess) : base(logger, httpContextAccessor, usersService)
    {
        _filesService = filesService;
        _env = env;
        _fileAccess = fileAccess;
    }

    /// <summary>
    /// GitHub #80 (S44 D4): evidence on an assessment answer is created and deleted only through
    /// <see cref="AssessmentRunEvidenceController"/>, which knows whether the run is still open, the
    /// per-answer limit and the evidence size cap. These generic routes know none of that, so a file
    /// that is — or would become — assessment evidence is refused here rather than half-validated.
    /// </summary>
    public const string AssessmentEvidenceRouteError = "assessment_evidence_route";

    private ActionResult AssessmentEvidenceRefused(User user, string operation)
    {
        Logger.Warning("User:{User} tried to {Operation} assessment evidence through the generic files route",
            user.Value, operation);
        return BadRequest(new
        {
            error = AssessmentEvidenceRouteError,
            Message = "Assessment evidence is managed through /Assessments/runs/{runId}/questions/{questionId}/evidence."
        });
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
    public async Task<ActionResult<FileListing>> CreateFile([FromBody] NrFile file)
    {

        var user = GetUser();

        if (file.AssessmentRunAnswerId is not null) return AssessmentEvidenceRefused(user, "create");

        try
        {
            // Finding NR-2026-035: the caller has to be able to write to the parent the body names. The
            // service then ignores every other identity field in the body (owner, entity, id).
            await _fileAccess.EnsureCanAttachAsync(file, user);

            var newFile = _filesService.Create(file, user);
            Logger.Information("User:{User} created a new file", user.Value);

            return Created("Files/" + newFile.UniqueName, newFile);
        }
        catch (InvalidParameterException ex)
        {
            Logger.Warning("User:{User} sent an invalid file on create: {Message}", user.Value, ex.Message);
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
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
            // Just persist the chunk; reassembly + DB record creation happens in the
            // separate "complete" call once all chunks have been uploaded.
            _filesService.SaveChunk(chunk);

            return Ok("Chunk uploaded successfully.");
        }
        catch (InvalidParameterException ex)
        {
            // Track 7 finding NR-2026-006: a file id that is not a single safe path segment is a
            // rejected request, not a server fault, and saying so keeps the traversal attempt out of
            // the 500 bucket where nobody looks for it.
            Logger.Warning("User:{User} sent an invalid chunk descriptor: {Message}", user.Value, ex.Message);
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (Exception ex)
        {
            // Log the error and provide an informative response
            Logger.Error(ex, "Error uploading chunk.");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }

    }

    [HttpPost]
    [Route("local/complete")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(FileListing))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<FileListing>> CompleteLocalFile([FromBody] NrFile file,
        [FromQuery] string fileId, [FromQuery] int totalChunks)
    {
        var user = GetUser();

        if (file.AssessmentRunAnswerId is not null) return AssessmentEvidenceRefused(user, "upload");

        try
        {
            // Finding NR-2026-035: checked before the chunks are reassembled and read into memory, so a
            // refused attach costs a query rather than a buffer the size of the upload. The staged chunks
            // of a refused upload are left for TmpCleanup, as an abandoned upload's are.
            await _fileAccess.EnsureCanAttachAsync(file, user);

            var newFile = _filesService.CompleteChunkedUpload(file, fileId, totalChunks, user);
            Logger.Information("User:{User} created a new file via chunked upload", user.Value);

            return Created("Files/" + newFile.UniqueName, newFile);
        }
        catch (InvalidParameterException ex)
        {
            Logger.Warning("User:{User} sent an invalid file id on upload completion: {Message}",
                user.Value, ex.Message);
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (UserNotAuthorizedException ex)
        {
            Logger.Warning("The user {UserName} is not authorized to create files message: {Message}", user.Name, ex.Message);
            return this.Unauthorized();
        }
        catch (DataNotFoundException ex)
        {
            Logger.Warning("Chunked upload completion failed, missing or incomplete chunks: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status400BadRequest);
        }
        catch (Exception ex)
        {
            Logger.Warning("Unknown error while completing chunked file upload: {Message}", ex.Message);
            return this.StatusCode(StatusCodes.Status500InternalServerError);
        }
    }


    
    [HttpPut]
    [Route("{name}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<NrFile>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<NrFile> SaveFile(string name, [FromBody] NrFile file)
    {

        var user = GetUser();

        // Moving a file onto an answer here would skip every evidence check; the service also refuses
        // to rewrite a file that already is evidence (InvalidOperationException, answered 400 below).
        if (file.AssessmentRunAnswerId is not null) return AssessmentEvidenceRefused(user, "update");

        // The route names the file being updated; a body describing a different one is a malformed
        // request, not a second way to address a file.
        if (!string.Equals(name, file.UniqueName, StringComparison.Ordinal))
            return BadRequest(new { error = "invalid_parameter", ParameterName = "name",
                Message = "The route and the body name different files." });

        try
        {
            // Finding NR-2026-034: ownership used to be checked here against file.User from the request
            // body — the caller's own claim. The service now checks the stored row's owner and copies the
            // name only, so the decision cannot be skipped by a caller and the body cannot reach anything
            // else.
            _filesService.Save(file, user);
            Logger.Information("User:{User} updated file:{FileId}", user.Value, file.Id);

            return Ok();
        }
        catch (UserNotAuthorizedException ex)
        {
            Logger.Warning("The user {UserName} is not authorized to update this file: {FileId} message: {Message}",
                user.Name, file.Id, ex.Message);
            return this.Unauthorized();
        }
        catch (DataNotFoundException ex)
        {
            // Same answer as a refusal, as on the read routes: otherwise the status tells a caller which
            // id/unique-name pairs exist.
            Logger.Warning("The user {UserName} tried to update a file that was not found: {FileId} message: {Message}",
                user.Name, file.Id, ex.Message);
            return this.Unauthorized();
        }
        catch (InvalidParameterException ex)
        {
            Logger.Warning("User:{User} sent an invalid file update: {Message}", user.Value, ex.Message);
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
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

            // Deleting evidence here would bypass the submitted-run rule (S44 D6).
            if (file.AssessmentRunAnswerId is not null) return AssessmentEvidenceRefused(user, "delete");

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
    public async Task<ActionResult<NrFile>> GetByUniqueName(string name)
    {
        var user = GetUser();

        try
        {
            var file = _filesService.GetByUniqueName(name);

            // Finding NR-2026-017: the caller has to be able to reach the file's parent. Checked
            // before the download is logged, so the log records reads that happened rather than
            // reads that were attempted.
            await _fileAccess.EnsureCanReadAsync(file, user);

            Logger.Information("User:{User} downloaded file:{FileUniqueName}", user.Value, name);

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
    public async Task<ActionResult<NrFile>> GetById(int id)
    {
        var user = GetUser();

        try
        {
            var file = _filesService.GetById(id);

            // This route is enumerable by integer id, so the parent-permission check matters more
            // here than on the unique-name route (finding NR-2026-017).
            await _fileAccess.EnsureCanReadAsync(file, user);

            Logger.Information("User:{User} downloaded file:{Id}", user.Value, id);

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
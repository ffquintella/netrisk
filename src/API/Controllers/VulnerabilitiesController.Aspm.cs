using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using API.Security;
using Contracts.Importers;
using DAL.Entities;
using DAL.Enums;
using Microsoft.AspNetCore.Mvc;
using Model;
using Model.Exceptions;
using Model.Findings;
using Model.Jobs;
using ServerServices.Importers;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.String;

namespace API.Controllers;

/// <summary>
/// Track 3 (ASPM) endpoints on the vulnerability register: importer discovery, dynamic imports,
/// import job status, the triage lifecycle, and the SLA views.
///
/// A partial of <see cref="VulnerabilitiesController"/> rather than a separate controller because
/// the spec fixes the routes under <c>/Vulnerabilities</c> — <c>GET /vulnerabilities/importers</c>,
/// <c>POST /vulnerabilities/import/{importerName}/{fileId}</c>,
/// <c>GET /vulnerabilities/import-jobs/{id}</c> — and splitting the file keeps the pre-Track-3
/// controller readable.
/// </summary>
public partial class VulnerabilitiesController
{
    // The Track 3 collaborators are constructor-injected on the other half of this partial.

    // --- 3.1.4 importer discovery and dynamic import ---------------------------------------

    /// <summary>
    /// Lists every importer, built-in and plugin alike, with the extensions it handles and the
    /// deduplication chain configured for it.
    /// </summary>
    [PermissionAuthorize("vulnerabilities")]
    [RequireApiScope(ApiTokenScopes.VulnerabilitiesRead, ApiTokenScopes.VulnerabilitiesImport)]
    [HttpGet]
    [Route("importers")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ImporterDescriptor>))]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<List<ImporterDescriptor>>> GetImporters()
    {
        var user = GetUser();

        try
        {
            var importers = await ImporterRegistry.GetImportersAsync();
            Logger.Information("User:{User} listed the available vulnerability importers", user.Value);
            return Ok(importers);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error listing vulnerability importers");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// Imports an already-uploaded file with a named importer, as a background job.
    ///
    /// The importer name <c>auto</c> asks the registry to sniff the file's content instead. An
    /// unknown name is a 404 carrying the list of names that would have worked.
    /// </summary>
    [PermissionAuthorize("vulnerabilities_create")]
    [RequireApiScope(ApiTokenScopes.VulnerabilitiesImport)]
    [HttpPost]
    [Route("import/{importerName}/{fileId}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ImportJobCreationResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ImportJobCreationResult>> ImportWithImporter(string importerName, string fileId,
        [FromQuery] bool ignoreNegligible = true)
    {
        var user = GetUser();

        try
        {
            if (!StringCleaner.IsSafeFilename(fileId)) return BadRequest("Invalid fileId");

            var importFile = Path.Combine(FilesService.GetUploadDirectory(), fileId + ".dat");
            if (!System.IO.File.Exists(importFile)) return NotFound("File not found");

            var started = await StartImportJobAsync(importerName, importFile, fileId + ".dat", fileId, user,
                ignoreNegligible, idempotencyKey: null);

            Logger.Information("User:{User} started a {Importer} import of file {File}. Job {Job}, import {Import}",
                user.Value, importerName, fileId, started.JobId, started.ImportId);

            return Ok(started);
        }
        catch (DataNotFoundException ex)
        {
            // The registry's not-found carries the available importer names; passing the message
            // through is what saves the caller a second guess.
            Logger.Warning("User:{User} asked for unknown importer {Importer}", user.Value, importerName);
            return NotFound(ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error starting a {Importer} import", importerName);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>
    /// The status and counts of an import (3.1.4).
    ///
    /// This is what a CI runner polls, and what the exit-code gate reads, so it reports the counts a
    /// gating decision needs — new findings by severity included.
    /// </summary>
    [PermissionAuthorize("vulnerabilities")]
    [RequireApiScope(ApiTokenScopes.VulnerabilitiesRead, ApiTokenScopes.VulnerabilitiesImport)]
    [HttpGet]
    [Route("import-jobs/{id}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ScanImport))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ScanImport>> GetImportJob(int id)
    {
        var user = GetUser();

        try
        {
            var import = await IngestionService.GetImportAsync(id);
            Logger.Debug("User:{User} read import {Import}", user.Value, id);
            return Ok(import);
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error reading import {Import}", id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Recent imports, newest first — the import history view.</summary>
    [PermissionAuthorize("vulnerabilities")]
    [RequireApiScope(ApiTokenScopes.VulnerabilitiesRead)]
    [HttpGet]
    [Route("import-jobs")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ScanImport>))]
    public async Task<ActionResult<List<ScanImport>>> GetImportJobs([FromQuery] int take = 50)
    {
        GetUser();

        try
        {
            return Ok(await IngestionService.GetRecentImportsAsync(take));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error listing imports");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    // --- 3.5.2 direct upload ---------------------------------------------------------------

    /// <summary>
    /// Imports a raw scan payload sent as the request body — one curl-able call, no separate upload
    /// step (3.5.2).
    ///
    /// An <c>Idempotency-Key</c> header makes a repeat harmless: the same key returns the original
    /// import rather than importing again, which is what a CI retry storm needs. <c>?wait=true</c>
    /// runs the import inline for small payloads and returns its final counts.
    /// </summary>
    [PermissionAuthorize("vulnerabilities_create")]
    [RequireApiScope(ApiTokenScopes.VulnerabilitiesImport)]
    [HttpPost]
    [Route("import/{importerName}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ImportJobCreationResult))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    public async Task<ActionResult<ImportJobCreationResult>> ImportDirect(string importerName,
        [FromQuery] bool wait = false, [FromQuery] bool ignoreNegligible = true,
        [FromQuery] string? fileName = null)
    {
        var user = GetUser();

        var idempotencyKey = Request.Headers["Idempotency-Key"].ToString();
        if (string.IsNullOrWhiteSpace(idempotencyKey)) idempotencyKey = null;

        try
        {
            // Streamed to a temporary file rather than buffered in memory: a 500 MB scan report is
            // an ordinary thing to upload, and holding it in the request pipeline is how a single
            // CI job takes the API down.
            var tempPath = Path.Combine(Path.GetTempPath(), $"netrisk-import-{Guid.NewGuid():N}.dat");

            await using (var file = System.IO.File.Create(tempPath))
            {
                await Request.Body.CopyToAsync(file);
            }

            var length = new FileInfo(tempPath).Length;
            if (length == 0)
            {
                System.IO.File.Delete(tempPath);
                return BadRequest("The request body is empty.");
            }

            var started = await StartImportJobAsync(importerName, tempPath,
                fileName ?? $"upload-{DateTime.UtcNow:yyyyMMddHHmmss}", fileId: null, user, ignoreNegligible,
                idempotencyKey, deleteAfterRead: true);

            if (started.IsReplay)
            {
                System.IO.File.Delete(tempPath);
                Logger.Information(
                    "User:{User} replayed idempotency key on a {Importer} import; returning import {Import}",
                    user.Value, importerName, started.ImportId);

                return Ok(started);
            }

            Logger.Information("User:{User} uploaded a {Importer} report ({Bytes} bytes). Job {Job}, import {Import}",
                user.Value, importerName, length, started.JobId, started.ImportId);

            if (!wait) return Ok(started);

            // Synchronous mode for small payloads. Bounded so a caller cannot turn ?wait=true into
            // an indefinitely held connection on a huge file.
            var finished = await AwaitImportAsync(started.ImportId, TimeSpan.FromMinutes(5));
            started.Import = finished;
            return Ok(started);
        }
        catch (DataNotFoundException ex)
        {
            return NotFound(ex.InnerException?.Message ?? ex.Message);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error importing a {Importer} report from the request body", importerName);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    // --- 3.2 lifecycle ---------------------------------------------------------------------

    /// <summary>
    /// Moves a finding through the triage lifecycle (3.2.1). An illegal transition is a 422 — the
    /// request is well-formed, the finding is just not somewhere it can move from.
    /// </summary>
    [PermissionAuthorize("vulnerabilities_update")]
    [RequireApiScope(ApiTokenScopes.VulnerabilitiesWrite)]
    [HttpPut]
    [Route("{id}/status")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(Vulnerability))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<Vulnerability>> UpdateLifecycleStatus(int id,
        [FromBody] FindingStatusChangeRequest request)
    {
        var user = GetUser();

        if (request == null) return BadRequest("A status change request is required.");

        try
        {
            var finding = await LifecycleService.TransitionAsync(id, request.Status, user.Value,
                FindingStatusChangeSource.Manual, request.Justification, request.DuplicateOfId);

            Logger.Information("User:{User} moved finding {Finding} to {Status}", user.Value, id, request.Status);
            return Ok(finding);
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidStateTransitionException ex)
        {
            Logger.Warning("User:{User} attempted an illegal finding transition {From} to {To}: {Message}",
                user.Value, ex.FromState, ex.ToState, ex.Message);
            return UnprocessableEntity(new { error = "invalid_transition", ex.FromState, ex.ToState, ex.Message });
        }
        catch (InvalidParameterException ex)
        {
            return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error changing the status of finding {Finding}", id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>The finding's audit timeline (3.2.2), newest first.</summary>
    [PermissionAuthorize("vulnerabilities")]
    [RequireApiScope(ApiTokenScopes.VulnerabilitiesRead)]
    [HttpGet]
    [Route("{id}/history")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<FindingStatusHistory>))]
    public async Task<ActionResult<List<FindingStatusHistory>>> GetStatusHistory(int id)
    {
        GetUser();

        try
        {
            return Ok(await LifecycleService.GetHistoryAsync(id));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error reading the history of finding {Finding}", id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    /// <summary>Which states this finding may move to right now, for the triage UI.</summary>
    [PermissionAuthorize("vulnerabilities")]
    [HttpGet]
    [Route("{id}/allowed-transitions")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<FindingStatus>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<FindingStatus>>> GetAllowedTransitions(int id)
    {
        GetUser();

        try
        {
            return Ok(await LifecycleService.GetAllowedTransitionsAsync(id));
        }
        catch (DataNotFoundException)
        {
            return NotFound();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error reading allowed transitions for finding {Finding}", id);
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    // --- 3.4 SLA views ---------------------------------------------------------------------

    /// <summary>SLA compliance by severity over the open findings — the dashboard widget (3.4.2).</summary>
    [PermissionAuthorize("vulnerabilities")]
    [RequireApiScope(ApiTokenScopes.VulnerabilitiesRead)]
    [HttpGet]
    [Route("sla/compliance")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<SlaComplianceBucket>))]
    public async Task<ActionResult<List<SlaComplianceBucket>>> GetSlaCompliance()
    {
        GetUser();

        try
        {
            return Ok(await SlaService.GetComplianceBySeverityAsync(DateTime.UtcNow));
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Unknown error computing SLA compliance");
            return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }

    // --- internals -------------------------------------------------------------------------

    /// <summary>
    /// Starts an import job over a file on disk. Shared by the upload-then-import route, the direct
    /// upload route, and the legacy <c>import/nessus/{fileId}</c> alias, so all three behave
    /// identically.
    /// </summary>
    private async Task<ImportJobCreationResult> StartImportJobAsync(string importerName, string path,
        string? fileName, string? fileId, User user, bool ignoreNegligible, string? idempotencyKey,
        bool deleteAfterRead = false)
    {
        var now = DateTime.UtcNow;

        var context = new ImportContext
        {
            FileName = fileName,
            IgnoreNegligible = ignoreNegligible,
            EntityId = CallerEntityId(),
            UserId = user.Value,
            ImportedAt = now
        };

        var request = new ImportIngestionRequest
        {
            Importer = importerName,
            FileName = fileName,
            FileId = fileId,
            UserId = user.Value,
            EntityId = CallerEntityId(),
            IdempotencyKey = idempotencyKey,
            ImportedAt = now
        };

        var job = new ScanImportJob(Logger, ImporterRegistry, IngestionService, JobManager, importerName,
            () => OpenReport(path, deleteAfterRead), context, request, user);

        var jobId = await job.StartAsync();

        return new ImportJobCreationResult
        {
            JobId = jobId,
            ImportId = job.ImportId,
            IsReplay = job.IsReplay,
            Success = true,
            Message = job.IsReplay
                ? "This idempotency key was already used; returning the original import."
                : "Import started",
            JobStatus = (int)IntStatus.Running
        };
    }

    /// <summary>
    /// The entity that findings from this import belong to.
    ///
    /// Taken from the caller's own scope rather than a request parameter, so an import cannot
    /// attribute findings to a tenant the caller cannot see. A caller scoped to exactly one entity
    /// gets that one; an unrestricted caller, or one spanning several, gets null — the import has no
    /// single tenant to claim and the register's existing unscoped behaviour applies.
    /// </summary>
    private int? CallerEntityId()
    {
        var scope = DalService.GetCurrentEntityScope();

        if (scope.IsUnrestricted) return null;

        var ids = scope.EntityIds.Distinct().ToList();
        return ids.Count == 1 ? ids[0] : null;
    }

    /// <summary>
    /// Opens the report for the job. <paramref name="deleteAfterRead"/> uses
    /// <see cref="FileOptions.DeleteOnClose"/> so a directly-uploaded temporary file cannot be left
    /// behind by a failed import.
    /// </summary>
    private static Stream OpenReport(string path, bool deleteAfterRead) =>
        new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize: 81920,
            deleteAfterRead ? FileOptions.DeleteOnClose : FileOptions.None);

    /// <summary>
    /// Waits for a background import to reach a terminal state, for <c>?wait=true</c>. Bounded, and
    /// returns whatever state the import is in when the bound is reached rather than failing — a
    /// caller that asked to wait still wants the job id it can poll.
    /// </summary>
    private async Task<ScanImport> AwaitImportAsync(int importId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        var import = await IngestionService.GetImportAsync(importId);

        while (DateTime.UtcNow < deadline && IsRunning(import))
        {
            await Task.Delay(250);
            import = await IngestionService.GetImportAsync(importId);
        }

        return import;
    }

    private static bool IsRunning(ScanImport import) =>
        import.Status is (int)ScanImportStatus.Queued or (int)ScanImportStatus.Running;
}

/// <summary>The body of a lifecycle transition request.</summary>
public class FindingStatusChangeRequest
{
    public FindingStatus Status { get; set; }

    /// <summary>Mandatory when moving to a suppressing state or to Duplicate.</summary>
    public string? Justification { get; set; }

    /// <summary>The canonical finding. Mandatory when moving to Duplicate.</summary>
    public int? DuplicateOfId { get; set; }
}

/// <summary>
/// What an import request returns: the job to watch, the import row to read, and whether an
/// idempotency key meant nothing was actually started.
/// </summary>
public class ImportJobCreationResult : JobCreationResult
{
    /// <summary>The <c>scan_imports</c> row id — what <c>GET import-jobs/{id}</c> takes.</summary>
    public int ImportId { get; set; }

    /// <summary>True when the idempotency key had already been used and this is the original import.</summary>
    public bool IsReplay { get; set; }

    /// <summary>The completed import, for a <c>?wait=true</c> call. Null otherwise.</summary>
    public ScanImport? Import { get; set; }
}

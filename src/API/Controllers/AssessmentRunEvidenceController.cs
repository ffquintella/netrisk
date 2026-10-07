using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Assessments;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// A comment and evidence files on each answer of an assessment run (GitHub #80, T297, S44).
///
/// Gated, like every run endpoint of <see cref="AssessmentsController"/>, by
/// <c>RequireAssessmentAccess</c> (the <c>assessments</c> permission) at class level, so no action can
/// be added here without it. The run is found through the caller's entity scope by the service.
///
/// The evidence is downloaded through <c>GET /Files/{uniqueName}</c>, where the file access authorizer
/// applies the same permission; the generic <c>/Files</c> write routes refuse evidence, so these are the
/// only routes that create or delete it.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireAssessmentAccess")]
[Route("Assessments/runs/{runId:int}")]
public class AssessmentRunEvidenceController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IAssessmentRunEvidenceService evidence)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    /// <summary>Sets or clears the comment on one answer of the run.</summary>
    [HttpPut]
    [Route("questions/{questionId:int}/comment")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AssessmentRunAnswer))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssessmentRunAnswer>> SaveComment(int runId, int questionId,
        [FromBody] AssessmentAnswerCommentRequest? request)
    {
        var user = GetUser();

        try
        {
            var answer = await evidence.SaveCommentAsync(runId, questionId, request?.Comment);
            Logger.Information("User:{User} saved the comment on assessment run {RunId}, question {QuestionId}",
                user.Value, runId, questionId);
            return Ok(answer);
        }
        catch (Exception ex)
        {
            return Fail(ex, $"saving a comment on assessment run {runId}");
        }
    }

    /// <summary>Every evidence file of the run, with the question each answers.</summary>
    [HttpGet]
    [Route("evidence")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AssessmentAnswerEvidence>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<AssessmentAnswerEvidence>>> GetRunEvidence(int runId)
    {
        GetUser();

        try
        {
            return Ok(await evidence.GetRunEvidenceAsync(runId));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"listing the evidence of assessment run {runId}");
        }
    }

    /// <summary>
    /// Completes an upload staged through <c>POST /Files/local/chunk</c> as evidence on one answer.
    /// </summary>
    [HttpPost]
    [Route("questions/{questionId:int}/evidence")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(AssessmentAnswerEvidence))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<AssessmentAnswerEvidence>> AttachEvidence(int runId, int questionId,
        [FromBody] AssessmentEvidenceUploadRequest? request)
    {
        var user = GetUser();

        if (request is null)
            return BadRequest(new { error = "invalid_parameter", ParameterName = "request",
                Message = "The upload description is missing." });

        try
        {
            var created = await evidence.AttachEvidenceAsync(runId, questionId, request, user);
            return Created($"Files/{created.UniqueName}", created);
        }
        catch (Exception ex)
        {
            return Fail(ex, $"attaching evidence to assessment run {runId}");
        }
    }

    /// <summary>Deletes one evidence file of one answer. Only its uploader or an administrator may.</summary>
    [HttpDelete]
    [Route("questions/{questionId:int}/evidence/{uniqueName}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> DeleteEvidence(int runId, int questionId, string uniqueName)
    {
        var user = GetUser();

        try
        {
            await evidence.DeleteEvidenceAsync(runId, questionId, uniqueName, user);
            return Ok();
        }
        catch (Exception ex)
        {
            return Fail(ex, $"deleting evidence from assessment run {runId}");
        }
    }

    /// <summary>
    /// The domain exceptions onto status codes; anything else is logged and answered 500 with no
    /// detail. A missing run, question or file is 404 whatever the reason — out of scope included.
    /// </summary>
    private ActionResult Fail(Exception exception, string operation)
    {
        switch (exception)
        {
            case InvalidParameterException ex:
                return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
            case DataNotFoundException:
                return NotFound();
            case RuleBrokenException ex:
                return Conflict(new { error = ex.RuleName, ex.Message });
            case PermissionInvalidException:
                return StatusCode(StatusCodes.Status403Forbidden, new { error = "not_the_uploader" });
            default:
                Logger.Error(exception, "Unknown error {Operation}", operation);
                return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

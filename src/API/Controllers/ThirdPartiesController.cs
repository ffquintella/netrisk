using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Security;
using DAL.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.ThirdParties;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// The third-party register (Stage 9.10, S51 §6): suppliers, clouds and identity providers with their contract terms,
/// what they supply or process, sub-processors, data locations, HECVAT assessments and SBOMs, and the concentration by
/// supplier, cloud and identity.
///
/// Two audiences, one attribute per action, pinned by <c>ThirdPartiesAuthorizationTest</c>:
/// <list type="bullet">
/// <item>reads — <c>[Authorize(Policy = "RequireThirdPartyRead")]</c>: the risk register's audience, or whoever manages
/// the register;</item>
/// <item>every write — <c>[PermissionAuthorize("third_party_manage")]</c>, the one new permission (<c>Data/99.sql</c>).
/// The third line holds neither and is refused every write by the requirement every policy carries (S50 §4.7).</item>
/// </list>
/// Errors are those of <see cref="IThirdPartiesService"/>, mapped by <see cref="DecisionCycleErrors"/>; a write that would
/// file a record outside the caller's entities is re-thrown for <c>EntityScopeViolationMiddleware</c> to answer 403.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class ThirdPartiesController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IThirdPartiesService thirdParties)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    public const string ReadPolicy = "RequireThirdPartyRead";
    public const string ManagePermission = "third_party_manage";

    /// <summary>
    /// The largest body <c>POST /ThirdParties/{id}/Sboms</c> accepts: the 5 MiB document, escaped inside a JSON string,
    /// with room for the escapes. The parser measures the document itself again (S51 §4.6).
    /// </summary>
    public const long MaxSbomRequestBytes = 16L * 1024 * 1024;

    // --- reads -------------------------------------------------------------------------------------------------------

    [HttpGet]
    [Route("")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ThirdPartySummaryDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<ThirdPartySummaryDto>>> GetThirdParties([FromQuery] ThirdPartyStatus? status = null,
        [FromQuery] bool includeTerminated = false)
    {
        GetUser();

        try
        {
            return Ok(await thirdParties.GetThirdPartiesAsync(status, includeTerminated));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, "listing the third parties");
        }
    }

    [HttpGet]
    [Route("{id:int}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ThirdPartyDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ThirdPartyDto>> GetThirdParty(int id)
    {
        GetUser();

        try
        {
            return Ok(await thirdParties.GetThirdPartyAsync(id));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading third party {id}");
        }
    }

    /// <summary>Concentration by supplier, cloud and identity (T203).</summary>
    [HttpGet]
    [Route("Concentration")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ThirdPartyConcentrationReportDto))]
    public async Task<ActionResult<ThirdPartyConcentrationReportDto>> GetConcentration()
    {
        GetUser();

        try
        {
            return Ok(await thirdParties.GetConcentrationAsync());
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, "computing the third-party concentration");
        }
    }

    /// <summary>The third parties linked to an IT service, a process or a data record (T204).</summary>
    [HttpGet]
    [Route("ByEntity/{entityId:int}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<EntityThirdPartyDto>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<EntityThirdPartyDto>>> GetByEntity(int entityId)
    {
        GetUser();

        try
        {
            return Ok(await thirdParties.GetByEntityAsync(entityId));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading the third parties of entity {entityId}");
        }
    }

    [HttpGet]
    [Route("{id:int}/Assessments/{assessmentId:int}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ThirdPartyAssessmentDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ThirdPartyAssessmentDto>> GetAssessment(int id, int assessmentId)
    {
        GetUser();

        try
        {
            return Ok(await thirdParties.GetAssessmentAsync(id, assessmentId));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading HECVAT {assessmentId} of third party {id}");
        }
    }

    [HttpGet]
    [Route("{id:int}/Sboms/{sbomId:int}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ThirdPartySbomDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ThirdPartySbomDto>> GetSbom(int id, int sbomId)
    {
        GetUser();

        try
        {
            return Ok(await thirdParties.GetSbomAsync(id, sbomId));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading SBOM {sbomId} of third party {id}");
        }
    }

    /// <summary>The field-level trail of the third party and what it declares, after the visibility check.</summary>
    [HttpGet]
    [Route("{id:int}/History")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<DAL.Entities.AuditLog>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<DAL.Entities.AuditLog>>> GetHistory(int id, [FromQuery] int limit = 500)
    {
        GetUser();

        try
        {
            return Ok(await thirdParties.GetHistoryAsync(id, limit));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading the history of third party {id}");
        }
    }

    // --- writes: the record ------------------------------------------------------------------------------------------

    [HttpPost]
    [Route("")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ThirdPartyDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ThirdPartyDto>> Create([FromBody] ThirdPartyRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await thirdParties.CreateAsync(request ?? new ThirdPartyRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, "registering a third party");
        }
    }

    [HttpPut]
    [Route("{id:int}")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ThirdPartyDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ThirdPartyDto>> Update(int id, [FromBody] ThirdPartyRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await thirdParties.UpdateAsync(id, request ?? new ThirdPartyRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"updating third party {id}");
        }
    }

    /// <summary>Deletes a third party nothing refers to; one in use answers 422 <c>third_party_in_use</c>.</summary>
    [HttpDelete]
    [Route("{id:int}")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult> Delete(int id)
    {
        var user = GetUser();

        try
        {
            await thirdParties.DeleteAsync(id, user.Value);
            return NoContent();
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"deleting third party {id}");
        }
    }

    // --- writes: links -----------------------------------------------------------------------------------------------

    [HttpPut]
    [Route("{id:int}/Links/{entityId:int}")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ThirdPartyDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ThirdPartyDto>> Link(int id, int entityId, [FromBody] ThirdPartyLinkRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await thirdParties.LinkAsync(id, entityId, request ?? new ThirdPartyLinkRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"linking third party {id} to entity {entityId}");
        }
    }

    [HttpDelete]
    [Route("{id:int}/Links/{entityId:int}")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> Unlink(int id, int entityId)
    {
        var user = GetUser();

        try
        {
            await thirdParties.UnlinkAsync(id, entityId, user.Value);
            return NoContent();
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"unlinking third party {id} from entity {entityId}");
        }
    }

    // --- writes: declarations ----------------------------------------------------------------------------------------

    /// <summary>Replaces the sub-processor list and records that it was declared — an empty list included.</summary>
    [HttpPut]
    [Route("{id:int}/Subprocessors")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ThirdPartyDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ThirdPartyDto>> SetSubprocessors(int id,
        [FromBody] ThirdPartySubprocessorsRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await thirdParties.SetSubprocessorsAsync(id, request ?? new ThirdPartySubprocessorsRequest(),
                user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"declaring the sub-processors of third party {id}");
        }
    }

    [HttpPut]
    [Route("{id:int}/DataLocations")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ThirdPartyDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ThirdPartyDto>> SetDataLocations(int id,
        [FromBody] ThirdPartyDataLocationsRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await thirdParties.SetDataLocationsAsync(id, request ?? new ThirdPartyDataLocationsRequest(),
                user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"declaring the data locations of third party {id}");
        }
    }

    // --- writes: HECVAT ----------------------------------------------------------------------------------------------

    [HttpPost]
    [Route("{id:int}/Assessments")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ThirdPartyAssessmentDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ThirdPartyAssessmentDto>> RecordAssessment(int id,
        [FromBody] ThirdPartyAssessmentRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await thirdParties.RecordAssessmentAsync(id, request ?? new ThirdPartyAssessmentRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"recording a HECVAT for third party {id}");
        }
    }

    /// <summary>Replaces the answers; a partial set is accepted and scores incomplete.</summary>
    [HttpPut]
    [Route("{id:int}/Assessments/{assessmentId:int}/Answers")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ThirdPartyAssessmentDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ThirdPartyAssessmentDto>> ReplaceAnswers(int id, int assessmentId,
        [FromBody] ThirdPartyAssessmentAnswersRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await thirdParties.ReplaceAnswersAsync(id, assessmentId,
                request ?? new ThirdPartyAssessmentAnswersRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"recording the answers of HECVAT {assessmentId}");
        }
    }

    [HttpPost]
    [Route("{id:int}/Assessments/{assessmentId:int}/Void")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ThirdPartyAssessmentDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ThirdPartyAssessmentDto>> VoidAssessment(int id, int assessmentId,
        [FromBody] ThirdPartyAssessmentVoidRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await thirdParties.VoidAssessmentAsync(id, assessmentId,
                request ?? new ThirdPartyAssessmentVoidRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"voiding HECVAT {assessmentId}");
        }
    }

    // --- writes: SBOM ------------------------------------------------------------------------------------------------

    /// <summary>
    /// Imports a CycloneDX or SPDX JSON SBOM sent as text — never fetched from a URL (S51 D9). The body is capped before it
    /// is read, and the parser caps the document, its depth and its components again.
    /// </summary>
    [HttpPost]
    [Route("{id:int}/Sboms")]
    [PermissionAuthorize(ManagePermission)]
    [RequestSizeLimit(MaxSbomRequestBytes)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ThirdPartySbomDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status413PayloadTooLarge)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ThirdPartySbomDto>> ImportSbom(int id, [FromBody] ThirdPartySbomRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await thirdParties.ImportSbomAsync(id, request ?? new ThirdPartySbomRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"importing an SBOM for third party {id}");
        }
    }

    [HttpDelete]
    [Route("{id:int}/Sboms/{sbomId:int}")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteSbom(int id, int sbomId)
    {
        var user = GetUser();

        try
        {
            await thirdParties.DeleteSbomAsync(id, sbomId, user.Value);
            return NoContent();
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"removing SBOM {sbomId} of third party {id}");
        }
    }
}

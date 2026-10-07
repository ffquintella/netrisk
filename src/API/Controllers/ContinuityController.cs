using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using API.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Continuity;
using Model.Exceptions;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Business impact analysis and continuity (Stage 9.3, S43 §6): MTPD/MAO, RTO and RPO on business
/// processes and IT services, the dependencies between them and their cascade, restoration tests, the
/// Phase 7 metric and the two parameters.
///
/// Four audiences, one attribute per action, pinned by <c>ContinuityAuthorizationTest</c>:
/// <list type="bullet">
/// <item>reads — <c>[Authorize(Policy = "RequireContinuityRead")]</c>: whoever sees the risk register
/// (<c>RequireRiskmanagement</c>) or writes continuity data;</item>
/// <item>BIA and dependencies — <c>[PermissionAuthorize("bia_manage")]</c>;</item>
/// <item>restoration tests — <c>[PermissionAuthorize("restoration_test_record")]</c>;</item>
/// <item>the parameters — <c>[Authorize(Policy = "RequireAdminOnly")]</c>, the policy of the other
/// product parameters: neither author nor tester sets the ruler their own work is judged by.</item>
/// </list>
/// Every write also needs global scope, checked by the service before any lookup.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class ContinuityController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IContinuityService continuity)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    public const string ReadPolicy = "RequireContinuityRead";
    public const string ManageBiaPermission = "bia_manage";
    public const string RecordTestsPermission = "restoration_test_record";
    public const string SettingsPolicy = "RequireAdminOnly";

    private ClaimsPrincipal? Principal => _httpContextAccessor.HttpContext?.User;

    // --- reads --------------------------------------------------------------------------------

    /// <summary>Every business process and IT service, with or without a BIA.</summary>
    [HttpGet]
    [Route("Subjects")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<ContinuitySubjectDto>))]
    public async Task<ActionResult<List<ContinuitySubjectDto>>> GetSubjects()
    {
        GetUser();

        try
        {
            return Ok(await continuity.GetSubjectsAsync());
        }
        catch (Exception ex)
        {
            return Fail(ex, "listing the continuity subjects");
        }
    }

    /// <summary>The continuity profile of one node: BIA, verification, dependencies, cascade, threat.</summary>
    [HttpGet]
    [Route("Subjects/{entityId:int}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ContinuityProfileDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ContinuityProfileDto>> GetProfile(int entityId)
    {
        GetUser();

        try
        {
            return Ok(await continuity.GetProfileAsync(entityId, Principal));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"reading the continuity profile of entity {entityId}");
        }
    }

    /// <summary>The node's restoration tests, most recent first, voided ones included.</summary>
    [HttpGet]
    [Route("Subjects/{entityId:int}/RestorationTests")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RestorationTestDto>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<List<RestorationTestDto>>> GetRestorationTests(int entityId)
    {
        GetUser();

        try
        {
            return Ok(await continuity.GetRestorationTestsAsync(entityId));
        }
        catch (Exception ex)
        {
            return Fail(ex, $"reading the restoration tests of entity {entityId}");
        }
    }

    /// <summary>The Phase 7 metric "restoration tested vs declared RTO/RPO".</summary>
    [HttpGet]
    [Route("Metrics/RestorationVerification")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RestorationVerificationMetricDto))]
    public async Task<ActionResult<RestorationVerificationMetricDto>> GetRestorationVerificationMetric()
    {
        GetUser();

        try
        {
            return Ok(await continuity.GetRestorationVerificationMetricAsync());
        }
        catch (Exception ex)
        {
            return Fail(ex, "computing the restoration verification metric");
        }
    }

    /// <summary>The two continuity parameters in force.</summary>
    [HttpGet]
    [Route("Settings")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ContinuitySettingsDto))]
    public async Task<ActionResult<ContinuitySettingsDto>> GetSettings()
    {
        GetUser();

        try
        {
            return Ok(await continuity.GetSettingsAsync());
        }
        catch (Exception ex)
        {
            return Fail(ex, "reading the continuity parameters");
        }
    }

    // --- BIA and dependencies -----------------------------------------------------------------

    /// <summary>Creates (201) or replaces (200) the node's BIA.</summary>
    [HttpPut]
    [Route("Subjects/{entityId:int}/Bia")]
    [PermissionAuthorize(ManageBiaPermission)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(BusinessImpactAnalysisDto))]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(BusinessImpactAnalysisDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BusinessImpactAnalysisDto>> SaveBia(int entityId,
        [FromBody] BusinessImpactAnalysisRequest request)
    {
        var user = GetUser();

        try
        {
            var result = await continuity.SaveBiaAsync(entityId, request, user.Value);

            Logger.Information("User:{User} {Action} the BIA of entity {EntityId}", user.Value,
                result.Created ? "declared" : "saved", entityId);

            return result.Created
                ? CreatedAtAction(nameof(GetProfile), new { entityId }, result.Bia)
                : Ok(result.Bia);
        }
        catch (Exception ex)
        {
            return Fail(ex, $"saving the BIA of entity {entityId}");
        }
    }

    [HttpDelete]
    [Route("Subjects/{entityId:int}/Bia")]
    [PermissionAuthorize(ManageBiaPermission)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult> DeleteBia(int entityId)
    {
        var user = GetUser();

        try
        {
            await continuity.DeleteBiaAsync(entityId);
            Logger.Information("User:{User} deleted the BIA of entity {EntityId}", user.Value, entityId);
            return NoContent();
        }
        catch (Exception ex)
        {
            return Fail(ex, $"deleting the BIA of entity {entityId}");
        }
    }

    [HttpPost]
    [Route("Subjects/{entityId:int}/Dependencies")]
    [PermissionAuthorize(ManageBiaPermission)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(BiaDependencyDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<BiaDependencyDto>> AddDependency(int entityId,
        [FromBody] BiaDependencyCreateRequest request)
    {
        var user = GetUser();

        try
        {
            var dependency = await continuity.AddDependencyAsync(entityId, request, user.Value);

            Logger.Information("User:{User} declared that entity {EntityId} depends on {ProviderId}", user.Value,
                entityId, dependency.ProviderEntityId);

            return CreatedAtAction(nameof(GetProfile), new { entityId }, dependency);
        }
        catch (Exception ex)
        {
            return Fail(ex, $"adding a dependency to entity {entityId}");
        }
    }

    [HttpDelete]
    [Route("Subjects/{entityId:int}/Dependencies/{dependencyId:int}")]
    [PermissionAuthorize(ManageBiaPermission)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> DeleteDependency(int entityId, int dependencyId)
    {
        var user = GetUser();

        try
        {
            await continuity.DeleteDependencyAsync(entityId, dependencyId);
            Logger.Information("User:{User} deleted dependency {DependencyId} of entity {EntityId}", user.Value,
                dependencyId, entityId);
            return NoContent();
        }
        catch (Exception ex)
        {
            return Fail(ex, $"deleting dependency {dependencyId} of entity {entityId}");
        }
    }

    // --- restoration tests --------------------------------------------------------------------

    [HttpPost]
    [Route("Subjects/{entityId:int}/RestorationTests")]
    [PermissionAuthorize(RecordTestsPermission)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(RestorationTestDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RestorationTestDto>> RecordRestorationTest(int entityId,
        [FromBody] RestorationTestCreateRequest request)
    {
        var user = GetUser();

        try
        {
            var test = await continuity.RecordRestorationTestAsync(entityId, request, user.Value);

            Logger.Information("User:{User} recorded restoration test {TestId} on entity {EntityId}", user.Value,
                test.Id, entityId);

            return CreatedAtAction(nameof(GetRestorationTests), new { entityId }, test);
        }
        catch (Exception ex)
        {
            return Fail(ex, $"recording a restoration test on entity {entityId}");
        }
    }

    [HttpPost]
    [Route("Subjects/{entityId:int}/RestorationTests/{testId:int}/Void")]
    [PermissionAuthorize(RecordTestsPermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RestorationTestDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RestorationTestDto>> VoidRestorationTest(int entityId, int testId,
        [FromBody] RestorationTestVoidRequest request)
    {
        var user = GetUser();

        try
        {
            var test = await continuity.VoidRestorationTestAsync(entityId, testId, request, user.Value);

            Logger.Information("User:{User} voided restoration test {TestId} on entity {EntityId}", user.Value,
                testId, entityId);

            return Ok(test);
        }
        catch (Exception ex)
        {
            return Fail(ex, $"voiding restoration test {testId} of entity {entityId}");
        }
    }

    // --- parameters ---------------------------------------------------------------------------

    /// <summary>Stores both continuity parameters, or refuses both. Administrators only.</summary>
    [HttpPut]
    [Route("Settings")]
    [Authorize(Policy = SettingsPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(ContinuitySettingsDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<ContinuitySettingsDto>> SaveSettings([FromBody] ContinuitySettingsRequest request)
    {
        var user = GetUser();

        try
        {
            var settings = await continuity.SaveSettingsAsync(request);

            Logger.Information("User:{User} set the continuity parameters to {Days} days and weight {Weight}",
                user.Value, settings.RestorationTestValidityDays, settings.UnverifiedThreatWeight);

            return Ok(settings);
        }
        catch (Exception ex)
        {
            return Fail(ex, "saving the continuity parameters");
        }
    }

    /// <summary>
    /// The domain exceptions onto the status codes the other controllers use for them; anything else is
    /// logged and answered 500 with no detail.
    /// </summary>
    private ActionResult Fail(Exception exception, string operation)
    {
        switch (exception)
        {
            case InvalidParameterException ex:
                return BadRequest(new { error = "invalid_parameter", ex.ParameterName, ex.Message });
            case DataNotFoundException:
                return NotFound();
            case DataAlreadyExistsException ex:
                return Conflict(new { error = "already_exists", ex.Message });
            case RuleBrokenException ex:
                return UnprocessableEntity(new { error = ex.RuleName, ex.Message });
            case PermissionInvalidException ex:
                return StatusCode(StatusCodes.Status403Forbidden,
                    new { error = "insufficient_permission", ex.Permission, ex.Message });
            default:
                Logger.Error(exception, "Unknown error {Operation}", operation);
                return StatusCode(StatusCodes.Status500InternalServerError);
        }
    }
}

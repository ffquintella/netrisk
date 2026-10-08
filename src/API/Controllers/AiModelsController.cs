using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Security;
using DAL.Entities;
using DAL.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.AiGovernance;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// AI governance (Stage 9.12, S53 §6): the model inventory — purpose, data, vendor, version, risk tier, human oversight,
/// owner — with its findings and the evaluation of each model's current version; the metric readings and the human
/// overrides; and the register's risks that involve a model, which derive flag 11.
///
/// <b>Governance, never use.</b> No action here runs a model, takes a model's output as a decision, or accepts, approves,
/// reviews, closes or votes on anything — the instrument comes before any use (S27), and the Phase 6 prohibitions stay in
/// the decision services and in the valid-user requirement of every policy (<c>NonUserApprovalInventoryTest</c>, T215).
///
/// Three audiences, one attribute per action, pinned by <c>AiGovernanceAuthorizationTest</c>:
/// <list type="bullet">
/// <item>reads — <c>[Authorize(Policy = "RequireAiGovernanceRead")]</c>: the risk register's audience, or whoever maintains
/// the inventory;</item>
/// <item>inventory, data, reading and override writes — <c>[PermissionAuthorize("ai_governance_manage")]</c>, the one new
/// permission (<c>Data/101.sql</c>), within the model's own entity scope, checked by the service;</item>
/// <item>a risk's models — <c>[Authorize(Policy = "RequireRiskmanagement")]</c>, the policy of the linkage chain (S41),
/// because linking a model to a risk is editing the risk.</item>
/// </list>
/// The third line holds neither write right and is refused every write by the requirement every policy carries (S50 §4.7).
/// Errors are those of <see cref="IAiGovernanceService"/>, mapped by <see cref="DecisionCycleErrors"/>.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class AiModelsController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IAiGovernanceService aiGovernance)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    public const string ReadPolicy = "RequireAiGovernanceRead";
    public const string ManagePermission = "ai_governance_manage";
    public const string RiskPolicy = "RequireRiskmanagement";

    // --- the inventory (T212) ----------------------------------------------------------------------------------------

    /// <summary>The models the caller sees, with their evaluation state and findings.</summary>
    [HttpGet]
    [Route("")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AiModelSummaryDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<AiModelSummaryDto>>> GetModels([FromQuery] AiModelStatus? status = null,
        [FromQuery] bool includeRetired = false, [FromQuery] bool withFindings = false)
    {
        GetUser();

        try
        {
            return Ok(await aiGovernance.GetModelsAsync(status, includeRetired, withFindings));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, "listing the AI model inventory");
        }
    }

    [HttpGet]
    [Route("{id:int}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AiModelDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AiModelDto>> GetModel(int id)
    {
        GetUser();

        try
        {
            return Ok(await aiGovernance.GetModelAsync(id));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading AI model {id}");
        }
    }

    [HttpGet]
    [Route("{id:int}/History")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AuditLog>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<AuditLog>>> GetHistory(int id, [FromQuery] int limit = 500)
    {
        GetUser();

        try
        {
            return Ok(await aiGovernance.GetHistoryAsync(id, limit));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading the history of AI model {id}");
        }
    }

    [HttpPost]
    [Route("")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AiModelDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AiModelDto>> CreateModel([FromBody] AiModelRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await aiGovernance.CreateAsync(request ?? new AiModelRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, "registering an AI model");
        }
    }

    [HttpPut]
    [Route("{id:int}")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AiModelDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AiModelDto>> UpdateModel(int id, [FromBody] AiModelRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await aiGovernance.UpdateAsync(id, request ?? new AiModelRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"updating AI model {id}");
        }
    }

    /// <summary>Retires the model with a written reason; it stays in the inventory, frozen, as evidence.</summary>
    [HttpPost]
    [Route("{id:int}/Retire")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AiModelDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AiModelDto>> RetireModel(int id, [FromBody] AiGovernanceReasonRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await aiGovernance.RetireAsync(id, request ?? new AiGovernanceReasonRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"retiring AI model {id}");
        }
    }

    /// <summary>Declares, whole, the data records the model uses (an empty list is a declaration).</summary>
    [HttpPut]
    [Route("{id:int}/Data")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AiModelDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AiModelDto>> SetData(int id, [FromBody] AiModelDataRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await aiGovernance.SetDataAsync(id, request ?? new AiModelDataRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"declaring the data of AI model {id}");
        }
    }

    // --- metrics and overrides (T214, T215) --------------------------------------------------------------------------

    [HttpGet]
    [Route("{id:int}/Readings")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AiModelReadingDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<AiModelReadingDto>>> GetReadings(int id, [FromQuery] AiModelMetric? metric = null,
        [FromQuery] bool includeVoided = false)
    {
        GetUser();

        try
        {
            return Ok(await aiGovernance.GetReadingsAsync(id, metric, includeVoided));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading the metrics of AI model {id}");
        }
    }

    /// <summary>Records a reading of the model's current version; the human override rate is computed, never taken.</summary>
    [HttpPost]
    [Route("{id:int}/Readings")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AiModelReadingDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AiModelReadingDto>> RecordReading(int id, [FromBody] AiModelReadingRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await aiGovernance.RecordReadingAsync(id, request ?? new AiModelReadingRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"recording a reading of AI model {id}");
        }
    }

    [HttpPost]
    [Route("{id:int}/Readings/{readingId:int}/Void")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AiModelReadingDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AiModelReadingDto>> VoidReading(int id, int readingId,
        [FromBody] AiGovernanceReasonRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await aiGovernance.VoidReadingAsync(id, readingId, request ?? new AiGovernanceReasonRequest(),
                user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"voiding reading {readingId} of AI model {id}");
        }
    }

    [HttpGet]
    [Route("{id:int}/Overrides")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AiModelOverrideDto>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<AiModelOverrideDto>>> GetOverrides(int id, [FromQuery] bool includeVoided = false)
    {
        GetUser();

        try
        {
            return Ok(await aiGovernance.GetOverridesAsync(id, includeVoided));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading the overrides of AI model {id}");
        }
    }

    /// <summary>Records a person's decision contrary to the model, with the caller as its author and a written reason.</summary>
    [HttpPost]
    [Route("{id:int}/Overrides")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AiModelOverrideDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AiModelOverrideDto>> RecordOverride(int id, [FromBody] AiModelOverrideRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await aiGovernance.RecordOverrideAsync(id, request ?? new AiModelOverrideRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"recording an override of AI model {id}");
        }
    }

    [HttpPost]
    [Route("{id:int}/Overrides/{overrideId:int}/Void")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AiModelOverrideDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AiModelOverrideDto>> VoidOverride(int id, int overrideId,
        [FromBody] AiGovernanceReasonRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await aiGovernance.VoidOverrideAsync(id, overrideId, request ?? new AiGovernanceReasonRequest(),
                user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"voiding override {overrideId} of AI model {id}");
        }
    }

    // --- the register's risks (T213) ---------------------------------------------------------------------------------

    /// <summary>The inventoried models a risk involves — the AI component on the risk detail.</summary>
    [HttpGet]
    [Route("Risks/{riskId:int}")]
    [Authorize(Policy = RiskPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskAiModelsDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskAiModelsDto>> GetRiskModels(int riskId)
    {
        GetUser();

        try
        {
            return Ok(await aiGovernance.GetRiskModelsAsync(riskId));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading the AI models of risk {riskId}");
        }
    }

    /// <summary>Links a risk to a model; a model that is not retired derives flag 11 on it at the next reconciliation.</summary>
    [HttpPut]
    [Route("{id:int}/Risks/{riskId:int}")]
    [Authorize(Policy = RiskPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskAiModelsDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RiskAiModelsDto>> LinkRisk(int id, int riskId, [FromBody] AiModelRiskLinkRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await aiGovernance.LinkRiskAsync(id, riskId, request ?? new AiModelRiskLinkRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"linking risk {riskId} to AI model {id}");
        }
    }

    [HttpDelete]
    [Route("{id:int}/Risks/{riskId:int}")]
    [Authorize(Policy = RiskPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UnlinkRisk(int id, int riskId)
    {
        var user = GetUser();

        try
        {
            await aiGovernance.UnlinkRiskAsync(id, riskId, user.Value);
            return NoContent();
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"unlinking risk {riskId} from AI model {id}");
        }
    }
}

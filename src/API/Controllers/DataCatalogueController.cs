using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Security;
using DAL.Entities;
using DAL.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.DataCatalogue;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// The LGPD data catalogue (Stage 9.11, S52 §6): the catalogue of each data record — personal-data category, purposes with
/// their legal basis, retention, location and international transfer — with its findings; the legal and contractual
/// requirements; the RIPD (DPIA); and the requirements of a risk.
///
/// Three audiences, one attribute per action, pinned by <c>DataCatalogueAuthorizationTest</c>:
/// <list type="bullet">
/// <item>reads — <c>[Authorize(Policy = "RequireDataCatalogueRead")]</c>: the risk register's audience, or whoever maintains
/// the catalogue;</item>
/// <item>catalogue, requirement and RIPD writes — <c>[PermissionAuthorize("data_catalogue_manage")]</c>, the one new
/// permission (<c>Data/100.sql</c>), and global scope, checked by the service;</item>
/// <item>a risk's requirements — <c>[Authorize(Policy = "RequireRiskmanagement")]</c>, the policy of the linkage chain
/// (S41), because linking a requirement to a risk is editing the risk.</item>
/// </list>
/// The third line holds neither write right and is refused every write by the requirement every policy carries (S50 §4.7).
/// Errors are those of <see cref="IDataCatalogueService"/>, mapped by <see cref="DecisionCycleErrors"/>.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class DataCatalogueController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IDataCatalogueService catalogue)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    public const string ReadPolicy = "RequireDataCatalogueRead";
    public const string ManagePermission = "data_catalogue_manage";
    public const string RiskPolicy = "RequireRiskmanagement";

    // --- data records ------------------------------------------------------------------------------------------------

    /// <summary>Every data record with its catalogue state and findings — the compliance list (T207, T210).</summary>
    [HttpGet]
    [Route("Records")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<DataRecordSummaryDto>))]
    public async Task<ActionResult<List<DataRecordSummaryDto>>> GetRecords([FromQuery] bool withFindings = false)
    {
        GetUser();

        try
        {
            return Ok(await catalogue.GetRecordsAsync(withFindings));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, "listing the data catalogue");
        }
    }

    [HttpGet]
    [Route("Records/{entityId:int}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DataRecordDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DataRecordDto>> GetRecord(int entityId)
    {
        GetUser();

        try
        {
            return Ok(await catalogue.GetRecordAsync(entityId));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading the catalogue of data record {entityId}");
        }
    }

    [HttpGet]
    [Route("Records/{entityId:int}/History")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AuditLog>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<AuditLog>>> GetRecordHistory(int entityId, [FromQuery] int limit = 500)
    {
        GetUser();

        try
        {
            return Ok(await catalogue.GetRecordHistoryAsync(entityId, limit));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading the catalogue history of data record {entityId}");
        }
    }

    /// <summary>Catalogues a data record, or replaces its catalogue whole (T207).</summary>
    [HttpPut]
    [Route("Records/{entityId:int}")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DataRecordDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DataRecordDto>> SaveRecord(int entityId, [FromBody] DataCatalogueEntryRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await catalogue.SaveRecordAsync(entityId, request ?? new DataCatalogueEntryRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"cataloguing data record {entityId}");
        }
    }

    // --- legal requirements ------------------------------------------------------------------------------------------

    [HttpGet]
    [Route("Requirements")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<LegalRequirementDto>))]
    public async Task<ActionResult<List<LegalRequirementDto>>> GetRequirements()
    {
        GetUser();

        try
        {
            return Ok(await catalogue.GetRequirementsAsync());
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, "listing the legal requirements");
        }
    }

    [HttpGet]
    [Route("Requirements/{id:int}/History")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AuditLog>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<AuditLog>>> GetRequirementHistory(int id, [FromQuery] int limit = 500)
    {
        GetUser();

        try
        {
            return Ok(await catalogue.GetRequirementHistoryAsync(id, limit));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading the history of legal requirement {id}");
        }
    }

    [HttpPost]
    [Route("Requirements")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LegalRequirementDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LegalRequirementDto>> CreateRequirement([FromBody] LegalRequirementRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await catalogue.CreateRequirementAsync(request ?? new LegalRequirementRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, "registering a legal requirement");
        }
    }

    [HttpPut]
    [Route("Requirements/{id:int}")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(LegalRequirementDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<LegalRequirementDto>> UpdateRequirement(int id, [FromBody] LegalRequirementRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await catalogue.UpdateRequirementAsync(id, request ?? new LegalRequirementRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"updating legal requirement {id}");
        }
    }

    /// <summary>Deletes a requirement nothing cites; one in use answers 422 <c>legal_requirement_in_use</c>.</summary>
    [HttpDelete]
    [Route("Requirements/{id:int}")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult> DeleteRequirement(int id)
    {
        var user = GetUser();

        try
        {
            await catalogue.DeleteRequirementAsync(id, user.Value);
            return NoContent();
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"deleting legal requirement {id}");
        }
    }

    // --- RIPD / DPIA -------------------------------------------------------------------------------------------------

    [HttpGet]
    [Route("Dpias")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<DpiaSummaryDto>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<DpiaSummaryDto>>> GetDpias([FromQuery] DpiaStatus? status = null)
    {
        GetUser();

        try
        {
            return Ok(await catalogue.GetDpiasAsync(status));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, "listing the RIPDs");
        }
    }

    [HttpGet]
    [Route("Dpias/{id:int}")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DpiaDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<DpiaDto>> GetDpia(int id)
    {
        GetUser();

        try
        {
            return Ok(await catalogue.GetDpiaAsync(id));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading RIPD {id}");
        }
    }

    [HttpGet]
    [Route("Dpias/{id:int}/History")]
    [Authorize(Policy = ReadPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AuditLog>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<AuditLog>>> GetDpiaHistory(int id, [FromQuery] int limit = 500)
    {
        GetUser();

        try
        {
            return Ok(await catalogue.GetDpiaHistoryAsync(id, limit));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading the history of RIPD {id}");
        }
    }

    [HttpPost]
    [Route("Dpias")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DpiaDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<DpiaDto>> CreateDpia([FromBody] DpiaRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await catalogue.CreateDpiaAsync(request ?? new DpiaRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, "drafting a RIPD");
        }
    }

    [HttpPut]
    [Route("Dpias/{id:int}")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DpiaDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DpiaDto>> UpdateDpia(int id, [FromBody] DpiaRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await catalogue.UpdateDpiaAsync(id, request ?? new DpiaRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"updating RIPD {id}");
        }
    }

    /// <summary>Links a draft RIPD to a data record or a business process (T208).</summary>
    [HttpPut]
    [Route("Dpias/{id:int}/Links/{entityId:int}")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DpiaDto))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DpiaDto>> LinkDpia(int id, int entityId)
    {
        var user = GetUser();

        try
        {
            return Ok(await catalogue.LinkDpiaAsync(id, entityId, user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"linking RIPD {id} to entity {entityId}");
        }
    }

    [HttpDelete]
    [Route("Dpias/{id:int}/Links/{entityId:int}")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult> UnlinkDpia(int id, int entityId)
    {
        var user = GetUser();

        try
        {
            await catalogue.UnlinkDpiaAsync(id, entityId, user.Value);
            return NoContent();
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"unlinking RIPD {id} from entity {entityId}");
        }
    }

    /// <summary>Approves a complete draft; the approver is the caller, never the third line.</summary>
    [HttpPost]
    [Route("Dpias/{id:int}/Approve")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DpiaDto))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DpiaDto>> ApproveDpia(int id)
    {
        var user = GetUser();

        try
        {
            return Ok(await catalogue.ApproveDpiaAsync(id, user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"approving RIPD {id}");
        }
    }

    [HttpPost]
    [Route("Dpias/{id:int}/Retire")]
    [PermissionAuthorize(ManagePermission)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(DpiaDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<DpiaDto>> RetireDpia(int id, [FromBody] DpiaRetireRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await catalogue.RetireDpiaAsync(id, request ?? new DpiaRetireRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"retiring RIPD {id}");
        }
    }

    // --- the requirements of a risk ----------------------------------------------------------------------------------

    /// <summary>The risk's requirements, the data records it reaches with their findings, and what they cite (T209).</summary>
    [HttpGet]
    [Route("Risks/{riskId:int}")]
    [Authorize(Policy = RiskPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskComplianceDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskComplianceDto>> GetRiskCompliance(int riskId)
    {
        GetUser();

        try
        {
            return Ok(await catalogue.GetRiskComplianceAsync(riskId));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"reading the legal requirements of risk {riskId}");
        }
    }

    [HttpPut]
    [Route("Risks/{riskId:int}/Requirements/{requirementId:int}")]
    [Authorize(Policy = RiskPolicy)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskComplianceDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<RiskComplianceDto>> LinkRiskRequirement(int riskId, int requirementId,
        [FromBody] RiskLegalRequirementRequest? request)
    {
        var user = GetUser();

        try
        {
            return Ok(await catalogue.LinkRiskRequirementAsync(riskId, requirementId,
                request ?? new RiskLegalRequirementRequest(), user.Value));
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"linking requirement {requirementId} to risk {riskId}");
        }
    }

    [HttpDelete]
    [Route("Risks/{riskId:int}/Requirements/{requirementId:int}")]
    [Authorize(Policy = RiskPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult> UnlinkRiskRequirement(int riskId, int requirementId)
    {
        var user = GetUser();

        try
        {
            await catalogue.UnlinkRiskRequirementAsync(riskId, requirementId, user.Value);
            return NoContent();
        }
        catch (Exception ex)
        {
            return DecisionCycleErrors.Map(this, Logger, ex, $"unlinking requirement {requirementId} from risk {riskId}");
        }
    }
}

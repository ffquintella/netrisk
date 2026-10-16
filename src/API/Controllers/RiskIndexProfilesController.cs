using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using API.Controllers.RiskIndex;
using API.Security;
using DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.RiskIndex;
using ServerServices.Interfaces;
using ServerServices.RiskIndex.Profiles;
using ServerServices.RiskIndex.Read;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// The profile lifecycle (S40 §8.4; M53, T250). No class attribute: each action declares its permission — reading with
/// <c>risk_index_view</c>, drafting with <c>risk_index_configure</c>, deciding with <c>risk_index_approve</c> — pinned by
/// <c>RiskIndexEndpointPermissionMapTest</c>. The attribute passes an administrator; the approval itself does not: the
/// service re-reads the approver's grants and enforces the segregation of duties (S40 §11.4).
/// </summary>
[ApiController]
[Route("RiskIndex/Profiles")]
public class RiskIndexProfilesController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IRiskIndexProfilesService profiles)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private RiskIndexCaller Caller() => RiskIndexCaller.FromPrincipal(GetUser().Value, _httpContextAccessor.HttpContext?.User);

    private async Task<ActionResult<T>> Run<T>(string operation, Func<RiskIndexCaller, Task<T>> action)
    {
        var caller = Caller();
        try
        {
            return Ok(await action(caller));
        }
        catch (Exception ex)
        {
            return RiskIndexApiErrors.Map(this, Logger, ex, operation);
        }
    }

    [HttpGet]
    [Route("")]
    [PermissionAuthorize(RiskIndexPermissions.View)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskIndexProfileSummaryDto>))]
    public Task<ActionResult<IReadOnlyList<RiskIndexProfileSummaryDto>>> List(CancellationToken ct = default) =>
        Run("listing the profiles", _ => profiles.ListAsync(ct));

    [HttpGet]
    [Route("{id:int}")]
    [PermissionAuthorize(RiskIndexPermissions.View)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexProfileDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<RiskIndexProfileDto>> Get(int id, CancellationToken ct = default) =>
        Run("reading a profile", _ => profiles.GetAsync(id, ct));

    [HttpGet]
    [Route("Active")]
    [PermissionAuthorize(RiskIndexPermissions.View)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexProfileDto))]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<ActionResult<RiskIndexProfileDto>> GetActive(CancellationToken ct = default)
    {
        GetUser();
        try
        {
            var active = await profiles.GetActiveAsync(ct);
            return active is null ? NoContent() : Ok(active);
        }
        catch (Exception ex)
        {
            return RiskIndexApiErrors.Map(this, Logger, ex, "reading the active profile");
        }
    }

    [HttpGet]
    [Route("Diff")]
    [PermissionAuthorize(RiskIndexPermissions.View)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexProfileDiffDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<RiskIndexProfileDiffDto>> Diff([FromQuery] int a, [FromQuery] int b, CancellationToken ct = default) =>
        Run("comparing two profiles", _ => profiles.DiffAsync(a, b, ct));

    [HttpGet]
    [Route("Schema")]
    [PermissionAuthorize(RiskIndexPermissions.View)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexParameterSchemaDto))]
    public Task<ActionResult<RiskIndexParameterSchemaDto>> Schema() =>
        Run("reading the parameter schema", _ => Task.FromResult(profiles.GetSchema()));

    [HttpGet]
    [Route("Presets")]
    [PermissionAuthorize(RiskIndexPermissions.Configure)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<RiskIndexPresetDto>))]
    public Task<ActionResult<IReadOnlyList<RiskIndexPresetDto>>> Presets() =>
        Run("reading the presets", _ => Task.FromResult(profiles.GetPresets()));

    [HttpPost]
    [Route("")]
    [PermissionAuthorize(RiskIndexPermissions.Configure)]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(RiskIndexProfileDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RiskIndexProfileDto>> Create([FromBody] RiskIndexProfileCreateRequest? request,
        CancellationToken ct = default)
    {
        var caller = Caller();
        try
        {
            var created = await profiles.CreateAsync(request ?? new RiskIndexProfileCreateRequest("", null, null), caller, ct);
            return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
        }
        catch (Exception ex)
        {
            return RiskIndexApiErrors.Map(this, Logger, ex, "drafting a profile");
        }
    }

    /// <summary>Edits a draft under <c>If-Match: ‹revision›</c> (S40 §6.2.1): a missing header is 428, a stale one 409.</summary>
    [HttpPut]
    [Route("{id:int}")]
    [PermissionAuthorize(RiskIndexPermissions.Configure)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexProfileDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status428PreconditionRequired)]
    public async Task<ActionResult<RiskIndexProfileDto>> Update(int id, [FromBody] RiskIndexProfileUpdateRequest? request,
        CancellationToken ct = default)
    {
        var caller = Caller();
        try
        {
            if (Revision() is not { } revision)
                throw new RiskIndexException(StatusCodes.Status428PreconditionRequired, RiskIndexErrors.IfMatchRequired,
                    "Send If-Match with the draft's revision.");
            if (request is null)
                throw RiskIndexException.BadRequest(RiskIndexErrors.ParameterOutOfRange, "The body is the draft's name, mode and parameters.");
            return Ok(await profiles.UpdateAsync(id, request, revision, caller, ct));
        }
        catch (Exception ex)
        {
            return RiskIndexApiErrors.Map(this, Logger, ex, $"editing profile {id}");
        }
    }

    /// <summary>The <c>If-Match</c> revision: a number, quoted or not, weak or not.</summary>
    private int? Revision()
    {
        var header = _httpContextAccessor.HttpContext?.Request.Headers.IfMatch.ToString();
        if (string.IsNullOrWhiteSpace(header)) return null;
        var value = header.Trim();
        if (value.StartsWith("W/", StringComparison.OrdinalIgnoreCase)) value = value[2..];
        value = value.Trim('"', ' ');
        return int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var revision) ? revision : null;
    }

    [HttpDelete]
    [Route("{id:int}")]
    [PermissionAuthorize(RiskIndexPermissions.Configure)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Delete(int id, CancellationToken ct = default)
    {
        var caller = Caller();
        try
        {
            await profiles.DeleteAsync(id, caller, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            return RiskIndexApiErrors.Map(this, Logger, ex, $"deleting profile {id}");
        }
    }

    [HttpPost]
    [Route("{id:int}/Validate")]
    [PermissionAuthorize(RiskIndexPermissions.Configure)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexValidationResultDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<RiskIndexValidationResultDto>> Validate(int id, CancellationToken ct = default) =>
        Run($"validating profile {id}", _ => profiles.ValidateAsync(id, ct));

    [HttpPost]
    [Route("{id:int}/Preview")]
    [PermissionAuthorize(RiskIndexPermissions.Configure)]
    [ProducesResponseType(StatusCodes.Status202Accepted, Type = typeof(RiskIndexPreviewStatusDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<RiskIndexPreviewStatusDto>> RequestPreview(int id, CancellationToken ct = default)
    {
        var caller = Caller();
        try
        {
            return Accepted(await profiles.RequestPreviewAsync(id, caller, ct));
        }
        catch (Exception ex)
        {
            return RiskIndexApiErrors.Map(this, Logger, ex, $"queueing the preview of profile {id}");
        }
    }

    [HttpGet]
    [Route("{id:int}/Preview")]
    [PermissionAuthorize(RiskIndexPermissions.View)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexPreviewDto))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<RiskIndexPreviewDto>> GetPreview(int id, CancellationToken ct = default) =>
        Run($"reading the preview of profile {id}", caller => profiles.GetPreviewAsync(id, caller, ct));

    [HttpPost]
    [Route("{id:int}/Submit")]
    [PermissionAuthorize(RiskIndexPermissions.Configure)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexProfileDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<RiskIndexProfileDto>> Submit(int id, [FromBody] RiskIndexSubmitRequest? request,
        CancellationToken ct = default) =>
        Run($"submitting profile {id}", caller => profiles.SubmitAsync(id, request ?? new RiskIndexSubmitRequest(""), caller, ct));

    [HttpGet]
    [Route("{id:int}/ApprovalEligibility")]
    [PermissionAuthorize(RiskIndexPermissions.Approve)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexApprovalEligibilityDto))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<RiskIndexApprovalEligibilityDto>> ApprovalEligibility(int id, CancellationToken ct = default) =>
        Run($"evaluating the approval of profile {id}", caller => profiles.GetApprovalEligibilityAsync(id, caller, ct));

    [HttpPost]
    [Route("{id:int}/Approve")]
    [PermissionAuthorize(RiskIndexPermissions.Approve)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexProfileDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<RiskIndexProfileDto>> Approve(int id, [FromBody] RiskIndexApproveRequest? request,
        CancellationToken ct = default) =>
        Run($"approving profile {id}", caller =>
            profiles.ApproveAsync(id, request ?? new RiskIndexApproveRequest("", null, false, null), caller, ct));

    [HttpPost]
    [Route("{id:int}/Reject")]
    [PermissionAuthorize(RiskIndexPermissions.Approve)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexProfileDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<RiskIndexProfileDto>> Reject(int id, [FromBody] RiskIndexRejectRequest? request,
        CancellationToken ct = default) =>
        Run($"rejecting profile {id}", caller => profiles.RejectAsync(id, request ?? new RiskIndexRejectRequest(""), caller, ct));

    [HttpPost]
    [Route("{id:int}/Retire")]
    [PermissionAuthorize(RiskIndexPermissions.Approve)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexProfileDto))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public Task<ActionResult<RiskIndexProfileDto>> Retire(int id, [FromBody] RiskIndexRetireRequest? request,
        CancellationToken ct = default) =>
        Run($"suspending profile {id}", caller =>
            profiles.RetireAsync(id, request ?? new RiskIndexRetireRequest("", ""), caller, ct));

    /// <summary>Links the committee's minutes, uploaded through <c>/Files</c> by the caller, to a pending profile (S40 §8.4).</summary>
    [HttpPost]
    [Route("{id:int}/Attachments")]
    [PermissionAuthorize(RiskIndexPermissions.Approve)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult> Attach(int id, [FromBody] RiskIndexAttachRequest? request, CancellationToken ct = default)
    {
        var caller = Caller();
        try
        {
            await profiles.AttachAsync(id, request ?? new RiskIndexAttachRequest(0), caller, ct);
            return NoContent();
        }
        catch (Exception ex)
        {
            return RiskIndexApiErrors.Map(this, Logger, ex, $"attaching evidence to profile {id}");
        }
    }

    [HttpGet]
    [Route("{id:int}/Attachments")]
    [PermissionAuthorize(RiskIndexPermissions.View)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<Model.DTO.FileListing>))]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<IReadOnlyList<Model.DTO.FileListing>>> Attachments(int id, CancellationToken ct = default) =>
        Run($"listing the evidence of profile {id}", _ => profiles.ListAttachmentsAsync(id, ct));

    [HttpGet]
    [Route("{id:int}/Bridge")]
    [PermissionAuthorize(RiskIndexPermissions.View)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(RiskIndexBridgeDto))]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<ActionResult<RiskIndexBridgeDto>> Bridge(int id, CancellationToken ct = default) =>
        Run($"reading the bridge of profile {id}", caller => profiles.GetBridgeAsync(id, caller, ct));

    [HttpGet]
    [Route("{id:int}/History")]
    [PermissionAuthorize(RiskIndexPermissions.View)]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AuditLog>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public Task<ActionResult<IReadOnlyList<AuditLog>>> History(int id, [FromQuery] int limit = 500, CancellationToken ct = default) =>
        Run($"reading the history of profile {id}", caller => profiles.GetHistoryAsync(id, limit, caller, ct));
}

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using API.Security;
using Microsoft.AspNetCore.Authorization;
using DAL.Enums;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Integrations;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// Issue links for records that are not findings (Track 4 milestone 4.6): incidents and risks.
///
/// One route with a checked target kind rather than two near-identical controllers. Two controllers is
/// how the permission on one of them ends up missing — the check is
/// <see cref="RequirePermissionFor"/>, called first in every action, and there is exactly one copy of
/// it to get right.
///
/// Findings keep their own controller (<see cref="FindingIssuesController"/>) because they carry the
/// parts these records do not have: the lifecycle transitions, the auto-create policy, the preview and
/// the conflict queue. A link to an incident or a risk is a reference — mirrored and displayed, never
/// transitioning the NetRisk record on its own.
/// </summary>
[ApiController]
[Route("[controller]")]
public class RecordIssuesController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IPermissionsService permissions,
    IJiraIntegrationService service)
    : IntegrationsControllerBase(logger, httpContextAccessor, usersService)
{
    /// <summary>The issues one record is linked to, across every connection.</summary>
    [Authorize]
    [HttpGet]
    [Route("{targetKind}/{targetId:int}")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<FindingIssueLinkView>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<List<FindingIssueLinkView>>> GetForRecord(string targetKind,
        int targetId)
    {
        GetUser();

        if (!TryParseKind(targetKind, out var kind)) return BadRequest(KindError(targetKind));

        if (!await RequirePermissionFor(kind, write: false)) return Forbid();

        return await RunAsync(() => service.GetLinksForRecordAsync(kind, targetId),
            $"listing issue links for {kind} {targetId}");
    }

    /// <summary>
    /// Creates an issue for the record and links it. Idempotent per (connection, record): pressing the
    /// button twice returns the existing link rather than filing a duplicate in someone else's project.
    /// </summary>
    [Authorize]
    [HttpPost]
    [Route("{targetKind}/{targetId:int}/create")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FindingIssueLinkView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FindingIssueLinkView>> Create(string targetKind, int targetId,
        [FromBody] RecordIssueRequest request)
    {
        var user = GetUser();

        if (!TryParseKind(targetKind, out var kind)) return BadRequest(KindError(targetKind));

        if (!await RequirePermissionFor(kind, write: true)) return Forbid();

        Logger.Information("User:{User} created an issue for {Kind} {Target} on connection {Connection}",
            user.Value, kind, targetId, request?.ConnectionId);

        return await RunAsync(
            () => service.CreateIssueForRecordAsync(request!.ConnectionId, kind, targetId, user.Value),
            $"creating an issue for {kind} {targetId}");
    }

    /// <summary>Links the record to an issue that already exists, by key or URL.</summary>
    [Authorize]
    [HttpPost]
    [Route("{targetKind}/{targetId:int}/link")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FindingIssueLinkView))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<FindingIssueLinkView>> Link(string targetKind, int targetId,
        [FromBody] RecordIssueRequest request)
    {
        var user = GetUser();

        if (!TryParseKind(targetKind, out var kind)) return BadRequest(KindError(targetKind));

        if (!await RequirePermissionFor(kind, write: true)) return Forbid();

        Logger.Information("User:{User} linked {Kind} {Target} to {Issue}", user.Value, kind, targetId,
            request?.IssueKey);

        return await RunAsync(
            () => service.LinkRecordAsync(request!.ConnectionId, kind, targetId,
                request.IssueKey ?? "", user.Value),
            $"linking {kind} {targetId} to an issue");
    }

    // --- guards -----------------------------------------------------------------------------

    /// <summary>
    /// The permission for the record kind in the route.
    ///
    /// Checked in the action rather than declared as an attribute, because the required permission is
    /// part of the URL: an attribute would have to name the union of all three, which would let
    /// somebody with only the incidents permission link a risk. Writes need the record's own modify
    /// permission — filing a ticket about a risk is a statement about that risk.
    /// </summary>
    private async Task<bool> RequirePermissionFor(IssueLinkTargetKind kind, bool write)
    {
        var permission = (kind, write) switch
        {
            (IssueLinkTargetKind.Incident, _) => "incident_management",
            (IssueLinkTargetKind.Risk, false) => "riskmanagement",
            (IssueLinkTargetKind.Risk, true) => "modify_risks",
            (_, false) => "vulnerabilities",
            _ => "vulnerabilities_update"
        };

        var user = await GetUserAsync();

        var granted = permissions.UserHasPermission(user, permission);

        if (!granted)
            Logger.Warning("User:{User} was refused an issue-link operation on {Kind}: "
                           + "the '{Permission}' permission is required",
                user.Name, kind, permission);

        return granted;
    }

    private static bool TryParseKind(string value, out IssueLinkTargetKind kind) =>
        Enum.TryParse(value, ignoreCase: true, out kind)
        && Enum.IsDefined(kind);

    private static object KindError(string value) => new
    {
        error = "invalid_parameter",
        parameterName = "targetKind",
        message = $"'{value}' is not a record kind. Use finding, incident or risk."
    };
}

/// <summary>Which connection, and which issue when linking an existing one.</summary>
public class RecordIssueRequest
{
    public int ConnectionId { get; set; }

    /// <summary>An issue key (<c>SD-4711</c>) or its URL. Only used by the link action.</summary>
    public string? IssueKey { get; set; }
}

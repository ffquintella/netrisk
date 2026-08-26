using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

/// <summary>
/// The field-level governance trail and the auditor evidence pack (Track 8 milestone 8.4).
///
/// Read-only by construction: there is no endpoint that writes or edits an audit row, and the only
/// one that removes any is the retention job. A trail an API caller can edit is not a trail.
/// </summary>
[ApiController]
[Authorize(Policy = "RequireValidUser")]
[Route("[controller]")]
public class AuditTrailController(
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService,
    IAuditTrailService auditTrail)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    /// <summary>The recorded changes to one governance record.</summary>
    [HttpGet]
    [Route("{entityType}/{entityId}")]
    [Authorize(Policy = "RequireRiskmanagement")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AuditLog>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<AuditLog>>> GetForRecord(string entityType, int entityId,
        [FromQuery] int limit = 500)
    {
        GetUser();

        // The allowlist is the answer, not a filter over an open query: accepting an arbitrary type
        // name would let a caller probe which types the interceptor covers, and would return an empty
        // list for a typo rather than telling them it was a typo.
        if (!AuditTrailService.AuditedTypes.Contains(entityType))
            return BadRequest(new
            {
                error = "not_audited",
                message = $"'{entityType}' is not in the audited scope.",
                audited = AuditTrailService.AuditedTypes
            });

        return Ok(await auditTrail.GetForRecordAsync(entityType, entityId, limit));
    }

    /// <summary>The types the interceptor writes rows for, so a client can render only what exists.</summary>
    [HttpGet]
    [Route("Scope")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(IReadOnlyCollection<string>))]
    public ActionResult<IReadOnlyCollection<string>> GetScope()
    {
        GetUser();
        return Ok(AuditTrailService.AuditedTypes);
    }

    /// <summary>
    /// The auditor evidence pack for one entity and period (8.4.2): the field-level trail over the
    /// whole governance aggregate, in the order it happened.
    ///
    /// Admin-only. This is a cross-record export of who-did-what, which is a different and larger
    /// disclosure than reading one risk's history.
    /// </summary>
    [HttpGet]
    [Route("Evidence")]
    [Authorize(Policy = "RequireAdminOnly")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(List<AuditLog>))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<List<AuditLog>>> GetEvidence([FromQuery] int? entityId,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] int limit = 20000)
    {
        var user = GetUser();

        var fromUtc = from ?? DateTime.UtcNow.AddYears(-1);
        var toUtc = to ?? DateTime.UtcNow;

        if (toUtc < fromUtc)
            return BadRequest(new { error = "invalid_period", message = "'to' is before 'from'." });

        Logger.Information(
            "User:{User} exported the governance evidence trail for entity {Entity} from {From:yyyy-MM-dd} " +
            "to {To:yyyy-MM-dd}", user.Value, entityId?.ToString() ?? "(all)", fromUtc, toUtc);

        return Ok(await auditTrail.GetForEntityPeriodAsync(entityId, fromUtc, toUtc, limit));
    }

    /// <summary>
    /// The same evidence pack rendered as a report through the 2.1 engine, so it arrives as the
    /// PDF/CSV an auditor asks for rather than as JSON somebody has to format.
    /// </summary>
    [HttpGet]
    [Route("Evidence/Report")]
    [Authorize(Policy = "RequireAdminOnly")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult> GetEvidenceReport([FromQuery] int? entityId,
        [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null,
        [FromQuery] string format = "csv")
    {
        var user = GetUser();

        var fromUtc = from ?? DateTime.UtcNow.AddYears(-1);
        var toUtc = to ?? DateTime.UtcNow;

        if (toUtc < fromUtc)
            return BadRequest(new { error = "invalid_period", message = "'to' is before 'from'." });

        var rows = await auditTrail.GetForEntityPeriodAsync(entityId, fromUtc, toUtc);

        Logger.Information("User:{User} rendered the governance evidence pack ({Count} rows) as {Format}",
            user.Value, rows.Count, format);

        // CSV is produced here rather than through the template engine on purpose: this is a flat
        // change log, and a report template would add a cover page to something an auditor wants to
        // open in a spreadsheet. The templated PDF path stays available through ReportsController for
        // the register itself, which is the part that benefits from branding.
        var csv = GovernanceEvidenceCsv.Render(rows);

        return File(System.Text.Encoding.UTF8.GetBytes(csv), "text/csv",
            $"netrisk-governance-evidence-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}.csv");
    }
}

using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Reports;
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
    IAuditTrailService auditTrail,
    IReportsService reports)
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
    /// The evidence pack as a file (8.4.2, with the campaign evidence of 8.6.5).
    ///
    /// <c>format=csv</c> renders it here — a flat multi-section export an auditor opens in a
    /// spreadsheet. <c>format=pdf</c> goes through the 2.1 reporting engine, which stores it as an
    /// <c>NrFile</c> and lists it with every other report, so a quarterly evidence pack is
    /// schedulable and "we produced this on the 3rd" becomes a record rather than a claim.
    ///
    /// Both render the same <c>GovernanceEvidencePack</c>, so the two formats cannot describe
    /// different evidence.
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

        var normalized = (format ?? "csv").Trim().ToLowerInvariant();

        if (normalized is not ("csv" or "pdf"))
            return BadRequest(new
            {
                error = "unsupported_format",
                message = $"'{format}' is not a supported evidence format.",
                supported = new[] { "csv", "pdf" }
            });

        var requester = $"{user.Name} ({user.Login}, #{user.Value})";

        var pack = await auditTrail.GetEvidencePackAsync(entityId, fromUtc, toUtc, requester);

        Logger.Information(
            "User:{User} exported the governance evidence pack for entity {Entity} from " +
            "{From:yyyy-MM-dd} to {To:yyyy-MM-dd} as {Format}: {Acceptances} acceptance(s), " +
            "{Reviews} review(s), {Decisions} business decision(s), {Changes} change(s)",
            user.Value, entityId?.ToString() ?? "(all)", fromUtc, toUtc, normalized,
            pack.Acceptances.Count, pack.Reviews.Count, pack.CampaignDecisions.Count,
            pack.Changes.Count);

        var stem = $"netrisk-governance-evidence-{fromUtc:yyyyMMdd}-{toUtc:yyyyMMdd}";

        if (normalized == "csv")
            return File(System.Text.Encoding.UTF8.GetBytes(GovernanceEvidenceCsv.Render(pack)),
                "text/csv", stem + ".csv");

        // The engine owns the PDF: it stores the artifact, so the export is itself recorded.
        var report = await reports.CreateAsync(new Report
        {
            Name = stem,
            Type = ReportParameters.GovernanceEvidenceReportType,
            Parameters = JsonSerializer.Serialize(new ReportParameters
            {
                ReportType = ReportParameters.GovernanceEvidenceReportType,
                EntityId = entityId,
                PeriodStart = fromUtc,
                PeriodEnd = toUtc
            }),
            CreationDate = DateTime.UtcNow,
            CreatorId = user.Value
        }, user);

        return Ok(report);
    }
}

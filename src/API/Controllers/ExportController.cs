using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ServerServices.Interfaces;
using ServerServices.Services;
using Sieve.Models;
using Sieve.Services;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class ExportController(
    IExportService exportService,
    ISieveProcessor sieveProcessor,
    IDalService dalService,
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private IExportService ExportService { get; } = exportService;
    private ISieveProcessor SieveProcessor { get; } = sieveProcessor;
    private IDalService DalService { get; } = dalService;

    [HttpGet]
    [Route("{format}")]
    public async Task<IActionResult> Export(
        [FromRoute] string format, 
        [FromQuery] string entityType, 
        [FromQuery] SieveModel sieveModel, 
        [FromQuery] string reportTitle = "Export")
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} requested export for entityType '{EntityType}' in format '{Format}' with title '{Title}'", 
            user.Value, entityType, format, reportTitle);

        if (!Enum.TryParse<ExportFormat>(format, true, out var exportFormat))
        {
            Logger.Warning("User requested unsupported format: {Format}", format);
            return BadRequest($"Unsupported format: {format}");
        }

        try
        {
            byte[] fileContents;
            using var dbContext = DalService.GetContext();

            switch (entityType.ToLowerInvariant())
            {
                case "risk":
                    var risksQuery = dbContext.Risks.AsNoTracking().AsQueryable();
                    var filteredRisks = SieveProcessor.Apply(sieveModel, risksQuery, applyPagination: false);
                    var risks = await filteredRisks.ToListAsync();
                    fileContents = await ExportService.ExportAsync(risks, exportFormat, reportTitle);
                    break;

                case "vulnerability":
                    var vulnsQuery = dbContext.Vulnerabilities.AsNoTracking().AsQueryable();
                    var filteredVulns = SieveProcessor.Apply(sieveModel, vulnsQuery, applyPagination: false);
                    var vulns = await filteredVulns.ToListAsync();
                    fileContents = await ExportService.ExportAsync(vulns, exportFormat, reportTitle);
                    break;

                case "host":
                    var hostsQuery = dbContext.Hosts.AsNoTracking().AsQueryable();
                    var filteredHosts = SieveProcessor.Apply(sieveModel, hostsQuery, applyPagination: false);
                    var hosts = await filteredHosts.ToListAsync();
                    fileContents = await ExportService.ExportAsync(hosts, exportFormat, reportTitle);
                    break;

                case "incident":
                    var incidentsQuery = dbContext.Incidents.AsNoTracking().AsQueryable();
                    var filteredIncidents = SieveProcessor.Apply(sieveModel, incidentsQuery, applyPagination: false);
                    var incidents = await filteredIncidents.ToListAsync();
                    fileContents = await ExportService.ExportAsync(incidents, exportFormat, reportTitle);
                    break;

                default:
                    Logger.Warning("Unsupported entity type: {EntityType}", entityType);
                    return BadRequest($"Unsupported entity type for export: {entityType}");
            }

            var contentType = exportFormat switch
            {
                ExportFormat.Csv => "text/csv",
                ExportFormat.Xlsx => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                ExportFormat.Pdf => "application/pdf",
                _ => "application/octet-stream"
            };

            var fileExtension = exportFormat switch
            {
                ExportFormat.Csv => ".csv",
                ExportFormat.Xlsx => ".xlsx",
                ExportFormat.Pdf => ".pdf",
                _ => ""
            };

            var safeTitle = reportTitle.Replace("\"", "'");
            var filename = $"{safeTitle}{fileExtension}";

            return File(fileContents, contentType, filename);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error executing export for entityType '{EntityType}'", entityType);
            return StatusCode(500, "Internal server error occurred during export");
        }
    }
}

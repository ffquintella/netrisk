using System;
using System.IO;
using System.Threading.Tasks;
using DAL.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Model.Assessments;
using ServerServices.Interfaces;
using ILogger = Serilog.ILogger;

namespace API.Controllers;

[Authorize(Policy = "RequireValidUser")]
[ApiController]
[Route("[controller]")]
public class ImportsController(
    IImportsService importsService,
    ILogger logger,
    IHttpContextAccessor httpContextAccessor,
    IUsersService usersService)
    : ApiBaseController(logger, httpContextAccessor, usersService)
{
    private IImportsService ImportsService { get; } = importsService;

    [HttpPost]
    [Route("assessment")]
    [ProducesResponseType(StatusCodes.Status201Created, Type = typeof(Assessment))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<Assessment>> ImportAssessment([FromQuery] string? assessmentName, IFormFile file)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} requested importing GRC Assessment template from file '{FileName}'", user.Value, file?.FileName);

        if (file == null || file.Length == 0)
        {
            return BadRequest("File is empty or not provided");
        }

        var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();

        try
        {
            Assessment assessment;

            if (fileExtension == ".json")
            {
                using var reader = new StreamReader(file.OpenReadStream());
                var jsonContent = await reader.ReadToEndAsync();
                assessment = await ImportsService.ImportAssessmentFromJsonAsync(jsonContent);
            }
            else if (fileExtension == ".xlsx")
            {
                var name = string.IsNullOrEmpty(assessmentName) ? Path.GetFileNameWithoutExtension(file.FileName) : assessmentName;
                using var stream = file.OpenReadStream();
                assessment = await ImportsService.ImportAssessmentFromExcelAsync(stream, name);
            }
            else
            {
                return BadRequest($"Unsupported file format: {fileExtension}. Please provide .json or .xlsx templates.");
            }

            return Created($"Assessments/{assessment.Id}", assessment);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error occurred importing GRC Assessment template");
            return StatusCode(500, $"Internal server error during import: {ex.Message}");
        }
    }

    /// <summary>
    /// Dry-run validation of an assessment template. Returns a summary (pages, questions,
    /// warnings, row-level errors) without importing anything. An invalid file returns a
    /// preview with <c>Valid = false</c> and the reasons in <c>Errors</c>.
    /// </summary>
    [HttpPost]
    [Route("assessment/preview")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(AssessmentImportPreview))]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<AssessmentImportPreview>> PreviewAssessment([FromQuery] string? assessmentName, IFormFile file)
    {
        var user = GetUser();
        Logger.Information("User:{UserValue} requested dry-run validation of GRC Assessment template from file '{FileName}'", user.Value, file?.FileName);

        if (file == null || file.Length == 0)
        {
            return BadRequest("File is empty or not provided");
        }

        var fileExtension = Path.GetExtension(file.FileName).ToLowerInvariant();

        try
        {
            AssessmentImportPreview preview;

            if (fileExtension == ".json")
            {
                using var reader = new StreamReader(file.OpenReadStream());
                var jsonContent = await reader.ReadToEndAsync();
                preview = await ImportsService.PreviewAssessmentFromJsonAsync(jsonContent);
            }
            else if (fileExtension == ".xlsx")
            {
                var name = string.IsNullOrEmpty(assessmentName) ? Path.GetFileNameWithoutExtension(file.FileName) : assessmentName;
                using var stream = file.OpenReadStream();
                preview = await ImportsService.PreviewAssessmentFromExcelAsync(stream, name);
            }
            else
            {
                return BadRequest($"Unsupported file format: {fileExtension}. Please provide .json or .xlsx templates.");
            }

            return Ok(preview);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error occurred validating GRC Assessment template");
            return StatusCode(500, $"Internal server error during validation: {ex.Message}");
        }
    }
}

using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.DI;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// The auditor evidence export (Track 8 milestones 8.4.2 and 8.6.5).
///
/// Two things are worth testing at this layer, and neither is the happy path. First, the CSV has to
/// survive being opened in a spreadsheet: a business justification is free text written by a person
/// under time pressure, so commas, quotes, newlines and leading <c>=</c> are the normal case. Second,
/// the endpoint has to refuse a format it cannot produce rather than silently returning CSV — an
/// auditor who asked for PDF and received CSV named ".pdf" has been handed a broken file.
/// </summary>
[TestSubject(typeof(GovernanceEvidenceCsv))]
public class GovernanceEvidenceExportTest : BaseControllerTest
{
    private readonly AuditTrailController _controller;

    public GovernanceEvidenceExportTest()
    {
        _controller = _serviceProvider.GetRequiredService<AuditTrailController>();
    }

    private async Task<string> ExportCsvAsync(int? entityId = null)
    {
        var result = await _controller.GetEvidenceReport(entityId,
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc));

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal("text/csv", file.ContentType);

        return Encoding.UTF8.GetString(file.FileContents);
    }

    [Fact]
    public async Task TestTheCsvExportCarriesAllFourEvidenceSections()
    {
        var csv = await ExportCsvAsync();

        Assert.Contains("section,key,value", csv);
        Assert.Contains("# risk_acceptances", csv);
        Assert.Contains("# mgmt_reviews", csv);
        Assert.Contains("# business_review_decisions", csv);
        Assert.Contains("# field_changes", csv);
    }

    /// <summary>
    /// The scope block. A file that does not state its own period cannot be filed against a finding,
    /// and a reader cannot tell an empty section from a section that was never queried.
    /// </summary>
    [Fact]
    public async Task TestTheScopeBlockStatesThePeriodAndTheRequester()
    {
        var csv = await ExportCsvAsync();

        Assert.Contains("scope,period_from_utc,2026-04-01T00:00:00.0000000Z", csv);
        Assert.Contains("scope,period_to_utc,2026-06-30T00:00:00.0000000Z", csv);
        Assert.Contains("scope,changes_truncated,false", csv);
        Assert.Contains("scope,requested_by,", csv);
    }

    /// <summary>
    /// CSV injection. A justification beginning with <c>=</c> is executed as a formula by Excel and
    /// Google Sheets on open, and the evidence pack is precisely a file that gets emailed and opened
    /// in a spreadsheet.
    /// </summary>
    [Fact]
    public async Task TestAJustificationBeginningWithAFormulaCharacterIsNeutralised()
    {
        var csv = await ExportCsvAsync();

        Assert.Contains("\"\t=SUM(A1:A2)", csv);
        Assert.DoesNotContain(",=SUM(A1:A2)", csv);
    }

    /// <summary>
    /// A newline inside a quoted field is legal RFC 4180 and must stay quoted; an unquoted one would
    /// shift every subsequent column by one for the rest of the file.
    /// </summary>
    [Fact]
    public async Task TestAFieldContainingANewlineStaysQuoted()
    {
        var csv = await ExportCsvAsync();

        var index = csv.IndexOf("\t=SUM", StringComparison.Ordinal);
        Assert.True(index > 0);
        Assert.Equal('"', csv[index - 1]);
    }

    [Fact]
    public async Task TestTheCampaignSectionCarriesTheBusinessRankAndTheLinkedAcceptance()
    {
        var csv = await ExportCsvAsync();

        var line = csv.Split('\n').Single(l => l.StartsWith("1,Q2 2026,", StringComparison.Ordinal));

        Assert.Contains("Accepted", line);
        Assert.Contains("Bob Reviewer", line);
    }

    [Fact]
    public async Task TestTheReviewSectionCarriesTheSegregationOverride()
    {
        var csv = await ExportCsvAsync();

        Assert.Contains("Sole approver on site", csv);
    }

    /// <summary>
    /// A format the endpoint cannot produce is a 400, not a silent fallback. Returning CSV bytes
    /// under a ".pdf" name hands the auditor a file that will not open.
    /// </summary>
    [Theory]
    [InlineData("xlsx")]
    [InlineData("docx")]
    [InlineData("")]
    public async Task TestAnUnsupportedFormatIsRefused(string format)
    {
        var result = await _controller.GetEvidenceReport(null,
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc), format);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Theory]
    [InlineData("csv")]
    [InlineData("CSV")]
    [InlineData(" Csv ")]
    public async Task TestTheFormatNameIsCaseAndWhitespaceInsensitive(string format)
    {
        var result = await _controller.GetEvidenceReport(null,
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc), format);

        Assert.IsType<FileContentResult>(result);
    }

    /// <summary>
    /// The PDF path goes through the 2.1 reporting engine, so what comes back is the stored report
    /// record rather than a byte stream: that is what makes the export itself auditable and
    /// schedulable.
    /// </summary>
    [Fact]
    public async Task TestThePdfFormatGoesThroughTheReportingEngine()
    {
        var result = await _controller.GetEvidenceReport(null,
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc), "pdf");

        var ok = Assert.IsType<OkObjectResult>(result);
        var report = Assert.IsType<DAL.Entities.Report>(ok.Value);

        Assert.Equal(Model.Reports.ReportParameters.GovernanceEvidenceReportType, report.Type);
        Assert.Contains("governance-evidence", report.Name);
        Assert.Contains("\"EntityId\":null", report.Parameters);
    }

    [Fact]
    public async Task TestAnInvertedPeriodIsRefused()
    {
        var result = await _controller.GetEvidenceReport(null,
            new DateTime(2026, 6, 30, 0, 0, 0, DateTimeKind.Utc),
            new DateTime(2026, 4, 1, 0, 0, 0, DateTimeKind.Utc));

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task TestTheEntityNameIsCarriedIntoAScopedExport()
    {
        var csv = await ExportCsvAsync(entityId: 5);

        Assert.Contains("scope,entity_id,5", csv);
        Assert.Contains("scope,entity_name,Retail Bank", csv);
    }
}

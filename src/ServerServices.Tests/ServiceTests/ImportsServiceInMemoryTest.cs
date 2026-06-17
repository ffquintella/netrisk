using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using ServerServices.Interfaces;
using ServerServices.Services;
using ClosedXML.Excel;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

public class ImportsServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IImportsService _svc;
    private readonly IDalService _dalService;

    public ImportsServiceInMemoryTest()
    {
        _svc = GetService<IImportsService>();
        _dalService = GetService<IDalService>();
    }

    [Fact]
    public async Task TestImportAssessmentFromJsonAsync_Success()
    {
        var jsonContent = @"{
            ""Name"": ""NIST SP 800-53 Rev. 5"",
            ""Description"": ""NIST Security and Privacy Controls for Information Systems"",
            ""Questions"": [
                {
                    ""QuestionText"": ""Are automated backup procedures executed weekly?"",
                    ""Order"": 5,
                    ""PageNumber"": 1,
                    ""ConditionJson"": null,
                    ""ExplanationMarkdown"": ""Verify backup schedules and logs.""
                },
                {
                    ""QuestionText"": ""Is multi-factor authentication enforced for all external access?"",
                    ""Order"": 10,
                    ""PageNumber"": 2,
                    ""ConditionJson"": ""{\""QuestionId\"": 1, \""Operator\"": \""equals\"", \""Value\"": \""Yes\""}"",
                    ""ExplanationMarkdown"": ""Check IdP config.""
                }
            ]
        }";

        var assessment = await _svc.ImportAssessmentFromJsonAsync(jsonContent);

        Assert.NotNull(assessment);
        Assert.True(assessment.Id > 0);
        Assert.Equal("NIST SP 800-53 Rev. 5", assessment.Name);

        using var context = _dalService.GetContext();
        var questions = context.AssessmentQuestions.Where(q => q.AssessmentId == assessment.Id).OrderBy(q => q.Order).ToList();
        Assert.Equal(2, questions.Count);

        Assert.Equal("Are automated backup procedures executed weekly?", questions[0].Question);
        Assert.Equal(5, questions[0].Order);
        Assert.Equal(1, questions[0].PageNumber);
        Assert.Null(questions[0].ConditionJson);
        Assert.Equal("Verify backup schedules and logs.", questions[0].ExplanationMarkdown);

        Assert.Equal("Is multi-factor authentication enforced for all external access?", questions[1].Question);
        Assert.Equal(10, questions[1].Order);
        Assert.Equal(2, questions[1].PageNumber);
        Assert.NotNull(questions[1].ConditionJson);
    }

    [Fact]
    public async Task TestImportAssessmentFromExcelAsync_Success()
    {
        // 1. Programmatically compile a valid Excel spreadsheet in memory using ClosedXML
        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Assessment Template");
        worksheet.Cell(1, 1).Value = "Page";
        worksheet.Cell(1, 2).Value = "Question";
        worksheet.Cell(1, 3).Value = "Order";
        worksheet.Cell(1, 4).Value = "Condition";
        worksheet.Cell(1, 5).Value = "Explanation";

        worksheet.Cell(2, 1).Value = 1;
        worksheet.Cell(2, 2).Value = "Are firewalls configured?";
        worksheet.Cell(2, 3).Value = 20;
        worksheet.Cell(2, 4).Value = "";
        worksheet.Cell(2, 5).Value = "Review firewall rules.";

        worksheet.Cell(3, 1).Value = 2;
        worksheet.Cell(3, 2).Value = "Is logging enabled?";
        worksheet.Cell(3, 3).Value = 30;
        worksheet.Cell(3, 4).Value = "{\"QuestionId\": 10, \"Operator\": \"equals\", \"Value\": \"Yes\"}";
        worksheet.Cell(3, 5).Value = "Check syslog endpoint.";

        using var ms = new MemoryStream();
        workbook.SaveAs(ms);
        ms.Position = 0;

        // 2. Import
        var assessment = await _svc.ImportAssessmentFromExcelAsync(ms, "ISO 27001 Checklist");

        Assert.NotNull(assessment);
        Assert.True(assessment.Id > 0);
        Assert.Equal("ISO 27001 Checklist", assessment.Name);

        using var context = _dalService.GetContext();
        var questions = context.AssessmentQuestions.Where(q => q.AssessmentId == assessment.Id).OrderBy(q => q.Order).ToList();
        Assert.Equal(2, questions.Count);

        Assert.Equal("Are firewalls configured?", questions[0].Question);
        Assert.Equal(20, questions[0].Order);
        Assert.Equal(1, questions[0].PageNumber);
        Assert.Null(questions[0].ConditionJson);

        Assert.Equal("Is logging enabled?", questions[1].Question);
        Assert.Equal(30, questions[1].Order);
        Assert.Equal(2, questions[1].PageNumber);
        Assert.NotNull(questions[1].ConditionJson);
    }

    [Fact]
    public async Task TestPreviewAssessmentFromJsonAsync_ValidImportsNothing()
    {
        var jsonContent = @"{
            ""Name"": ""NIST CSF 2.0"",
            ""Questions"": [
                { ""QuestionText"": ""Q1"", ""Order"": 1, ""PageNumber"": 1 },
                { ""QuestionText"": ""Q2"", ""Order"": 2, ""PageNumber"": 2 }
            ]
        }";

        var preview = await _svc.PreviewAssessmentFromJsonAsync(jsonContent);

        Assert.True(preview.Valid);
        Assert.Empty(preview.Errors);
        Assert.Equal("NIST CSF 2.0", preview.Name);
        Assert.Equal(2, preview.QuestionCount);
        Assert.Equal(2, preview.PageCount);

        // Dry-run must not write anything.
        using var context = _dalService.GetContext();
        Assert.Empty(context.Assessments.Where(a => a.Name == "NIST CSF 2.0"));
    }

    [Fact]
    public async Task TestPreviewAssessmentFromJsonAsync_InvalidReportsRowErrorsAndImportsNothing()
    {
        // Missing name + a question with no text => two blocking errors.
        var jsonContent = @"{
            ""Name"": """",
            ""Questions"": [
                { ""QuestionText"": ""Q1"", ""Order"": 1, ""PageNumber"": 1 },
                { ""QuestionText"": """", ""Order"": 2, ""PageNumber"": 1 }
            ]
        }";

        var preview = await _svc.PreviewAssessmentFromJsonAsync(jsonContent);

        Assert.False(preview.Valid);
        Assert.NotEmpty(preview.Errors);
        Assert.Contains(preview.Errors, e => e.Row == 2);      // the empty question row
        Assert.Equal(0, preview.QuestionCount);                 // invalid file advertises nothing

        // A committed import of the same invalid content must throw and persist nothing.
        await Assert.ThrowsAsync<InvalidOperationException>(() => _svc.ImportAssessmentFromJsonAsync(jsonContent));
    }

    [Fact]
    public async Task TestPreviewAssessmentFromJsonAsync_MalformedJsonIsInvalid()
    {
        var preview = await _svc.PreviewAssessmentFromJsonAsync("{ not valid json ");

        Assert.False(preview.Valid);
        Assert.NotEmpty(preview.Errors);
    }
}

using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using JetBrains.Annotations;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.ServiceTests;

[TestSubject(typeof(Services.QuestPdfRenderingService))]
public class QuestPdfRenderingServiceInMemoryTest : InMemoryServiceTestBase
{
    private readonly IQuestPdfRenderingService _svc;

    public QuestPdfRenderingServiceInMemoryTest()
    {
        _svc = GetService<IQuestPdfRenderingService>();
    }

    private List<TestExportDto> GetTestData()
    {
        return new List<TestExportDto>
        {
            new() { Id = 1, Name = "Item One", Score = 8.5, Date = new DateTime(2026, 6, 15), IsActive = true },
            new() { Id = 2, Name = "Item Two", Score = 9.2, Date = new DateTime(2026, 6, 16), IsActive = false }
        };
    }

    [Fact]
    public async Task TestRenderFromTemplateAsync_WithValidJSONs()
    {
        var data = GetTestData();

        var layoutJson = @"{
            ""Sections"": [
                { ""Type"": ""Title"", ""Content"": ""Custom Dynamic Executive Report"" },
                { ""Type"": ""Text"", ""Content"": ""This report presents high-priority GRC statistics and risks compiled dynamically.""},
                { ""Type"": ""Table"", ""TableColumns"": [""Id"", ""Name"", ""Score""] }
            ]
        }";

        var brandingJson = @"{
            ""PrimaryColor"": ""#1ABB9C"",
            ""SecondaryColor"": ""#2A3F54"",
            ""FontName"": ""Arial"",
            ""LogoBase64"": ""iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==""
        }";

        var result = await _svc.RenderFromTemplateAsync(layoutJson, brandingJson, data, "Executive GRC Overview");

        Assert.NotNull(result);
        Assert.True(result.Length > 0);

        // Verify PDF signature (%PDF-)
        var pdfSignature = Encoding.ASCII.GetString(result, 0, 5);
        Assert.Equal("%PDF-", pdfSignature);
    }

    [Fact]
    public async Task TestRenderFromTemplateAsync_GracefulFallbackWithEmptyJSONs()
    {
        var data = GetTestData();

        // Pass nulls to trigger standard graceful fallbacks
        var result = await _svc.RenderFromTemplateAsync(null!, null!, data, "Fallback GRC Overview");

        Assert.NotNull(result);
        Assert.True(result.Length > 0);

        // Verify PDF signature (%PDF-)
        var pdfSignature = Encoding.ASCII.GetString(result, 0, 5);
        Assert.Equal("%PDF-", pdfSignature);
    }
}

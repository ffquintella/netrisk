using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using System.Linq;
using JetBrains.Annotations;
using ServerServices.Tests.Reports;
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

    // A DTO with more than MaxGridColumns (9) primitive properties to exercise the
    // per-record "card" layout that replaces the unreadable wide grid.
    private class WideExportDto
    {
        public int Col1 { get; set; }
        public string Col2 { get; set; } = "a";
        public string Col3 { get; set; } = "b";
        public string Col4 { get; set; } = "c";
        public string Col5 { get; set; } = "d";
        public string Col6 { get; set; } = "e";
        public string Col7 { get; set; } = "f";
        public string Col8 { get; set; } = "g";
        public string Col9 { get; set; } = "h";
        public string Col10 { get; set; } = "i";
        public string ReportedByEntity { get; set; } = "j";
    }

    /// <summary>
    /// A value containing a character no registered font has (an emoji) still exports. QuestPDF
    /// 2026.9 aborts the document on a missing glyph unless told otherwise, so without the setting in
    /// RenderFromTemplateAsync one emoji in one risk subject fails the whole export.
    /// </summary>
    [Fact]
    public async Task TestRenderFromTemplateAsync_TextOutsideTheFontsStillRenders()
    {
        var data = new List<TestExportDto>
        {
            new() { Id = 1, Name = "Patched \u2705 \U0001F44D", Score = 1.0, Date = new DateTime(2026, 6, 15), IsActive = true }
        };

        var result = await _svc.RenderFromTemplateAsync("", "", data, "Glyphs");

        Assert.Equal("%PDF-", Encoding.ASCII.GetString(result, 0, 5));
    }

    [Fact]
    public async Task TestRenderFromTemplateAsync_WideData_UsesCardLayout()
    {
        var data = new List<WideExportDto> { new(), new() };

        // Null layout => default (all columns) => >9 columns => card layout path.
        var result = await _svc.RenderFromTemplateAsync(null!, null!, data, "Wide Export");

        Assert.NotNull(result);
        Assert.True(result.Length > 0);

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

    /// <summary>
    /// A template that names "Arial" renders regular text in a regular face and bold text in
    /// bold, never everything in italic. The only face once registered under the family "Arial" was
    /// Fonts/Arial-BoldItalic.ttf, so the export used to draw every run, regular text included, in Bold Italic.
    /// </summary>
    [Fact]
    public async Task TestRenderFromTemplateAsync_ArialBrandingKeepsRegularBoldAndItalicApart()
    {
        var layoutJson = @"{
            ""Sections"": [
                { ""Type"": ""Title"", ""Content"": ""Bold Title Text"" },
                { ""Type"": ""Text"", ""Content"": ""Plain paragraph text."" }
            ]
        }";
        var brandingJson = @"{ ""FontName"": ""Arial"" }";

        var result = await _svc.RenderFromTemplateAsync(layoutJson, brandingJson, GetTestData(), "Fonts");
        var pdf = PdfText.Read(result);

        Assert.True(pdf.Contains("Plain paragraph text."));
        Assert.DoesNotContain(pdf.Fonts, f => f.Contains("Italic", StringComparison.OrdinalIgnoreCase));
        // The plain paragraph is drawn in a face that is neither bold nor italic ...
        Assert.Contains(pdf.Fonts, f => !f.Contains("Bold", StringComparison.OrdinalIgnoreCase)
                                        && !f.Contains("Italic", StringComparison.OrdinalIgnoreCase));
        // ... and the title and the table header stay bold.
        Assert.Contains(pdf.Fonts, f => f.Contains("Bold", StringComparison.OrdinalIgnoreCase));
    }
}

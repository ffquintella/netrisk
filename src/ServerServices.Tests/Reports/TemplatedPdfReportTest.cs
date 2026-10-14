using System;
using System.IO;
using System.Threading.Tasks;
using DAL.Entities;
using JetBrains.Annotations;
using QuestPDF.Fluent;
using ServerServices.Reports;
using ServerServices.Services;
using ServerServices.Tests.ServiceTests;
using Xunit;

namespace ServerServices.Tests.Reports;

/// <summary>
/// What every hand-written report inherits from <see cref="TemplatedPdfReport"/>: the title block
/// with its logo, the font mapping the previous engine's font resolver did, and the page numbering. Driven
/// through a minimal subclass so a failure points at the base class rather than at a report body.
/// </summary>
[TestSubject(typeof(TemplatedPdfReport))]
public class TemplatedPdfReportTest : InMemoryServiceTestBase
{
    private sealed class OneLineReport(IDalService dalService)
        : TemplatedPdfReport(new Report { Id = 1, Name = "Probe", CreatorId = 1 }, new KeyEchoLocalizer(), dalService)
    {
        protected override void ComposeBody(ColumnDescriptor body) => body.Item().Text("Body line");
    }

    private OneLineReport NewReport() => new(GetService<IDalService>());

    /// <summary>
    /// The previous font resolver's mapping, kept: "Courier New" is the bundled Courier face, and every
    /// other name is Arial Unicode MS — "Arial" because the bundled Arial files are partial, anything
    /// else because there is nothing else bundled to give it.
    /// </summary>
    [Theory]
    [InlineData("Arial", TemplatedPdfReport.UnicodeFontFamily)]
    [InlineData("arial", TemplatedPdfReport.UnicodeFontFamily)]
    [InlineData("Times New Roman", TemplatedPdfReport.UnicodeFontFamily)]
    [InlineData("", TemplatedPdfReport.UnicodeFontFamily)]
    [InlineData("Courier New", TemplatedPdfReport.MonospaceFontFamily)]
    [InlineData("COURIER NEW", TemplatedPdfReport.MonospaceFontFamily)]
    public void TestResolveFontFamilyMapsNamesToTheBundledFaces(string fontName, string expected)
    {
        Assert.Equal(expected, TemplatedPdfReport.ResolveFontFamily(fontName));
    }

    [Fact]
    public async Task TestTheTitleBlockCarriesTheLogoAndTheTitle()
    {
        var pdf = PdfText.Read(await NewReport().GenerateReportAsync("Probe Title"));

        Assert.Equal(1, pdf.Images);
        Assert.True(pdf.Contains("Probe Title"));
        Assert.True(pdf.Contains("Body line"));
        Assert.True(pdf.Contains("1 / 1"));
        Assert.True(pdf.IndexOf("Probe Title") < pdf.IndexOf("Body line"), "the title is not above the body");
    }

    /// <summary>
    /// A missing logo file leaves the logo out and renders the rest. The previous engine drew a
    /// placeholder for a missing image; QuestPDF throws on it, which would fail a report over a decoration.
    /// </summary>
    [Fact]
    public async Task TestAMissingLogoStillRendersTheReport()
    {
        var report = NewReport();
        report.ImagesDirectory = Path.Combine(Path.GetTempPath(), "netrisk-no-such-dir-" + Guid.NewGuid().ToString("N"));

        var data = await report.GenerateReportAsync("No Logo");

        Assert.True(PdfText.IsPdf(data));
        var pdf = PdfText.Read(data);
        Assert.Equal(0, pdf.Images);
        Assert.True(pdf.Contains("No Logo"));
        Assert.True(pdf.Contains("Body line"));
    }

    [Fact]
    public async Task TestCourierNewRendersInTheBundledCourierFace()
    {
        var report = NewReport();
        report.FontName = "Courier New";

        var pdf = PdfText.Read(await report.GenerateReportAsync("Mono"));

        Assert.True(pdf.Contains("Body line"));
        Assert.Contains("CourierNewPSMT", pdf.Fonts);
        Assert.DoesNotContain(pdf.Fonts, font => font.StartsWith("ArialUnicodeMS", StringComparison.Ordinal));
    }
}

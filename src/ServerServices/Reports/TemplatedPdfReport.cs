using System.Reflection;
using DAL.Entities;
using Microsoft.Extensions.Localization;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using Serilog;
using ServerServices.Services;

namespace ServerServices.Reports;

/// <summary>
/// The base of every hand-written PDF report: page setup, the branded title block, the page numbering
/// and the document metadata. A subclass contributes only its body, through <see cref="ComposeBody"/>.
///
/// Rendered with QuestPDF. The fonts are the <c>Arial Unicode MS</c> faces shipped under
/// <c>ServerServices/Fonts</c>, which QuestPDF registers by itself from the application directory
/// (<c>Settings.FontDiscoveryPath</c>), so the file embeds the same glyphs on every host instead of
/// whatever the server happens to have installed.
/// </summary>
public abstract class TemplatedPdfReport(Report report, IStringLocalizer localizer, IDalService dalService)
{

    #region PROPERTIES

    /// <summary>The family name stored in the bundled <c>Fonts/Arial-Unicode-*.ttf</c> faces.</summary>
    internal const string UnicodeFontFamily = "Arial Unicode MS";

    /// <summary>The family name stored in the bundled <c>Fonts/CourierNew.ttf</c>.</summary>
    internal const string MonospaceFontFamily = "Courier New CE";

    private Report Report { get; set; } = report;
    public int TitleFontSize { get; set; } = 14;
    public int FooterFontSize { get; set; } = 9;

    /// <summary>The size of text that sets none of its own.</summary>
    public int DefaultFontSize { get; set; } = 10;
    public string ReportTitle { get; set; } = "";
    public string FontName { get; set; } = "Arial";

    protected IStringLocalizer Localizer { get; } = localizer;

    /// <summary>Where the logo is read from. Internal so a test can point it at a missing directory.</summary>
    internal string ImagesDirectory { get; set; } = Path.Combine(Path.GetDirectoryName(Assembly.GetAssembly(typeof(TemplatedPdfReport))!.Location)!, "Images");

    protected IDalService DalService { get; } = dalService;

    #endregion

    #region METHODS
    public async Task<byte[]> GenerateReportAsync(string title = "")
    {
        // Same as QuestPdfRenderingService: the licence and the missing-glyph policy are set at each
        // entry point that renders, so no outcome depends on which renderer ran first in the process.
        // A character no bundled font has (an emoji in a comment) is drawn as a replacement glyph, as
        // the previous PDF engine did, instead of failing the whole report with a DocumentDrawingException.
        QuestPDF.Settings.License = LicenseType.Community;
        QuestPDF.Settings.ThrowOnMissingTextGlyphs = false;

        ReportTitle = title;

        var metadata = new DocumentMetadata
        {
            Title = title,
            Author = Localizer["Netrisk - Risk Management System"].Value,
            Subject = "Report"
        };

        try
        {
            return await Task.Run(() => Document.Create(Compose).WithMetadata(metadata).GeneratePdf());
        }
        catch (Exception e)
        {
            Log.Error("Error saving report {Message}", e.Message);
            throw;
        }
    }

    /// <summary>
    /// The QuestPDF family a <see cref="FontName"/> renders with. Mirrors the font resolver of the PDF engine
    /// this replaced: "Courier New" is the bundled Courier face, and every other name — "Arial"
    /// included, since only the Arial Unicode MS faces are bundled — is Arial Unicode MS.
    /// </summary>
    internal static string ResolveFontFamily(string fontName) =>
        fontName.Equals("Courier New", StringComparison.OrdinalIgnoreCase)
            ? MonospaceFontFamily
            : UnicodeFontFamily;

    private void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Size(PageSizes.A4);
            page.MarginHorizontal(2.5f, Unit.Centimetre);
            page.MarginTop(2.5f, Unit.Centimetre);
            page.MarginBottom(1.25f, Unit.Centimetre);
            page.DefaultTextStyle(style => style.FontFamily(ResolveFontFamily(FontName)).FontSize(DefaultFontSize));

            page.Content().Column(column =>
            {
                column.Item().Element(ComposeHeader);
                ComposeBody(column);
            });

            page.Footer()
                .PaddingTop(0.5f, Unit.Centimetre)
                .DefaultTextStyle(style => style.FontSize(FooterFontSize))
                .AlignCenter()
                .Text(text =>
                {
                    text.CurrentPageNumber();
                    text.Span(" / ");
                    text.TotalPages();
                });
        });
    }

    /// <summary>
    /// The logo followed by the title, centred, on the first page only. A missing logo file leaves
    /// the logo out rather than failing the report: the image is decoration, the report is not.
    /// </summary>
    private void ComposeHeader(IContainer container)
    {
        var logoPath = Path.Combine(ImagesDirectory, "NetRisk.png");
        var hasLogo = File.Exists(logoPath);

        if (!hasLogo)
            Log.Warning("Report logo not found at {Path}; rendering without it", logoPath);

        container.Text(text =>
        {
            text.AlignCenter();

            if (hasLogo)
            {
                // 200x164 px at 96 dpi scaled to 20 %, the size the logo has always had.
                text.Element(TextInjectedElementAlignment.AboveBaseline).Width(30).Image(logoPath);
                text.Span("   ");
            }

            text.Span(ReportTitle).Bold().FontSize(TitleFontSize);
        });
    }

    /// <summary>
    /// Adds the report's content below the title block. Called once, synchronously, while the PDF is
    /// generated on a worker thread; anything read from the database has to be materialised here.
    /// </summary>
    protected abstract void ComposeBody(ColumnDescriptor body);

    #endregion

}

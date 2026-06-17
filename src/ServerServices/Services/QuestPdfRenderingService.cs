using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Localization;
using ServerServices.Interfaces;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ILogger = Serilog.ILogger;

namespace ServerServices.Services;

// Define C# DTOs to deserialize layout and branding JSONs
public class ReportBranding
{
    public string? LogoBase64 { get; set; }
    public string PrimaryColor { get; set; } = "#2A3F54";
    public string SecondaryColor { get; set; } = "#1ABB9C";
    public string FontName { get; set; } = "Arial";
}

public class ReportLayout
{
    public List<ReportSection> Sections { get; set; } = new();
}

public class ReportSection
{
    public string Type { get; set; } = "Text"; // Text, Title, Table, Header, Footer
    public string? Content { get; set; }
    public List<string>? TableColumns { get; set; }
}

public class QuestPdfRenderingService(ILogger logger, IDalService dalService, ILocalizationService localization)
    : LocalizableService(logger, dalService, localization), IQuestPdfRenderingService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<byte[]> RenderFromTemplateAsync<T>(string layoutJson, string brandingJson, IEnumerable<T> data, string reportTitle)
    {
        Logger.Information("Rendering QuestPDF report with template for type {Type}", typeof(T).Name);

        // Enforce the QuestPDF Community license
        QuestPDF.Settings.License = LicenseType.Community;

        try
        {
            var layout = string.IsNullOrEmpty(layoutJson) 
                ? CreateDefaultLayout() 
                : JsonSerializer.Deserialize<ReportLayout>(layoutJson, JsonOptions) ?? CreateDefaultLayout();

            var branding = string.IsNullOrEmpty(brandingJson) 
                ? new ReportBranding() 
                : JsonSerializer.Deserialize<ReportBranding>(brandingJson, JsonOptions) ?? new ReportBranding();

            var pdfDocument = new DynamicQuestPdfDocument<T>(layout, branding, data, reportTitle);
            
            using var ms = new MemoryStream();
            await Task.Run(() => pdfDocument.GeneratePdf(ms));
            return ms.ToArray();
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error rendering QuestPDF from template");
            throw;
        }
    }

    // A small, generic sample data set so the designer preview shows a populated,
    // branded document even though no real query has run yet.
    private sealed class PreviewSampleRow
    {
        public int Id { get; init; }
        public string Title { get; init; } = "";
        public string Severity { get; init; } = "";
        public string Status { get; init; } = "";
        public string Owner { get; init; } = "";
        public DateTime CreatedAt { get; init; }
    }

    private static readonly PreviewSampleRow[] PreviewSampleData =
    {
        new() { Id = 1, Title = "Sample risk item", Severity = "High", Status = "Open", Owner = "A. Analyst", CreatedAt = new DateTime(2026, 1, 12) },
        new() { Id = 2, Title = "Outdated dependency", Severity = "Medium", Status = "Mitigating", Owner = "B. Engineer", CreatedAt = new DateTime(2026, 2, 3) },
        new() { Id = 3, Title = "Exposed admin port", Severity = "Critical", Status = "Open", Owner = "C. SecOps", CreatedAt = new DateTime(2026, 3, 21) },
        new() { Id = 4, Title = "Missing MFA on VPN", Severity = "High", Status = "Reviewing", Owner = "D. IT", CreatedAt = new DateTime(2026, 4, 8) },
    };

    public Task<byte[]> RenderPreviewImageAsync(string layoutJson, string brandingJson, string reportTitle)
    {
        Logger.Information("Rendering QuestPDF template preview image");

        QuestPDF.Settings.License = LicenseType.Community;

        try
        {
            var layout = string.IsNullOrEmpty(layoutJson)
                ? CreateDefaultLayout()
                : JsonSerializer.Deserialize<ReportLayout>(layoutJson, JsonOptions) ?? CreateDefaultLayout();

            var branding = string.IsNullOrEmpty(brandingJson)
                ? new ReportBranding()
                : JsonSerializer.Deserialize<ReportBranding>(brandingJson, JsonOptions) ?? new ReportBranding();

            var pdfDocument = new DynamicQuestPdfDocument<PreviewSampleRow>(
                layout, branding, PreviewSampleData,
                string.IsNullOrWhiteSpace(reportTitle) ? "Report Preview" : reportTitle);

            // Render only the first page as a PNG for the live preview pane.
            var firstPage = pdfDocument
                .GenerateImages(new ImageGenerationSettings { ImageFormat = ImageFormat.Png, RasterDpi = 144 })
                .FirstOrDefault();

            return Task.FromResult(firstPage ?? Array.Empty<byte>());
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error rendering QuestPDF template preview");
            throw;
        }
    }

    private ReportLayout CreateDefaultLayout()
    {
        return new ReportLayout
        {
            Sections = new List<ReportSection>
            {
                new() { Type = "Title", Content = "Default Report" },
                new() { Type = "Text", Content = "This report was generated with the default layout template." },
                new() { Type = "Table" }
            }
        };
    }
}

// Fluent Document implementation
public class DynamicQuestPdfDocument<T>(ReportLayout layout, ReportBranding branding, IEnumerable<T> data, string reportTitle) : IDocument
{
    public DocumentMetadata GetMetadata() => DocumentMetadata.Default;
    public DocumentSettings GetSettings() => DocumentSettings.Default;

    public void Compose(IDocumentContainer container)
    {
        var primaryColor = Color.FromHex(branding.PrimaryColor);

        container
            .Page(page =>
            {
                // Landscape gives wide, column-heavy exports far more horizontal room.
                page.Size(PageSizes.A4.Landscape());
                page.Margin(1.2f, Unit.Centimetre);
                page.PageColor(Colors.White);
                
                // Set default style & fallback fonts (compact, to fit wide exports)
                page.DefaultTextStyle(x => x.FontFamily(branding.FontName).FontSize(8));

                // Header
                page.Header().Element(ComposeHeader);

                // Content
                page.Content().Element(ComposeContent);

                // Footer
                page.Footer().Element(ComposeFooter);
            });
    }

    private void ComposeHeader(IContainer container)
    {
        var primaryColor = Color.FromHex(branding.PrimaryColor);

        container.Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text(reportTitle).FontSize(20).Bold().FontColor(primaryColor);
                column.Item().Text($"Generated on {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC").FontSize(8).FontColor(Colors.Grey.Medium);
            });

            if (!string.IsNullOrEmpty(branding.LogoBase64))
            {
                try
                {
                    var logoBytes = Convert.FromBase64String(branding.LogoBase64);
                    row.ConstantItem(60).Height(40).Image(logoBytes).FitHeight();
                }
                catch
                {
                    // Fail gracefully on corrupted base64 strings
                }
            }
        });
    }

    private void ComposeContent(IContainer container)
    {
        var primaryColor = Color.FromHex(branding.PrimaryColor);

        container.PaddingVertical(0.8f, Unit.Centimetre).Column(column =>
        {
            column.Spacing(12);

            foreach (var section in layout.Sections)
            {
                switch (section.Type.ToLowerInvariant())
                {
                    case "title":
                        column.Item().Text(section.Content ?? "").FontSize(14).Bold().FontColor(primaryColor);
                        break;

                    case "text":
                        column.Item().Text(section.Content ?? "").FontSize(10).LineHeight(1.4f);
                        break;

                    case "table":
                        column.Item().Element(c => ComposeTable(c, section));
                        break;
                }
            }
        });
    }

    // Beyond this many columns, a side-by-side grid becomes unreadable on a single
    // page, so we switch to a per-record "card" layout instead.
    private const int MaxGridColumns = 9;

    private void ComposeTable(IContainer container, ReportSection section)
    {
        var properties = typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => !(p.PropertyType.IsClass && p.PropertyType != typeof(string)) &&
                        !(p.PropertyType.IsGenericType && typeof(System.Collections.IEnumerable).IsAssignableFrom(p.PropertyType)))
            .ToList();

        var columns = section.TableColumns ?? new List<string>();
        if (columns.Count == 0)
        {
            // Auto-discover primitive properties if none defined
            columns = properties.Select(p => p.Name).ToList();
        }

        if (columns.Count > MaxGridColumns)
            ComposeRecordCards(container, properties, columns);
        else
            ComposeGridTable(container, properties, columns);
    }

    private void ComposeGridTable(IContainer container, List<PropertyInfo> properties, List<string> columns)
    {
        var primaryColor = Color.FromHex(branding.PrimaryColor);

        container.Table(table =>
        {
            // Define layout columns
            table.ColumnsDefinition(columnsDefinition =>
            {
                foreach (var _ in columns)
                {
                    columnsDefinition.RelativeColumn();
                }
            });

            // Table Header row (repeats on every page)
            table.Header(header =>
            {
                foreach (var colName in columns)
                {
                    header.Cell().Background(primaryColor).Padding(3).Text(Humanize(colName)).FontColor(Colors.White).Bold().FontSize(7);
                }
            });

            // Table Data rows
            foreach (var item in data)
            {
                foreach (var colName in columns)
                {
                    var val = GetValue(properties, item, colName);

                    table.Cell()
                        .BorderBottom(0.5f, Unit.Point)
                        .BorderColor(Colors.Grey.Lighten2)
                        .Padding(3)
                        .Text(val)
                        .FontSize(7);
                }
            }
        });
    }

    // Renders each record as a self-contained card with a two-pair-per-row
    // label/value grid. Stays readable no matter how many columns there are.
    private void ComposeRecordCards(IContainer container, List<PropertyInfo> properties, List<string> columns)
    {
        var primaryColor = Color.FromHex(branding.PrimaryColor);
        var index = 0;

        container.Column(column =>
        {
            column.Spacing(8);

            foreach (var item in data)
            {
                index++;
                var capturedIndex = index;

                column.Item()
                    .Border(0.75f, Unit.Point)
                    .BorderColor(Colors.Grey.Lighten1)
                    .Column(card =>
                    {
                        card.Item().Background(primaryColor).Padding(4)
                            .Text($"#{capturedIndex}").FontColor(Colors.White).Bold().FontSize(8);

                        card.Item().Padding(5).Table(fields =>
                        {
                            // Two label/value pairs per row: [label | value | label | value]
                            fields.ColumnsDefinition(c =>
                            {
                                c.ConstantColumn(110);
                                c.RelativeColumn();
                                c.ConstantColumn(110);
                                c.RelativeColumn();
                            });

                            foreach (var colName in columns)
                            {
                                var val = GetValue(properties, item, colName);

                                fields.Cell().PaddingVertical(2).PaddingRight(4)
                                    .Text(Humanize(colName)).Bold().FontSize(7).FontColor(primaryColor);
                                fields.Cell().PaddingVertical(2).PaddingRight(8)
                                    .Text(val).FontSize(7);
                            }
                        });
                    });
            }
        });
    }

    private static string GetValue(List<PropertyInfo> properties, T item, string colName)
    {
        var prop = properties.FirstOrDefault(p => p.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));
        return prop?.GetValue(item)?.ToString() ?? "";
    }

    // Turns a PascalCase property name into spaced words ("ReportedByEntity" ->
    // "Reported By Entity") so headers wrap on word boundaries instead of per-character.
    private static string Humanize(string name)
    {
        if (string.IsNullOrEmpty(name)) return name;

        var sb = new System.Text.StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (i > 0 && char.IsUpper(c) && (!char.IsUpper(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1]))))
                sb.Append(' ');
            sb.Append(c);
        }
        return sb.ToString();
    }

    private void ComposeFooter(IContainer container)
    {
        container.BorderTop(0.5f, Unit.Point).BorderColor(Colors.Grey.Lighten2).PaddingTop(5).Row(row =>
        {
            row.RelativeItem().Text("NetRisk GRC Platform").FontSize(8).FontColor(Colors.Grey.Darken1);
            row.RelativeItem().AlignRight().Text(x =>
            {
                x.Span("Page ").FontSize(8).FontColor(Colors.Grey.Darken1);
                x.CurrentPageNumber().FontSize(8).FontColor(Colors.Grey.Darken1);
                x.Span(" of ").FontSize(8).FontColor(Colors.Grey.Darken1);
                x.TotalPages().FontSize(8).FontColor(Colors.Grey.Darken1);
            });
        });
    }
}

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
                page.Size(PageSizes.A4);
                page.Margin(1.5f, Unit.Centimetre);
                page.PageColor(Colors.White);
                
                // Set default style & fallback fonts
                page.DefaultTextStyle(x => x.FontFamily(branding.FontName).FontSize(10));

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

    private void ComposeTable(IContainer container, ReportSection section)
    {
        var primaryColor = Color.FromHex(branding.PrimaryColor);

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

            // Table Header row
            table.Header(header =>
            {
                foreach (var colName in columns)
                {
                    header.Cell().Background(primaryColor).Padding(5).Text(colName).FontColor(Colors.White).Bold().FontSize(9);
                }
            });

            // Table Data rows
            foreach (var item in data)
            {
                foreach (var colName in columns)
                {
                    var prop = properties.FirstOrDefault(p => p.Name.Equals(colName, StringComparison.OrdinalIgnoreCase));
                    var val = prop?.GetValue(item)?.ToString() ?? "";
                    
                    table.Cell()
                        .BorderBottom(0.5f, Unit.Point)
                        .BorderColor(Colors.Grey.Lighten2)
                        .Padding(4)
                        .Text(val)
                        .FontSize(9);
                }
            }
        });
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

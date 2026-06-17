using System.Collections.Generic;

namespace GUIClient.Models.Reports;

/// <summary>
/// Built-in starter templates offered in the designer's "New from preset" picker.
/// These are pure client-side seeds: applying one populates the section list and
/// branding, which are then saved through the normal create/update path.
/// </summary>
public class ReportTemplatePreset
{
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public ReportLayoutDto Layout { get; set; } = new();
    public ReportBrandingDto Branding { get; set; } = new();

    public override string ToString() => Name;
}

public static class ReportTemplatePresets
{
    public static IReadOnlyList<ReportTemplatePreset> All { get; } = new List<ReportTemplatePreset>
    {
        new()
        {
            Name = "Executive Risk Summary",
            Description = "High-level risk posture overview for leadership.",
            Branding = new ReportBrandingDto { PrimaryColor = "#2A3F54", SecondaryColor = "#1ABB9C", FontName = "Arial" },
            Layout = new ReportLayoutDto
            {
                Sections = new List<ReportSectionDto>
                {
                    new() { Type = "Title", Content = "Executive Risk Summary" },
                    new() { Type = "Text", Content = "This report summarizes the organization's current risk posture for executive review." },
                    new() { Type = "Title", Content = "Risk Register" },
                    new() { Type = "Table" }
                }
            }
        },
        new()
        {
            Name = "Vulnerability Posture",
            Description = "Open vulnerabilities by severity and host.",
            Branding = new ReportBrandingDto { PrimaryColor = "#C0392B", SecondaryColor = "#E67E22", FontName = "Arial" },
            Layout = new ReportLayoutDto
            {
                Sections = new List<ReportSectionDto>
                {
                    new() { Type = "Title", Content = "Vulnerability Posture" },
                    new() { Type = "Text", Content = "Current open vulnerabilities, prioritized by severity." },
                    new() { Type = "Table" }
                }
            }
        },
        new()
        {
            Name = "Incident Review",
            Description = "Open and recent incidents with status.",
            Branding = new ReportBrandingDto { PrimaryColor = "#34495E", SecondaryColor = "#2980B9", FontName = "Arial" },
            Layout = new ReportLayoutDto
            {
                Sections = new List<ReportSectionDto>
                {
                    new() { Type = "Title", Content = "Incident Review" },
                    new() { Type = "Text", Content = "Summary of open and recently closed incidents." },
                    new() { Type = "Table" }
                }
            }
        }
    };
}

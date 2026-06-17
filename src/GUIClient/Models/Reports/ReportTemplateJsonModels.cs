using System.Collections.Generic;

namespace GUIClient.Models.Reports;

/// <summary>
/// Client-side mirrors of the backend's report layout/branding JSON contract
/// (see <c>ServerServices.Services.ReportLayout / ReportSection / ReportBranding</c>).
/// Kept deliberately small so the designer can (de)serialize the same documents the
/// renderer consumes, without sharing the server assembly.
/// </summary>
public class ReportLayoutDto
{
    public List<ReportSectionDto> Sections { get; set; } = new();
}

public class ReportSectionDto
{
    public string Type { get; set; } = "Text";
    public string? Content { get; set; }
    public List<string>? TableColumns { get; set; }
}

public class ReportBrandingDto
{
    public string? LogoBase64 { get; set; }
    public string PrimaryColor { get; set; } = "#2A3F54";
    public string SecondaryColor { get; set; } = "#1ABB9C";
    public string FontName { get; set; } = "Arial";
}

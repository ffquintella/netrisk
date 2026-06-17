namespace DAL.EntitiesDto;

public class ReportTemplateUpdateDto
{
    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public string LayoutJson { get; set; } = null!;
    public string BrandingJson { get; set; } = null!;
}

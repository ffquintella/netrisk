using System;

namespace DAL.Entities;

public partial class ReportTemplateVersion
{
    public int Id { get; set; }

    public int TemplateId { get; set; }

    public int Version { get; set; }

    public string LayoutJson { get; set; } = null!;

    public string BrandingJson { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public virtual ReportTemplate Template { get; set; } = null!;
}

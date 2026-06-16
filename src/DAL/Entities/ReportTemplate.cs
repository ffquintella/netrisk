using System;
using System.Collections.Generic;

namespace DAL.Entities;

public partial class ReportTemplate
{
    public int Id { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public int OwnerId { get; set; }

    public DateTime CreatedAt { get; set; }

    public DateTime UpdatedAt { get; set; }

    public virtual User Owner { get; set; } = null!;

    public virtual ICollection<ReportTemplateVersion> Versions { get; set; } = new List<ReportTemplateVersion>();
}

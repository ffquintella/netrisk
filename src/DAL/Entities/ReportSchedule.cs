using System;

namespace DAL.Entities;

public partial class ReportSchedule
{
    public int Id { get; set; }

    public int ReportTemplateVersionId { get; set; }

    public string FrequencyCron { get; set; } = null!;

    public string Timezone { get; set; } = "UTC";

    public string RecipientsJson { get; set; } = null!;

    public bool IsEnabled { get; set; }

    public DateTime? LastRunAt { get; set; }

    public string? LastStatus { get; set; }

    public virtual ReportTemplateVersion ReportTemplateVersion { get; set; } = null!;
}

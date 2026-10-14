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

    /// <summary>
    /// Who created the schedule — whose entity scope, as it stands when the job runs, every read of the report is made
    /// under (T237 review). Stamped by the server, never sent by a client. Null on a schedule created before the column
    /// existed, or whose creator was deleted: such a schedule does not run until an administrator saves it, which makes
    /// it theirs.
    /// </summary>
    public int? CreatedById { get; set; }

    public virtual ReportTemplateVersion ReportTemplateVersion { get; set; } = null!;
}

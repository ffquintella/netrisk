namespace DAL.EntitiesDto
{
    public class ReportScheduleCreateDto
    {
        public int ReportTemplateVersionId { get; set; }
        public string FrequencyCron { get; set; } = null!;
        public string Timezone { get; set; } = "UTC";
        public string RecipientsJson { get; set; } = null!;
        public bool IsEnabled { get; set; }
    }
}

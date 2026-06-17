namespace GUIClient.ViewModels.Dialogs.Results
{
    public class EditReportScheduleDialogResult : DialogResultBase
    {
        public int ReportTemplateVersionId { get; set; }
        public string FrequencyCron { get; set; } = string.Empty;
        public string Timezone { get; set; } = string.Empty;
        public string RecipientsJson { get; set; } = string.Empty;
        public bool IsEnabled { get; set; }
    }
}

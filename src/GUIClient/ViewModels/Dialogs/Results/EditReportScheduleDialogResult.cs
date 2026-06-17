namespace GUIClient.ViewModels.Dialogs.Results
{
    public class EditReportScheduleDialogResult : DialogResultBase
    {
        public int ReportTemplateVersionId { get; set; }
        public string FrequencyCron { get; set; }
        public string Timezone { get; set; }
        public string RecipientsJson { get; set; }
        public bool IsEnabled { get; set; }
    }
}

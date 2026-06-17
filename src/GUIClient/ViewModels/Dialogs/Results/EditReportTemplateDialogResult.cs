namespace GUIClient.ViewModels.Dialogs.Results
{
    public class EditReportTemplateDialogResult : DialogResultBase
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string LayoutJson { get; set; }
        public string BrandingJson { get; set; }
    }
}

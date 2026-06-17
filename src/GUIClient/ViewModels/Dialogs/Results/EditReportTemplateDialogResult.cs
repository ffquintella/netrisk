namespace GUIClient.ViewModels.Dialogs.Results
{
    public class EditReportTemplateDialogResult : DialogResultBase
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public string LayoutJson { get; set; }
        public string BrandingJson { get; set; }

        /// <summary>When true, the manager should create a new template instead of updating the edited one.</summary>
        public bool SaveAsCopy { get; set; }
    }
}

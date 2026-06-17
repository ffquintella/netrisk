namespace GUIClient.ViewModels.Dialogs.Results
{
    public class EditReportTemplateDialogResult : DialogResultBase
    {
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string LayoutJson { get; set; } = string.Empty;
        public string BrandingJson { get; set; } = string.Empty;

        /// <summary>When true, the manager should create a new template instead of updating the edited one.</summary>
        public bool SaveAsCopy { get; set; }
    }
}

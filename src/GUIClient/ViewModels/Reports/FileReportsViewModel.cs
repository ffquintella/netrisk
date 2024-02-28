namespace GUIClient.ViewModels.Reports;

public class FileReportsViewModel: ReportsViewModelBase
{
    #region LANGUAGE
        public string StrFiles { get; } = Localizer["Files"];
        public string StrOperations { get; } = Localizer["Operations"];
    #endregion
}
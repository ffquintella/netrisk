namespace GUIClient.ViewModels.Reports;

public class RiskReviewViewModel: ViewModelBase
{
    #region LANGUAGE
    public string StrFilters { get; }
    

    #endregion

    public RiskReviewViewModel()
    {
        StrFilters = Localizer["Filters"];
    }
}
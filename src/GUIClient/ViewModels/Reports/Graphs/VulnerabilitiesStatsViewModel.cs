using ReactiveUI;

namespace GUIClient.ViewModels.Reports.Graphs;

public class VulnerabilitiesStatsViewModel: GraphsViewModelBase
{
    #region LANGUAGE
        public string StrVulnerabilities => Localizer["Vulnerabilities"];
    #endregion
    
    #region PROPERTIES
        private VulnerabilitiesDistributionViewModel _vulnerabilitiesDistributionViewModel = new();
        public VulnerabilitiesDistributionViewModel VulnerabilitiesDistributionViewModel
        {
            get => _vulnerabilitiesDistributionViewModel;
            set => this.RaiseAndSetIfChanged(ref _vulnerabilitiesDistributionViewModel, value);
        }
    
    #endregion

    #region SERVICES

        

    #endregion
    
    #region CONSTRUCTOR

    public VulnerabilitiesStatsViewModel()
    {
        
    }
    #endregion
    
    #region METHODS
    
    #endregion
    

    
}
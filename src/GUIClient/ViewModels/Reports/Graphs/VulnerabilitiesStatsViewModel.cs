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
        
        private VulnerabilitiesVerifiedViewModel _vulnerabilitiesVerifiedViewModel = new();
        public VulnerabilitiesVerifiedViewModel VulnerabilitiesVerifiedViewModel
        {
            get => _vulnerabilitiesVerifiedViewModel;
            set => this.RaiseAndSetIfChanged(ref _vulnerabilitiesVerifiedViewModel, value);
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
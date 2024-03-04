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
        
        private VulnerabilityNumbersViewModel _vulnerabilityNumbersViewModel = new();
        public VulnerabilityNumbersViewModel VulnerabilityNumbersViewModel
        {
            get => _vulnerabilityNumbersViewModel;
            set => this.RaiseAndSetIfChanged(ref _vulnerabilityNumbersViewModel, value);
        }
        
        private VulnerabilityImportSourcesViewModel _vulnerabilityImportViewModel = new();
        public VulnerabilityImportSourcesViewModel VulnerabilityImportViewModel
        {
            get => _vulnerabilityImportViewModel;
            set => this.RaiseAndSetIfChanged(ref _vulnerabilityImportViewModel, value);
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
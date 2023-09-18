using System.Collections.Generic;
using System.Linq;
using GUIClient.Models;
using GUIClient.ViewModels.Reports;
using Material.Icons;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class ReportsViewModel: ViewModelBase
{
    #region LANGUAGE
        public string StrReports { get; }
    

    #endregion
    
    #region PROPERTIES

    private ReportType? _selectedReport;
    public ReportType? SelectedReport {
        get => _selectedReport;
        set => this.RaiseAndSetIfChanged(ref _selectedReport, value);
    }
    
    private List<ReportType> _reportTypes = new();
    public List<ReportType> ReportTypes {
        get => _reportTypes;
        set => this.RaiseAndSetIfChanged(ref _reportTypes, value);
    }
    
    private RiskReviewViewModel _riskReviewViewModel = new();
    public RiskReviewViewModel RiskReviewViewModel {
        get => _riskReviewViewModel;
        set => this.RaiseAndSetIfChanged(ref _riskReviewViewModel, value);
    }
    
    private RisksVsCostsViewModel _risksVsCostsViewModel = new();
    public RisksVsCostsViewModel RisksVsCostsViewModel {
        get => _risksVsCostsViewModel;
        set => this.RaiseAndSetIfChanged(ref _risksVsCostsViewModel, value);
    }
    
    #endregion

    public ReportsViewModel()
    {
        StrReports = Localizer["Reports"];
        
        ReportTypes.Add(new ReportType(1, Localizer["Risk review by time"], 1, MaterialIconKind.RateReview));
        ReportTypes.Add(new ReportType(2, Localizer["Cost vs Risk"], 2, MaterialIconKind.RateReview));

        ReportTypes = ReportTypes.OrderBy(rt => rt.Order).ToList();
        
        SelectedReport = ReportTypes[0];
    }
    
    #region METHODS

    

    #endregion
}
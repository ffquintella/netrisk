using System.Collections.Generic;
using GUIClient.Models;
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
    
    #endregion

    public ReportsViewModel()
    {
        StrReports = Localizer["Reports"];
        
        ReportTypes.Add(new ReportType(1, Localizer["Risk review by time"], MaterialIconKind.RateReview));
        ReportTypes.Add(new ReportType(1, Localizer["Cost vs Risk"], MaterialIconKind.RateReview));
    }
    
    #region METHODS

    

    #endregion
}
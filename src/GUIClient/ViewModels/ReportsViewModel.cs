using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using Avalonia.Controls;
using GUIClient.Models;
using GUIClient.ViewModels.Reports;
using GUIClient.Views;
using GUIClient.Views.Reports;
using Material.Icons;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

public class ReportsViewModel: ViewModelBase
{
    #region LANGUAGE
    public string StrTitle => Localizer["Reports"];
    public string StrManageTemplates => Localizer["ManageTemplates"];
    public string StrManageSchedules => Localizer["ManageSchedules"];

        public string StrReports { get; }
        
    #endregion
    
    #region COMMANDS
    
    public ReactiveCommand<RxVoid, RxVoid> ManageTemplatesCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> ManageSchedulesCommand { get; }
    
    #endregion
    
    #region PROPERTIES
    
    private bool _loadingSpinner;
    public bool LoadingSpinner {
        get => _loadingSpinner;
        set => this.RaiseAndSetIfChanged(ref _loadingSpinner, value);
    }

    private ReportType? _selectedReport;
    public ReportType? SelectedReport {
        get => _selectedReport;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedReport, value);

            // Loaded on first view rather than when the window opens: it is a server computation
            // over the whole chain, and most visits to Reports are for another report.
            if (value?.Id == CriticalProcessCoverageReportId)
                _ = CriticalProcessCoverageViewModel.EnsureLoadedAsync();
        }
    }

    /// <summary>Stage 9.1 (S41 §7): report 7, critical-process coverage.</summary>
    public const int CriticalProcessCoverageReportId = 7;

    private CriticalProcessCoverageViewModel _criticalProcessCoverageViewModel = new();
    public CriticalProcessCoverageViewModel CriticalProcessCoverageViewModel {
        get => _criticalProcessCoverageViewModel;
        set => this.RaiseAndSetIfChanged(ref _criticalProcessCoverageViewModel, value);
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
    
    private RisksImpactVsProbabilityViewModel _risksImpactVsProbabilityViewModel = new();
    public RisksImpactVsProbabilityViewModel RisksImpactVsProbabilityViewModel {
        get => _risksImpactVsProbabilityViewModel;
        set => this.RaiseAndSetIfChanged(ref _risksImpactVsProbabilityViewModel, value);
    }
    
    private EntitiesRisksViewModel _entitiesRisksViewModel = new();
    public EntitiesRisksViewModel EntitiesRisksViewModel {
        get => _entitiesRisksViewModel;
        set => this.RaiseAndSetIfChanged(ref _entitiesRisksViewModel, value);
    }
    
    private FileReportsViewModel _fileReportsViewModel = new();
    public FileReportsViewModel FileReportsViewModel {
        get => _fileReportsViewModel;
        set => this.RaiseAndSetIfChanged(ref _fileReportsViewModel, value);
    }
    
    private VulnerabilitiesByTimeViewModel _vulnerabilitiesByTimeViewModel = new();
    public VulnerabilitiesByTimeViewModel VulnerabilitiesByTimeViewModel {
        get => _vulnerabilitiesByTimeViewModel;
        set => this.RaiseAndSetIfChanged(ref _vulnerabilitiesByTimeViewModel, value);
    }
    
    
    #endregion

    #region CONSTRUCTOR
    public ReportsViewModel()
    {
        
        StrReports = Localizer["Reports"];
        
        ReportTypes.Add(new ReportType(1, Localizer["Risk review by time"], 1, MaterialIconKind.RateReview));
        ReportTypes.Add(new ReportType(6, Localizer["Vulnerabilities by time"], 2, MaterialIconKind.ShieldAlertOutline));
        ReportTypes.Add(new ReportType(2, Localizer["Cost vs Risk"], 3, MaterialIconKind.RateReview));
        ReportTypes.Add(new ReportType(3, Localizer["Impact vs Probability"], 4, MaterialIconKind.RateReview));
        ReportTypes.Add(new ReportType(4, Localizer["Entities Risks"], 5, MaterialIconKind.RateReview));
        ReportTypes.Add(new ReportType(5, Localizer["File Reports"], 6, MaterialIconKind.FileCabinet));
        ReportTypes.Add(new ReportType(CriticalProcessCoverageReportId, Localizer["Critical process coverage"], 7,
            MaterialIconKind.ShieldCheckOutline));

        ReportTypes = ReportTypes.OrderBy(rt => rt.Order).ToList();
        
        SelectedReport = ReportTypes[0];
        
        VulnerabilitiesByTimeViewModel.Parent = this;
        
        ManageTemplatesCommand = ReactiveCommand.Create(ExecuteManageTemplates);
        ManageSchedulesCommand = ReactiveCommand.Create(ExecuteManageSchedules);
    }
    #endregion
    
    #region METHODS

    private void ExecuteManageTemplates()
    {
        var templateManagerWindow = new ReportTemplateManagerWindow()
        {
            DataContext = new ReportTemplateManagerViewModel(),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        templateManagerWindow.Show();
    }

    private void ExecuteManageSchedules()
    {
        var scheduleManagerWindow = new ReportScheduleManagerWindow()
        {
            DataContext = new ReportScheduleManagerViewModel(),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        scheduleManagerWindow.Show();
    }

    #endregion
}
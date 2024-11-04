using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools;
using GUIClient.ViewModels.Reports.Graphs;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using ReactiveUI;
using SkiaSharp;

namespace GUIClient.ViewModels;

public class DashboardViewModel : ViewModelBase
{

    #region LANGUAGE
    private string StrWelcome { get; }
    private string StrRisksOverTime { get; }
    private string StrControlStatistics { get; }
    private string StrControlRisk { get; }
    
    private string StrRiskPanel { get; }
    
    #endregion
    
    #region PROPERTIES
    private IStatisticsService _statisticsService;
    
    private bool _initialized = false;
    private Timer? _updateTimer;

    private ObservableCollection<ISeries> _risksOverTime = new ObservableCollection<ISeries>();
    private List<Axis> _risksOverTimeXAxis;


    private string? _lastUpdated;
    private string? LastUpdated
    {
        get => _lastUpdated;
        set => this.RaiseAndSetIfChanged(ref _lastUpdated, value); }
    
    private VulnerabilitiesStatsViewModel _vulnerabilitiesStatsViewModel = new VulnerabilitiesStatsViewModel(); 
    public VulnerabilitiesStatsViewModel VulnerabilitiesStatsViewModel
    {
        get => _vulnerabilitiesStatsViewModel;
        set => this.RaiseAndSetIfChanged(ref _vulnerabilitiesStatsViewModel, value);
    }
    
    private RisksStatsViewModel _risksStatsViewModel = new RisksStatsViewModel();
    public RisksStatsViewModel RisksStatsViewModel
    {
        get => _risksStatsViewModel;
        set => this.RaiseAndSetIfChanged(ref _risksStatsViewModel, value);
    }
    
    private ObservableCollection<ISeries> RisksOverTime
    {
        get => _risksOverTime;
        set => this.RaiseAndSetIfChanged(ref _risksOverTime, value);
    }
    
    private List<Axis> RisksOverTimeXAxis
    {
        get => _risksOverTimeXAxis;
        set => this.RaiseAndSetIfChanged(ref _risksOverTimeXAxis, value);
    }

    private ObservableCollection<ISeries>? _frameworkControls;
    private ObservableCollection<ISeries>? FrameworkControls
    {
        get => _frameworkControls; 
        set => this.RaiseAndSetIfChanged(ref _frameworkControls, value); 
    }
    
    private List<Axis> _frameworkControlsXAxis;
    
    private List<Axis> FrameworkControlsXAxis
    {
        get => _frameworkControlsXAxis;
        set => this.RaiseAndSetIfChanged(ref _frameworkControlsXAxis, value);
    }
    
    private ObservableCollection<ISeries>? _controlRisks;
    private ObservableCollection<ISeries>? ControlRisks
    {
        get => _controlRisks; 
        set => this.RaiseAndSetIfChanged(ref _controlRisks, value); 
    }
    
    public RisksPanelViewModel RisksPanelViewModel { get; set;  }
    
    #endregion
    
    #region CONSTRUCTOR
    public DashboardViewModel()
    {
        _statisticsService = GetService<IStatisticsService>();
        
        _risksOverTimeXAxis = new List<Axis>();

        _frameworkControlsXAxis = new List<Axis>
        {
            new Axis
            {
                Labels = new List<string>(),
                TextSize = 9,
                LabelsRotation = 0,
            }
        };
        
        _frameworkControls = new ObservableCollection<ISeries>
        {
            new StackedColumnSeries<int>
            {
                Values = new List<int>(),
                Name = Localizer["Maturity"],
                Stroke = null,
                DataLabelsPaint = new SolidColorPaint(new SKColor(45, 45, 45)),
                DataLabelsSize = 14,
                DataLabelsPosition = DataLabelsPosition.Middle
            },
            new StackedColumnSeries<int>
            {
                Values = new List<int>(),
                Name = Localizer["DesiredMaturity"],  
                Stroke = null,
                DataLabelsPaint = new SolidColorPaint(new SKColor(45, 5, 5)),
                DataLabelsSize = 14,
                DataLabelsPosition = DataLabelsPosition.Middle
            },
        };
        
        RisksPanelViewModel = new RisksPanelViewModel();
        
        AuthenticationService.AuthenticationSucceeded += (obj, args) =>
        {
            Initialize();
        };
        
        StrWelcome = Localizer["WelcomeMSG"];
        StrRisksOverTime = Localizer["RisksOverTime"];
        StrControlStatistics = Localizer["ControlStatistics"];
        StrControlRisk = Localizer["ControlRisk"];
        StrRiskPanel = Localizer["StrRiskPanel"]; 
    }
    
    #endregion
    
    #region METHODS

    private async void UpdateData(object? state)
    {
        // This is called here to cause a early load of the data
        var constManager = GetService<ConstantManager>();
        
        LastUpdated = "Dt: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        
        var risksOverTimeValues = await _statisticsService.GetRisksOverTimeAsync();
        var riskDays = risksOverTimeValues.Select(r => r.Day.ToShortDateString()).ToList();
        
        RisksOverTime = new ObservableCollection<ISeries>
        {
            new LineSeries<int>
            {
                Name = "Risks Over Time",
                Values = risksOverTimeValues.Select(rot => rot.TotalRisks).ToList()
            }
        };

        RisksOverTimeXAxis = new List<Axis>
        {
            new Axis
            {
                Labels = riskDays,
                TextSize = 9,
                LabelsRotation = 90,
                MinLimit = 20,
                MaxLimit = riskDays.Count 
                
            }
        };
       
    }
    
    public void Initialize()
    {
        if (!_initialized)
        {
            UpdateData(null);
            _initialized = true;
            
            RisksPanelViewModel.Initialize();
            RisksStatsViewModel.InitializeAsync();
            
            _updateTimer = new Timer(UpdateData, null, TimeSpan.Zero, TimeSpan.FromMinutes(1));
            

        }
    }
    
    #endregion
    
}
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Model.Statistics;
using ReactiveUI;
using SkiaSharp;

namespace GUIClient.ViewModels;

public class DashboardViewModel : ViewModelBase
{

    private IStatisticsService _statisticsService;


    private bool _initialized = false;

    private ObservableCollection<ISeries>? _risksOverTime;
    private List<Axis> _risksOverTimeXAxis;
    private string StrWelcome { get; }
    private string StrRisksOverTime { get; }
    private string StrControlStatistics { get; }
    private string StrControlRisk { get; }
    
    private string StrRiskPanel { get; }

    private string? _lastUpdated;
    private string? LastUpdated
    {
        get => _lastUpdated;
        set => this.RaiseAndSetIfChanged(ref _lastUpdated, value); }
    
    private ObservableCollection<ISeries>? RisksOverTime
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
    //private List<Axis> _frameworkControlsYAxis;
    
    private List<Axis> FrameworkControlsXAxis
    {
        get => _frameworkControlsXAxis;
        set => this.RaiseAndSetIfChanged(ref _frameworkControlsXAxis, value);
    }
    
    /*private List<Axis> FrameworkControlsYAxis
    {
        get => _frameworkControlsYAxis;
        set => this.RaiseAndSetIfChanged(ref _frameworkControlsYAxis, value);
    }*/
    
    private ObservableCollection<ISeries>? _controlRisks;
    private ObservableCollection<ISeries>? ControlRisks
    {
        get => _controlRisks; 
        set => this.RaiseAndSetIfChanged(ref _controlRisks, value); 
    }

    //public RisksPanelViewModel _risksPanelViewModel;
    
    public RisksPanelViewModel RisksPanelViewModel { get; set;  }
    
    public DashboardViewModel()
    {
        _statisticsService = GetService<IStatisticsService>();

        _risksOverTimeXAxis = new List<Axis>();
        //_frameworkControlsXAxis = new List<Axis>();
        //_frameworkControlsYAxis = new List<Axis>();

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

    private void UpdateData()
    {
        LastUpdated = "Dt: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss");
        
        var risksOverTimeValues = _statisticsService.GetRisksOverTime();
        var riskDays = risksOverTimeValues.Select(r => r.Day.ToShortDateString()).ToList();
        
        RisksOverTime = new ObservableCollection<ISeries>
        {
            new LineSeries<int>
            {
                Name = "Risks Over Time",
                Values = risksOverTimeValues.Select(rot => rot.RisksCreated).ToList()
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
        
        // Security Control 
        var securityControlsStatistics = _statisticsService.GetSecurityControlStatistics();

        if (securityControlsStatistics.FameworkStats == null) throw new Exception("Error collecting statistics");
        
        var totalMaturity = securityControlsStatistics.FameworkStats!.Select(s => s.TotalMaturity).ToList();
        var totalDesiredMaturity = securityControlsStatistics.FameworkStats!.Select(s => s.TotalDesiredMaturity - s.TotalMaturity).ToList();
        
        FrameworkControls = new ObservableCollection<ISeries>
        {
            new StackedColumnSeries<int>
            {
                Values = totalMaturity,
                Name = Localizer["Maturity"],
                Stroke = null,
                DataLabelsPaint = new SolidColorPaint(new SKColor(45, 45, 45)),
                DataLabelsSize = 14,
                DataLabelsPosition = DataLabelsPosition.Middle
            },
            new StackedColumnSeries<int>
            {
                Values = totalDesiredMaturity,
                Name = Localizer["DesiredMaturity"],  
                Stroke = null,
                DataLabelsPaint = new SolidColorPaint(new SKColor(45, 5, 5)),
                DataLabelsSize = 14,
                DataLabelsPosition = DataLabelsPosition.Middle
            },
        };
        
        FrameworkControlsXAxis = new List<Axis>
        {
            new Axis
            {
                Labels = securityControlsStatistics.FameworkStats.Select(s => s.Framework).ToList(),
                TextSize = 9,
                LabelsRotation = 0,
           }
        };
        
        var controlRisks = securityControlsStatistics.SecurityControls
            .Select(sc => new {sc.ControlName, sc.TotalRisk}).ToList();

        ControlRisks = new ObservableCollection<ISeries>();

        foreach (var controlRisk in controlRisks)
        {
            ControlRisks.Add(new PieSeries<double>
            {
                Values = new double[] {controlRisk.TotalRisk},
                Name = controlRisk.ControlName,
                DataLabelsPosition = PolarLabelsPosition.Outer,
                DataLabelsFormatter = p => $"{p.PrimaryValue} / {p.StackedValue!.Total} ({p.StackedValue.Share:P2})"
            });
        }
        

        
        /*ControlRisks = controlRisks.AsLiveChartsPieSeries((value, series) =>
        {
            // here you can configure the series assigned to each value.
            series.Name = $"Series for value {value}";
            series.DataLabelsPaint = new SolidColorPaint(new SKColor(30, 30, 30));
            series.DataLabelsPosition = LiveChartsCore.Measure.PolarLabelsPosition.Outer;
            series.DataLabelsFormatter = p => $"{p.PrimaryValue} / {p.StackedValue!.Total} ({p.StackedValue.Share:P2})";
        });*/
    }
    
    public void Initialize()
    {
        if (!_initialized)
        {
            UpdateData();
            _initialized = true;
            
            RisksPanelViewModel.Initialize();

            Task.Run(() =>
            {
                while (true)
                {
                    Thread.Sleep(TimeSpan.FromMinutes(1));
                    UpdateData();
                }
               
                
            });
        }
    }
    
}
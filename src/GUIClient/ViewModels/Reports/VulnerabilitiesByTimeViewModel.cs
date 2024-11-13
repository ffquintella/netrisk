using System.Collections.ObjectModel;
using ReactiveUI;
using System.Reactive;
using ClientServices.Interfaces;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;

namespace GUIClient.ViewModels.Reports;

public class VulnerabilitiesByTimeViewModel: ReportsViewModelBase
{
    #region LANGUAGE

    public string StrDaysSpan => Localizer["Days Span"];
    
    #endregion
    
    #region PROPERTIES

    private decimal _daysSpan = 30; 
    public decimal DaysSpan
    {
        get => _daysSpan;
        set => this.RaiseAndSetIfChanged(ref _daysSpan, value);
    }
    
    private ObservableCollection<ISeries> _series = new();

    public ObservableCollection<ISeries> Series
    {
        get => _series;
        set => this.RaiseAndSetIfChanged(ref _series, value);
    }
    
    private Axis[] _xAxes =
    {
        new Axis{}
    };

    public Axis[] XAxes
    {
        get => _xAxes;
        set => this.RaiseAndSetIfChanged(ref _xAxes, value);
    }

    public Axis[] YAxes { get; set; } =
    {
        new Axis
        {
            Name = Localizer["Number of Vulnerabilities by Severity"],
            NamePaint = new SolidColorPaint(SKColors.White),
            TextSize = 18,
            Padding = new Padding(5, 15, 5, 5),
            LabelsPaint = new SolidColorPaint(SKColors.White),
        }
    };
    
    #endregion
    
    #region FIELDS
        private IStatisticsService _statisticsService = GetService<IStatisticsService>();
    #endregion
    
    #region BUTTONS
    public ReactiveCommand<Unit, Unit> BtGenerateClicked { get; } 
    #endregion
    
    #region CONSTRUCTOR

    public VulnerabilitiesByTimeViewModel()
    {
        BtGenerateClicked = ReactiveCommand.Create(ExecuteGenerate);
    }
    
    #endregion
    
    #region METHODS

    private async void ExecuteGenerate()
    {
        var dataList = await _statisticsService.GetVulnerabilityNumbersByTimeAsync();
    }

    #endregion
}
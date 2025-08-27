using System.Collections.Generic;
using System.Collections.ObjectModel;
using ReactiveUI;
using System.Reactive;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DynamicData;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;
using LiveChartsCore.Painting;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Drawing;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using SkiaSharp;

namespace GUIClient.ViewModels.Reports;

public class VulnerabilitiesByTimeViewModel: ReportsViewModelBase
{
    #region LANGUAGE

    public string StrDaysSpan => Localizer["Days Span"];
    
    public string StrIncludeLevels => Localizer["Include Levels"];
    
    #endregion
    
    #region PROPERTIES

    private ReportsViewModel? _parent;
    public ReportsViewModel? Parent
    {
        get => _parent;
        set => this.RaiseAndSetIfChanged(ref _parent, value);
    }
    
    private bool _includeLevels = false;
    public bool IncludeLevels
    {
        get => _includeLevels;
        set => this.RaiseAndSetIfChanged(ref _includeLevels, value);
    }
    
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
    
    private Paint LegendTextPaint { get; } = new SolidColorPaint(SKColors.Azure);
    
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
        _parent!.LoadingSpinner = true;
        
        await Task.Run(async () =>
            {
                var dataList = await _statisticsService.GetVulnerabilityNumbersByTimeAsync((int)_daysSpan);

                Series.Clear();

                var openValues = new List<int>();
                var closedValues = new List<int>();
                var labels = new List<string>();
                foreach (var dayKp in dataList.Open)
                {
                    openValues.Add(dayKp.Value.Total);
                    labels.Add(dayKp.Key);
                }

                foreach (var dayKp in dataList.Closed)
                {
                    closedValues.Add(dayKp.Value.Total);
                }

                XAxes =
                [
                    new Axis
                    {
                        Labels = labels.ToArray(),
                        LabelsRotation = -45,
                        TextSize = 6
                    }
                ];

                Series.Add(
                    new LineSeries<int>
                    {
                        Name = Localizer["Total - Open"],
                        Values = openValues,
                        Stroke = new SolidColorPaint(SKColors.Red),
                        Fill = new SolidColorPaint(SKColor.Empty),
                        GeometrySize = 15,
                        ScalesXAt = 0,
                        ScalesYAt = 0
                    });

                Series.Add(
                    new LineSeries<int>
                    {
                        Name = Localizer["Total - Closed"],
                        Values = closedValues,
                        Stroke = new SolidColorPaint(SKColors.Green),
                        Fill = new SolidColorPaint(SKColor.Empty),
                        GeometrySize = 15,
                        ScalesXAt = 0,
                        ScalesYAt = 0
                    });

                if (IncludeLevels)
                {
                    var lowValues = new List<int>();
                    var mediumValues = new List<int>();
                    var highValues = new List<int>();
                    var criticalValues = new List<int>();
                    foreach (var dayKp in dataList.Open)
                    {
                        lowValues.Add(dayKp.Value.Low);
                        mediumValues.Add(dayKp.Value.Medium);
                        highValues.Add(dayKp.Value.High);
                        criticalValues.Add(dayKp.Value.Critical);
                    }

                    Series.Add(
                        new LineSeries<int>
                        {
                            Name = Localizer["Low - Open"],
                            Values = lowValues,
                            Stroke = new SolidColorPaint(SKColors.Blue),
                            Fill = new SolidColorPaint(SKColor.Empty),
                            GeometrySize = 15,
                            ScalesXAt = 0,
                            ScalesYAt = 0
                        });

                    Series.Add(
                        new LineSeries<int>
                        {
                            Name = Localizer["Medium - Open"],
                            Values = mediumValues,
                            Stroke = new SolidColorPaint(SKColors.Yellow),
                            Fill = new SolidColorPaint(SKColor.Empty),
                            GeometrySize = 15,
                            ScalesXAt = 0,
                            ScalesYAt = 0
                        });

                    Series.Add(
                        new LineSeries<int>
                        {
                            Name = Localizer["High - Open"],
                            Values = highValues,
                            Stroke = new SolidColorPaint(SKColors.Orange),
                            Fill = new SolidColorPaint(SKColor.Empty),
                            GeometrySize = 15,
                            ScalesXAt = 0,
                            ScalesYAt = 0
                        });

                    Series.Add(
                        new LineSeries<int>
                        {
                            Name = Localizer["Critical - Open"],
                            Values = criticalValues,
                            Stroke = new SolidColorPaint(SKColors.DarkMagenta),
                            Fill = new SolidColorPaint(SKColor.Empty),
                            GeometrySize = 15,
                            ScalesXAt = 0,
                            ScalesYAt = 0
                        });

                }
            }
        ).ContinueWith( _ => { _parent!.LoadingSpinner = false; });
        
        
    }

    #endregion
}
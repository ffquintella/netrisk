using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ClientServices.Interfaces;
using ReactiveUI;
using System.Reactive;
using DynamicData;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Model.Statistics;
using SkiaSharp;

namespace GUIClient.ViewModels.Reports;

public class RisksVsCostsViewModel: ReportsViewModelBase
{
    #region LANGUAGE

    public string MinimumRisk => Localizer["Min Risk"];
    public string MaximumRisk => Localizer["Max Risk"];

     
    
    #endregion

    #region PROPERTIES

    private double _minimumRiskValue = 0; 
    
    public double MinimumRiskValue
    {
        get => _minimumRiskValue;
        set => this.RaiseAndSetIfChanged(ref _minimumRiskValue, value);
    }
    
    private double _maximumRiskValue = 10; 
    
    public double MaximumRiskValue
    {
        get => _maximumRiskValue;
        set => this.RaiseAndSetIfChanged(ref _maximumRiskValue, value);
    }

    public RectangularSection[] Sections { get; set; } =
    {
        new RectangularSection
        {
            Xi = 0,
            Xj = 5,
            Yi = 5,
            Yj = 10,
            Fill = new SolidColorPaint { Color = SKColors.Cyan.WithAlpha(20) }
        },
        new RectangularSection
        {
            Xi = 5,
            Xj = 10,
            Yi = 5,
            Yj = 10,
            Fill = new SolidColorPaint { Color = SKColors.Bisque.WithAlpha(20) }
        },
        new RectangularSection
        {
            Xi = 0,
            Xj = 5,
            Yi = 0,
            Yj = 5,
            Fill = new SolidColorPaint { Color = SKColors.Orange.WithAlpha(20) }
        },
        new RectangularSection
        {
            Xi = 5,
            Xj = 10,
            Yi = 0,
            Yj = 5,
            Fill = new SolidColorPaint { Color = SKColors.Orchid.WithAlpha(20) }
        },
        new RectangularSection
        {
            Yi = 5,
            Yj = 5,
            Stroke = new SolidColorPaint
            {
                Color = SKColors.Red,
                StrokeThickness = 3,
                PathEffect = new DashEffect(new float[] { 6, 6 })
            }
        },
        new RectangularSection
        {
            Xi = 5,
            Xj = 5,
            Stroke = new SolidColorPaint
            {
                Color = SKColors.Red,
                StrokeThickness = 3,
                PathEffect = new DashEffect(new float[] { 6, 6 })
            }
        },
    };

    private ISeries[]? _series;

    public ISeries[]? Series
    {
        get => _series;
        set => this.RaiseAndSetIfChanged(ref _series, value);
    }

    public Axis[] XAxes { get; set; } =
    {
        new Axis
        {
            Name = "X axis",
            NamePaint = new SolidColorPaint(s_gray1),
            TextSize = 18,
            Padding = new Padding(5, 15, 5, 5),
            LabelsPaint = new SolidColorPaint(s_gray),
            SeparatorsPaint = new SolidColorPaint
            {
                Color = s_gray,
                StrokeThickness = 1,
                PathEffect = new DashEffect(new float[] { 3, 3 })
            },
            SubseparatorsPaint = new SolidColorPaint
            {
                Color = s_gray2,
                StrokeThickness = 0.5f
            },
            ZeroPaint = new SolidColorPaint
            {
                Color = s_gray1,
                StrokeThickness = 2
            },
            TicksPaint = new SolidColorPaint
            {
                Color = s_gray,
                StrokeThickness = 1.5f
            },
            SubticksPaint = new SolidColorPaint
            {
                Color = s_gray,
                StrokeThickness = 1
            }
        }
    };

    public Axis[] YAxes { get; set; } =
    {
        new Axis
        {
            Name = "Y axis",
            NamePaint = new SolidColorPaint(s_gray1),
            TextSize = 18,
            Padding = new Padding(5, 0, 15, 0),
            LabelsPaint = new SolidColorPaint(s_gray),
            SeparatorsPaint = new SolidColorPaint
            {
                Color = s_gray,
                StrokeThickness = 1,
                PathEffect = new DashEffect(new float[] { 3, 3 })
            },
            SubseparatorsPaint = new SolidColorPaint
            {
                Color = s_gray2,
                StrokeThickness = 0.5f
            },
            ZeroPaint = new SolidColorPaint
            {
                Color = s_gray1,
                StrokeThickness = 2
            },
            TicksPaint = new SolidColorPaint
            {
                Color = s_gray,
                StrokeThickness = 1.5f
            },
            SubticksPaint = new SolidColorPaint
            {
                Color = s_gray,
                StrokeThickness = 1
            }
        }
    };
    
    
    public ReactiveCommand<Unit, Unit> BtGenerateClicked { get; }
    
    #endregion

    #region FIELDS
    
    private static readonly SKColor s_gray = new(195, 195, 195);
    private static readonly SKColor s_gray1 = new(160, 160, 160);
    private static readonly SKColor s_gray2 = new(90, 90, 90);
    private static readonly SKColor s_dark3 = new(60, 60, 60);

    private IRisksService _risksService;
    private IStatisticsService _statisticsService;

    #endregion
    
    public RisksVsCostsViewModel()
    {

        XAxes[0].Name = Localizer["Risk"];
        YAxes[0].Name = Localizer["Cost"];
        
        BtGenerateClicked = ReactiveCommand.Create(ExecuteGenerate);
        
        _risksService = GetService<IRisksService>();
        _statisticsService = GetService<IStatisticsService>();

    }
    
        
    #region METHODS

    public void ExecuteGenerate()
    {
        var dataList = _statisticsService.GetRisksVsCosts(MinimumRiskValue, MaximumRiskValue);

        var serie =
            new ScatterSeries<LabeledPoints>
            {
                GeometrySize = 10,
                Stroke = new SolidColorPaint { Color = SKColors.Cyan, StrokeThickness = 1 },
                Fill = null,
                DataLabelsPaint =  new SolidColorPaint { Color = SKColors.White, StrokeThickness = 1 },
                DataLabelsFormatter = point => $"{point.Model!.Label}",
                //DataLabelsTranslate = new (-1, 0),
                //DataPadding = new (0, 0),
                TooltipLabelFormatter = point => $"{point.Model!.Label} -> R:{point.Model!.X} C:{point.Model!.Y}",
                DataLabelsPosition = DataLabelsPosition.Right,
                Values = dataList
            };

        var series = new List<ISeries>();
        series.Add(serie);

        Series = series.ToArray();

        var ymax = dataList.Select(dl => dl.Y).Max();
        var ymin = dataList.Select(dl => dl.Y).Min();
        var xmax = dataList.Select(dl => dl.X).Max();
        var xmin = dataList.Select(dl => dl.X).Min();

        var ydelta = ymax - ymin;
        var xdelta = xmax - xmin;

        var ymid = (ydelta / 2) + ymin;
        var xmid = (xdelta / 2) + xmin;
        
        
        Sections[0].Yi = ymid;
        Sections[0].Yj = ymax;
        Sections[0].Xi = xmin;
        Sections[0].Xj = xmid;
        
        Sections[1].Yi = ymid;
        Sections[1].Yj = ymax;
        Sections[1].Xi = xmid;
        Sections[1].Xj = xmax;

        Sections[2].Yi = ymin;
        Sections[2].Yj = ymid;
        Sections[2].Xi = xmin;
        Sections[2].Xj = xmid;
        
        Sections[3].Yi = ymin;
        Sections[3].Yj = ymid;
        Sections[3].Xi = xmid;
        Sections[3].Xj = xmax;
        
        Sections[4].Yi = ymid;
        Sections[4].Yj = ymid;
        
        Sections[5].Xi = xmid;
        Sections[5].Xj = xmid;
        
    }

    #endregion

}
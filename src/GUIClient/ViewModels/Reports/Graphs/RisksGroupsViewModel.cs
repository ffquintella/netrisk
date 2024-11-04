using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ExCSS;
using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Kernel;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Drawing.Geometries;
using LiveChartsCore.SkiaSharpView.Painting;
using Model.Statistics;
using ReactiveUI;
using SkiaSharp;

namespace GUIClient.ViewModels.Reports.Graphs;

public class RisksGroupsViewModel : ViewModelBase
{
    #region LANGUAGE

    private string StrGroups => Localizer["Groups"];

    #endregion

    #region FIELDS
    

    #endregion

    #region PROPERTIES

    private SolidColorPaint LegendBackgroundPaint { get; set; } = new SolidColorPaint(SKColors.Transparent);
    
    private ObservableCollection<RiskGroup> _riskGroups = new ObservableCollection<RiskGroup>();
    public ObservableCollection<RiskGroup> RiskGroups
    {
        get => _riskGroups;
        set => this.RaiseAndSetIfChanged(ref _riskGroups, value);
    }
    
    private ISeries[] _series = [
        new PolarLineSeries<float> {
            Values = [0],
            LineSmoothness = 0,
            GeometrySize= 0,
            Fill = new SolidColorPaint(SKColors.Blue.WithAlpha(90))
        }];
    
    public ISeries[] Series
    {
        get => _series;
        set => this.RaiseAndSetIfChanged(ref _series, value);
    }
    
    private PolarAxis[]? angleAxes  = [
        new PolarAxis
        {
            LabelsRotation = LiveCharts.TangentAngle,
            Labels = [""]
        }
    ];
    
    public PolarAxis[]? AngleAxes
    {
        get => angleAxes;
        set => this.RaiseAndSetIfChanged(ref angleAxes, value);
    }
        
    #endregion

    #region METHODS

    public void Initialize(List<RiskGroup> riskGroups)
    {
        RiskGroups = new ObservableCollection<RiskGroup>(riskGroups);
        
        Series = new ISeries[]
        {
            new PolarLineSeries<float>
            {
                Values = RiskGroups.Select(r=> r.Score).ToArray(),
                LineSmoothness = 0,
                GeometrySize = 0,
                DataLabelsSize = 0,
                IsVisibleAtLegend = false,
                Fill = new SolidColorPaint(SKColors.Blue.WithAlpha(90))
            }
        };
        
        AngleAxes = new PolarAxis[]
        {
            new PolarAxis
            {
                Name = "Groups",
                LabelsRotation = LiveCharts.CotangentAngle,
                TextSize = 7,
                LabelsBackground = LvcColor.Empty,
                LabelsAngle = 30,
                
                //SeparatorsPaint = new SolidColorPaint(SKColors.Transparent),
                //LabelsPaint = new SolidColorPaint(SKColors.Transparent),
                Labels = RiskGroups.Select(x => x.Name).ToArray()
            },
        };
        
    }

    #endregion
}
using System.Collections.Generic;
using System.Collections.ObjectModel;
using DynamicData;
using LiveChartsCore;
using LiveChartsCore.Kernel.Sketches;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Model.Statistics;
using ReactiveUI;
using SkiaSharp;

namespace GUIClient.ViewModels.Reports.Graphs;

public class BusinessProcessRisksViewModel: GraphsViewModelBase
{
    #region LANGUAGE

    private string StrBussinessProcess => Localizer["BussinessProcess"];

    #endregion
    
    #region PROPERTIES
    
    private List<RiskEntity> _bpEntities = new List<RiskEntity>();
    public List<RiskEntity> BpEntities
    {
        get => _bpEntities;
        set => this.RaiseAndSetIfChanged(ref _bpEntities, value);
    }
    
    private ICartesianAxis[] _xAxis = [
        new Axis
        {
            //LabelsRotation = -25,
            Labels = [""]
        }
    ];
    
    public ICartesianAxis[] XAxis
    {
        get => _xAxis;
        set => this.RaiseAndSetIfChanged(ref _xAxis, value);
    }
    
    private ObservableCollection<ISeries> _series = [
        new ColumnSeries<float>
        {
            Values = [0],
        }
    ];
    
    public ObservableCollection<ISeries> Series
    {
        get => _series;
        set => this.RaiseAndSetIfChanged(ref _series, value);
    }
    
    #endregion
    
    #region METHOS

    public async void Initialize(List<RiskEntity> bpEntities)
    {
        BpEntities = bpEntities;

        var labels = new List<string>();
        var fullLabels = new List<string>();
        var values = new List<float>();
        
        foreach (var entity in BpEntities)
        {
            if(entity.EntityName.Length > 20)
                labels.Add(entity.EntityName.Substring(0, 20) + "...");
            else
                labels.Add(entity.EntityName);
            
            fullLabels.Add(entity.EntityName);
            
            values.Add(entity.TotalCalculatedRisk);
        }

        XAxis =
        [
            new Axis
            {
                Labels = labels.ToArray(),
                LabelsRotation = -45,
                TextSize = 6
            }
        ];
        
        Series = new ObservableCollection<ISeries>(
        [
            new ColumnSeries<float>
            {
                Values = values.ToArray(),
                Fill = new SolidColorPaint(SKColors.Azure),
                Padding = 2
            }
        ]);

    }
    
    #endregion
}
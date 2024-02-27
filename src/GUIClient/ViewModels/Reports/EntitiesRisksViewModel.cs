using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ClientServices.Interfaces;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Model.Statistics;
using ReactiveUI;
using SkiaSharp;

namespace GUIClient.ViewModels.Reports;

public class EntitiesRisksViewModel: ReportsViewModelBase
{
    #region LANGUAGE
        public string StrParentEntity { get; } = Localizer["ParentEntity"];
    #endregion

    #region PROPERTIES
    
    private string _parentEntity = string.Empty;
    public string ParentEntity
    {
        get => _parentEntity;
        set => this.RaiseAndSetIfChanged(ref _parentEntity, value);
    }
    
    private ObservableCollection<ISeries> _series = new ObservableCollection<ISeries>();

    public ObservableCollection<ISeries> Series
    {
        get => _series;
        set => this.RaiseAndSetIfChanged(ref _series, value);
    }
    
    public Axis[] XAxes { get; set; } =
    {
        new Axis
        {
            Labels = new[] { "" }
        }
    };

    public Axis[] YAxes { get; set; } =
    {
        new Axis
        {
            Labels = new[] { "" }
        }
    };
    #endregion
    
    #region SERVICES

    private IStatisticsService StatisticsService { get; } = GetService<IStatisticsService>();
    
    #endregion

    #region CONSTRUCTOR
        public EntitiesRisksViewModel()
        {
            
        }
    #endregion

    #region METHODS

    public void ExecuteGenerate()
    {
        var dataList = StatisticsService.GetEntitiesRiskValues();

        var grouped = dataList.GroupBy(v => v.Type).ToList();

        var labels = new List<string>();
        
        foreach (var group in grouped)
        {
            Series.Add(
                new ColumnSeries<float>
                {
                    Name = group.Select(v => v.Name).FirstOrDefault(),
                    Values = group.Select(v => v.Value)
                });
            if(!labels.Contains(group.Key))
                labels.Add(group.Key);
        }

        XAxes = new[]
        {
            new Axis
            {
                Labels = labels.ToArray(),
                LabelsRotation = 0,
                SeparatorsPaint = new SolidColorPaint(new SKColor(200, 200, 200)),
                SeparatorsAtCenter = false,
                TicksPaint = new SolidColorPaint(new SKColor(35, 35, 35)),
                TicksAtCenter = true,
                ForceStepToMin = true,
                MinStep = 1
            }
        };
        

    }

    #endregion
}
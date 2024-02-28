using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ClientServices.Interfaces;
using DAL.Entities;
using DynamicData;
using LiveChartsCore;
using LiveChartsCore.Drawing;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Drawing;
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
    
    private string? _parentEntity = string.Empty;
    public string? ParentEntity
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
    
    private ObservableCollection<Entity> _entities = new ObservableCollection<Entity>();

    public ObservableCollection<Entity> Entities
    {
        get => _entities;
        set => this.RaiseAndSetIfChanged(ref _entities, value);
    }
    
    private ObservableCollection<string> _entityNames = new ObservableCollection<string>();
    public ObservableCollection<string> EntityNames
    {
        get => _entityNames;
        set => this.RaiseAndSetIfChanged(ref _entityNames, value);
    }
    
    public IPaint<SkiaSharpDrawingContext> LegendTextPaint { get; set; } = new SolidColorPaint(new SKColor(200, 200, 200));

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
            Name = Localizer["TotalRiskRelatedToEntity"],
            NamePaint = new SolidColorPaint(SKColors.White),
            TextSize = 18,
            Padding = new Padding(5, 15, 5, 5),
            LabelsPaint = new SolidColorPaint(SKColors.White),
        }
    };
    #endregion
    
    #region SERVICES

    private IStatisticsService StatisticsService { get; } = GetService<IStatisticsService>();
    private IEntitiesService EntitiesService { get; } = GetService<IEntitiesService>();
    
    #endregion

    #region CONSTRUCTOR
        public EntitiesRisksViewModel()
        {
            LoadData();
        }
    #endregion

    #region METHODS
    
    private async void LoadData()
    {
        var entities = await EntitiesService.GetAllAsync();
        Entities = new ObservableCollection<Entity>(entities);
        
        //EntityNames = new ObservableCollection<string>(entities.Select(e => e.EntitiesProperties.FirstOrDefault(ep=>ep.Type == "name")!.Value));
        
        foreach (var entity in entities)
        {
            EntityNames.Add(entity.EntitiesProperties.FirstOrDefault(ep=> ep.Type == "name")!.Value + " (" + entity.Id + ")");
        }
        
    }

    public void ExecuteGenerate()
    {
        int? entityId = null;

        if (!string.IsNullOrEmpty(ParentEntity))
        {
            entityId = Int32.Parse(ParentEntity.Split(" (")[1].Split(")")[0]);
        }
        
        
        var dataList = StatisticsService.GetEntitiesRiskValues(entityId);

        var grouped = dataList.GroupBy(v => v.Type).ToList();

        var labels = new List<string>();
        
        Series.Clear();
        
        foreach (var group in grouped)
        {
            Series.Add(
                new ColumnSeries<float>
                {
                    Name = group.Select(v => v.Name).FirstOrDefault(),
                    Values = group.Select(v => v.Value),
                });
            if(!labels.Contains(group.Key))
                labels.Add(group.Key);
        }

        XAxes =
        [
            new Axis
            {
                Labels = labels.ToArray(),
                LabelsRotation = 0,
                SeparatorsPaint = new SolidColorPaint(new SKColor(200, 200, 200)),
                SeparatorsAtCenter = false,
                TicksPaint = new SolidColorPaint(new SKColor(135, 135, 135)),
                TicksAtCenter = true,
                ForceStepToMin = true,
                MinStep = 1,
                LabelsPaint = new SolidColorPaint(SKColors.White),
            }
        ];
        
    }

    #endregion
}
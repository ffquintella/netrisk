using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ReactiveUI;
using System.Reactive;
using ClientServices.Interfaces;
using DynamicData;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Model.Statistics;
using SkiaSharp;

namespace GUIClient.ViewModels.Reports;

public class RisksImpactVsProbabilityViewModel: ReportsViewModelBase
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
            Labels = new[] { "", Localizer["Insignificant"].ToString(),
                Localizer["Low"].ToString(), Localizer["Medium"].ToString(), 
                Localizer["High"].ToString(), Localizer["Critical"].ToString() }
        }
    };

    public Axis[] YAxes { get; set; } =
    {
        new Axis
        {
            Labels = new[] {"", Localizer["Remote"].ToString(),
                Localizer["Unlikely"].ToString(), Localizer["Believable"].ToString(), 
                Localizer["Likely"].ToString(), Localizer["Almost Certain"].ToString() }
        }
    };
    
    
    #endregion

    #region BUTTONS

    public ReactiveCommand<Unit, Unit> BtGenerateClicked { get; }

    #endregion
    
    #region SERVICES
    
        private IStatisticsService StatisticsService { get; } = GetService<IStatisticsService>();
    
    #endregion
    
    public RisksImpactVsProbabilityViewModel()
    {
        
        BtGenerateClicked = ReactiveCommand.Create(ExecuteGenerate);

        AddHeatSeries();

    }
    
    #region METHODS

    private void AddHeatSeries()
    {
        var heatSerie = new HeatSeries<WeightedPoint>
        {
            HeatMap = new[]
            {
                //new SKColor(255, 241, 118).AsLvcColor(), // the first element is the "coldest"
                SKColors.Green.AsLvcColor(),
                SKColors.Yellow.AsLvcColor(),
                SKColors.Red.AsLvcColor() // the last element is the "hottest"
            },
            Values = new ObservableCollection<WeightedPoint>
            {
                // Insignificant
                new(1, 1, 0), // Remote
                new(1, 2, 50), // Unlikely
                new(1, 3, 100), // Believable
                new(1, 4, 150), // Likely
                new(1, 5, 200), // Almost Certain


                // Low
                new(2, 1, 50), // Remote
                new(2, 2, 100), // Unlikely
                new(2, 3, 150), // Believable
                new(2, 4, 200), // Likely
                new(2, 5, 250), // Almost Certain


                // Medium
                new(3, 1, 100), // Remote
                new(3, 2, 150), // Unlikely
                new(3, 3, 200), // Believable
                new(3, 4, 250), // Likely
                new(3, 5, 300), // Almost Certain


                // High
                new(4, 1, 150), // Remote
                new(4, 2, 200), // Unlikely
                new(4, 3, 250), // Believable
                new(4, 4, 300), // Likely
                new(4, 5, 350), // Almost Certain


                // Critical
                new(5, 1, 200), // Remote
                new(5, 2, 250), // Unlikely
                new(5, 3, 300), // Believable
                new(5, 4, 350), // Likely
                new(5, 5, 400), // Almost Certain

            },
        };
        
        Series.Add(heatSerie);
    }

    public void ExecuteGenerate()
    {

        var dataList = StatisticsService.GetRisksImpactVsProbability(MinimumRiskValue, MaximumRiskValue);
        
        var displacementDistance = 0.25;
        
        // Cria um dicionário para armazenar as coordenadas originais de cada ponto
        var originalPoints = new Dictionary<LabeledPoints, (double X, double Y)>();

        // Cria um HashSet para armazenar as coordenadas dos pontos já plotados
        var plottedPoints = new HashSet<(double, double)>();

        // Define o raio máximo e as larguras dos eixos
        var a = displacementDistance; // Raio máximo agora é controlado pela propriedade DisplacementDistance

        foreach (var point in dataList)
        {
            // Armazena as coordenadas originais do ponto
            originalPoints[point] = (point.X!.Value, point.Y!.Value);

            // Se um ponto já foi plotado nessas coordenadas, aplica o deslocamento
            while (!plottedPoints.Add((point.X!.Value, point.Y!.Value)))
            {
                // Recupera as coordenadas originais do ponto
                var (originalX, originalY) = originalPoints[point];

                // Calcula a distância do ponto ao ponto original
                var dx = point.X.Value - originalX;
                var dy = point.Y.Value - originalY;

                // Calcula o ângulo theta
                var theta = Math.Atan2(dy, dx);

                // Calcula o deslocamento usando a função espiral
                var r = a + displacementDistance * theta;
                point.X += r * Math.Cos(theta);
                point.Y += r * Math.Sin(theta);
            }
        }

        var serie =
            new ScatterSeries<LabeledPoints>
            {
                GeometrySize = 10,
                Stroke = new SolidColorPaint { Color = SKColors.Cyan, StrokeThickness = 2 },
                Fill = new SolidColorPaint { Color = SKColors.Blue },
                DataLabelsPaint =  new SolidColorPaint { Color = SKColors.Black, StrokeThickness = 1 },
                DataLabelsFormatter = point => $"{point.Model!.Label}",
                XToolTipLabelFormatter = point => $"{point.Model!.Label} -> L:{point.Model!.X} I:{point.Model!.Y}",
                DataLabelsPosition = DataLabelsPosition.Right,
                Values = dataList
            };
        
        Series.Clear();
        AddHeatSeries();
        Series.Add(serie);

    }

    #endregion
}
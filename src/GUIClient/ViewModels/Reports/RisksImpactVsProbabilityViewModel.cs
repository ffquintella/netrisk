using System.Collections.ObjectModel;
using ReactiveUI;
using System.Reactive;
using DynamicData;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
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
    
    
    public ISeries[] Series { get; set; } =
    {

        new HeatSeries<WeightedPoint>
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
                new(0, 0, 0), // Remote
                new(0, 1, 50), // Unlikely
                new(0, 2, 100), // Believable
                new(0, 3, 150), // Likely
                new(0, 4, 200), // Almost Certain


                // Low
                new(1, 0, 50), // Remote
                new(1, 1, 100), // Unlikely
                new(1, 2, 150), // Believable
                new(1, 3, 200), // Likely
                new(1, 4, 250), // Almost Certain


                // Medium
                new(2, 0, 100), // Remote
                new(2, 1, 150), // Unlikely
                new(2, 2, 200), // Believable
                new(2, 3, 250), // Likely
                new(2, 4, 300), // Almost Certain


                // High
                new(3, 0, 150), // Remote
                new(3, 1, 200), // Unlikely
                new(3, 2, 250), // Believable
                new(3, 3, 300), // Likely
                new(3, 4, 350), // Almost Certain

                
                // Critical
                new(4, 0, 200), // Remote
                new(4, 1, 250), // Unlikely
                new(4, 2, 300), // Believable
                new(4, 3, 350), // Likely
                new(4, 4, 400), // Almost Certain

            },
        },
        new ScatterSeries<ObservablePoint>
        {
            GeometrySize = 15, 
            Fill = new SolidColorPaint { Color = SKColors.Blue },
            Values = new ObservableCollection<ObservablePoint>
            {
                new(2.2, 3.4),
                new(2.5, 2.5),
                new(4.2, 1.4),
                new(2.4, 1.9),
                new(1.2, 3.2),
                new(4, 3.5),

            }
        }
        
    };
    
    public Axis[] XAxes { get; set; } =
    {
        new Axis
        {
            Labels = new[] { Localizer["Insignificant"].ToString(),
                Localizer["Low"].ToString(), Localizer["Medium"].ToString(), 
                Localizer["High"].ToString(), Localizer["Critical"].ToString() }
        }
    };

    public Axis[] YAxes { get; set; } =
    {
        new Axis
        {
            Labels = new[] { Localizer["Remote"].ToString(),
                Localizer["Unlikely"].ToString(), Localizer["Believable"].ToString(), 
                Localizer["Likely"].ToString(), Localizer["Almost Certain"].ToString() }
        }
    };
    
    
    #endregion

    #region BUTTONS

    public ReactiveCommand<Unit, Unit> BtGenerateClicked { get; }

    #endregion
    
    public RisksImpactVsProbabilityViewModel()
    {
        
        BtGenerateClicked = ReactiveCommand.Create(ExecuteGenerate);
    }
    
    #region METHODS

    public void ExecuteGenerate()
    {
        var serie =
            new ScatterSeries<ObservablePoint>
            {
                Values = new ObservableCollection<ObservablePoint>
                {
                    new(2.2, 5.4),
                    new(4.5, 2.5),
                    new(4.2, 7.4),
                    new(6.4, 9.9),
                    new(4.2, 9.2),
                    new(5.8, 3.5),
                    new(7.3, 5.8),
                    new(8.9, 3.9),
                    new(6.1, 4.6),
                    new(9.4, 7.7),
                    new(8.4, 8.5),
                    new(3.6, 9.6),
                    new(4.4, 6.3),
                    new(5.8, 4.8),
                    new(6.9, 3.4),
                    new(7.6, 1.8),
                    new(8.3, 8.3),
                    new(9.9, 5.2),
                    new(8.1, 4.7),
                    new(7.4, 3.9),
                    new(6.8, 2.3),
                    new(5.3, 7.1),
                }
            };

        //Series = serie;


    }

    #endregion
}
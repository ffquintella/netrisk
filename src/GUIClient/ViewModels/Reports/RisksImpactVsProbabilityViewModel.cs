using System.Collections.ObjectModel;
using ReactiveUI;
using System.Reactive;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.SkiaSharpView;
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
    }

    #endregion
}
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Extensions;
using GUIClient.Tools;
using ReactiveUI;
using Tools.Extensions;


namespace GUIClient.ViewModels.Reports.Graphs;

public class VulnerabilitiesDistributionViewModel: GraphsViewModelBase
{
    #region LANGUAGE
        public string StrDistribution => Localizer["Distribution"];
        
        
        private ObservableCollection<ISeries> _series = new();

        public ObservableCollection<ISeries> Series
        {
            get => _series;
            set => this.RaiseAndSetIfChanged(ref _series, value);
        }
        
        private float _maxValue = 100;

        public float MaxValue
        {
            get => _maxValue;
            set => this.RaiseAndSetIfChanged(ref _maxValue, value);
        }
        
    #endregion
    
    #region SERVICES
    private IStatisticsService StatisticsService { get; } = GetService<IStatisticsService>();
    
    #endregion

    #region CONSTRUCTOR
    
        public VulnerabilitiesDistributionViewModel()
        {
            
            AuthenticationService.AuthenticationSucceeded += (_, _) =>
            {
                _= LoadDataAsync();
            };
            
            
        }

    #endregion
    
    #region METHODS

    private async Task LoadDataAsync()
    {
        var vulnerabilitiesDistribution = await StatisticsService.GetVulnerabilitiesDistributionAsync();

        var gaugeItems = ViewModelCollection.Materialize(vulnerabilitiesDistribution, vulnerability =>
        {
            if(vulnerability.Value > MaxValue) MaxValue = vulnerability.Value;

            return new GaugeItem(vulnerability.Value, series => SetStyle(vulnerability.Name, series));
        });
        
        gaugeItems.Add(new GaugeItem(GaugeItem.Background, series =>
        {
            series.InnerRadius = 30;
        }));
        
        Series = new ObservableCollection<ISeries>(GaugeGenerator.BuildSolidGauge(gaugeItems.ToArray()));
    }
    
    private static void SetStyle(string name, PieSeries<ObservableValue> series)
    {
        series.Name = name;
        series.DataLabelsSize = 6;
        series.DataLabelsPosition = PolarLabelsPosition.Start;
        series.DataLabelsFormatter =
            point => $"{point.Coordinate.PrimaryValue} {point.Context.Series.Name.Truncate(10)}";
        series.InnerRadius = 30;
        series.RelativeOuterRadius = 8;
        series.RelativeInnerRadius = 8;
        
    }
    
    #endregion
}

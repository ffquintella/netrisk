using System.Collections.Generic;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Drawing;
using LiveChartsCore.SkiaSharpView.Extensions;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.VisualElements;
using LiveChartsCore.VisualElements;
using ReactiveUI;
using SkiaSharp;

namespace GUIClient.ViewModels.Reports.Graphs;

public class VulnerabilitiesVerifiedViewModel: GraphsViewModelBase
{
    #region LANGUAGES
    private string StrVerified => "% " + Localizer["Verified"] + " (S > 2)";
    #endregion
    
    #region PROPERTIES
    public IEnumerable<ISeries> Series { get; set; }

    public IEnumerable<VisualElement<SkiaSharpDrawingContext>> VisualElements { get; set; }

    private NeedleVisual _needle = new NeedleVisual();

    public NeedleVisual Needle
    {
        get => _needle;
        set => this.RaiseAndSetIfChanged(ref _needle, value);
    }
    
    #endregion
    
    #region SERVICES
    
    private IStatisticsService StatisticsService { get; } = GetService<IStatisticsService>();
    
    #endregion

    #region CONSTRUCTOR

    public VulnerabilitiesVerifiedViewModel()
    {
        var sectionsOuter = 100;
        var sectionsWidth = 20;

        Needle = new NeedleVisual
        {
            Value = 0,
            
        };
        
        Series = GaugeGenerator.BuildAngularGaugeSections(
            new GaugeItem(60, s => SetStyle(sectionsOuter, sectionsWidth, s, new SolidColorPaint(SKColors.Red))),
            new GaugeItem(30, s => SetStyle(sectionsOuter, sectionsWidth, s, new SolidColorPaint(SKColors.Yellow))),
            new GaugeItem(10, s => SetStyle(sectionsOuter, sectionsWidth, s, new SolidColorPaint(SKColors.Green))));

        VisualElements = new VisualElement<SkiaSharpDrawingContext>[]
        {
            new AngularTicksVisual
            {
                LabelsSize = 16,
                LabelsOuterOffset = 15,
                OuterOffset = 65,
                TicksLength = 10
            },
            Needle
        };
        
        AuthenticationService.AuthenticationSucceeded += (_, _) =>
        {
            _= LoadDataAsync();
        };

    }

    #endregion
   
    #region METHODS

    private async Task LoadDataAsync()
    {
        var needleValue = await StatisticsService.GetVulnerabilitiesVerifiedPercentageAsync();
        
        Needle.Value = needleValue;
    }
    
    private static void SetStyle(
        double sectionsOuter, double sectionsWidth, PieSeries<ObservableValue> series, SolidColorPaint? fill )
    {
        series.OuterRadiusOffset = sectionsOuter;
        series.MaxRadialColumnWidth = sectionsWidth;
        series.Stroke = new SolidColorPaint(SKColors.Brown);
        series.Fill = fill;
        series.DataLabelsSize = 9;

    }
    
    #endregion
}
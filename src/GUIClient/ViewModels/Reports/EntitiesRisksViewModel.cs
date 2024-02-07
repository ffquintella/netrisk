using System.Collections.ObjectModel;
using LiveChartsCore;
using LiveChartsCore.SkiaSharpView;
using ReactiveUI;

namespace GUIClient.ViewModels.Reports;

public class EntitiesRisksViewModel: ReportsViewModelBase
{
    #region LANGUAGE
        public string StrParentEntity { get; } = Localizer["ParentEntity"];
    #endregion

    #region PROPERTIES
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

    #region CONSTRUCTOR
        public EntitiesRisksViewModel()
        {
            
        }
    #endregion

    #region METHODS

    public void ExecuteGenerate()
    {
    }

    #endregion
}
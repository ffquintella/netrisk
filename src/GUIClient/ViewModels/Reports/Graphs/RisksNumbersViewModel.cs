using Model.Statistics;
using ReactiveUI;

namespace GUIClient.ViewModels.Reports.Graphs;

public class RisksNumbersViewModel: ViewModelBase
{
    #region LANGUAGE
    private string StrNumbers => Localizer["Numbers"];
    private string StrByScore => Localizer["ByScore"];
    private string StrByStatus => Localizer["ByStatus"];
    private string StrHigh => Localizer["High"];
    private string StrMedium => Localizer["Medium"];
    private string StrLow => Localizer["Low"];
    private string StrTotal => Localizer["Total"];
    #endregion
    
    #region FIELDS
    #endregion
    
    #region PROPERTIES
    
    private RisksNumbers _risksNumbers = new RisksNumbers();
    public RisksNumbers RisksNumbers
    {
        get => _risksNumbers;
        set => this.RaiseAndSetIfChanged(ref _risksNumbers, value);
    }
    
    #endregion
    
    #region SERVICES
    #endregion
    
    #region METHODS
        public void Initialize(RisksNumbers risksNumbers)
        {
            RisksNumbers = risksNumbers;
        }
    #endregion
}
using Model.Statistics;
using ReactiveUI;

namespace GUIClient.ViewModels.Reports.Graphs;

public class RisksNumbersViewModel: ViewModelBase
{
    #region LANGUAGE
    public string StrNumbers => Localizer["Numbers"];
    public string StrByScore => Localizer["ByScore"];
    public string StrByStatus => Localizer["ByStatus"];
    public string StrHigh => Localizer["High"];
    public string StrMedium => Localizer["Medium"];
    public string StrLow => Localizer["Low"];
    public string StrTotal => Localizer["Total"];
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
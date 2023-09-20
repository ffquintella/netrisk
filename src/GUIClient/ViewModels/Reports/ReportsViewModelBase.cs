using ReactiveUI;

namespace GUIClient.ViewModels.Reports;

public class ReportsViewModelBase: ViewModelBase
{

    #region LANGUAGE

    public string StrFilters { get; }
    public string StrGenerate { get; }
    public string StrData { get; }

    #endregion
    
    #region PROPERTIES
    
    private bool _showFilters = true;
    public bool ShowFilters {
        get => _showFilters;
        set => this.RaiseAndSetIfChanged(ref _showFilters, value);
    }
    
    #endregion


    public ReportsViewModelBase()
    {
        StrFilters = Localizer["Filters"];
        StrGenerate = Localizer["Generate"];
        StrData = Localizer["Data"];
    }
}
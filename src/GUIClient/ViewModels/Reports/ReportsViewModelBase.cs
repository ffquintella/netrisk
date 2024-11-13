using ReactiveUI;

namespace GUIClient.ViewModels.Reports;

public class ReportsViewModelBase: ViewModelBase
{

    #region LANGUAGE

    public string StrFilters { get; } = Localizer["Filters"];
    public string StrGenerate { get; } = Localizer["Generate"];
    public string StrData { get; } = Localizer["Data"];

    #endregion
    
    #region PROPERTIES
    
    private bool _showFilters = true;
    public bool ShowFilters {
        get => _showFilters;
        set => this.RaiseAndSetIfChanged(ref _showFilters, value);
    }
    
    #endregion
}
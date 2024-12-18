using System.Threading.Tasks;

namespace GUIClient.ViewModels;

public class IncidentsViewModel: ViewModelBase
{
    #region LANGUAGE
    public string StrIncidents { get; } = Localizer["Incidents"] ;
    #endregion
    
    #region FIELDS
    private bool _dataLoaded;
    #endregion

    #region PROPERTIES
    #endregion

    #region CONSTRUCTOR
    public IncidentsViewModel()
    {
        _ = LoadDataAsync();
    }
    #endregion

    #region METHODS

    private async Task LoadDataAsync()
    {
        
        if(!_dataLoaded)
        {
            // Load data from the server
            // await LoadDataFromServerAsync();
        }
        
        _dataLoaded = true;
    }

    #endregion

}
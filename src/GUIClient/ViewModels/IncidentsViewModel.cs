using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using ReactiveUI;

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

    private ObservableCollection<Incident> _incidents;
    
    public ObservableCollection<Incident> Incidents
    {
        get => _incidents;
        set => this.RaiseAndSetIfChanged(ref _incidents, value);
    }
    
    #endregion
    
    #region SERVICES
    
    private IIncidentsService IncidentsService { get; } = GetService<IIncidentsService>();
    
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
            Incidents = new ObservableCollection<Incident>(await IncidentsService.GetAllAsync());
        }
        
        _dataLoaded = true;
    }

    #endregion

}
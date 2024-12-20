using System.Collections.ObjectModel;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Views;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class IncidentsViewModel: ViewModelBase
{
    #region LANGUAGE
    public string StrIncidents { get; } = Localizer["Incidents"] ;
    public string StrIncidentList { get; } = Localizer["IncidentList"];
    #endregion
    
    #region FIELDS
    private bool _dataLoaded;
    #endregion

    #region PROPERTIES

    private ObservableCollection<Incident>? _incidents;
    
    public ObservableCollection<Incident>? Incidents
    {
        get => _incidents;
        set => this.RaiseAndSetIfChanged(ref _incidents, value);
    }
    
    private Incident? _selectedIncident;
    public Incident? SelectedIncident
    {
        get => _selectedIncident;
        set => this.RaiseAndSetIfChanged(ref _selectedIncident, value);
    }
    
    private MainWindow ParentWindow { get; set; }
    
    #endregion
    
    #region SERVICES
    
    private IIncidentsService IncidentsService { get; } = GetService<IIncidentsService>();
    
    #endregion
    
    #region COMMANDS

    public ReactiveCommand<Window, Unit> BtAddIncidentClicked { get; }

    #endregion

    #region CONSTRUCTOR
    public IncidentsViewModel(MainWindow parentWindow)
    {
        ParentWindow = parentWindow;
        
        _ = LoadDataAsync();
        
        BtAddIncidentClicked = ReactiveCommand.CreateFromTask<Window>(AddIncidentAsync);
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
    
    private async Task AddIncidentAsync(Window window)
    {
        var incident = new Incident();

    }

    #endregion

}
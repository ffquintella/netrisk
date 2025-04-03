using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Events;
using GUIClient.Models;
using GUIClient.Views;
using Model.DTO;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using MsBox.Avalonia.Models;
using ReactiveUI;
using Serilog;

namespace GUIClient.ViewModels;

public class IncidentsViewModel: ViewModelBase
{
    #region LANGUAGE
    public string StrIncidents { get; } = Localizer["Incidents"] ;
    public string StrIncidentList { get; } = Localizer["IncidentList"];
    public string StrNames { get; } = Localizer["Names"];
    public string StrEventDetails { get; } = Localizer["EventDetails"];
    public string StrName { get; } = Localizer["Name"]+ ":";
    public string StrCreationDate { get; } = Localizer["CreationDate"]+ ":";
    public string StrCreatedBy { get; } = Localizer["CreatedBy"] + ":";
    public string StrLastUpdate { get; } = Localizer["LastUpdate"]+ ":";
    public string StrUpdatedBy { get; } = Localizer["UpdatedBy"]+ ":";
    public string StrMetada { get; } = Localizer["Metadata"];
    public string StrReport { get; } = Localizer["Report"]+ ":";
    public string StrImpact { get; } = Localizer["Impact"]+ ":";
    public string StrCause { get; } = Localizer["Cause"]+ ":";
    public string StrSolution { get; } = Localizer["Solution"]+ ":";
    public string StrReportedBy { get; } = Localizer["ReportedBy"]+ ":";
    public string StrReportDate { get; } = Localizer["ReportDate"]+ ":";
    public string StrMoreInfo { get; } = Localizer["MoreInfo"];
    public string StrStatus { get; } = Localizer["Status"]+ ":";
    public string StrNotes { get; } = Localizer["Notes"]+ ":";
    public string StrRecommendation { get; } = Localizer["Recommendation"]+ ":";
    public string StrStartDate { get; } = Localizer["StartDate"]+ ":";
    public string StrDuration { get; } = Localizer["Duration"]+ ":";
    public string StrAssignedTo { get; } = Localizer["Assigned to"]+ ":";
    private string StrImpactedEntity => Localizer["Impacted Entity"] + ":";
    private string StrDescription => Localizer["Description"] + ":";
    private string StrAttachments => Localizer["Attachments"] ;
    private string StrIncidentResponsePlansActivated => Localizer["Incident response plans activated"] ;
    #endregion
    
    #region FIELDS
    private bool _dataLoaded;
    #endregion

    #region PROPERTIES

    private ObservableCollection<Incident>? _listedIncidents;
    
    public ObservableCollection<Incident>? ListedIncidents
    {
        get => _listedIncidents;
        set => this.RaiseAndSetIfChanged(ref _listedIncidents, value);
    }
    
    private ObservableCollection<Incident> _incidents = new();
    public ObservableCollection<Incident> Incidents
    {
        get => _incidents;
        set => this.RaiseAndSetIfChanged(ref _incidents, value);
    }
    
    
    private Incident? _selectedIncident;
    public Incident? SelectedIncident
    {
        get => _selectedIncident;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedIncident, value);
            _ = LoadAttachmentsAsync();
            _ = LoadIncidentResponsePlansAsync();
        }
    }

    private ObservableCollection<FileListing> _attachments = new();
    public ObservableCollection<FileListing> Attachments
    {
        get => _attachments;
        set => this.RaiseAndSetIfChanged(ref _attachments, value);
    }
    
    private MainWindow ParentWindow { get; set; }
    
    private ObservableCollection<IncidentResponsePlan> _incidentResponsePlansActivated = new();
    
    public ObservableCollection<IncidentResponsePlan> IncidentResponsePlansActivated
    {
        get => _incidentResponsePlansActivated;
        set => this.RaiseAndSetIfChanged(ref _incidentResponsePlansActivated, value);
    }

    private bool _isSearchVisible = false;
    
    public bool IsSearchVisible
    {
        get => _isSearchVisible;
        set => this.RaiseAndSetIfChanged(ref _isSearchVisible, value);
    }
    
    private string _searchText = string.Empty;
    
    public string SearchText
    {
        get => _searchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchText, value);
            if (ListedIncidents != null)
            {
                if (SearchText != string.Empty)
                {
                    var filteredIncidents = Incidents.Where(i => i.Name.ToLower().Contains(SearchText.ToLower()));
                    ListedIncidents = new ObservableCollection<Incident>(filteredIncidents);
                }else
                {
                    ListedIncidents = Incidents;
                }

            }
        }
    }
    
    
    #endregion
    
    #region SERVICES
    
    private IIncidentsService IncidentsService { get; } = GetService<IIncidentsService>();
    private IFilesService FilesService { get; } = GetService<IFilesService>();
    private IIncidentResponsePlansService IncidentResponsePlansService { get; } = GetService<IIncidentResponsePlansService>();
    
    #endregion
    
    #region COMMANDS

    public ReactiveCommand<Window, Unit> BtAddIncidentClicked { get; }
    public ReactiveCommand<Window, Unit> BtEditIncidentClicked { get; }
    public ReactiveCommand<FileListing, Unit> BtFileDownloadClicked { get; } 
    public ReactiveCommand<Window, Unit> BtDeleteIncidentClicked { get; }
    public ReactiveCommand<Unit, Unit> BtShowSearchClicked { get; }
    
    #endregion
    
    #region EVENTS
    
    private void IncidentCreated(object? sender, IncidentEventArgs e)
    {
        Log.Debug("New incident created {Incident}", e.Incident.Name);

        ListedIncidents ??= [];
        
        ListedIncidents.Insert(0, e.Incident);
        
    }
    
    private void IncidentUpdated(object? sender, IncidentEventArgs e)
    {
        Log.Debug("Incident updated {Incident}", e.Incident.Name);

        var listIncident = ListedIncidents!.FirstOrDefault(i => i.Id == e.Incident.Id);
        
        var idx = ListedIncidents!.IndexOf(listIncident!);
        
        ListedIncidents[idx] = e.Incident;
        
    }
    
    #endregion
    
    #region CONSTRUCTOR
    public IncidentsViewModel(MainWindow parentWindow)
    {
        ParentWindow = parentWindow;
        
        _ = LoadDataAsync();
        
        BtAddIncidentClicked = ReactiveCommand.CreateFromTask<Window>(AddIncidentAsync);
        BtEditIncidentClicked = ReactiveCommand.CreateFromTask<Window>(EditIncidentAsync);
        BtFileDownloadClicked = ReactiveCommand.CreateFromTask<FileListing>(ExecuteFileDownloadAsync);
        BtDeleteIncidentClicked = ReactiveCommand.CreateFromTask<Window>(DeleteIncidentAsync);
        BtShowSearchClicked = ReactiveCommand.CreateFromTask(ShowSearchBarAsync);
    }
    #endregion

    #region METHODS

    private async Task ExecuteFileDownloadAsync(FileListing file)
    {
        var topLevel = TopLevel.GetTopLevel(ParentWindow);
        
        var openFile = await topLevel!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer["SaveDocumentMSG"],
            DefaultExtension = FilesService.ConvertTypeToExtension(file.Type!),
            SuggestedFileName = file.Name + FilesService.ConvertTypeToExtension(file.Type!),
            
        });

        if (openFile == null) return;
            
        _= FilesService.DownloadFileAsync(file.UniqueName, openFile.Path);
    }

    private async Task LoadAttachmentsAsync()
    {
        if(SelectedIncident == null)
        {
            return;
        }
        
        var files = await IncidentsService.GetAttachmentsAsync(SelectedIncident.Id);
        
        Attachments = new ObservableCollection<FileListing>(files);
        
    }

    private async Task LoadIncidentResponsePlansAsync()
    {
        if(SelectedIncident == null)
        {
            return;
        }
        
        var planIds = await IncidentsService.GetIncidentResponsPlanIdsByIdAsync(SelectedIncident.Id);
        
        ParallelOptions parallelOptions = new()
        {
            MaxDegreeOfParallelism = 3
        };
        
        IncidentResponsePlansActivated.Clear();
        
        await Parallel.ForEachAsync( planIds, parallelOptions, async (planId, token) =>
        {
            var plan = await IncidentResponsePlansService.GetByIdAsync(planId);
            IncidentResponsePlansActivated.Add(plan);
        });

        IncidentResponsePlansActivated =
            new ObservableCollection<IncidentResponsePlan>(IncidentResponsePlansActivated.OrderBy(irp => irp.Name)
                .ToList());
    }

    private async Task LoadDataAsync()
    {
        
        if(!_dataLoaded)
        {
           Incidents = new ObservableCollection<Incident>((await IncidentsService.GetAllAsync()).OrderByDescending(irp => irp.Name).ToList());
           ListedIncidents = Incidents;
        }
        
        _dataLoaded = true;
    }

    private async Task ShowSearchBarAsync()
    {
        await Task.Run(()=>
        {
            IsSearchVisible = !IsSearchVisible;
            if (IsSearchVisible)
            {
                SearchText = string.Empty;
            }
        });
    }
    
    private async Task EditIncidentAsync(Window window)
    {
        if(SelectedIncident == null)
        {
            var msgBox1 = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["Please select an incident to edit"],
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgBox1.ShowAsync();
            return;
        }
        
        var editIncidentWindow = new EditIncidentWindow(OperationType.Edit, SelectedIncident);
        
        ((EditIncidentViewModel)editIncidentWindow.DataContext!).IncidentUpdated += IncidentUpdated; 
        
        await editIncidentWindow.ShowDialog<Incident>(window);
        
    }

    private async Task AddIncidentAsync(Window window)
    {
        
        var editIncidentWindow = new EditIncidentWindow(OperationType.Create);
        
        ((EditIncidentViewModel)editIncidentWindow.DataContext!).IncidentCreated += IncidentCreated;
        ((EditIncidentViewModel)editIncidentWindow.DataContext!).IncidentUpdated += IncidentUpdated; 
        
        await editIncidentWindow.ShowDialog<Incident>(window);

    }

    private async Task DeleteIncidentAsync(Window window)
    {
        if(SelectedIncident == null)
        {
            var msgBox1 = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["Please select an incident"],
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgBox1.ShowAsync();
            return;
        }
        
        var msgBox = MessageBoxManager
            .GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["Are you sure you want to delete this incident?"],
                Icon = Icon.Warning,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ButtonDefinitions = ButtonEnum.YesNo
            });
        
        var result = await msgBox.ShowAsync();
        
        if(result == ButtonResult.No)
        {
            return;
        }
        
        await IncidentsService.DeleteAsync(SelectedIncident.Id);
        
        ListedIncidents!.Remove(SelectedIncident);
        
        SelectedIncident = null;
        Attachments.Clear();
        IncidentResponsePlansActivated.Clear();
        
        
    }
    
    

    #endregion

}
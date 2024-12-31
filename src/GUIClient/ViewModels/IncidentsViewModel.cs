﻿using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Events;
using GUIClient.Models;
using GUIClient.Views;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
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
    public ReactiveCommand<Window, Unit> BtEditIncidentClicked { get; }

    #endregion
    
    #region EVENTS
    
    private void IncidentCreated(object? sender, IncidentEventArgs e)
    {
        Log.Debug("New incident created {Incident}", e.Incident.Name);

        Incidents ??= [];
        
        Incidents.Add(e.Incident);
        
    }
    
    private void IncidentUpdated(object? sender, IncidentEventArgs e)
    {
        Log.Debug("Incident updated {Incident}", e.Incident.Name);

        var listIncident = Incidents!.FirstOrDefault(i => i.Id == e.Incident.Id);
        
        var idx = Incidents!.IndexOf(listIncident!);
        
        Incidents[idx] = e.Incident;
        
    }
    
    #endregion
    
    #region CONSTRUCTOR
    public IncidentsViewModel(MainWindow parentWindow)
    {
        ParentWindow = parentWindow;
        
        _ = LoadDataAsync();
        
        BtAddIncidentClicked = ReactiveCommand.CreateFromTask<Window>(AddIncidentAsync);
        BtEditIncidentClicked = ReactiveCommand.CreateFromTask<Window>(EditIncidentAsync);
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

    #endregion

}
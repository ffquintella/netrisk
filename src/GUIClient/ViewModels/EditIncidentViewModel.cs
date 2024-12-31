﻿using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.Tools;
using GUIClient.Views;
using Model;
using Model.Authentication;
using Model.DTO;
using Model.Incidents;
using Model.Status;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using Serilog;
using Tools.Helpers;
using TimeSpan = System.TimeSpan;
using System.Reactive;
using AvaloniaExtraControls.Models;
using GUIClient.Events;

namespace GUIClient.ViewModels;

public class EditIncidentViewModel: ViewModelBase
{

    #region LANGUAGE

    private string StrIdentification => Localizer["Identification"];
    private string StrYear => Localizer["Year"] + ":";
    private string StrSequence => Localizer["Sequence"]+ ":";
    private string StrName => Localizer["Name"]+ ":";
    private string StrEnableFreeNaming => Localizer["Enable free naming"];
    private string StrIncidentDetails => Localizer["Incident details"];
    private string StrCreationDate => Localizer["Creation date"] + ":";
    private string StrIncidentDates => Localizer["Incident dates"];
    private string StrReportDate => Localizer["Report date"] + ":";
    private string StrDuration => Localizer["Duration"] + " (" + Localizer["Hours"] + ")" + ":";
    private string StrStatus => Localizer["Status"] + ":";
    private string StrCategory => Localizer["Category"] + ":";
    private string StrReportedBy => Localizer["ReportedBy"] + ":";
    private string StrImpactedEntity => Localizer["Impacted Entity"] + ":";
    private string StrAssignedTo => Localizer["Assigned to"] + ":";
    private string StrDescription => Localizer["Description"] + ":";
    private string StrReport => Localizer["Report"] + ":";
    private string StrCause => Localizer["Cause"] + ":";
    private string StrImpact => Localizer["Impact"] + ":";
    private string StrSolution => Localizer["Solution"] + ":";
    private string StrRecommendations => Localizer["Recommendations"] + ":";
    private string StrNotes => Localizer["Notes"] + ":";
    private string StrAttachments => Localizer["Attachments"] + ":";
    private string StrSave => Localizer["Save"];
    private string StrSaveAndClose => Localizer["Save & Close"];
    private string StrClose => Localizer["Close"];
    private string StrActivateIncidentResponsePlans => Localizer["ActivateIncidentResponsePlans"];
    private string StrAvailable => Localizer["Available"];
    private string StrSelected => Localizer["Selected"];
    
    #endregion

    #region FIELDS

    #endregion
    
    #region PROPERTIES
    
    
    private OperationType _windowOperationType;
    private OperationType WindowOperationType
    {
        get => _windowOperationType;
        set
        {
            
            if(value == OperationType.Edit)
            {
                EnableFreeNaming = false;
                IsEditAndNotFreeNaming = true;
                IsCreateAndNotFreeNaming = false;
                IsEditOrFreeNaming = true;
                IsCreate = false;
                IsEdit = true;
                WindowTitle = Localizer["Edit Incident"];
            }
            else if(value == OperationType.Create)
            {
                if(!EnableFreeNaming) IsCreateAndNotFreeNaming = true;
                IsEditAndNotFreeNaming = false;
                IsEditOrFreeNaming = false;
                IsCreate = true;
                IsEdit = false;
                WindowTitle = Localizer["Create Incident"];
            }
            this.RaiseAndSetIfChanged(ref _windowOperationType, value);
        }
    }

    private Incident _incident = new ();
    
    public Incident Incident
    {
        get => _incident;
        set => this.RaiseAndSetIfChanged(ref _incident, value);
    }
    
    private string _windowTitle = string.Empty;

    public string WindowTitle
    {
        get => _windowTitle;
        set => this.RaiseAndSetIfChanged(ref _windowTitle, value);
    }
    
    
    private string _footerText = string.Empty;
    
    public string FooterText
    {
        get => _footerText;
        set => this.RaiseAndSetIfChanged(ref _footerText, value);
    }
    
    private AuthenticatedUserInfo? _userInfo;
    public AuthenticatedUserInfo? UserInfo
    {
        get => _userInfo;
        set => this.RaiseAndSetIfChanged(ref _userInfo, value);
    }

    public EditIncidentWindow? _parentWindow;
    public EditIncidentWindow? ParentWindow
    {
        get=> _parentWindow;
        set
        {
            _parentWindow = value;
            _ = AdjustAutoComplete();
        }
    }

    private bool _isCreate;

    public bool IsCreate
    {
        get => _isCreate;
        set => this.RaiseAndSetIfChanged(ref _isCreate, value);
    }

    private bool _isEdit;

    public bool IsEdit
    {
        get => _isEdit;
        set => this.RaiseAndSetIfChanged(ref _isEdit, value);
    }
    
    private bool _enableFreeNaming;
    
    public bool EnableFreeNaming
    {
        get => _enableFreeNaming;
        set
        {
            if (IsEdit)
            {
                IsEditAndNotFreeNaming = !value;
                IsEditOrFreeNaming = true;
            }
            else if (IsCreate)
            {
                IsCreateAndNotFreeNaming = !value;
                IsEditOrFreeNaming = value;
            }
            this.RaiseAndSetIfChanged(ref _enableFreeNaming, value);
        }
    }

    private bool _isEditAndNotFreeNaming;
    
    public bool IsEditAndNotFreeNaming
    {
        get => _isEditAndNotFreeNaming;
        set => this.RaiseAndSetIfChanged(ref _isEditAndNotFreeNaming, value);
    }
    
    private bool _isCreateAndNotFreeNaming;
    
    public bool IsCreateAndNotFreeNaming
    {
        get => _isCreateAndNotFreeNaming;
        set => this.RaiseAndSetIfChanged(ref _isCreateAndNotFreeNaming, value);
    }
    
    private bool _isEditOrFreeNaming;
    
    public bool IsEditOrFreeNaming
    {
        get => _isEditOrFreeNaming;
        set => this.RaiseAndSetIfChanged(ref _isEditOrFreeNaming, value);
    }
    
    public DateTimeOffset SelectedYear
    {
        get => new DateTimeOffset (new DateTime( Incident.Year, 1, 1));
        set
        {
            Incident.Year = value.Year;
            _ = AdjustIncidentName();
        }
    }
    
    public DateTimeOffset ReportDate
    {
        get => new DateTimeOffset(Incident.ReportDate);
        set => Incident.ReportDate = value.DateTime;
    }

    public decimal Duration
    {
        get
        {
            if(Incident.Duration == null)
            {
                Incident.Duration = new TimeSpan(1,0,0);
            }
            return Convert.ToDecimal(Incident.Duration.Value.TotalHours);
        }
        set => Incident.Duration = TimeSpan.FromHours(Convert.ToDouble(value));
    }

    public decimal SelectedSequence
    {
        get => Incident.Sequence;
        set
        {
            Incident.Sequence = Convert.ToInt32(value);
            _ = AdjustIncidentName();
        }
    }

    private string _name;
    public string Name
    {
        get => _name;
        set
        {
            Incident.Name = value;
            this.RaiseAndSetIfChanged(ref _name, value);
        }
    }
    
    public List<IntStatusItem> StatusItems { get; } = IncidentStatus.GetAll(Localizer);
    
    public IntStatusItem SelectedStatus
    {
        get => StatusItems.Find(x => x.IntStatus == Incident.Status) ?? StatusItems.FirstOrDefault(x => x.IntStatus == (int)IntStatus.Active)!;
        set => Incident.Status = value.IntStatus;
    }
    
    public List<IncidentCategory> Categories { get; } = IncidentCategories.GetCategories(Localizer);
    
    public IncidentCategory SelectedCategory
    {
        get => Categories.Find(x => x.DbName == Incident.Category) ?? Categories.FirstOrDefault(x => x.DbName == "not_specified")!;
        set => Incident.Category = value.DbName ?? "not_specified";
    }
    
    private ObservableCollection<string> _people = new();
    
    public ObservableCollection<string> People
    {
        get => _people;
        set => this.RaiseAndSetIfChanged(ref _people, value);
    }
    
    private ObservableCollection<UserListing> _users = new();
    
    public ObservableCollection<UserListing> Users
    {
        get => _users;
        set => this.RaiseAndSetIfChanged(ref _users, value);
    }
    
    private string _selectedReporter;
    
    public string SelectedReporter
    {
        get => _selectedReporter;
        set
        {
            _selectedReporter = value;

            if (People.Contains(value))
            {
                // Reporter is a person
                var id = AutoCompleteHelper.ExtractNumber(value)!.Value;
                Incident.ReportEntityId = id;
                Incident.ReportedByEntity = true;
            }
            else
            {
                Incident.ReportedBy = value; 
                Incident.ReportedByEntity = false;
            }
            
        }
    }

    private string _selectedImpactedEntity;
    public string SelectedImpactedEntity
    {
        get => _selectedImpactedEntity;
        set
        {
 
            if (ImpactedEntitiesList.Contains(value))
            {
                // Entity is valid
                var id = AutoCompleteHelper.ExtractNumber(value)!.Value;
                Incident.ImpactedEntityId = id;
                this.RaiseAndSetIfChanged(ref _selectedImpactedEntity, value);
            }
            else
            {
                // Invalid entity
                Incident.ImpactedEntityId = null;
                this.RaiseAndSetIfChanged(ref _selectedImpactedEntity, string.Empty);
            }
            
        }
    }
    
    private string _selectedAssignee;
    
    public string SelectedAssignee
    {
        get => _selectedAssignee;
        set
        {
            _selectedAssignee = value;

            var id = AutoCompleteHelper.ExtractNumber(value)!.Value;

            if (Users.Any(u => u.Id == id))
            {
                Incident.AssignedToId = id;
            }

        }
    }

    private ObservableCollection<string> _impactedEntitiesList = new();
    
    public ObservableCollection<string> ImpactedEntitiesList
    {
        get => _impactedEntitiesList;
        set => this.RaiseAndSetIfChanged(ref _impactedEntitiesList, value);
    }
    
    private ObservableCollection<IncidentResponsePlan> _incidentResponsePlans = new();
    
    public ObservableCollection<IncidentResponsePlan> IncidentResponsePlans
    {
        get => _incidentResponsePlans;
        set => this.RaiseAndSetIfChanged(ref _incidentResponsePlans, value);
    }
    
    private ObservableCollection<SelectEntity> _availablePlans = new();
    
    public ObservableCollection<SelectEntity> AvailablePlans
    {
        get => _availablePlans;
        set => this.RaiseAndSetIfChanged(ref _availablePlans, value);
    }
    
    private ObservableCollection<SelectEntity> _selectedPlans = new();
    
    public ObservableCollection<SelectEntity> SelectedPlans
    {
        get => _selectedPlans;
        set => this.RaiseAndSetIfChanged(ref _selectedPlans, value);
    }

    #endregion
    
    #region SERVICES
    private IEntitiesService EntitiesService { get; } = GetService<IEntitiesService>();
    private IUsersService UsersService { get; } = GetService<IUsersService>();
    private IIncidentsService IncidentsService { get; } = GetService<IIncidentsService>();
    private IIncidentResponsePlansService IncidentResponsePlansService { get; } = GetService<IIncidentResponsePlansService>();
    
    #endregion
    
    #region COMMANDS
        public ReactiveCommand<Unit, Unit> BtSaveClicked { get; }
    #endregion
    
    #region EVENTS
        public event EventHandler<IncidentEventArgs> IncidentCreated = delegate { };
        protected virtual void OnIncidentCreated(IncidentEventArgs e)
        {
            IncidentCreated.Invoke(this, e);
        }
    #endregion 
    
    #region CONSTRUCTOR

    public EditIncidentViewModel() : this( OperationType.Create)
    {
        
    }
    
    public EditIncidentViewModel( Incident incident) : this( OperationType.Edit, incident)
    {
        
    }

    public EditIncidentViewModel( OperationType operationType, Incident? incident = null)
    {
        
        WindowOperationType = operationType;

        Incident = WindowOperationType switch
        {
            OperationType.Edit => Incident = incident ?? throw new ArgumentNullException(nameof(incident)),
            OperationType.Create =>  Incident = new Incident(),
            _ => throw new Exception("Invalid operation type")
        };
        
        BtSaveClicked = ReactiveCommand.CreateFromTask(ExecuteSaveAsync);
        
        _ = LoadDataAsync();

    }
    
    #endregion
    
    #region METHODS

    private async Task ExecuteSaveAsync()
    {
        if(WindowOperationType == OperationType.Create)
        {
            await CreateIncidentAsync();
        }
        else if(WindowOperationType == OperationType.Edit)
        {
            await UpdateIncidentAsync();
        }
    }

    private async Task CreateIncidentAsync()
    {
        try
        {
            
            if(!EnableFreeNaming)
            {
                var sequence = await IncidentsService.GetNextSequenceAsync(Incident.Year);
                Incident.Sequence = sequence;
                await AdjustIncidentName();
            }

            
            var incident = await IncidentsService.CreateAsync(Incident);   
            Incident = incident;
            
            OnIncidentCreated(new ()
            {
                OperationType = OperationType.Create,
                Incident = incident
            });
            
            WindowOperationType = OperationType.Edit;
            
            var msgBox1 = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Success"],
                    ContentMessage = Localizer["Incident created successfully"],
                    Icon = Icon.Success,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgBox1.ShowAsync();
            
            
        }catch (Exception ex)
        {
            var msgBox1 = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["Error creating incident"],
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgBox1.ShowAsync();
            
            Log.Error(ex, "Error creating incident");
        }
        
        
    }
    
    private async Task UpdateIncidentAsync()
    {
        
    }

    
    private async Task AdjustAutoComplete()
    {
        if(ParentWindow == null)
        {
            return;
        }
        var userListingBox = ParentWindow!.Get<AutoCompleteBox>("UserListingBox");
        
        if(userListingBox == null)
        {
            return;
        }
        
        userListingBox.AsyncPopulator = GetUserByNameAsync;
        userListingBox.TextSelector = TextSelector;
    }

    private string TextSelector(string? text, string? item)
    {
        return item ?? string.Empty;
    }

    private async Task AdjustIncidentName()
    {
        string fmt = "0000.##";
        
        if(IsCreate)
        {
            Name = $"SI-{Incident.Year}-{Incident.Sequence.ToString(fmt)}";
        }
    }
    
    private async Task LoadDataAsync()
    {
        // Get authenticated user info
        UserInfo = AuthenticationService.AuthenticatedUserInfo;

        EnableFreeNaming = false;
        
        if(UserInfo == null)
        {
            var msgBox1 = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["Not authenticated"],
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgBox1.ShowAsync();
            
            Log.Error("Cloud not retrieve authenticated user info");
            
            ParentWindow?.Close();
            
        }
        
        FooterText = $"{Localizer["Logged as"]}: {UserInfo?.UserName}";
        
        // Get Incident Name
        if (IsCreate)
        {
            Incident.Year = DateTime.Now.Year;
            Incident.Sequence = 1;
            Incident.CreationDate = DateTime.Now;
            Incident.ReportDate = DateTime.Now;
            Incident.Duration = new TimeSpan(1, 0, 0);
            //Incident.Name = Localizer["Not defined"];
            await AdjustIncidentName();
        }
        
        await LoadPeopleAsync();
        await LoadImpactedEntitiesListAsync();
        await LoadUsersAsync();
        await LoadIncidentResponsePlansAsync();

    }
    
    public async Task<IEnumerable<object>> GetUserByNameAsync(string? searchText, CancellationToken cancellationToken)
    {
        await Task.Delay(TimeSpan.FromSeconds(0.5), cancellationToken);
        if(searchText == null)
        {
            return new List<UserListing>();
        }
        var users = await Users.ToAsyncEnumerable().Where(u => u.Name.Contains(searchText)).ToListAsync();
        return users;
    }
 
    
    private async Task LoadUsersAsync()
    {
        var users = await UsersService.GetAllAsync();
        
        Users = new ObservableCollection<UserListing>(users);
    }
    
    private async Task LoadIncidentResponsePlansAsync()
    {
        var incidentResponsePlans = await IncidentResponsePlansService.GetAllAsync();
        
        IncidentResponsePlans = new ObservableCollection<IncidentResponsePlan>(incidentResponsePlans);
        
        AvailablePlans = new ObservableCollection<SelectEntity>(incidentResponsePlans.Select(irp => new SelectEntity(irp.Id.ToString(), irp.Name)));
    }
    
    private async Task LoadPeopleAsync()
    {
        var people = await EntitiesService.GetAllAsync("person", true);

        People = await LoadListAsync(people);        

    }
    
    private async Task LoadImpactedEntitiesListAsync()
    {
        var entities = await EntitiesService.GetAllAsync("organization", true);

        entities.AddRange(await EntitiesService.GetAllAsync("organizationUnit", true));
        entities.AddRange(await EntitiesService.GetAllAsync("subOrganizationUnit", true));
        entities.AddRange(await EntitiesService.GetAllAsync("application", true));
        entities.AddRange(await EntitiesService.GetAllAsync("applicationModule", true));
        entities.AddRange(await EntitiesService.GetAllAsync("businessProcess", true));

        ImpactedEntitiesList = await LoadListAsync(entities);        

    }
    
    private async Task<ObservableCollection<string>> LoadListAsync(List<Entity> entities)
    {
        var entitiesString = new List<string>();
        await Task.Run(() =>
        {
            Parallel.ForEach(entities, entity =>
            {
                entitiesString.Add($"{entity.EntitiesProperties.Where(ep => ep.Type == "name").FirstOrDefault()?.Value} ({entity.Id})");
            });
        });
        
        entitiesString.Sort();
        
        return new ObservableCollection<string>(entitiesString);
    }
    
    
    #endregion
    
}
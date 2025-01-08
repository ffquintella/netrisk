using System;
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
using TimeSpan = System.TimeSpan;
using System.Reactive;
using Avalonia.Platform.Storage;
using AvaloniaExtraControls.Models;
using GUIClient.Events;
using Model.File;
using ReactiveUI.Validation.Extensions;

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

    private EditIncidentWindow? _parentWindow;
    public EditIncidentWindow? ParentWindow
    {
        get=> _parentWindow;
        set
        {
            _parentWindow = value;
            AdjustAutoComplete();
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
            AdjustIncidentName();
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
            AdjustIncidentName();
        }
    }

    private string _name = string.Empty;
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
    
    private string _selectedReporter = string.Empty;
    
    public string SelectedReporter
    {
        get => _selectedReporter;
        set
        {
            if (People.Contains(value))
            {
                // Reporter is a person
                var id = AutoCompleteHelper.ExtractNumber(value)!.Value;
                Incident.ReportEntityId = id;
                Incident.ReportedByEntity = true;
                this.RaiseAndSetIfChanged(ref _selectedReporter, value);
            }
            else
            {
                Incident.ReportedBy = value; 
                Incident.ReportedByEntity = false;
                this.RaiseAndSetIfChanged(ref _selectedReporter, string.Empty);
            }
            
        }
    }

    private string _selectedImpactedEntity = string.Empty;
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
    
    private UserListing? _selectedAssignee;
    
    public UserListing? SelectedAssignee
    {
        get => _selectedAssignee;
        set
        {
            //_selectedAssignee = value;
            
            if(value == null)
            {
                Incident.AssignedToId = null;
                return;
            }
            Incident.AssignedToId = value.Id;
            
            this.RaiseAndSetIfChanged(ref _selectedAssignee, value);

        }
    }
    
    private ObservableCollection<int> _incidentResponsePlanIds = new();
    
    public ObservableCollection<int> IncidentResponsePlanIds
    {
        get => _incidentResponsePlanIds;
        set => this.RaiseAndSetIfChanged(ref _incidentResponsePlanIds, value);
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
    
    private List<int> NewIncidentResponsePlanIds { get; set; } = new();
    
    private IEnumerable<SelectEntity> _selectedPlans = new List<SelectEntity>();
    
    public IEnumerable<SelectEntity> SelectedPlans
    {
        get => _selectedPlans;
        set => this.RaiseAndSetIfChanged(ref _selectedPlans, value);
    }
    
    private ObservableCollection<FileListing> _attachments = new ObservableCollection<FileListing>();
    
    public ObservableCollection<FileListing> Attachments
    {
        get => _attachments;
        set => this.RaiseAndSetIfChanged(ref _attachments, value);
    }
    
    private bool _saveButtonEnabled = true;
    
    public bool SaveButtonEnabled
    {
        get => _saveButtonEnabled;
        set => this.RaiseAndSetIfChanged(ref _saveButtonEnabled, value);
    }

    #endregion
    
    #region SERVICES
    private IEntitiesService EntitiesService { get; } = GetService<IEntitiesService>();
    private IUsersService UsersService { get; } = GetService<IUsersService>();
    private IIncidentsService IncidentsService { get; } = GetService<IIncidentsService>();
    private IIncidentResponsePlansService IncidentResponsePlansService { get; } = GetService<IIncidentResponsePlansService>();
    private IFilesService FilesService { get; } = GetService<IFilesService>();
    
    #endregion
    
    #region COMMANDS
        public ReactiveCommand<Unit, Unit> BtSaveClicked { get; }
        public ReactiveCommand<Unit, Unit> BtSaveAndCloseClicked { get; }
        public ReactiveCommand<Unit, Unit> BtCloseClicked { get; }
        public ReactiveCommand<Unit, Unit> BtFileAddClicked { get; }
        public ReactiveCommand<FileListing, Unit> BtFileDownloadClicked { get; }
        public ReactiveCommand<FileListing, Unit> BtFileDeleteClicked { get; }

        #endregion
    
    #region EVENTS
        public event EventHandler<IncidentEventArgs> IncidentCreated = delegate { };
        protected virtual void OnIncidentCreated(IncidentEventArgs e)
        {
            IncidentCreated.Invoke(this, e);
        }
        
        public event EventHandler<IncidentEventArgs> IncidentUpdated = delegate { };
        protected virtual void OnIncidentUpdated(IncidentEventArgs e)
        {
            IncidentUpdated.Invoke(this, e);
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
        BtFileAddClicked = ReactiveCommand.CreateFromTask(ExecuteFileAddAsync);
        BtSaveAndCloseClicked = ReactiveCommand.CreateFromTask(ExecuteSaveAndCloseAsync);
        BtCloseClicked = ReactiveCommand.CreateFromTask(ExecuteCloseAsync);
        BtFileDownloadClicked = ReactiveCommand.CreateFromTask<FileListing>(ExecuteFileDownloadAsync);
        BtFileDeleteClicked = ReactiveCommand.CreateFromTask<FileListing>(ExecuteDeleteFileAsync);
        
        _ = LoadDataAsync();

        #region VALIDATION

        this.ValidationRule(
            viewModel => viewModel.SelectedReporter, 
            prop => !string.IsNullOrEmpty(prop),
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedImpactedEntity, 
            prop => !string.IsNullOrEmpty(prop),
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedAssignee, 
            prop => prop!= null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.IsValid()
            .Subscribe(x =>
            {
                SaveButtonEnabled = x;
            });

        #endregion


    }
    
    #endregion
    
    #region METHODS

    private async Task ExecuteDeleteFileAsync(FileListing file)
    {
        try
        {
            var messageBoxConfirm = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["FileDeleteConfirmationMSG"]  ,
                    ButtonDefinitions = ButtonEnum.OkAbort,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner,
                    Icon = Icon.Question,
                });
                        
            var confirmation = await messageBoxConfirm.ShowAsync();

            if (confirmation == ButtonResult.Ok)
            {
                FilesService.DeleteFile(file.UniqueName);

                if (Attachments == null) throw new Exception("Unexpected error deleting file");

                Attachments.Remove(file);

            }

        }
        catch (Exception ex)
        {
            var msgSelect = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["FileDeletionErrorMSG"] + " :" + ex.Message ,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelect.ShowAsync();
        }
    }
    
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
    
    private async Task ExecuteSaveAndCloseAsync()
    {
        await ExecuteSaveAsync();
        ParentWindow?.Close();
    }
    
    private async Task ExecuteCloseAsync()
    {
        var msgConfirm = MessageBoxManager
            .GetMessageBoxStandard(   new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["All non saved data will be lost. Do you want to continue?"], 
                Icon = Icon.Warning,
                ButtonDefinitions = ButtonEnum.YesNo,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            });

        var result = await msgConfirm.ShowAsync();
        
        if(result != ButtonResult.Yes) return;
        
        ParentWindow?.Close();
    }

    private async Task ExecuteFileAddAsync()
    {
        if (WindowOperationType == OperationType.Create)
        {
            var msgSelect = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["You need to save first"] ,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelect.ShowAsync();
            return;
        }
        
        Log.Debug("Adding File ...");
        
        var topLevel = TopLevel.GetTopLevel(ParentWindow);
        
        var file = await topLevel!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = Localizer["AddDocumentMSG"],
        });
        
        if (file.Count == 0) return;
        
        var result = await FilesService.UploadFileAsync(file.First().Path, Incident.Id,
            AuthenticationService.AuthenticatedUserInfo!.UserId!.Value, FileCollectionType.IncidentFile);
        
        Attachments.Add(result);
    }
    
    private async Task LoadAttachmentsAsync()
    {
        if(Incident.Id == 0) return;
        
        var files = await IncidentsService.GetAttachmentsAsync(Incident.Id);
        
        //if(files == null) return;
        
        Attachments = new ObservableCollection<FileListing>(files);
    }
    
    private async Task LoadIrpIdsAsync()
    {
        if(Incident.Id == 0) return;
        
        var irpIds = await IncidentsService.GetIncidentResponsPlanIdsByIdAsync(Incident.Id);
        
        //if(irpIds == null) return;
        
        IncidentResponsePlanIds = new ObservableCollection<int>(irpIds);
        
        var selectedPlans = new ObservableCollection<SelectEntity>(IncidentResponsePlans.Where(irp => irpIds.Contains(irp.Id)).Select(irp => new SelectEntity(irp.Id.ToString(), irp.Name)));

        SelectedPlans = selectedPlans;
        
        AvailablePlans = new ObservableCollection<SelectEntity>(IncidentResponsePlans.Where(irp => !irpIds.Contains(irp.Id)).Select(irp => new SelectEntity(irp.Id.ToString(), irp.Name)));
        
    }

    private async Task ExecuteSaveAsync()
    {
        if(string.IsNullOrEmpty(Incident.Description))
        {
            var msgSelect = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["Please fill the description field"] ,
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelect.ShowAsync();
            return;
        }
        
        if(WindowOperationType == OperationType.Create)
        {
            await CreateIncidentAsync();
        }
        else if(WindowOperationType == OperationType.Edit)
        {
            await UpdateIncidentAsync();
        }
    }
    
    private bool VerifyIncidentResponsePlans()
    {
        var hasChanges = false;
        
        var selectedIds = SelectedPlans.Select(sp => Convert.ToInt32(sp.Key)).ToList();
        
        NewIncidentResponsePlanIds = selectedIds;        
        
        if(selectedIds.Count != IncidentResponsePlanIds.Count)
        {
            return true;
        }
        
        Parallel.ForEach(IncidentResponsePlanIds, id =>
        {
            if(!selectedIds.Contains(id))
            {
                hasChanges = true;
            }
        });

        return hasChanges;

    }

    private async Task CreateIncidentAsync()
    {
        try
        {
            
            if(!EnableFreeNaming)
            {
                var sequence = await IncidentsService.GetNextSequenceAsync(Incident.Year);
                Incident.Sequence = sequence;
                AdjustIncidentName();
            }

            if (VerifyIncidentResponsePlans())
            {
                var messageBoxConfirm = MessageBoxManager
                    .GetMessageBoxStandard(   new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Warning"],
                        ContentMessage = Localizer["IncidentResponsePlansChangesMSG"]  ,
                        ButtonDefinitions = ButtonEnum.OkAbort,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Icon = Icon.Question,
                    });
                        
                var confirmation = await messageBoxConfirm.ShowAsync();

                if (confirmation != ButtonResult.Ok)
                {
                    return;
                }
            }
            
            
            var incident = await IncidentsService.CreateAsync(Incident);   
            Incident = incident;
            
            if(NewIncidentResponsePlanIds.Count > 0)
            {
                await IncidentsService.AssociateIncidentResponsPlanIdsByIdAsync(Incident.Id, NewIncidentResponsePlanIds);
            }
            
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
        try
        {
            var changedIrps = VerifyIncidentResponsePlans();
            if (changedIrps)
            {
                var messageBoxConfirm = MessageBoxManager
                    .GetMessageBoxStandard(   new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Warning"],
                        ContentMessage = Localizer["IncidentResponsePlansChangesMSG"]  ,
                        ButtonDefinitions = ButtonEnum.OkAbort,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Icon = Icon.Question,
                    });
                        
                var confirmation = await messageBoxConfirm.ShowAsync();

                if (confirmation != ButtonResult.Ok)
                {
                    return;
                }
            }
            
            var incident = await IncidentsService.UpdateAsync(Incident);   
            Incident = incident;
            
            if(changedIrps) await IncidentsService.AssociateIncidentResponsPlanIdsByIdAsync(Incident.Id, NewIncidentResponsePlanIds);
            
            OnIncidentUpdated(new ()
            {
                OperationType = OperationType.Edit,
                Incident = incident
            });
            
            
            var msgBox1 = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Success"],
                    ContentMessage = Localizer["Incident updated successfully"],
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
                    ContentMessage = Localizer["Error updating incident"],
                    Icon = Icon.Warning,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgBox1.ShowAsync();
            
            Log.Error(ex, "Error updating incident");
        }
    }

    
    private void AdjustAutoComplete()
    {
        if(ParentWindow == null)
        {
            return;
        }
        var userListingBox = ParentWindow!.Get<AutoCompleteBox>("UserListingBox");
        
        /*if(userListingBox == null)
        {
            return;
        }*/
        
        userListingBox.AsyncPopulator = GetUserByNameAsync;
        userListingBox.TextSelector = TextSelector;
    }

    private string TextSelector(string? text, string? item)
    {
        return item ?? string.Empty;
    }

    private void AdjustIncidentName()
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
            AdjustIncidentName();
        }
        
        await LoadPeopleAsync();
        await LoadImpactedEntitiesListAsync();
        await LoadUsersAsync();
        await LoadIncidentResponsePlansAsync();
        await LoadAttachmentsAsync();
        await LoadIrpIdsAsync();
        
        if(IsEdit)
        {
            if (Incident.ReportEntityId != null)
            {
                SelectedReporter = People.FirstOrDefault(p => p.Contains(Incident.ReportEntityId!.ToString()!)) ??
                                   string.Empty;
            }else SelectedReporter = string.Empty;

            if(Incident.ImpactedEntityId != null)
            {
                SelectedImpactedEntity =ImpactedEntitiesList.FirstOrDefault( p => p.Contains(Incident.ImpactedEntityId!.ToString()!)) ?? string.Empty; 
            }else SelectedImpactedEntity = string.Empty;

            if (Incident.AssignedToId != null)
            {
                SelectedAssignee = Users.FirstOrDefault(u => u.Id == Incident.AssignedToId)!;
            }else SelectedAssignee = null;
            
            
            SelectedStatus = StatusItems.Find(x => x.IntStatus == Incident.Status) ?? StatusItems.FirstOrDefault(x => x.IntStatus == (int)IntStatus.Active)!;
            SelectedCategory = Categories.Find(x => x.DbName == Incident.Category) ?? Categories.FirstOrDefault(x => x.DbName == "not_specified")!;
            
            ReportDate = new DateTimeOffset(Incident.ReportDate);
            Duration = Convert.ToDecimal(Incident.Duration!.Value.TotalHours);
        }

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
        var incidentResponsePlans = await IncidentResponsePlansService.GetAllApprovedAsync();
        
        IncidentResponsePlans = new ObservableCollection<IncidentResponsePlan>(incidentResponsePlans);
        
        AvailablePlans = new ObservableCollection<SelectEntity>(incidentResponsePlans.Select(irp => new SelectEntity(irp.Id.ToString(), irp.Name)));
    }
    
    private async Task LoadPeopleAsync()
    {
        var people = await EntitiesService.GetAllAsync("person");

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
using GUIClient.ViewModels.Dialogs.Results;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs;
using GUIClient.Validation;
using GUIClient.Interfaces;
using System.Windows.Input;
using System.Threading;
using GUIClient.Tools;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;
using Mapster;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using ClientServices;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using Model.DTO;
using Model.Exceptions;
using Model.File;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using Serilog;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

/// <summary>
/// Plans or revises the mitigation of a risk. Migrated onto the single dialog stack (IX-2):
/// Esc/Ctrl+S, owner-centring and the typed result all come from the base class, and the
/// reference data is now awaited before the edit-mode fields are populated from it (previously
/// the constructor read collections a fire-and-forget load had not necessarily filled yet).
/// </summary>
public class EditMitigationViewModel
    : ParameterizedDialogViewModelBaseAsync<MitigationDialogResult, MitigationDialogParameter>, ISaveableDialog
{
    #region LANGUAGE

    public string StrMitigation { get; }
    public string StrSubmissionDate { get; }
    public string StrSolution { get; }
    public string StrPlannedDate { get; }
    public string StrPlanningStrategy { get; }
    public string StrSecurityRequirements { get; }
    public string StrMitigationEffort { get; }
    public string StrMitigationCost { get; }
    public string StrSecurityRecommendation { get; }
    public string StrMitigationOwner { get; }
    public string StrMitigationTeam { get; }
    public string StrMitigationPercent { get; }
    public string StrDocumentation { get; }
    public new string StrSave { get; }
    public new string StrCancel { get; }
    public string StrFiles { get; }
    public string StrAddDocumentMsg { get; }
    public string StrSaveDocumentMsg { get; }
    public string StrDownload { get; }
    public string StrDelete { get; }
    public string StrAddFile { get; }

    #endregion

    #region INTERNAL FIELDS

    private OperationType _operationType;
    private Mitigation? _mitigation;
    private int _riskId;

    #endregion
    
    #region SERVICES
    private readonly IMitigationService _mitigationService = GetService<IMitigationService>();
    private readonly IAuthenticationService _authenticationService = GetService<IAuthenticationService>();
    private readonly ITeamsService _teamsService = GetService<ITeamsService>();
    private readonly IFilesService _filesService = GetService<IFilesService>();
    private readonly IUsersService _usersService = GetService<IUsersService>();
    
    #endregion

    public EditMitigationViewModel()
    {
        StrMitigation = Localizer["Mitigation"];
        StrSubmissionDate = Localizer["SubmissionDate"] + ":";
        StrSolution = Localizer["Solution"] + ":";
        StrPlannedDate = Localizer["PlannedDate"] + ":";
        StrPlanningStrategy = Localizer["PlanningStrategy"] + ":";
        StrSecurityRequirements = Localizer["SecurityRequirements"] + ":";
        StrMitigationEffort = Localizer["MitigationEffort"] + ":";
        StrMitigationCost = Localizer["MitigationCost"] + ":";
        StrSecurityRecommendation = Localizer["SecurityRecommendation"] + ":";
        StrMitigationOwner = Localizer["MitigationOwner"] + ":";
        StrMitigationTeam = Localizer["MitigationTeam"] + ":";
        StrMitigationPercent = Localizer["MitigationPercent"] + ":";
        StrDocumentation = Localizer["Documentation"] + ":";
        StrSave = Localizer["Save"];
        StrCancel = Localizer["Cancel"];
        StrFiles = Localizer["Files"];
        StrAddDocumentMsg = Localizer["AddDocumentMsg"];
        StrSaveDocumentMsg = Localizer["SaveDocumentMsg"];
        StrDownload = Localizer["Download"];
        StrDelete = Localizer["Delete"];
        StrAddFile = Localizer["Add"];

        BtSaveClicked = ReactiveCommand.CreateFromTask(ExecuteSave,
            this.WhenAnyValue(x => x.SaveEnabled));
        BtCancelClicked = ReactiveCommand.Create(ExecuteCancel);
        BtFileAddClicked = ReactiveCommand.CreateFromTask(ExecuteAddFile);
        BtFileDownloadClicked = ReactiveCommand.CreateFromTask<FileListing>(ExecuteFileDownload);
        BtFileDeleteClicked = ReactiveCommand.CreateFromTask<FileListing>(ExecuteFileDelete);

        #region VALIDATION

        this.ValidationRule(
            viewModel => viewModel.SelectedMitigationOwner, 
            prob => prob != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedMitigationCost, 
            prob => prob != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedMitigationEffort, 
            prob => prob != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedPlanningStrategy, 
            prob => prob != null,
            Localizer["PleaseSelectOneMSG"]);

        this.ValidationRule(
            viewModel => viewModel.SelectedMitigationTeam,
            team => team != null,
            Localizer["PleaseSelectOneMSG"]);

        this.IsValid()
            .Subscribe(x =>
            {
                SaveEnabled = x;
            });

        #endregion
    }

    /// <inheritdoc />
    public override async Task ActivateAsync(MitigationDialogParameter parameter,
        CancellationToken cancellationToken = default)
    {
        _operationType = parameter.Operation;
        _riskId = parameter.RiskId;

        if (_operationType == OperationType.Edit && parameter.Mitigation == null)
        {
            throw new InvalidParameterException("mitigation", "Mitigation cannot be null on edit operations");
        }

        _mitigation = _operationType == OperationType.Create ? new Mitigation() : parameter.Mitigation;

        // Awaited, not fire-and-forget: the edit-mode fields below are looked up *in* these
        // collections, so they have to be populated first.
        await LoadDataAsync();

        if (_operationType == OperationType.Create)
        {
            SubmissionDate = new DateTimeOffset(DateTime.Now);
            Solution = "";
            PlannedDate = new DateTimeOffset(DateTime.Now + TimeSpan.FromDays(2));
            _mitigation!.LastUpdate = DateTime.Now;
            SecurityRequirements = "";
            RecommendedSolution = "";
            SelectedMitigationOwner = Users.ToList().Find(x => x.Id == _authenticationService.AuthenticatedUserInfo!.UserId);
            MitigationPercent = 0;
        }
        else
        {
            SubmissionDate = new DateTimeOffset(_mitigation!.SubmissionDate);
            Solution = _mitigation!.CurrentSolution;
            PlannedDate = new DateTimeOffset(_mitigation!.PlanningDate.ToDateTime(new TimeOnly(0,0)));
            _mitigation.LastUpdate = DateTime.Now;
            SelectedPlanningStrategy = PlanningStrategies.ToList().Find(x => x.Value == _mitigation.PlanningStrategy);
            SecurityRequirements = _mitigation.SecurityRequirements;
            SelectedMitigationEffort = MitigationEfforts.ToList().Find(x => x.Value == _mitigation.MitigationEffort);
            SelectedMitigationCost = MitigationCosts.ToList().Find(x => x.Value == _mitigation.MitigationCost);
            RecommendedSolution = _mitigation.SecurityRecommendations;
            SelectedMitigationOwner = Users.ToList().Find(x => x.Id == _mitigation.MitigationOwner);

            var mitigationTeams = _mitigationService.GetTeamsById(_mitigation.Id);
            if (mitigationTeams != null)
                SelectedMitigationTeam =
                    Teams.FirstOrDefault(at => mitigationTeams.Select(mt => mt.Value).Contains(at.Value));
            
            MitigationPercent = _mitigation.MitigationPercent;
        }
    }

    #region PROPERTIES

        public ReactiveCommand<RxVoid, RxVoid> BtFileAddClicked { get; }
        public ReactiveCommand<RxVoid, RxVoid> BtSaveClicked { get; }
        public ReactiveCommand<RxVoid, RxVoid> BtCancelClicked { get; }

        /// <inheritdoc />
        public ICommand? SaveCommand => BtSaveClicked;
        public ReactiveCommand<FileListing, RxVoid> BtFileDownloadClicked { get; }
        public ReactiveCommand<FileListing, RxVoid> BtFileDeleteClicked { get; }
        
        private bool _saveEnabled;
        public bool SaveEnabled
        {
            get => _saveEnabled;
            set => this.RaiseAndSetIfChanged(ref _saveEnabled, value);
        }
        
        private ObservableCollection<FileListing>? _selectedMitigationFiles;
    
        public ObservableCollection<FileListing>? SelectedMitigationFiles
        {
            get => _selectedMitigationFiles;
            set => this.RaiseAndSetIfChanged(ref _selectedMitigationFiles, value);
        }

        private ObservableCollection<Team> _teams = new();
        public ObservableCollection<Team> Teams
        {
            get => _teams;
            set => this.RaiseAndSetIfChanged(ref _teams, value);
        }
       
        private Team? _selectedMitigationTeam;
        public Team? SelectedMitigationTeam
        {
            get => _selectedMitigationTeam;
            set => this.RaiseAndSetIfChanged(ref _selectedMitigationTeam, value);
        }

        private ObservableCollection<UserListing> _users = new ();
        public ObservableCollection<UserListing> Users
        {
            get => _users;
            set => this.RaiseAndSetIfChanged(ref _users, value);
        }

        private UserListing? _selectedMitigationOwner;
        public UserListing? SelectedMitigationOwner
        {
            get => _selectedMitigationOwner;
            set => this.RaiseAndSetIfChanged(ref _selectedMitigationOwner, value);
        }
        
        private string _recommendedSolution = "";
        public string RecommendedSolution
        {
            get => _recommendedSolution;
            set => this.RaiseAndSetIfChanged(ref _recommendedSolution, value);
        }

        private ObservableCollection<MitigationEffort> _mitigationEfforts = new();
        public ObservableCollection<MitigationEffort> MitigationEfforts
        {
            get => _mitigationEfforts;
            set => this.RaiseAndSetIfChanged(ref _mitigationEfforts, value);
        }

        //public List<MitigationEffort> MitigationEfforts => _mitigationService.GetEfforts()!;
        
        private MitigationEffort? _selectedMitigationEffort;
        public MitigationEffort? SelectedMitigationEffort
        {
            get => _selectedMitigationEffort;
            set => this.RaiseAndSetIfChanged(ref _selectedMitigationEffort, value);
        }
        
        private ObservableCollection<MitigationCost> _mitigationCosts = new();
        public ObservableCollection<MitigationCost> MitigationCosts 
        {
            get => _mitigationCosts;
            set => this.RaiseAndSetIfChanged(ref _mitigationCosts, value);
        }
        
        //public List<MitigationCost> MitigationCosts => _mitigationService.GetCosts()!;
        
        
        
        private MitigationCost? _selectedMitigationCost;
        public MitigationCost? SelectedMitigationCost
        {
            get => _selectedMitigationCost;
            set => this.RaiseAndSetIfChanged(ref _selectedMitigationCost, value);
        }


        private ObservableCollection<PlanningStrategy> _planningStrategies = new ();

        public ObservableCollection<PlanningStrategy> PlanningStrategies
        {
            get => _planningStrategies;
            set => this.RaiseAndSetIfChanged(ref _planningStrategies, value);
        }

        private PlanningStrategy? _selectedPlanningStrategy;
        public PlanningStrategy? SelectedPlanningStrategy
        {
            get => _selectedPlanningStrategy;
            set => this.RaiseAndSetIfChanged(ref _selectedPlanningStrategy, value);
        }
        
        private DateTimeOffset _submissionDate;
        public DateTimeOffset SubmissionDate
        {
            get => _submissionDate;
            set => this.RaiseAndSetIfChanged(ref _submissionDate, value);
        }
        
        private string _solution = "";
        public string Solution
        {
            get => _solution;
            set => this.RaiseAndSetIfChanged(ref _solution, value);
        }
        
        private string _securityRequirements = "";
        public string SecurityRequirements
        {
            get => _securityRequirements;
            set => this.RaiseAndSetIfChanged(ref _securityRequirements, value);
        }
        
        private DateTimeOffset _plannedDate;
        public DateTimeOffset PlannedDate
        {
            get => _plannedDate;
            set => this.RaiseAndSetIfChanged(ref _plannedDate, value);
        }
    
        private decimal _mitigationPercent;
        public decimal MitigationPercent
        {
            get => _mitigationPercent;
            set => this.RaiseAndSetIfChanged(ref _mitigationPercent, value);
        }

    #endregion
    
    #region METHODS

    private async Task LoadDataAsync()
    {
        var costs = await _mitigationService.GetCostsAsync();
        var strategies = await _mitigationService.GetStrategiesAsync();
        var efforts = await _mitigationService.GetEffortsAsync();
        
        MitigationCosts = costs != null ? new ObservableCollection<MitigationCost>(costs) : new ObservableCollection<MitigationCost>();
        PlanningStrategies = strategies != null ? new ObservableCollection<PlanningStrategy>(strategies) : new ObservableCollection<PlanningStrategy>();
        MitigationEfforts = efforts != null ? new ObservableCollection<MitigationEffort>(efforts) : new ObservableCollection<MitigationEffort>();

        SelectedMitigationFiles = new ObservableCollection<FileListing>( await _mitigationService.GetFilesAsync(_mitigation?.Id ?? 0));
        
        Users = new ObservableCollection<UserListing>(await _usersService.GetAllAsync());
        Teams = new ObservableCollection<Team>(await _teamsService.GetAllAsync());
    }

    private async Task ExecuteAddFile()
    {
        
        
        var storageProvider = StorageProviderAccessor.Current;
        if (storageProvider == null) return;

        var file = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = StrAddDocumentMsg,
        });

        if (file.Count == 0) return;

        if (_mitigation == null) return;

        try
        {
            var result = await _filesService.UploadFileAsync(file.First().Path, _mitigation.Id,
                _authenticationService.AuthenticatedUserInfo!.UserId!.Value, FileCollectionType.MitigationFile);

            SelectedMitigationFiles ??= new ObservableCollection<FileListing>();
            SelectedMitigationFiles.Add(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error uploading file to mitigation {Id}", _mitigation.Id);

            var msgError = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorUploadingFileMSG"],
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgError.ShowAsync();
        }

    }

    private async Task ExecuteFileDownload(FileListing listing)
    {

        var storageProvider = StorageProviderAccessor.Current;
        if (storageProvider == null) return;

        if (listing.Type != null)
        {
            var file = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = StrSaveDocumentMsg,
                DefaultExtension = _filesService.ConvertTypeToExtension(listing.Type),
                SuggestedFileName = listing.Name + _filesService.ConvertTypeToExtension(listing.Type),
            
            });

            if (file == null) return;
            
            _ = _filesService.DownloadFileAsync(listing.UniqueName, file.Path);
        }
        else
        {
            throw new Exception("File type is null");
        }
    }

    private async Task ExecuteFileDelete(FileListing listing)
    {
        try
        {
            if (await ConfirmationDialog.ConfirmDeleteAsync(listing.Name))
            {
                _filesService.DeleteFile(listing.UniqueName);

                if (SelectedMitigationFiles == null) throw new Exception("Unexpected error deleting file");

                SelectedMitigationFiles.Remove(listing);

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
    
    private async Task ExecuteSave()
    {
        // IX-4: re-check before dereferencing the Selected* values in SyncMitigation.
        if (!SaveEnabled) return;

        SyncMitigation();
        if (_operationType == OperationType.Create)
        {
            try
            {
                var mitigationDto = _mitigation!.Adapt<MitigationDto>();
                
                mitigationDto.SubmittedBy = _authenticationService.AuthenticatedUserInfo!.UserId!.Value;
                
                var newMitigation = _mitigationService.Create(mitigationDto);
                if (newMitigation != null)
                {
                    try
                    {
                        _mitigationService.AssociateMitigationToTeam(newMitigation.Id, SelectedMitigationTeam!.Value);
                        Close(new MitigationDialogResult { Action = ResultActions.Ok });
                    }catch(Exception e)
                    {
                        Logger.Error("Error associating mitigation to team: {Message}", e.Message);

                    }
                }
            }catch(Exception e)
            {
                Logger.Error("Error creating mitigation: {Message}", e.Message);
                
                var msgError = MessageBoxManager
                    .GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Error"],
                        ContentMessage = Localizer["ErrorMitigationMSG"],
                        Icon = Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                await msgError.ShowAsync();
                
            }

        }
        else
        {
            try
            {
                if(_mitigation == null) throw new InvalidParameterException("_mitigation", "Mitigation is null");
                
                var mitigationDto = _mitigation!.Adapt<MitigationDto>();
                mitigationDto.SubmittedBy = _authenticationService.AuthenticatedUserInfo!.UserId!.Value;
                
                _mitigationService.Save(mitigationDto);
                _mitigationService.DeleteTeamsAssociations(_mitigation.Id);
                _mitigationService.AssociateMitigationToTeam(_mitigation.Id, SelectedMitigationTeam!.Value);
                Close(new MitigationDialogResult { Action = ResultActions.Ok });

            }catch(Exception e)
            {
                Logger.Error("Error saving mitigation: {Message}", e.Message);
                
                var msgError = MessageBoxManager
                    .GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Error"],
                        ContentMessage = Localizer["ErrorMitigationMSG"],
                        Icon = Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                await msgError.ShowAsync();
                
            }
        }
    }

    private void SyncMitigation()
    {
        _mitigation ??= new Mitigation();
        _mitigation.RiskId = _riskId;
        _mitigation.MitigationCost = SelectedMitigationCost!.Value;
        _mitigation.MitigationEffort = SelectedMitigationEffort!.Value;
        _mitigation.MitigationOwner = SelectedMitigationOwner!.Id;
        _mitigation.PlanningStrategy = SelectedPlanningStrategy!.Value;
        _mitigation.PlanningDate = new DateOnly(PlannedDate.DateTime.Year, PlannedDate.DateTime.Month, PlannedDate.DateTime.Day);
        _mitigation.SecurityRecommendations = RecommendedSolution;
        _mitigation.SecurityRequirements = SecurityRequirements;
        _mitigation.CurrentSolution = Solution;
        _mitigation.SubmissionDate = SubmissionDate.DateTime;
        _mitigation.MitigationPercent = Convert.ToInt32(MitigationPercent);
    }
    
    private void ExecuteCancel() =>
        Close(new MitigationDialogResult { Action = ResultActions.Cancel });

    #endregion
}
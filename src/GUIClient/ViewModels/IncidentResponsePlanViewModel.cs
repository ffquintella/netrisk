using GUIClient.ViewModels.Dialogs.Results;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs;
using GUIClient.Navigation;
using GUIClient.Interfaces;
using System.Windows.Input;
using System.Threading;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using DAL.Entities;
using GUIClient.Models;
using Microsoft.AspNetCore.Authentication;
using Model.Authentication;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ClientServices;
using ClientServices.Interfaces;
using GUIClient.Events;
using GUIClient.Tools;
using GUIClient.Views;
using Model;
using Model.DTO;
using Model.File;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using Serilog;
using Tools.IncidentResponsePlans;
using Exception = System.Exception;

namespace GUIClient.ViewModels;

/// <summary>
/// Creates, edits or views an incident-response plan. Migrated onto the single dialog stack
/// (IX-2) and made <b>modal</b> (IX-1): it used to open modeless while its own task children
/// opened modal, which is the inverted modality the standard calls out. Task children now hand
/// their result back as a typed result and this view-model updates its task list in place.
/// </summary>
public class IncidentResponsePlanViewModel
    : ParameterizedDialogViewModelBaseAsync<IrpDialogResult, IrpDialogParameter>, ISaveableDialog
{
    #region LANGUAGE

    public string StrTitle => Localizer["Incident Response Plan"];
    public string StrDate => Localizer["Date"] + ":";
    public string StrRisk => Localizer["Risk"];
    public string StrName => Localizer["Name"];
    public string StrPlan => Localizer["Plan"];
    public string StrDescription => Localizer["Description"];
    public string StrComments => Localizer["Comments"];
    public string StrHasBeenTested => Localizer["Has been tested"];
    private string StrHasBeenUpdated => Localizer["Has been updated"];
    public string StrHasBeenExercised => Localizer["Has been exercised"];
    public string StrHasBeenApproved => Localizer["Has been approved"];
    public string StrHasBeenReviewed => Localizer["Has been reviewed"];
    private string StrStatus => Localizer["Status"];
    public string StrLifeCicleStatus => Localizer["Life cycle status"];
    public new string StrSave => Localizer["Save"];
    public new string StrCancel => Localizer["Cancel"];
    public string StrMetadata => Localizer["Metadata"];
    public string StrCreationDate => Localizer["Creation date"] + ":";
    public string StrLastUpdate => Localizer["Last update"] + ":";
    public string StrLoggedUser => Localizer["Logged user"] + ":";
    public string StrTasks => Localizer["Tasks"];
    public string StrGantt => Localizer["Response Timeline"];
    public string StrAttachments => Localizer["Attachments"];
    public string StrDownload => Localizer["Download"];
    public string StrDelete => Localizer["Delete"];
    public string StrAdd => Localizer["Add"];
    public new string StrClose => Localizer["Close"];
    public string StrApprover => Localizer["Approver"];
    public string StrReviewer => Localizer["Reviewer"];
    private string StrUpdater => Localizer["Updater"];
    private string StrTrainer => Localizer["Trainer"];
    private string StrExecutioner => Localizer["Executioner"];
    public string StrTester => Localizer["Tester"];
    public string StrExerciser => Localizer["Exerciser"];

#endregion
    
    #region FIELDS
    private readonly Thickness _editAlignMargin = new Thickness(0, 10, 5, 0);
    private readonly Thickness _readAlignMargin = new Thickness(0, 10, 5, 0);
    private readonly Thickness _viewAlignMargin = new Thickness(0, 0, 5, 0);
    #endregion
    
    #region PROPERTIES
    
    private bool _canSave;
    
    public bool CanSave
    {
        get => _canSave;
        set => this.RaiseAndSetIfChanged(ref _canSave, value);
    }
    
    private bool _canCancel;
    
    public bool CanCancel
    {
        get => _canCancel;
        set => this.RaiseAndSetIfChanged(ref _canCancel, value);
    }
    
    private bool _canClose;
    
    public bool CanClose
    {
        get => _canClose;
        set => this.RaiseAndSetIfChanged(ref _canClose, value);
    }

    public bool CanExercise
    {
        get
        {
            if(AuthenticationService.AuthenticatedUserInfo == null) return false;
            if(AuthenticationService.AuthenticatedUserInfo.IsAdmin) return true;
            if(AuthenticationService.AuthenticatedUserInfo.UserPermissions == null) return false;
            if(AuthenticationService.AuthenticatedUserInfo.UserPermissions.Contains("irp-exercise")) return true;
            return false;
        }
    }
    
    public bool CanTest
    {
        get
        {
            if(AuthenticationService.AuthenticatedUserInfo == null) return false;
            if(AuthenticationService.AuthenticatedUserInfo.IsAdmin) return true;
            if(AuthenticationService.AuthenticatedUserInfo.UserPermissions == null) return false;
            if(AuthenticationService.AuthenticatedUserInfo.UserPermissions.Contains("irp-test")) return true;
            return false;
        }
    }
    
    public bool CanUpdate
    {
        get
        {
            if(AuthenticationService.AuthenticatedUserInfo == null) return false;
            if(AuthenticationService.AuthenticatedUserInfo.IsAdmin) return true;
            if(AuthenticationService.AuthenticatedUserInfo.UserPermissions == null) return false;
            if(AuthenticationService.AuthenticatedUserInfo.UserPermissions.Contains("irp-update")) return true;
            return false;
        }
    }
    
    public bool CanApprove
    {
        get
        {
            if(AuthenticationService.AuthenticatedUserInfo == null) return false;
            if(AuthenticationService.AuthenticatedUserInfo.IsAdmin) return true;
            if(AuthenticationService.AuthenticatedUserInfo.UserPermissions == null) return false;
            if(AuthenticationService.AuthenticatedUserInfo.UserPermissions.Contains("irp-approve")) return true;
            return false;
        }
    }
    
    public bool CanReview
    {
        get
        {
            if(AuthenticationService.AuthenticatedUserInfo == null) return false;
            if(AuthenticationService.AuthenticatedUserInfo.IsAdmin) return true;
            if(AuthenticationService.AuthenticatedUserInfo.UserPermissions == null) return false;
            if(AuthenticationService.AuthenticatedUserInfo.UserPermissions.Contains("irp-review")) return true;
            return false;
        }
    }


    private IncidentResponsePlan? _incidentResponsePlan;
    public IncidentResponsePlan? IncidentResponsePlan
    {
        get => _incidentResponsePlan;
        set => this.RaiseAndSetIfChanged(ref _incidentResponsePlan, value);
    }
    
    private ObservableCollection<FileListing> _attachments = new ObservableCollection<FileListing>();
    
    public ObservableCollection<FileListing> Attachments
    {
        get => _attachments;
        set => this.RaiseAndSetIfChanged(ref _attachments, value);
    }

    private ObservableCollection<IncidentResponsePlanTask> _tasks = new();
    
    public ObservableCollection<IncidentResponsePlanTask> Tasks
    {
        get => _tasks;
        set => this.RaiseAndSetIfChanged(ref _tasks, value);
    }

    private IncidentResponsePlanTask? _selectedTask;
    
    public IncidentResponsePlanTask? SelectedTask
    {
        get => _selectedTask;
        set => this.RaiseAndSetIfChanged(ref _selectedTask, value);
    }
    
    private Risk? _relatedRisk;
    
    public Risk? RelatedRisk
    {
        get => _relatedRisk;
        set => this.RaiseAndSetIfChanged(ref _relatedRisk, value);
    }
    
    private OperationType _windowOperationType;
    
    public OperationType WindowOperationType
    {
        get => _windowOperationType;
        set
        {
            
            IsEditOperation = false;
            IsCreateOperation = false;
            IsViewOperation = false;
            IsEditOrViewOperation = false;
            IsCreateOrEditOperation = false;

            if (value == OperationType.Edit)
            {
                IsEditOperation = true;
                IsEditOrViewOperation = true;
                IsCreateOrEditOperation = true;
            }
            if (value == OperationType.Create)
            {
                IsCreateOperation = true;
                IsCreateOrEditOperation = true;
            }
            if (value == OperationType.View)
            {
                IsViewOperation = true;
                IsEditOrViewOperation = true;
            }
            
            this.RaiseAndSetIfChanged(ref _windowOperationType, value);
        }
    }

    private bool _isCreateOperation;
    
    public bool IsCreateOperation
    {
        get => _isCreateOperation;
        set
        {
            if (value) IsCreateOrEditOperation = value;
            else IsCreateOrEditOperation = IsEditOperation;
            this.RaiseAndSetIfChanged(ref _isCreateOperation, value);
        }
    }

    private bool _isEditOrViewOperation;
    
    public bool IsEditOrViewOperation
    {
        get => _isEditOrViewOperation;
        set => this.RaiseAndSetIfChanged(ref _isEditOrViewOperation, value);
    }
    
    private bool _isCreateOrEditOperation;
    
    public bool IsCreateOrEditOperation
    {
        get => _isCreateOrEditOperation;
        set => this.RaiseAndSetIfChanged(ref _isCreateOrEditOperation, value);
    }
    
    private bool _isEditOperation;
    
    public bool IsEditOperation
    {
        get => _isEditOperation;
        set
        {
            if (value)
            {
                IsEditOrViewOperation = value;
                IsCreateOrEditOperation = value;
            }
            else
            {
                IsEditOrViewOperation = IsViewOperation;
                IsCreateOrEditOperation = IsCreateOperation;
            }
            this.RaiseAndSetIfChanged(ref _isEditOperation, value);
        }
    }

    private bool _isViewOperation;
    
    public bool IsViewOperation
    {
        get => _isViewOperation;
        set
        {
            if (value) IsEditOrViewOperation = value;
            else IsEditOrViewOperation = IsEditOperation;
            this.RaiseAndSetIfChanged(ref _isViewOperation, value);
        }
    }

    private string _name = "";
    public string Name
    {
        get => _name;
        set => this.RaiseAndSetIfChanged(ref _name, value);
    }

    private string _description = "";
    public string Description
    {
        get => _description;
        set => this.RaiseAndSetIfChanged(ref _description, value);
    }
    
    private string _notes = "";
    public string Notes
    {
        get => _notes;
        set => this.RaiseAndSetIfChanged(ref _notes, value);
    }
    
    private bool _hasBeenTested;
    
    public bool HasBeenTested
    {
        get => _hasBeenTested;
        set
        {
            if(!IsViewOperation) ShowTesterTextBox = value;
            this.RaiseAndSetIfChanged(ref _hasBeenTested, value);
        }
    }

    private bool _hasBeenUpdated;
    
    public bool HasBeenUpdated
    {
        get => _hasBeenUpdated;
        set
        {
            if(!IsViewOperation) ShowUpdaterTextBox = value;
            this.RaiseAndSetIfChanged(ref _hasBeenUpdated, value);
        }
    }

    private bool _hasBeenExercised;
    
    public bool HasBeenExercised
    {
        get => _hasBeenExercised;
        set
        {
            if(!IsViewOperation) ShowExerciserTextBox = value;
            this.RaiseAndSetIfChanged(ref _hasBeenExercised, value);
        }
    }

    private bool _hasBeenApproved;
    
    public bool HasBeenApproved
    {
        get => _hasBeenApproved;
        set
        {
            if(!IsViewOperation) ShowApproverTextBox = value;
            this.RaiseAndSetIfChanged(ref _hasBeenApproved, value);
        }
    }

    private bool _hasBeenReviewed;
    
    public bool HasBeenReviewed
    {
        get => _hasBeenReviewed;
        set
        {
            if(!IsViewOperation) ShowReviewerTextBox = value;
            this.RaiseAndSetIfChanged(ref _hasBeenReviewed, value);
        }
    }

    private bool _showApproverTextBox;
    public bool ShowApproverTextBox
    {
        get => _showApproverTextBox;
        set => this.RaiseAndSetIfChanged(ref _showApproverTextBox, value);
    }
    
    private bool _showReviewerTextBox;
    public bool ShowReviewerTextBox
    {
        get => _showReviewerTextBox;
        set => this.RaiseAndSetIfChanged(ref _showReviewerTextBox, value);
    }
    
    private bool _showUpdaterTextBox;
    public bool ShowUpdaterTextBox
    {
        get => _showUpdaterTextBox;
        set => this.RaiseAndSetIfChanged(ref _showUpdaterTextBox, value);
    }
    
    
    private bool _showTesterTextBox;
    
    public bool ShowTesterTextBox
    {
        get => _showTesterTextBox;
        set => this.RaiseAndSetIfChanged(ref _showTesterTextBox, value);
    }
    
    private bool _showExerciserTextBox;
    
    public bool ShowExerciserTextBox
    {
        get => _showExerciserTextBox;
        set => this.RaiseAndSetIfChanged(ref _showExerciserTextBox, value);
    }
    
    
    private ObservableCollection<string> _peopleEntities = new ObservableCollection<string>();
    
    public ObservableCollection<string> PeopleEntities
    {
        get => _peopleEntities;
        set => this.RaiseAndSetIfChanged(ref _peopleEntities, value);
    }
    
    private string? _selectedApprover;
    
    public string? SelectedApprover
    {
        get => _selectedApprover;
        set => this.RaiseAndSetIfChanged(ref _selectedApprover, value);
    }
    
    private string? _selectedReviewer;
    
    public string? SelectedReviewer
    {
        get => _selectedReviewer;
        set => this.RaiseAndSetIfChanged(ref _selectedReviewer, value);
    }
    
    private string? _selectedUpdater;
    
    public string? SelectedUpdater
    {
        get => _selectedUpdater;
        set => this.RaiseAndSetIfChanged(ref _selectedUpdater, value);
    }
    
    private string? _selectedTrainer;
    
    public string? SelectedTrainer
    {
        get => _selectedTrainer;
        set => this.RaiseAndSetIfChanged(ref _selectedTrainer, value);
    }
    
    private string? _selectedExecutioner;
    
    public string? SelectedExecutioner
    {
        get => _selectedExecutioner;
        set => this.RaiseAndSetIfChanged(ref _selectedExecutioner, value);
    }
    
    private string? _selectedTester;
    
    public string? SelectedTester
    {
        get => _selectedTester;
        set => this.RaiseAndSetIfChanged(ref _selectedTester, value);
    }
    
    private string? _selectedExerciser;
    
    public string? SelectedExerciser
    {
        get => _selectedExerciser;
        set => this.RaiseAndSetIfChanged(ref _selectedExerciser, value);
    }
    
    
    public DateTime CreationDate => IncidentResponsePlan?.CreationDate ?? DateTime.Now;
    public DateTime LastUpdate => IncidentResponsePlan?.LastUpdate ?? DateTime.Now;
    
    public DateTime LastTestDate => IncidentResponsePlan?.LastTestDate ?? DateTime.Now;
    
    public DateTime LastExerciseDate => IncidentResponsePlan?.LastExerciseDate ?? DateTime.Now;
    
    public DateTime ApprovalDate => IncidentResponsePlan?.ApprovalDate ?? DateTime.Now;
    
    public DateTime LastReviewDate => IncidentResponsePlan?.LastReviewDate ?? DateTime.Now;
    
    
    public int Status
    {
        get => IncidentResponsePlan?.Status ?? 0;
        set
        {
            if (IncidentResponsePlan != null)
            {
                IncidentResponsePlan.Status = value;
            }
        }
    }

    public Thickness AlignMargin
    {
        get
        {
            if (IsEditOperation) return _editAlignMargin;
            if (IsViewOperation) return _viewAlignMargin;
            return _readAlignMargin;
        }   
    }
    
    private AuthenticatedUserInfo? _userInfo;
    
    public AuthenticatedUserInfo? UserInfo
    {
        get => _userInfo;
        set => this.RaiseAndSetIfChanged(ref _userInfo, value);
    }
    
    private bool IsTestOnly { get; set; }
    
    #endregion

    #region SERVICES

    private IDialogService DialogService { get; } = GetService<IDialogService>();
    
        private IIncidentResponsePlansService IncidentResponsePlansService { get; } =  GetService<IIncidentResponsePlansService>();
        private IRisksService RisksService { get; } =  GetService<IRisksService>();
        private IEntitiesService EntitiesService { get; } = GetService<IEntitiesService>();
        private IFilesService FilesService { get; } = GetService<IFilesService>();
        
    #endregion

    #region COMMANDS
    public ReactiveCommand<RxVoid, RxVoid> BtSaveClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtCancelClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtCloseClicked { get; }

    /// <inheritdoc />
    public ICommand? SaveCommand => BtSaveClicked;
    public ReactiveCommand<RxVoid, RxVoid> BtFileAddClicked { get; }
    public ReactiveCommand<FileListing, RxVoid> BtFileDownloadClicked { get; }
    public ReactiveCommand<FileListing, RxVoid> BtFileDeleteClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAddTaskClicked { get; }
    public ReactiveCommand<IncidentResponsePlanTask?, RxVoid> BtDeleteTaskClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtEditTaskClicked { get; }
    
    public ReactiveCommand<RxVoid, RxVoid> BtViewTaskClicked { get; }

    /// <summary>Opens the plan's critical-path Gantt (Track 2 milestone 2.4.3).</summary>
    public ReactiveCommand<RxVoid, RxVoid> BtShowGanttClicked { get; }
    
    
    #endregion
    
    #region EVENTS
    
    
    #endregion

    #region FIELDS

    /// <summary>The plan as persisted, once Save has succeeded; null while nothing is committed.</summary>
    private IncidentResponsePlan? _committedPlan;
    private bool _committedPlanWasCreated;

    #endregion

    #region CONSTRUCTOR
    
    public IncidentResponsePlanViewModel()
    {
        
        BtSaveClicked = ReactiveCommand.CreateFromTask(async () =>
        {
            if (IsCreateOperation)
            {
                await ExecuteCreateAsync();
            }
            else
            {
                await ExecuteUpdateAsync();
            }
        });
        
        BtCancelClicked = ReactiveCommand.CreateFromTask(ExecuteCancelAsync);
        BtCloseClicked = ReactiveCommand.CreateFromTask(ExecuteCloseAsync);
        BtFileAddClicked = ReactiveCommand.CreateFromTask(ExecuteAddFileAsync);
        BtFileDownloadClicked = ReactiveCommand.CreateFromTask<FileListing>(ExecuteDownloadFileAsync);
        BtFileDeleteClicked = ReactiveCommand.CreateFromTask<FileListing>(ExecuteDeleteFileAsync);
        BtAddTaskClicked = ReactiveCommand.CreateFromTask(ExecuteAddTaskAsync);
        BtShowGanttClicked = ReactiveCommand.Create(ExecuteShowGantt);
        BtEditTaskClicked = ReactiveCommand.CreateFromTask(ExecuteEditTaskAsync);
        BtDeleteTaskClicked = ReactiveCommand.CreateFromTask<IncidentResponsePlanTask?>(ExecuteDeleteTaskAsync);
        BtViewTaskClicked = ReactiveCommand.CreateFromTask(ExecuteViewTaskAsync);

        CanSave = false;
        CanClose = true;
        CanCancel = true;

        {
            this.ValidationRule(
                viewModel => viewModel.Name, 
                p => !string.IsNullOrEmpty(p),
                Localizer["PleaseEnterAValidValueMSG"]);
            
            this.ValidationRule(
                viewModel => viewModel.Description, 
                p => !string.IsNullOrEmpty(p),
                Localizer["PleaseEnterAValidValueMSG"]);
            
            this.ValidationRule(
                viewModel => viewModel.SelectedApprover, 
                p =>
                {
                    if (HasBeenApproved)
                    {
                        if (string.IsNullOrEmpty(p)) return false;
                        return PeopleEntities.Contains(p);
                    }
                    return true;
                },
                Localizer["PleaseEnterAValidValueMSG"]);
            
            this.ValidationRule(
                viewModel => viewModel.SelectedReviewer, 
                p =>
                {
                    if (HasBeenReviewed)
                    {
                        if (string.IsNullOrEmpty(p)) return false;
                        return PeopleEntities.Contains(p);
                    }
                    return true;
                },
                Localizer["PleaseEnterAValidValueMSG"]);
            
            this.ValidationRule(
                viewModel => viewModel.SelectedTester, 
                p =>
                {
                    if (HasBeenTested)
                    {
                        if (string.IsNullOrEmpty(p)) return false;
                        return PeopleEntities.Contains(p);
                    }
                    return true;
                },
                Localizer["PleaseEnterAValidValueMSG"]);
            
            this.ValidationRule(
                viewModel => viewModel.SelectedExerciser, 
                p =>
                {
                    if (HasBeenExercised)
                    {
                        if (string.IsNullOrEmpty(p)) return false;
                        return PeopleEntities.Contains(p);
                    }
                    return true;
                },
                Localizer["PleaseEnterAValidValueMSG"]);
            
            this.ValidationRule(
                viewModel => viewModel.SelectedUpdater, 
                p =>
                {
                    if (HasBeenUpdated)
                    {
                        if (string.IsNullOrEmpty(p)) return false;
                        return PeopleEntities.Contains(p);
                    }
                    return true;
                },
                Localizer["PleaseEnterAValidValueMSG"]);
            
            this.IsValid()
                .Subscribe(x =>
                {
                    CanSave = x;
                });
        }


    }
    
    /// <inheritdoc />
    public override async Task ActivateAsync(IrpDialogParameter parameter,
        CancellationToken cancellationToken = default)
    {
        WindowOperationType = parameter.Operation;
        RelatedRisk = parameter.RelatedRisk;
        IsTestOnly = parameter.TestOnly;

        if (WindowOperationType == OperationType.Create)
        {
            IncidentResponsePlan = new IncidentResponsePlan
            {
                LastUpdate = DateTime.Now,
                CreationDate = DateTime.Now,
                Attachments = new List<NrFile>()
            };

            await LoadDataAsync();
            return;
        }

        var plan = parameter.Plan ??
            throw new ArgumentNullException(nameof(parameter), "Plan cannot be null on edit or view");

        IncidentResponsePlan = plan;

        Name = plan.Name;
        Description = plan.Description;
        Notes = plan.Notes ?? "";
        if (plan.HasBeenTested != null) HasBeenTested = plan.HasBeenTested.Value;
        if (plan.HasBeenUpdated != null) HasBeenUpdated = plan.HasBeenUpdated.Value;
        if (plan.HasBeenExercised != null) HasBeenExercised = plan.HasBeenExercised.Value;
        if (plan.HasBeenApproved != null) HasBeenApproved = plan.HasBeenApproved.Value;
        if (plan.HasBeenReviewed != null) HasBeenReviewed = plan.HasBeenReviewed.Value;

        await LoadDataAsync();
        await LoadAttachmentsAsync();
    }

    #endregion
    
    #region METHODS

    private async Task LoadAttachmentsAsync()
    {
        if(IncidentResponsePlan == null) return;
        
        var files = await IncidentResponsePlansService.GetAttachmentsAsync(IncidentResponsePlan.Id);
        
        //if(files == null) return;
        
        Attachments = new ObservableCollection<FileListing>(files);
    }
    
    private async Task LoadDataAsync()
    {
        UserInfo = AuthenticationService.AuthenticatedUserInfo;

        var people = await EntitiesService.GetAllAsync("person");
        
        await LoadListAsync(entities: people);
         
        if (UserInfo == null) return;

        if (IsCreateOperation) IncidentResponsePlan!.CreatedById = UserInfo.UserId!.Value;
        else IncidentResponsePlan!.UpdatedById = UserInfo.UserId!.Value;

        if (IsEditOrViewOperation)
        {
            Tasks = new ObservableCollection<IncidentResponsePlanTask>(await IncidentResponsePlansService.GetTasksByPlanIdAsync(IncidentResponsePlan!.Id));
            
            if(IncidentResponsePlan.HasBeenApproved != null && IncidentResponsePlan.HasBeenApproved.Value) 
                SelectedApprover = PeopleEntities.FirstOrDefault(x => x.Contains("("+IncidentResponsePlan.ApprovedById+")"));
            if(IncidentResponsePlan.HasBeenReviewed != null && IncidentResponsePlan.HasBeenReviewed.Value)
                SelectedReviewer = PeopleEntities.FirstOrDefault(x => x.Contains("("+IncidentResponsePlan.LastReviewedById+")"));
            if(IncidentResponsePlan.HasBeenTested != null && IncidentResponsePlan.HasBeenTested.Value)
                SelectedTester = PeopleEntities.FirstOrDefault(x => x.Contains("("+IncidentResponsePlan.LastTestedById+")"));
            if(IncidentResponsePlan.HasBeenUpdated != null && IncidentResponsePlan.HasBeenUpdated.Value)
                SelectedUpdater = PeopleEntities.FirstOrDefault(x => x.Contains("("+IncidentResponsePlan.UpdatedById+")"));
            if(IncidentResponsePlan.HasBeenExercised != null && IncidentResponsePlan.HasBeenExercised.Value)
                SelectedExerciser = PeopleEntities.FirstOrDefault(x => x.Contains("("+IncidentResponsePlan.LastExercisedById+")"));
        }

    }
    
    private async Task LoadListAsync(List<Entity> entities)
    {
        var people = new List<string>();
        await Task.Run(() =>
        {
            Parallel.ForEach(entities, entity =>
            {
                people.Add($"{entity.EntitiesProperties.Where(ep => ep.Type == "name").FirstOrDefault()?.Value} ({entity.Id})");
            });
        });
        
        PeopleEntities = new ObservableCollection<string>(people);
    }
    
    private async Task ExecuteCreateAsync()
    {
        var newIrp = new IncidentResponsePlan()
        {
            Id = 0,
            Name = Name,
            Description = Description,
            Notes = Notes,
            CreationDate = DateTime.Now,
            UpdatedById = UserInfo!.UserId!.Value,
            CreatedById = UserInfo!.UserId!.Value,
            LastUpdate = DateTime.Now,
            Status = (int)IntStatus.New,
            HasBeenApproved = HasBeenApproved,
            HasBeenExercised = HasBeenExercised,
            HasBeenTested = HasBeenTested,
            HasBeenUpdated = HasBeenUpdated,
            HasBeenReviewed = HasBeenReviewed
        };

        if (HasBeenApproved)
        {
            newIrp.ApprovalDate = DateTime.Now;
            newIrp.ApprovedById = AutoCompleteHelper.ExtractNumber(SelectedApprover!);
        }

        if (HasBeenExercised)
        {
            newIrp.LastExerciseDate = DateTime.Now;
            newIrp.LastExercisedById = AutoCompleteHelper.ExtractNumber(SelectedExerciser!);
        }

        if (HasBeenTested)
        {
            newIrp.LastTestDate = DateTime.Now;
            newIrp.LastTestedById = AutoCompleteHelper.ExtractNumber(SelectedTester!);
        }

        if (HasBeenUpdated)
        {
            newIrp.LastUpdate = DateTime.Now;
            newIrp.UpdatedById = AutoCompleteHelper.ExtractNumber(SelectedUpdater!);
        }

        if (HasBeenReviewed)
        {
            newIrp.LastReviewDate = DateTime.Now;
            newIrp.LastReviewedById = AutoCompleteHelper.ExtractNumber(SelectedReviewer!);
        }
        
        if (RelatedRisk == null)
        {
            Log.Error("Cannot save a IRP without a related risk");
            
            var msgSelect = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["Something went wrong"],
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelect.ShowAsync();
            return;
        }

        try
        {
            var createdIrp = await IncidentResponsePlansService.CreateAsync(newIrp);
            
            /*if (createdIRP == null)
            {
                Log.Error("Error saving the IRP");

                var msgSelect = MessageBoxManager
                    .GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Error"],
                        ContentMessage = Localizer["Something went wrong"],
                        Icon = Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                await msgSelect.ShowAsync();
                return;
            }*/

            IncidentResponsePlan = createdIrp;

            await RisksService.AssociateRiskToIncidentResponsePlanAsync(RelatedRisk.Id, createdIrp.Id);

            // IX-3: this editor legitimately stays open after saving so tasks can be added to the
            // new plan, so it records what it committed and reports it when the dialog closes.
            _committedPlan = createdIrp;
            _committedPlanWasCreated = true;

            WindowOperationType = OperationType.Edit;

            Toasts.Success(Localizer["Incident Response Plan created successfully"]);
        }
        catch (Exception ex)
        {
            Log.Error("Server error saving the irp: {Ex}", ex.Message);

            var msgSelect = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["Something went wrong"],
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelect.ShowAsync();
        }
        
    }
    
    private async Task ExecuteUpdateAsync()
    {
        var upIrp = IncidentResponsePlan;
        
        if(upIrp == null)
        {
            Log.Error("Cannot update a null IRP");
            
            var msgSelect = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["Something went wrong"],
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelect.ShowAsync();
            return;
        }
        
        
        upIrp.Name = Name;
        upIrp.Description = Description;
        upIrp.Notes = Notes;
        upIrp.LastUpdate = DateTime.Now;
        upIrp.UpdatedById = UserInfo!.UserId!.Value;
        upIrp.HasBeenTested = HasBeenTested;
        upIrp.HasBeenUpdated = HasBeenUpdated;
        upIrp.HasBeenExercised = HasBeenExercised;
        upIrp.HasBeenApproved = HasBeenApproved;
        upIrp.HasBeenReviewed = HasBeenReviewed;
        
        if (HasBeenApproved)
        {
            upIrp.ApprovalDate = DateTime.Now;
            upIrp.ApprovedById = AutoCompleteHelper.ExtractNumber(SelectedApprover!);
        }
        
        if (HasBeenExercised)
        {
            upIrp.LastExerciseDate = DateTime.Now;
            upIrp.LastExercisedById = AutoCompleteHelper.ExtractNumber(SelectedExerciser!);
        }
        
        if (HasBeenTested)
        {
            upIrp.LastTestDate = DateTime.Now;
            upIrp.LastTestedById = AutoCompleteHelper.ExtractNumber(SelectedTester!);
        }
        
        if (HasBeenReviewed)
        {
            upIrp.LastReviewDate = DateTime.Now;
            upIrp.LastReviewedById = AutoCompleteHelper.ExtractNumber(SelectedReviewer!);
        }
        
        try
        {
            var updatedIrp = await IncidentResponsePlansService.UpdateAsync(upIrp);
            /*if (updatedIrp == null)
            {
                Log.Error("Error saving the IRP");

                var msgSelect = MessageBoxManager
                    .GetMessageBoxStandard(new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Error"],
                        ContentMessage = Localizer["Something went wrong"],
                        Icon = Icon.Error,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                await msgSelect.ShowAsync();
                return;
            }*/

            IncidentResponsePlan = updatedIrp;

            _committedPlan = updatedIrp;

            Toasts.Success(Localizer["Incident Response Plan updated successfully"]);
        }
        catch (Exception ex)
        {
            Log.Error("Server error saving the irp: {Ex}", ex.Message);

            var msgSelect = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["Something went wrong"],
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelect.ShowAsync();
        }
        

        
    } 
    
    private async Task ExecuteCancelAsync()
    {
        if (await ConfirmationDialog.ConfirmAsync(Localizer["Warning"], Localizer["AbortOperationMSG"]))
        {
            await ExecuteCloseAsync();
        }
    } 
    
    private Task ExecuteCloseAsync()
    {
        Close(new IrpDialogResult
        {
            Action = _committedPlan == null ? ResultActions.Cancel : ResultActions.Ok,
            Plan = _committedPlan,
            WasCreated = _committedPlanWasCreated
        });

        return Task.CompletedTask;
    } 
    
    public async Task ExecuteAddFileAsync()
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
        }
        
        Log.Debug("Adding File ...");
        
        var storageProvider = StorageProviderAccessor.Current;
        if (storageProvider == null) return;
        
        var file = await storageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = Localizer["AddDocumentMSG"],
        });
        
        if (file.Count == 0) return;

        try
        {
            var result = await FilesService.UploadFileAsync(file.First().Path, IncidentResponsePlan!.Id,
                AuthenticationService.AuthenticatedUserInfo!.UserId!.Value, FileCollectionType.IncidentResponsePlanFile);

            Attachments.Add(result);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error uploading file to incident response plan {Id}", IncidentResponsePlan!.Id);

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

        //IncidentResponsePlan.

    }

    private async Task ExecuteDownloadFileAsync (FileListing file)
    {
        
        var storageProvider = StorageProviderAccessor.Current;
        if (storageProvider == null) return;

        if (file.Type == null)
        {
            Log.Error("File type is null: NR002");
            throw new InvalidOperationException($"File type is null: {file.Name}");
        }
        
        var openFile = await storageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer["SaveDocumentMSG"],
            DefaultExtension = FilesService.ConvertTypeToExtension(file.Type),
            SuggestedFileName = file.Name + FilesService.ConvertTypeToExtension(file.Type),
            
        });

        if (openFile == null) return;
            
        _= FilesService.DownloadFileAsync(file.UniqueName, openFile.Path);
        
        
    }

    private async Task ExecuteDeleteFileAsync (FileListing file)
    {
        
        try
        {
            if (await ConfirmationDialog.ConfirmDeleteAsync(file.Name))
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
    
    private Task ExecuteAddTaskAsync() => ShowTaskDialogAsync(OperationType.Create);

    private Task ExecuteEditTaskAsync() => ShowTaskDialogAsync(OperationType.Edit);

    private Task ExecuteViewTaskAsync() => ShowTaskDialogAsync(OperationType.View);

    /// <summary>
    /// IX-1/IX-2: one modal task dialog opened through <see cref="IDialogService"/>, sized by its
    /// own XAML rather than by three copies of a hardcoded 900×900 here, with the saved task
    /// returned as a typed result and merged into <see cref="Tasks"/> in place.
    /// </summary>
    private async Task ShowTaskDialogAsync(OperationType operation)
    {
        if (IncidentResponsePlan == null) return;

        if (operation != OperationType.Create && SelectedTask == null)
        {
            await MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["Please select a task"],
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                })
                .ShowAsync();
            return;
        }

        var result = await DialogService
            .ShowDialogAsync<IrpTaskDialogResult, IrpTaskDialogParameter>(
                nameof(IncidentResponsePlanTaskViewModel),
                new IrpTaskDialogParameter
                {
                    Operation = operation,
                    Plan = IncidentResponsePlan,
                    Task = operation == OperationType.Create ? null : SelectedTask
                });

        if (result?.Action != ResultActions.Ok || result.Task == null) return;

        await MergeTaskAsync(result.Task, result.WasCreated);
    }

    /// <summary>Inserts or replaces <paramref name="task"/> in <see cref="Tasks"/>, keeping the sort order.</summary>
    private async Task MergeTaskAsync(IncidentResponsePlanTask task, bool wasCreated)
    {
        var tasks = Tasks.ToList();

        if (wasCreated)
        {
            Log.Debug("New task created {Task} for plan {Plan}", task.Id, IncidentResponsePlan?.Id);
            tasks.Add(task);
        }
        else
        {
            Log.Debug("Task updated {Task} for plan {Plan}", task.Id, IncidentResponsePlan?.Id);

            var existing = tasks.FirstOrDefault(x => x.Id == task.Id);
            if (existing == null)
            {
                Log.Warning("task not found");
                return;
            }

            tasks[tasks.IndexOf(existing)] = task;
        }

        Tasks = new ObservableCollection<IncidentResponsePlanTask>(await TaskSorter.SortTasksAsync(tasks));
        SelectedTask = Tasks.FirstOrDefault(t => t.Id == task.Id);
    }

    private async Task ExecuteDeleteTaskAsync(IncidentResponsePlanTask? task)
    {
        if (IncidentResponsePlan == null) return;

        if (SelectedTask == null)
        {
            var msgSelect = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["Please select a task"],
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelect.ShowAsync();
            return;
        }
        
        if (await ConfirmationDialog.ConfirmDeleteAsync(SelectedTask!.Name))
        {
            await IncidentResponsePlansService.DeleteTaskAsync(IncidentResponsePlan.Id, SelectedTask!.Id);
            
            Tasks.Remove(SelectedTask);
        }
    }
    
    public void OnClose()
    {
        Dispose();
    }
    
    #endregion

    /// <summary>
    /// Shows the plan's Gantt in a parented, singleton auxiliary window (IX-1/IX-7). An unsaved
    /// plan has no server-side tasks to schedule, so the action is a no-op until it is created.
    /// </summary>
    private void ExecuteShowGantt()
    {
        var plan = IncidentResponsePlan;

        if (plan == null || plan.Id <= 0)
        {
            Toasts.Warning(Localizer["Save the plan before opening its timeline"]);
            return;
        }

        GetService<INavigationService>()
            .ShowAuxiliaryWindow<IrpGanttWindow>(() => new IrpGanttViewModel(plan.Id, plan.Name));
    }
}

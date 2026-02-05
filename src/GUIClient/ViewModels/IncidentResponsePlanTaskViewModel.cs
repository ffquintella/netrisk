using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Avalonia;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.Views;
using Model.Authentication;
using Model.IncidentResponsePlan;
using ReactiveUI;
using System.Reactive;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Events;
using Model;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Platform.Storage;
using GUIClient.Tools;
using Model.DTO;
using Model.File;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using Serilog;

namespace GUIClient.ViewModels;

public class IncidentResponsePlanTaskViewModel: ViewModelBase
{
    #region LANGUAGE
    private string StrLoggedUser => Localizer["Logged user"] + ":";
    private string StrTitle => Localizer["Task"] ;
    private string StrPlan => Localizer["Plan"] ;
    private string StrName => Localizer["Name"] ;
    private string StrTask => Localizer["Task"] ;
    private string StrDescription => Localizer["Description"] ;
    private string StrComments => Localizer["Comments"] ;
    private string StrTaskType => Localizer["Task type"] ;
    private string StrSucessCriteria => Localizer["Success criteria"] ;
    private string StrFailureCriteria => Localizer["Failure criteria"] ;
    private string StrCompletionCriteria => Localizer["Completion criteria"] ;
    private string StrVerificationCriteria => Localizer["Verification criteria"] ;
    private string StrConditionToProceed => Localizer["Condition to proceed"] ;
    private string StrConditionToSkip => Localizer["Condition to skip"] ;
    private string StrSave => Localizer["Save"] ;
    private string StrCancel => Localizer["Cancel"] ;
    private string StrClose => Localizer["Close"] ;
    private string StrEstimatedDuration => Localizer["Estimated duration"] ;
    private string StrPriority => Localizer["Priority"] ;
    private string StrParallel => Localizer["Parallel"] ;
    private string StrMetadata => Localizer["Metadata"] ;
    private string StrIsSequential => Localizer["Is sequential"] ;
    private string StrIsOptional => Localizer["Is optional"] ;
    private string StrIsParallel => Localizer["Is parallel"] ;
    private string StrAssignedTo => Localizer["Assigned to"];
    private string StrAttachments => Localizer["Attachments"];
    
    #endregion
    
    #region FIELDS
    private readonly Thickness _editAlignMargin = new Thickness(0, 10, 5, 0);
    private readonly Thickness _readAlignMargin = new Thickness(0, 10, 5, 0);
    private readonly Thickness _viewAlignMargin = new Thickness(0, 0, 5, 0);

    private bool _dataLoaded = false;
    
    #endregion
    
    #region PROPERTIES
    
        private IncidentResponsePlanTaskWindow? _parentWindow;
        
        public IncidentResponsePlanTaskWindow? ParentWindow
        {
            get => _parentWindow;
            set => this.RaiseAndSetIfChanged(ref _parentWindow, value);
        }
        
        private AuthenticatedUserInfo? _userInfo;
    
        public AuthenticatedUserInfo? UserInfo
        {
            get => _userInfo;
            set => this.RaiseAndSetIfChanged(ref _userInfo, value);
        }
        
        private IncidentResponsePlan _incidentResponsePlan;
        public IncidentResponsePlan IncidentResponsePlan 
        {
            get => _incidentResponsePlan;
            set => this.RaiseAndSetIfChanged(ref _incidentResponsePlan, value);
        }
        
        private IncidentResponsePlanTask _incidentResponsePlanTask;
        public IncidentResponsePlanTask IncidentResponsePlanTask 
        {
            get => _incidentResponsePlanTask;
            set => this.RaiseAndSetIfChanged(ref _incidentResponsePlanTask, value);
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

        private string? _name;
        public string? Name
        {
            get => _name;
            set => this.RaiseAndSetIfChanged(ref _name, value);
        }
        
        private string? _description;
        public string? Description
        {
            get => _description;
            set => this.RaiseAndSetIfChanged(ref _description, value);
        }

        private string? _notes;
        public string? Notes
        {
            get => _notes;
            set => this.RaiseAndSetIfChanged(ref _notes, value);
        }

        private decimal _extimatedDuration;
        public decimal EstimatedDuration
        {
            get => _extimatedDuration;
            set => this.RaiseAndSetIfChanged(ref _extimatedDuration, value);
        }
        private decimal _priority;
        public decimal Priority
        {
            get => _priority;
            set => this.RaiseAndSetIfChanged(ref _priority, value);
        }
        
        private bool _isParallel;
        public bool IsParallel
        {
            get => _isParallel;
            set => this.RaiseAndSetIfChanged(ref _isParallel, value);
        }
        
        private bool _isSequential;
        public bool IsSequential
        {
            get => _isSequential;
            set => this.RaiseAndSetIfChanged(ref _isSequential, value);
        }
        
        private bool _isOptional;
        public bool IsOptional
        {
            get => _isOptional;
            set => this.RaiseAndSetIfChanged(ref _isOptional, value);
        }
        
        private string? _successCriteria;
        public string? SuccessCriteria
        {
            get => _successCriteria;
            set => this.RaiseAndSetIfChanged(ref _successCriteria, value);
        }
        
        private string? _failureCriteria;
        public string? FailureCriteria
        {
            get => _failureCriteria;
            set => this.RaiseAndSetIfChanged(ref _failureCriteria, value);
        }
        
        private string? _completionCriteria;
        public string? CompletionCriteria
        {
            get => _completionCriteria;
            set => this.RaiseAndSetIfChanged(ref _completionCriteria, value);
        }
        
        private string? _verificationCriteria;
        public string? VerificationCriteria
        {
            get => _verificationCriteria;
            set => this.RaiseAndSetIfChanged(ref _verificationCriteria, value);
        }
        
        private string? _conditionToProceed;
        public string? ConditionToProceed
        {
            get => _conditionToProceed;
            set => this.RaiseAndSetIfChanged(ref _conditionToProceed, value);
        }
        
        private string? _conditionToSkip;
        public string? ConditionToSkip
        {
            get => _conditionToSkip;
            set => this.RaiseAndSetIfChanged(ref _conditionToSkip, value);
        }
        
        private string? _taskType;
        public string? TaskType
        {
            get => _taskType;
            set => this.RaiseAndSetIfChanged(ref _taskType, value);
        }
        
        
        private Thickness AlignMargin
        {
            get
            {
                if (IsEditOperation) return _editAlignMargin;
                if (IsViewOperation) return _viewAlignMargin;
                return _readAlignMargin;
            }   
        }
        
        private ObservableCollection<TaskType> _taskTypes = new ObservableCollection<TaskType>();
        public ObservableCollection<TaskType> TaskTypes
        {
            get => _taskTypes;
            set => this.RaiseAndSetIfChanged(ref _taskTypes, value);
        }
        
        private TaskType? _selectedTaskType;
        public TaskType? SelectedTaskType
        {
            get => _selectedTaskType;
            set => this.RaiseAndSetIfChanged(ref _selectedTaskType, value);
        }
        
        private ObservableCollection<string> _peopleAndTeamsEntities = new ObservableCollection<string>();
    
        public ObservableCollection<string> PeopleAndTeamsEntities
        {
            get => _peopleAndTeamsEntities;
            set => this.RaiseAndSetIfChanged(ref _peopleAndTeamsEntities, value);
        }
        
        private string? _assignedEntity;
        
        public string? AssignedEntity
        {
            get => _assignedEntity;
            set => this.RaiseAndSetIfChanged(ref _assignedEntity, value);
        }
    
        private List<Entity> _entities = new();
        
        public List<Entity> Entities
        {
            get => _entities;
            set => this.RaiseAndSetIfChanged(ref _entities, value);
        }
        
        private ObservableCollection<FileListing> _attachments = new ObservableCollection<FileListing>();
    
        public ObservableCollection<FileListing> Attachments
        {
            get => _attachments;
            set => this.RaiseAndSetIfChanged(ref _attachments, value);
        }
        
        private bool _canSave;
        
        public bool CanSave
        {
            get => _canSave;
            set => this.RaiseAndSetIfChanged(ref _canSave, value);
        }
        
    #endregion
    
    #region COMMANDS
    
    public ReactiveCommand<Unit, Unit> BtSaveClicked { get; }
    public ReactiveCommand<Unit, Unit> BtCancelClicked { get; }
    public ReactiveCommand<Unit, Unit> BtCloseClicked { get; }
    public ReactiveCommand<Window, Unit> BtFileAddClicked { get; }
    public ReactiveCommand<FileListing, Unit> BtFileDeleteClicked { get; }
    public ReactiveCommand<FileListing, Unit> BtFileDownloadClicked { get; }

    
    #endregion
    
    #region SERVICES

    private IIncidentResponsePlansService IncidentResponsePlansService { get; } = GetService<IIncidentResponsePlansService>();
    private IEntitiesService EntitiesService { get; } = GetService<IEntitiesService>();
    private IFilesService FilesService { get; } = GetService<IFilesService>();
    
    #endregion
    
    #region CONSTRUCTORS

    public IncidentResponsePlanTaskViewModel(IncidentResponsePlan plan)
    {
        if (_incidentResponsePlanTask == null)
        {
            _incidentResponsePlanTask = new IncidentResponsePlanTask();
            _incidentResponsePlanTask.Id = 0;
            WindowOperationType = OperationType.Create;
        }
        
        _incidentResponsePlan = plan;
        UserInfo = AuthenticationService.AuthenticatedUserInfo;
        TaskTypes = new ObservableCollection<TaskType>(IncidentResponsePlanTaskTypes.GetTypes(Localizer));
        
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
        
        BtFileDownloadClicked = ReactiveCommand.CreateFromTask<FileListing>(ExecuteDownloadFileAsync);
        BtFileDeleteClicked = ReactiveCommand.CreateFromTask<FileListing>(ExecuteDeleteFileAsync);
        BtFileAddClicked = ReactiveCommand.CreateFromTask<Window>(ExecuteAddFileAsync);
        BtCancelClicked = ReactiveCommand.CreateFromTask(ExecuteCancelAsync);
        
        BtCloseClicked = ReactiveCommand.Create(() =>
        {
            ParentWindow?.Close();
        });

        if (WindowOperationType != OperationType.View)
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
                viewModel => viewModel.CompletionCriteria,
                p => !string.IsNullOrEmpty(p),
                Localizer["PleaseEnterAValidValueMSG"]);
            
            this.ValidationRule(
                viewModel => viewModel.FailureCriteria,
                p => !string.IsNullOrEmpty(p),
                Localizer["PleaseEnterAValidValueMSG"]);
            
            this.ValidationRule(
                viewModel => viewModel.SuccessCriteria,
                p => !string.IsNullOrEmpty(p),
                Localizer["PleaseEnterAValidValueMSG"]);
            
            this.ValidationRule(
                viewModel => viewModel.SelectedTaskType,
                p =>
                {
                    if (p == null) return false;
                    return TaskTypes.Contains(p);
                },
                Localizer["PleaseEnterAValidValueMSG"]);
            
            this.ValidationRule(
                viewModel => viewModel.AssignedEntity, 
                p =>
                {
                    if (string.IsNullOrEmpty(p)) return false;
                    return PeopleAndTeamsEntities.Contains(p);
                },
                Localizer["PleaseEnterAValidValueMSG"]);
            
            this.ValidationRule(
                viewModel => viewModel.EstimatedDuration,
                p => p > 0,
                Localizer["PleaseEnterAValidValueMSG"]);
            
            this.IsValid()
                .Subscribe(x =>
                {
                    CanSave = x;
                });
            
        }


        _= LoadDataAsync();
    }

    public IncidentResponsePlanTaskViewModel(IncidentResponsePlan plan, IncidentResponsePlanTask task, bool isView): this(plan)
    {
        _incidentResponsePlanTask = task;
        WindowOperationType = isView ? OperationType.View : OperationType.Edit;
        UserInfo = AuthenticationService.AuthenticatedUserInfo;

        _ = LoadFieldsFromTask();
    }

    #endregion
    
    #region EVENTS
    
    public event EventHandler<IncidentResponsePlanTaskEventArgs> PlanTaskCreated = delegate { };
    protected virtual void OnPlanTaskCreated(IncidentResponsePlanTaskEventArgs e)
    {
        PlanTaskCreated.Invoke(this, e);
    }
    
    public event EventHandler<IncidentResponsePlanTaskEventArgs> PlanTaskUpdated = delegate { };

    protected virtual void OnPlanTaskUpdated(IncidentResponsePlanTaskEventArgs e)
    {
        PlanTaskUpdated.Invoke(this, e);
    }
    
    #endregion
    
    #region METHODS

    private async Task LoadDataAsync()
    {
        Entities = await EntitiesService.GetAllAsync("person");
        Entities.AddRange(await EntitiesService.GetAllAsync("team"));

        await LoadListAsync(Entities);
        
        _dataLoaded = true;
    }

    private async Task LoadFieldsFromTask()
    {
        Name = IncidentResponsePlanTask.Name;
        Description = IncidentResponsePlanTask.Description;
        Notes = IncidentResponsePlanTask.Notes;
        if(IncidentResponsePlanTask.EstimatedDuration != null) EstimatedDuration = (decimal)IncidentResponsePlanTask.EstimatedDuration!.Value.TotalMinutes;
        Priority = IncidentResponsePlanTask.Priority;
        IsParallel = IncidentResponsePlanTask.IsParallel.HasValue && IncidentResponsePlanTask.IsParallel.Value;
        IsSequential = IncidentResponsePlanTask.IsSequential.HasValue && IncidentResponsePlanTask.IsSequential.Value;
        IsOptional = IncidentResponsePlanTask.IsOptional.HasValue && IncidentResponsePlanTask.IsOptional.Value;
        SuccessCriteria = IncidentResponsePlanTask.SuccessCriteria;
        FailureCriteria = IncidentResponsePlanTask.FailureCriteria;
        CompletionCriteria = IncidentResponsePlanTask.CompletionCriteria;
        VerificationCriteria = IncidentResponsePlanTask.VerificationCriteria;
        ConditionToProceed = IncidentResponsePlanTask.ConditionToProceed;
        ConditionToSkip = IncidentResponsePlanTask.ConditionToSkip;
        
        if(!_dataLoaded) await LoadDataAsync();
        
        AssignedEntity = PeopleAndTeamsEntities.FirstOrDefault(pt => pt.Contains(IncidentResponsePlanTask.AssignedToId.ToString()));
        
        //TaskType = IncidentResponsePlanTask.TaskType;
        SelectedTaskType = TaskTypes.FirstOrDefault(tt => tt.DbName == IncidentResponsePlanTask.TaskType);
        
        await LoadAttachments();
    }
    
    private async Task LoadListAsync(List<Entity> entities)
    {
        var entResult = new List<string>();
        await Task.Run(() =>
        {
            Parallel.ForEach(entities, entity =>
            {
                entResult.Add($"{entity.EntitiesProperties.Where(ep => ep.Type == "name").FirstOrDefault()?.Value} ({entity.Id})");
            });
        });
        
        PeopleAndTeamsEntities = new ObservableCollection<string>(entResult);
    }
    
    private async Task ExecuteCreateAsync()
    {
        var newIrpTask = new IncidentResponsePlanTask();
        newIrpTask.Id = 0;
        LoadDataToTask(ref newIrpTask);
        newIrpTask.Status = (int)IntStatus.New;

        var task = await IncidentResponsePlansService.CreateTaskAsync(newIrpTask);
        
        OnPlanTaskCreated(new IncidentResponsePlanTaskEventArgs()
        {
            PlanId = IncidentResponsePlan.Id,
            Task = task
        });
        
        var msgSelectSuc = MessageBoxManager
            .GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Success"],
                ContentMessage = Localizer["Incident Response Plan task created successfully"],
                Icon = Icon.Success,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            });

        await msgSelectSuc.ShowAsync();
        
        ParentWindow?.Close();
    }
    
    private async Task LoadAttachments()
    {
        
        if(IncidentResponsePlanTask.Id == 0) return;
        
        var files = await IncidentResponsePlansService.GetTaskAttachmentsAsync(IncidentResponsePlan.Id, IncidentResponsePlanTask.Id);
        
        Attachments = new ObservableCollection<FileListing>(files);
        
        
    }
    
    private async Task ExecuteUpdateAsync()
    {
        
        LoadDataToTask(ref _incidentResponsePlanTask);

        try
        {
            var task = await IncidentResponsePlansService.UpdateTaskAsync(_incidentResponsePlanTask);

            IncidentResponsePlanTask = task;

            var args = new IncidentResponsePlanTaskEventArgs()
            {
                PlanId = IncidentResponsePlan.Id,
                Task = task
            };
            
            OnPlanTaskUpdated(args);
            
            var msgSelectSuc = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Success"],
                    ContentMessage = Localizer["Task updated successfully"],
                    Icon = Icon.Success,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelectSuc.ShowAsync();
            
        }
        catch (Exception ex)
        {
            var msgSelectSuc = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = "Error updating task: " + ex.Message,
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelectSuc.ShowAsync();
        }
        
        
        
        
    }

    private async Task ExecuteCancelAsync()
    {
        var messageBoxConfirm = MessageBoxManager
            .GetMessageBoxStandard(   new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["NoChangesWillBeSavedMSG"]  ,
                ButtonDefinitions = ButtonEnum.OkAbort,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = Icon.Question,
            });
                        
        var confirmation = await messageBoxConfirm.ShowAsync();

        if (confirmation == ButtonResult.Ok)
        {
            ParentWindow!.Close();
        }
    }
    
    public async Task ExecuteAddFileAsync(Window window)
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
        
        var topLevel = TopLevel.GetTopLevel(window);
        
        var file = await topLevel!.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions()
        {
            Title = Localizer["AddDocumentMSG"],
        });
        
        if (file.Count == 0) return;
        
        var result = await FilesService.UploadFileAsync(file.First().Path, IncidentResponsePlanTask!.Id,
            AuthenticationService.AuthenticatedUserInfo!.UserId!.Value, FileCollectionType.IncidentResponsePlanTaskFile);
        
        Attachments.Add(result);
        
    }
    
    private async Task ExecuteDownloadFileAsync(FileListing file)
    {
        var topLevel = TopLevel.GetTopLevel(ParentWindow);

        if (file.Type == null)
        {
            Log.Error("The file must have a type: NE0001");
            throw new NullReferenceException("The file must have a type: NE0001");
        }
        
        var openFile = await topLevel!.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = Localizer["SaveDocumentMSG"],
            DefaultExtension = FilesService.ConvertTypeToExtension(file.Type),
            SuggestedFileName = file.Name + FilesService.ConvertTypeToExtension(file.Type),
            
        });

        if (openFile == null) return;
            
        _= FilesService.DownloadFileAsync(file.UniqueName, openFile.Path);
    }
    
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

    private void LoadDataToTask(ref IncidentResponsePlanTask task)
    {
        task.Name = Name ?? string.Empty;
        task.Description = Description;
        task.Notes = Notes;
        //task.EstimatedDuration = new TimeSpan(Decimal.ToInt64(EstimatedDuration));
        task.EstimatedDuration = new TimeSpan(0, Decimal.ToInt32(EstimatedDuration), 0);
        task.Priority = Decimal.ToInt32(Priority);
        task.IsParallel = IsParallel;
        task.IsSequential = IsSequential;
        task.IsOptional = IsOptional;
        task.SuccessCriteria = SuccessCriteria;
        task.FailureCriteria = FailureCriteria;
        task.CompletionCriteria = CompletionCriteria;
        task.VerificationCriteria = VerificationCriteria;
        task.ConditionToProceed = ConditionToProceed;
        task.ConditionToSkip = ConditionToSkip;
        if(AssignedEntity != null) task.AssignedToId = AutoCompleteHelper.ExtractNumber(AssignedEntity)!.Value;
        if(SelectedTaskType != null) task.TaskType = SelectedTaskType.DbName;
        task.PlanId = IncidentResponsePlan.Id;
        
    }
    #endregion
}
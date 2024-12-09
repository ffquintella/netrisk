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
using System.Reactive;
using Avalonia.Controls;
using ClientServices.Interfaces;
using Model;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI.Validation.Extensions;
using Serilog;
using Exception = System.Exception;

namespace GUIClient.ViewModels;

public class IncidentResponsePlanViewModel : ViewModelBase
{
    #region LANGUAGE

    private string StrTitle => Localizer["Incident Response Plan"];
    private string StrRisk => Localizer["Risk"];
    private string StrName => Localizer["Name"];
    private string StrPlan => Localizer["Plan"];
    private string StrDescription => Localizer["Description"];
    private string StrComments => Localizer["Comments"];
    private string StrHasBeenTested => Localizer["Has been tested"];
    private string StrHasBeenUpdated => Localizer["Has been updated"];
    private string StrHasBeenExercised => Localizer["Has been exercised"];
    private string StrHasBeenApproved => Localizer["Has been approved"];
    private string StrHasBeenReviewed => Localizer["Has been reviewed"];
    private string StrStatus => Localizer["Status"];
    private string StrLifeCicleStatus => Localizer["Life cycle status"];
    private string StrSave => Localizer["Save"];
    private string StrCancel => Localizer["Cancel"];
    private string StrMetadata => Localizer["Metadata"];
    private string StrCreationDate => Localizer["Creation date"] + ":";
    private string StrLastUpdate => Localizer["Last update"] + ":";
    private string StrLoggedUser => Localizer["Logged user"] + ":";
    private string StrTasks => Localizer["Tasks"];
    private string StrAttachments => Localizer["Attachments"];
    private string StrClose => Localizer["Close"];

#endregion
    
    #region FIELDS
    private readonly Thickness _editAlignMargin = new Thickness(0, 5, 5, 0);
    private readonly Thickness _readAlignMargin = new Thickness(0, 5, 5, 0);
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
            
            if(value == OperationType.Edit) IsEditOperation = true;
            if(value == OperationType.Create) IsCreateOperation = true;
            if(value == OperationType.View) IsViewOperation = true;
            
            this.RaiseAndSetIfChanged(ref _windowOperationType, value);
        }
    }

    private bool _isCreateOperation;
    
    public bool IsCreateOperation
    {
        get => _isCreateOperation;
        set => this.RaiseAndSetIfChanged(ref _isCreateOperation, value);
    }
    
    private bool _isEditOperation;
    
    public bool IsEditOperation
    {
        get => _isEditOperation;
        set => this.RaiseAndSetIfChanged(ref _isEditOperation, value);
    }
    
    private bool _isViewOperation;
    
    public bool IsViewOperation
    {
        get => _isViewOperation;
        set => this.RaiseAndSetIfChanged(ref _isViewOperation, value);
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
        set => this.RaiseAndSetIfChanged(ref _hasBeenTested, value);
    }
    
    private bool _hasBeenUpdated;
    
    public bool HasBeenUpdated
    {
        get => _hasBeenUpdated;
        set => this.RaiseAndSetIfChanged(ref _hasBeenUpdated, value);
    }
    
    private bool _hasBeenExercised;
    
    public bool HasBeenExercised
    {
        get => _hasBeenExercised;
        set => this.RaiseAndSetIfChanged(ref _hasBeenExercised, value);
    }
    
    private bool _hasBeenApproved;
    
    public bool HasBeenApproved
    {
        get => _hasBeenApproved;
        set => this.RaiseAndSetIfChanged(ref _hasBeenApproved, value);
    }
    
    private bool _hasBeenReviewed;
    
    public bool HasBeenReviewed
    {
        get => _hasBeenReviewed;
        set => this.RaiseAndSetIfChanged(ref _hasBeenReviewed, value);
    }
    
    private ObservableCollection<NrFile> _attachments = new ObservableCollection<NrFile>();
    
    public ObservableCollection<NrFile> Attachments
    {
        get => _attachments;
        set => this.RaiseAndSetIfChanged(ref _attachments, value);
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
    
    private Thickness AlignMargin => IsEditOperation ? _editAlignMargin : _readAlignMargin;
    
    private AuthenticatedUserInfo? _userInfo;
    
    public AuthenticatedUserInfo? UserInfo
    {
        get => _userInfo;
        set => this.RaiseAndSetIfChanged(ref _userInfo, value);
    }
    
    private bool IsTestOnly { get; }
    
    #endregion

    #region SERVICES
    
        private IIncidentResponsePlansService IncidentResponsePlansService => GetService<IIncidentResponsePlansService>();
        private IRisksService RisksService => GetService<IRisksService>();
        
    #endregion

    #region COMMANDS
    public ReactiveCommand<Unit, Unit> BtSaveClicked { get; }
    public ReactiveCommand<Window, Unit> BtCancelClicked { get; }
    public ReactiveCommand<Window, Unit> BtCloseClicked { get; }
    public ReactiveCommand<Window, Unit> BtFileAddClicked { get; }
    
    
    
    #endregion

    #region CONSTRUCTOR
    
    private IncidentResponsePlanViewModel()
    {

        _ = LoadDataAsync();
        
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
        
        BtCancelClicked = ReactiveCommand.CreateFromTask<Window>(ExecuteCancelAsync);
        BtCloseClicked = ReactiveCommand.CreateFromTask<Window>(ExecuteCloseAsync);
        BtFileAddClicked = ReactiveCommand.CreateFromTask<Window>(ExecuteAddFileAsync);

        CanSave = false;
        CanClose = true;
        CanCancel = true;

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
            
            this.IsValid()
                .Subscribe(x =>
                {
                    CanSave = x;
                });
        }


    }
    
    /// <summary>
    /// Create a new instance of IncidentResponsePlanViewModel on edit operation
    /// </summary>
    /// <param name="incidentResponsePlan"></param>
    /// <param name="relatedRisk"></param>
    public IncidentResponsePlanViewModel(IncidentResponsePlan incidentResponsePlan, Risk relatedRisk): this()
    {
        IncidentResponsePlan = incidentResponsePlan;
        WindowOperationType = OperationType.Edit;
        RelatedRisk = relatedRisk;
    }
    
    /// <summary>
    /// Create a new instance of IncidentResponsePlanViewModel on create operation
    /// </summary>
    /// <param name="relatedRisk"></param>
    public IncidentResponsePlanViewModel(Risk relatedRisk, bool testOnly = false): this()
    {
        RelatedRisk = relatedRisk;
        WindowOperationType = OperationType.Create;
        IncidentResponsePlan = new IncidentResponsePlan();
        IncidentResponsePlan.LastUpdate = DateTime.Now;
        IncidentResponsePlan.CreationDate = DateTime.Now;
        IncidentResponsePlan.Attachments = new List<NrFile>();
        IsTestOnly = testOnly;
        

    }
    #endregion
    
    #region METHODS

    private async Task LoadDataAsync()
    {
         UserInfo = AuthenticationService.AuthenticatedUserInfo;
         
         
        if (UserInfo == null) return;

        if (IsCreateOperation) IncidentResponsePlan!.CreatedById = UserInfo.UserId!.Value;
        else IncidentResponsePlan!.UpdatedById = UserInfo.UserId!.Value;

    }

    private async Task ExecuteCreateAsync()
    {
        var newIRP = new IncidentResponsePlan()
        {
            Id = 0,
            Name = Name,
            Description = Description,
            Notes = Notes,
            CreationDate = DateTime.Now,
            UpdatedById = UserInfo.UserId!.Value,
            CreatedById = UserInfo.UserId!.Value,
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
            newIRP.LastReviewDate = DateTime.Now;
            newIRP.LastReviewedById = UserInfo.UserId!.Value;
        }

        if (HasBeenExercised)
        {
            newIRP.LastExerciseDate = DateTime.Now;
            newIRP.LastExercisedById = UserInfo.UserId!.Value;
        }

        if (HasBeenTested)
        {
            newIRP.LastTestDate = DateTime.Now;
            newIRP.LastTestedById = UserInfo.UserId!.Value;
        }

        if (HasBeenUpdated)
        {
            newIRP.LastUpdate = DateTime.Now;
            newIRP.UpdatedById = UserInfo.UserId!.Value;
        }

        if (HasBeenReviewed)
        {
            newIRP.LastReviewDate = DateTime.Now;
            newIRP.LastReviewedById = UserInfo.UserId!.Value;
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

        //newIRP.RelatedRisks.Add(RelatedRisk);

        try
        {
            var createdIRP = await IncidentResponsePlansService.CreateAsync(newIRP);
            if (createdIRP == null)
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
            }

            IncidentResponsePlan = createdIRP;
            
            await RisksService.AssociateRiskToIncidentResponsePlanAsync(RelatedRisk.Id, createdIRP.Id);
            
            WindowOperationType = OperationType.Edit;

            var msgSelectSuc = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Success"],
                    ContentMessage = Localizer["Incident Response Plan created successfully"],
                    Icon = Icon.Success,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgSelectSuc.ShowAsync();
        }
        catch (Exception ex)
        {
            Log.Error("Server error saving the irp: {ex}", ex.Message);

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
        
    } 
    
    private async Task ExecuteCancelAsync(Window window)
    {
        var messageBoxConfirm = MessageBoxManager
            .GetMessageBoxStandard(   new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Warning"],
                ContentMessage = Localizer["AbortOperationMSG"],
                ButtonDefinitions = ButtonEnum.OkAbort,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Icon = Icon.Question,
            });
                        
        var confirmation = await messageBoxConfirm.ShowAsync();

        if (confirmation == ButtonResult.Ok)
        {
            await ExecuteCloseAsync(window);
        }
    } 
    
    private async Task ExecuteCloseAsync(Window window)
    {
        window.Close();
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
    }

    public void OnClose()
    {
        Dispose();
    }
    #endregion
}
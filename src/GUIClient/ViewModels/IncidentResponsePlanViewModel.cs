using System;
using System.Collections.Generic;
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
        set => this.RaiseAndSetIfChanged(ref _windowOperationType, value);
    }
    
    public bool IsCreateOperation => WindowOperationType == OperationType.Create;
    public bool IsEditOperation => WindowOperationType == OperationType.Edit;
    
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

    
    #endregion

    #region COMMANDS
    public ReactiveCommand<Unit, Unit> BtSaveClicked { get; }
    public ReactiveCommand<Window, Unit> BtCancelClicked { get; }
    public ReactiveCommand<Window, Unit> BtCloseClicked { get; }
    
    #endregion

    #region CONSTRUCTOR
    
    private IncidentResponsePlanViewModel()
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
        
        BtCancelClicked = ReactiveCommand.CreateFromTask<Window>(ExecuteCancelAsync);
        BtCloseClicked = ReactiveCommand.CreateFromTask<Window>(ExecuteCloseAsync);


        if (IsCreateOperation)
        {
            CanSave = true;
            CanCancel = true;
            CanClose = false;
        }
        else
        {
            CanSave = true;
            CanCancel = false;
            CanClose = true;
        }

        
        _ = LoadDataAsync();
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
        
    }
    private async Task ExecuteUpdateAsync()
    {
        
    } 
    
    private async Task ExecuteCancelAsync(Window window)
    {
        
    } 
    
    private async Task ExecuteCloseAsync(Window window)
    {
        
    } 

    public void OnClose()
    {
        Dispose();
    }
    #endregion
}
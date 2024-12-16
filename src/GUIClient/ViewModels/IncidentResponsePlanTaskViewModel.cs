using System;
using Avalonia;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.Views;
using Model.Authentication;
using ReactiveUI;

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
    
    #endregion
    
    #region FIELDS
    private readonly Thickness _editAlignMargin = new Thickness(0, 10, 5, 0);
    private readonly Thickness _readAlignMargin = new Thickness(0, 10, 5, 0);
    private readonly Thickness _viewAlignMargin = new Thickness(0, 0, 5, 0);
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
        
        
        
        
        
        
        private Thickness AlignMargin
        {
            get
            {
                if (IsEditOperation) return _editAlignMargin;
                if (IsViewOperation) return _viewAlignMargin;
                return _readAlignMargin;
            }   
        }
        
    #endregion
    
    #region COMMANDS
    
    #endregion
    
    #region SERVICES
    
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
    }

    public IncidentResponsePlanTaskViewModel(IncidentResponsePlan plan, IncidentResponsePlanTask task, bool isView): this(plan)
    {
        _incidentResponsePlanTask = task;
        WindowOperationType = isView ? OperationType.View : OperationType.Edit;
        UserInfo = AuthenticationService.AuthenticatedUserInfo;
    }

    #endregion
    
    #region METHODS
    
    #endregion
}
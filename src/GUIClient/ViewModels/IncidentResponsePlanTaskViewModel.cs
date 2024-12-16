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
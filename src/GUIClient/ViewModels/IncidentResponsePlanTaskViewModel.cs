using DAL.Entities;
using GUIClient.Views;
using Model.Authentication;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class IncidentResponsePlanTaskViewModel: ViewModelBase
{
    #region LANGUAGE
    private string StrLoggedUser => Localizer["Logged user"] + ":";
    private string StrTitle => Localizer["Task"] ;
    
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
    
    #endregion
    
    #region COMMANDS
    
    #endregion
    
    #region SERVICES
    
    #endregion
    
    #region CONSTRUCTORS

    public IncidentResponsePlanTaskViewModel(IncidentResponsePlan plan)
    {
        _incidentResponsePlan = plan;
        UserInfo = AuthenticationService.AuthenticatedUserInfo;
    }
    #endregion
    
    #region METHODS
    
    #endregion
}
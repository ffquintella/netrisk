using GUIClient.Views;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class IncidentResponsePlanTaskViewModel: ViewModelBase
{
    #region LANGUAGE
    
    #endregion
    
    #region PROPERTIES
    
        private IncidentResponsePlanTaskWindow? _parentWindow;
        
        public IncidentResponsePlanTaskWindow? ParentWindow
        {
            get => _parentWindow;
            set => this.RaiseAndSetIfChanged(ref _parentWindow, value);
        }
    
    #endregion
    
    #region COMMANDS
    
    #endregion
    
    #region CONSTRUCTORS

    public IncidentResponsePlanTaskViewModel()
    {
        
    }
    #endregion
    
    #region METHODS
    
    #endregion
}
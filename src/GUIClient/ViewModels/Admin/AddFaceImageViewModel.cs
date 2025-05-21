using Avalonia.Controls;

namespace GUIClient.ViewModels.Admin;

public class AddFaceImageViewModel: ViewModelBase
{
    #region PROPERTIES
        private int UserId { get; set; }
        private Window ParentWindow { get; set; }
    #endregion
    
    
    #region SERVICES
    
    #endregion
    
    
    public AddFaceImageViewModel(int userId, Window parentWindow)
    {
        ParentWindow = parentWindow;
        UserId = userId;
    }


    
}
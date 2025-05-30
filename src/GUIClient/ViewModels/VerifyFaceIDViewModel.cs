namespace GUIClient.ViewModels;

public class VerifyFaceIDViewModel: ViewModelBase
{
    
    #region LANGUAGES

    public string StrTitle { get; } = Localizer["VerifyFaceImageTitle"];

    #endregion
    
    #region PROPERTIES

    public bool IsFaceIdVerified { get; set; } = false;

    #endregion
    
    #region CONSTRUCTORS
    
    public VerifyFaceIDViewModel()
    {
        // Initialize any necessary services or properties here
        // For example, you might want to inject services like ILogger, IAuthenticationService, etc.
    }
    
    #endregion
}
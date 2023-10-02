using Microsoft.Extensions.Localization;

namespace GUIClient.ViewModels;

public class VulnerabilitiesViewModel: ViewModelBase
{
    #region LANGUAGE

    private string StrVulnerabilities { get;  } = Localizer["Vulnerabilities"];
    private string StrReload { get;  } = Localizer["Reload"];

    #endregion
    
    #region PROPERTIES
    
    #endregion
    
    public VulnerabilitiesViewModel()
    {
        
    }
}
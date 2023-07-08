using System.Reflection;
using System.Resources;
using ClientServices.Interfaces;
using Tools.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;

namespace ClientServices.Services;

public class LocalizationService: ILocalizationService
{

    private ILoggerFactory _loggerFactory;
    
    public LocalizationService(ILoggerFactory loggerFactory)
    {
        _loggerFactory = loggerFactory;
    }
    
    public IStringLocalizer GetLocalizer(Assembly callingAssembly)
    {

        var localizer = new Locator(callingAssembly);

        return localizer;
    }
    
    public ResourceManager GetResourceManager()
    {
        ResourceManager rm = new ResourceManager("GUIClient.Resources.Localization",
            typeof(LocalizationService).Assembly);
        
        return rm;
    }
}
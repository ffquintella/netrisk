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
    private Assembly _callingAssembly;
    
    public LocalizationService(ILoggerFactory loggerFactory, Assembly callingAssembly)
    {
        _loggerFactory = loggerFactory;
        _callingAssembly = callingAssembly;
    }
    
    public IStringLocalizer GetLocalizer()
    {
        return GetLocalizer(_callingAssembly);
    }
    
    public IStringLocalizer GetLocalizer(Assembly callingAssembly)
    {

        var localizer = new Locator(callingAssembly, GetResourceManager());

        return localizer;
    }
    
    public ResourceManager GetResourceManager()
    {
        ResourceManager rm = new ResourceManager("GUIClient.Resources.Localization",
            _callingAssembly);
        
        return rm;
    }
}
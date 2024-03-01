using System.Reflection;
using System.Resources;
using Tools.Globalization;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
using ServerServices.Interfaces;

namespace ServerServices.Services;

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
        ResourceManager rm = new ResourceManager("API.Resources.Localization",
            _callingAssembly);
        
        return rm;
    }
}
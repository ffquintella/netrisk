using System.Reflection;
using System.Resources;
using Microsoft.Extensions.Localization;

namespace ServerServices.Interfaces;

public interface ILocalizationService
{

    public IStringLocalizer GetLocalizer();
    IStringLocalizer GetLocalizer(Assembly callingAssembly);

    ResourceManager GetResourceManager();
}
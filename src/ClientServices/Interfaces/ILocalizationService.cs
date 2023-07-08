using System.Reflection;
using System.Resources;
using Microsoft.Extensions.Localization;

namespace ClientServices.Interfaces;

public interface ILocalizationService
{
    IStringLocalizer GetLocalizer(Assembly callingAssembly);

    ResourceManager GetResourceManager();
}
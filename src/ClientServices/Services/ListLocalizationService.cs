using System.Reflection;
using ClientServices.Interfaces;
using Microsoft.Extensions.Localization;
using Model.Globalization;
using Tools.Globalization;

namespace ClientServices.Services;

public class ListLocalizationService: ServiceBase, IListLocalizationService
{
    private static IStringLocalizer _localizer = null!;
    
    
    public ListLocalizationService(Assembly localizerAssembly)
    {
        _localizer = GetService<ILocalizationService>().GetLocalizer(localizerAssembly);
    }
    
    public List<LocalizableListItem> LocalizeList(List<LocalizableListItem> list)
    {
        foreach (var item in list)
        {
            if(item.Value!= null)
                item.LocalizedValue = _localizer[item.Value];
        }

        return list;
    }


}
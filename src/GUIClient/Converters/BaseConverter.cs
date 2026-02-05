using System;
using ClientServices.Interfaces;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.DependencyInjection;

namespace GUIClient.Converters;

public class BaseConverter
{
    protected IStringLocalizer _localizer;
    public IStringLocalizer Localizer
    {
        get => _localizer;
        set => _localizer = value;
    }
    
    public BaseConverter()
    {
        var localizationService = GetService<ILocalizationService>();
        _localizer = localizationService.GetLocalizer(typeof(BaseConverter).Assembly);
    }
    
    protected static T GetService<T>()
    {
        var result = Program.ServiceProvider.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    }
}
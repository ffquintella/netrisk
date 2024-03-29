﻿using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Resources;
using Microsoft.Extensions.Localization;

namespace Tools.Globalization;

public class Locator: IStringLocalizer
{

    private CultureInfo? _culture;
    private readonly Assembly _callingAssembly;
    private ResourceManager _resourceManager;
    
    public Locator(Assembly callingAssembly, ResourceManager resourceManager, CultureInfo? culture =  null)
    {
        //if (culture == null) culture = new CultureInfo("en-US");
        _culture = culture;
        _callingAssembly = callingAssembly;
        _resourceManager = resourceManager;
    }
    
    
    public IEnumerable<LocalizedString> GetAllStrings(bool includeParentCultures)
    {
        throw new System.NotImplementedException();
    }

    public LocalizedString this[string name]  {
        get
        {

            //ResourceManager rm = new ResourceManager("GUIClient.Resources.Localization",
                //typeof(Locator).Assembly);
            //    _callingAssembly);
            
            var rm = _resourceManager;
            
            string? str;
            
            if (_culture == null)
            {
                str = rm.GetString(name);
                if (str == null)
                {
                    str = "";
                }
                return new LocalizedString(name, str);
            }
            str = rm.GetString(name, _culture);
            if (str == null)
            {
                str = "";
            }
            return new LocalizedString(name, str);
        }
     
    }

    public LocalizedString this[string name, params object[] arguments] => this[name];
}
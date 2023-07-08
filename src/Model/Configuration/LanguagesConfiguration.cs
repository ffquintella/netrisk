using System.Collections.Generic;

namespace Model.Configuration;

public class LanguagesConfiguration
{
    public List<string> AvailableLocales { get; set; } = new List<string>();
    
    public string DefaultLocale { get; set; } = "en_US";
}
using Model.Configuration;
using Tools.Identification;

namespace GUIClient.ViewModels;

public class SettingsViewModel: ViewModelBase
{
    
    public string StrServer { get; }
    public string StrSystem { get; }
    public string StrOperationalSystem { get; }
    public string StrOperationalSystemData { get; }
    
    public string StrHost { get; }
    public string StrHostData { get; }
    
    public string StrDescription { get; }
    
    public ServerConfiguration ServerConfiguration { get; }
    public string ServerURL { get; }
    
    public SettingsViewModel(ServerConfiguration serverConfiguration)
    {
       StrSystem = Localizer["Sys"];
       StrServer = Localizer["Server"] ;
       StrOperationalSystem = Localizer["OperationalSystem"] + ":";
       StrHost = Localizer["Host"] +':';
       StrDescription = Localizer["Description"] +':';
       
       StrOperationalSystemData = ComputerInfo.GetOsVersion();
       StrHostData = ComputerInfo.GetComputerName() ;

       ServerConfiguration = serverConfiguration;
       
       ServerURL = serverConfiguration.Url;

    }

}
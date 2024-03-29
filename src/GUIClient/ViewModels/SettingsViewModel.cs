﻿using System.Reflection;
using ClientServices.Interfaces;
using Model.Configuration;
using ReactiveUI;
using Tools.Identification;

namespace GUIClient.ViewModels;

public class SettingsViewModel: ViewModelBase
{

    #region LANGUAGE
    public string StrServer { get; } = "";
    public string StrSystem { get; } = "";
    public string StrOperationalSystem { get; } = "";
    public string StrOperationalSystemData { get; } = "";
    public string StrHost { get; } = "";
    public string StrHostData { get; } = "";
    public string StrDescription { get; } = "";
    public string StrVersion { get; } = "";
    
    #endregion

    #region PROPERTIES
    
    public ServerConfiguration? ServerConfiguration { get; }
    public string ServerURL { get; } = "";

    private string _version = "0.0.0";

    public string Version
    {
        get => _version;
        set => this.RaiseAndSetIfChanged(ref _version, value);
    }
    
    #endregion

    #region CONSTRUCTOR
    
    public SettingsViewModel(ServerConfiguration serverConfiguration)
    {
       StrSystem = Localizer["Sys"];
       StrServer = Localizer["Server"] ;
       StrOperationalSystem = Localizer["OperationalSystem"] + ":";
       StrHost = Localizer["Host"] +':';
       StrDescription = Localizer["Description"] +':';
       StrVersion = Localizer["NetRiskVersionMGS"] +':';
       
       StrOperationalSystemData = ComputerInfo.GetOsVersion();
       StrHostData = ComputerInfo.GetComputerName() ;
       var mutableService = GetService<IMutableConfigurationService>();

       ServerConfiguration = serverConfiguration;
       
       ServerURL = mutableService.GetConfigurationValue("Server") ?? "";

       var systemService = GetService<ISystemService>();

       Version = systemService.GetClientAssemblyVersion();

    }
    
    #endregion

    #region METHODS
    public SettingsViewModel()
    {
        
    }
    #endregion
    
}
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using Contracts;
using Model.Plugins;
using ReactiveUI;


namespace GUIClient.ViewModels.Admin;

public class PluginsViewModel: ViewModelBase
{
    #region LANGUAGE
    public string StrTitle { get;  } = Localizer["Plugins"];
    public string StrName { get;  } = Localizer["Name"];
    public string StrDescription { get;  } = Localizer["Description"];
    public string StrEnabled { get;  } = Localizer["Enabled"];
    public string StrVersion { get;  } = Localizer["Version"];
    #endregion
    
    #region PROPERTIES
    private ObservableCollection<PluginInfo> _pluginsList = new();
    
    public ObservableCollection<PluginInfo> PluginsList
    {
        get => _pluginsList;
        set => this.RaiseAndSetIfChanged(ref _pluginsList, value);
    }
        
    #endregion

    #region SERVICES
    private IPluginsService _pluginsService = null!;
    public IPluginsService PluginsService
    {
        get => _pluginsService;
        set => this.RaiseAndSetIfChanged(ref _pluginsService, value);
    }

    

    #endregion
    
    #region CONSTRUCTOR
    public PluginsViewModel(IPluginsService pluginsService)
    {
        PluginsService = pluginsService;
    }
    #endregion
    
    #region EVENTS
    
    
    #endregion
    
    #region BUTTONS

    public async Task ReloadPluginsCommand()
    {
        await PluginsService.RequestPluginsReloadAsync();
        await LoadPluginsAsync();
    }
    
    #endregion
    
    #region METHODS

    public void Initialize()
    {
        _ = LoadPluginsAsync();
    }
    
    private  Task LoadPluginsAsync()
    {
        return Task.Run(async () =>
        {
            var plugins = await PluginsService.GetPluginsAsync();
            PluginsList = new ObservableCollection<PluginInfo>(plugins);
        });
    }
    
    public void SetPluginEnabledStatus(string pluginName, bool enabled)
    {
       _= PluginsService.SetPluginEnabledAsync(pluginName, enabled);
    }
    #endregion
}
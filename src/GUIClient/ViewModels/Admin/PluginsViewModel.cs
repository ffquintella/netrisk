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
    private string StrTitle { get;  } = Localizer["Plugins"];
    private string StrName { get;  } = Localizer["Name"];
    private string StrDescription { get;  } = Localizer["Description"];
    private string StrEnabled { get;  } = Localizer["Enabled"];
    private string StrVersion { get;  } = Localizer["Version"];
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
    private IPluginsService _pluginsService;
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
    
    #region METHODS

    public void Initialize()
    {
        _ = LoadPluginsAsync();
    }
    
    private Task LoadPluginsAsync()
    {
        return Task.Run(async () =>
        {
            var plugins = await PluginsService.GetPluginsAsync();
            PluginsList = new ObservableCollection<PluginInfo>(plugins);
        });
    }
    #endregion
}
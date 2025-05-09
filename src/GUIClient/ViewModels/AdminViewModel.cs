using GUIClient.ViewModels.Admin;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class AdminViewModel: ViewModelBase
{
    #region LANGUAGE

    public string StrAdmin { get; } = Localizer["Administration"];
    public string StrUsers { get; } = Localizer["Users"];
    public string StrDevices { get; } = Localizer["Devices"];
    public string StrConfiguration { get; }= Localizer["Configuration"];
    
    public string StrPlugins { get; }= Localizer["Plugins"];

    #endregion

    #region PROPERTIES

    private UsersViewModel UsersVM { get; set; }
    private DeviceViewModel DeviceVM { get; set; }
    
    private ConfigurationViewModel ConfigurationVM { get; set; }= new ConfigurationViewModel();
    
    private PluginsViewModel PluginsVM { get; set; }= new PluginsViewModel();
    
    private bool _usersIsVisible = true;
    public bool UsersIsVisible
    {
        get => _usersIsVisible;
        set => this.RaiseAndSetIfChanged(ref _usersIsVisible, value);
    }
    
    private bool _devicesIsVisible = false;
    public bool DevicesIsVisible
    {
        get => _devicesIsVisible;
        set => this.RaiseAndSetIfChanged(ref _devicesIsVisible, value);
    }
    
    private bool _configurationsIsVisible = false;
    public bool ConfigurationsIsVisible
    {
        get => _configurationsIsVisible;
        set => this.RaiseAndSetIfChanged(ref _configurationsIsVisible, value);
    }
    
    private bool _pluginsIsVisible = false;
    
    public bool PluginsIsVisible
    {
        get => _pluginsIsVisible;
        set => this.RaiseAndSetIfChanged(ref _pluginsIsVisible, value);
    }
    
    #endregion
    
    public AdminViewModel()
    {
        UsersVM = new UsersViewModel();
        UsersVM.Initialize();
        
        DeviceVM = new DeviceViewModel();
        DeviceVM.Initialize();
    }

    #region METHODS

    private void DisableButtons()
    {
        UsersIsVisible = false;
        DevicesIsVisible = false;
        ConfigurationsIsVisible = false;
        PluginsIsVisible = false;

    }
    
    public void BtUsersClicked()
    {
        DisableButtons();
        UsersIsVisible = true;
    }
    public void BtDevicesClicked()
    {
        DisableButtons();
        DevicesIsVisible = true;
    }
    
    public void BtPluginsClicked()
    {
        DisableButtons();
        PluginsIsVisible = true;
    }
    
    public void BtConfigurationsClicked()
    {
        DisableButtons();
        ConfigurationsIsVisible = true;
    }
    #endregion

}
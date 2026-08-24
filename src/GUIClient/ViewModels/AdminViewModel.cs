using ClientServices.Interfaces;
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

    public string StrIrpTemplates { get; } = Localizer["IRP Templates"];

    public string StrEntityAccess { get; } = Localizer["Entity Access"];

    #endregion

    #region PROPERTIES

    public UsersViewModel UsersVM { get; set; }
    public DeviceViewModel DeviceVM { get; set; }
    
    public ConfigurationViewModel ConfigurationVM { get; set; }= new ConfigurationViewModel();
    
    public PluginsViewModel? PluginsVM { get; set; }

    public IrpTemplatesViewModel IrpTemplatesVM { get; set; }

    public EntityAccessViewModel EntityAccessVM { get; set; }
    
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
    
    private bool _entityAccessIsVisible = false;

    public bool EntityAccessIsVisible
    {
        get => _entityAccessIsVisible;
        set => this.RaiseAndSetIfChanged(ref _entityAccessIsVisible, value);
    }

    private bool _irpTemplatesIsVisible = false;

    public bool IrpTemplatesIsVisible
    {
        get => _irpTemplatesIsVisible;
        set => this.RaiseAndSetIfChanged(ref _irpTemplatesIsVisible, value);
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
        _ = UsersVM.Initialize();
        
        DeviceVM = new DeviceViewModel();
        DeviceVM.Initialize();
        
        PluginsVM = new PluginsViewModel(GetService<IPluginsService>());
        PluginsVM.Initialize();

        IrpTemplatesVM = new IrpTemplatesViewModel();
        _ = IrpTemplatesVM.InitializeAsync();

        EntityAccessVM = new EntityAccessViewModel();
        _ = EntityAccessVM.InitializeAsync();
    }

    #region METHODS

    private void DisableButtons()
    {
        UsersIsVisible = false;
        DevicesIsVisible = false;
        ConfigurationsIsVisible = false;
        PluginsIsVisible = false;
        IrpTemplatesIsVisible = false;
        EntityAccessIsVisible = false;
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

    public void BtIrpTemplatesClicked()
    {
        DisableButtons();
        IrpTemplatesIsVisible = true;
    }

    public void BtEntityAccessClicked()
    {
        DisableButtons();
        EntityAccessIsVisible = true;
    }
    #endregion

}
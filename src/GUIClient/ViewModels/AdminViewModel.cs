using ReactiveUI;

namespace GUIClient.ViewModels;

public class AdminViewModel: ViewModelBase
{
    #region LANGUAGE

    public string StrAdmin { get; } = Localizer["Administration"];
    public string StrUsers { get; } = Localizer["Users"];
    public string StrDevices { get; } = Localizer["Devices"];

    #endregion

    #region PROPERTIES

    private UsersViewModel UsersVM { get; set; }
    private DeviceViewModel DeviceVM { get; set; }
    
    private bool _usersIsVisible = true;
    public bool UsersIsVisible
    {
        get => _usersIsVisible;
        set => this.RaiseAndSetIfChanged(ref _usersIsVisible, value);
    }
    
    private bool devicesIsVisible = false;
    public bool DevicesIsVisible
    {
        get => devicesIsVisible;
        set => this.RaiseAndSetIfChanged(ref devicesIsVisible, value);
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
    #endregion

}
using System;
using System.Reactive;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using Model.Authentication;
using ReactiveUI;
using Tools.Identification;

namespace GUIClient.ViewModels;

public class UserInfoViewModel: ViewModelBase
{
    private AuthenticatedUserInfo _userInfo;
    
    private string StrUserName { get;  }
    
    private string StrUserAccount { get; }
    
    private string StrRole { get; }
    
    private string StrLogout { get; }
    
    private string StrClient { get; } = Localizer["Client"]+ ": ";
    private string StrVersion { get; } = Localizer["Version"]+ ": ";
    private string StrHost { get; } = Localizer["Host"]+ ": ";
    
    private string StrServer { get; } = Localizer["Server"] + ": ";
    
    private string _url = "http://localhost:5443";
    public string Url
    {
        get => _url;
        set => this.RaiseAndSetIfChanged(ref _url, value);
    }
    
    private string _hostData = "localhost";
    public string HostData
    {
        get => _hostData;
        set => this.RaiseAndSetIfChanged(ref _hostData, value);
    }
    
    private string _version = "1.0.0";
    public string Version
    {
        get => _version;
        set => this.RaiseAndSetIfChanged(ref _version, value);
    }
    
    public ReactiveCommand<Unit, Unit> BtLogoutClicked { get; }
    
    public UserInfoViewModel(AuthenticatedUserInfo userInfo)
    {
        _userInfo = userInfo;
        StrUserName = Localizer["Username"];
        StrUserAccount = Localizer["Account"];
        StrRole = Localizer["Role"];
        StrLogout = Localizer["LogoutQuit"];
        
        BtLogoutClicked = ReactiveCommand.Create(ExecuteLogout);
        
        Task.Run(Initialize);
        
    }

    private void ExecuteLogout()
    {
        AuthenticationService.Logout();
        
        Environment.Exit(0);
    }
    
    public AuthenticatedUserInfo UserInfo
    {
        get => _userInfo;
        set => this.RaiseAndSetIfChanged(ref _userInfo, value);
    }
    
    private void Initialize()
    {
        var mutableConfigurationService = GetService<IMutableConfigurationService>();
        
        Url = mutableConfigurationService.GetConfigurationValue("Server")!;
        
        //Url = _userInfo.ServerUrl;
        HostData = ComputerInfo.GetComputerName() ;
        
        var systemService = GetService<ISystemService>();
        Version = systemService.GetClientAssemblyVersion();
    }
    
}
using System;
using System.Reactive;
using ClientServices.Interfaces;
using Model.Authentication;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class UserInfoViewModel: ViewModelBase
{
    private AuthenticatedUserInfo _userInfo;
    
    private string StrUserName { get;  }
    
    private string StrUserAccount { get; }
    
    private string StrRole { get; }
    
    private string StrLogout { get; }
    
    public ReactiveCommand<Unit, Unit> BtLogoutClicked { get; }
    
    private IAuthenticationService _authenticationService;
    public UserInfoViewModel(AuthenticatedUserInfo userInfo)
    {
        _userInfo = userInfo;
        StrUserName = Localizer["Username"];
        StrUserAccount = Localizer["Account"];
        StrRole = Localizer["Role"];
        StrLogout = Localizer["LogoutQuit"];
        
        BtLogoutClicked = ReactiveCommand.Create(ExecuteLogout);

        _authenticationService = GetService<IAuthenticationService>();
    }

    private void ExecuteLogout()
    {
        _authenticationService.Logout();
        Environment.Exit(0);
    }
    
    public AuthenticatedUserInfo UserInfo
    {
        get => _userInfo;
        set => this.RaiseAndSetIfChanged(ref _userInfo, value);
    }
    
    //private static T GetService<T>() => Locator.Current.GetService<T>();
    
}
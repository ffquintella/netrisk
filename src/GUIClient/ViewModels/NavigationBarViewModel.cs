using System;
using System.Reactive;
using Avalonia.Controls;
using GUIClient.Views;
using GUIClient.Models;
using Model.Configuration;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class NavigationBarViewModel: ViewModelBase
{

    
    private ServerConfiguration _configuration;
    private bool _isEnabled = false;
    private bool _isAdmin = false;
    private bool _hasAssessmentPermission = false;
    private bool _hasRiskPermission = false;
    public string? _loggedUser;

    public Boolean IsAdmin
    {
        get
        {
            if (_isEnabled) return _isAdmin;
            return false;
        }
        set => this.RaiseAndSetIfChanged(ref _isAdmin, value);
    }
    public Boolean IsEnabled
    {
        get => _isEnabled;
        set => this.RaiseAndSetIfChanged(ref _isEnabled, value);
    }

    public Boolean HasAssessmentPermission
    {
        get
        {
            if (!_isEnabled) return false;
            return _hasAssessmentPermission;
        }
        set => this.RaiseAndSetIfChanged(ref _hasAssessmentPermission, value);
    }
    public Boolean HasRiskPermission
    {
        get
        {
            if (!_isEnabled) return false;
            return _hasRiskPermission;
        }
        set => this.RaiseAndSetIfChanged(ref _hasRiskPermission, value);
    }
    
    public String? LoggedUser
    {
        get => _loggedUser;
        set => this.RaiseAndSetIfChanged(ref _loggedUser, value);
    }

    public ReactiveCommand<MainWindow, Unit> BtDashboardClicked { get; }
    public ReactiveCommand<Window, Unit> BtSettingsClicked { get; }
    public ReactiveCommand<MainWindow, Unit> BtDeviceClicked { get; }
    public ReactiveCommand<MainWindow, Unit> BtAssessmentClicked { get; }
    public ReactiveCommand<MainWindow, Unit> BtRiskClicked { get; }
    
    public ReactiveCommand<MainWindow, Unit> BtUsersClicked { get; }
    public ReactiveCommand<MainWindow, Unit> BtAccountClicked { get; }
    
    public NavigationBarViewModel(
        ServerConfiguration configuration)
    {
        
        _configuration = configuration;

        AuthenticationService.AuthenticationSucceeded += (obj, args) =>
        {
            Initialize();
        };
        
        BtDashboardClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenDashboard);
        BtSettingsClicked = ReactiveCommand.Create<Window>(ExecuteOpenSettings);
        BtDeviceClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenDevice);
        BtUsersClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenUsers);
        BtAssessmentClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenAssessment);
        BtRiskClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenRisk);
        BtAccountClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenAccount);
    }

    public void Initialize()
    {
        UpdateAuthenticationStatus();
    }
    
    public void UpdateAuthenticationStatus()
    {

        IsEnabled = true;
        if (AuthenticationService!.AuthenticatedUserInfo == null) AuthenticationService.GetAuthenticatedUserInfo();
        LoggedUser = AuthenticationService!.AuthenticatedUserInfo!.UserName!;
        if (AuthenticationService.AuthenticatedUserInfo.UserRole == "Administrator") IsAdmin = true;
        if (AuthenticationService.AuthenticatedUserInfo.UserPermissions!.Contains("assessments")) HasAssessmentPermission = true;
        if (AuthenticationService.AuthenticatedUserInfo.UserPermissions!.Contains("riskmanagement")) HasRiskPermission = true;
    }

    public void ExecuteOpenSettings(Window sender)
    {
        var dialog = new Settings()
        {
            DataContext = new SettingsViewModel(_configuration),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.ShowDialog( sender );

    }

    public void ExecuteOpenAccount(MainWindow window)
    {
        if (AuthenticationService == null)
        {
            AuthenticationService!.GetAuthenticatedUserInfo();
        }

        var dialog = new UserInfo()
        {
            DataContext = new UserInfoViewModel(AuthenticationService.AuthenticatedUserInfo!),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.ShowDialog( window );
    }
    
    
    public void ExecuteOpenDevice(MainWindow window)
    {
        ((MainWindowViewModel)window.DataContext!)
            .NavigateTo(AvaliableViews.Devices);
        
    }
    
    public void ExecuteOpenUsers(MainWindow window)
    {
        ((MainWindowViewModel)window.DataContext!).NavigateTo(AvaliableViews.Users);
    }
    
    public void  ExecuteOpenDashboard(MainWindow window)
    {

        ((MainWindowViewModel)window.DataContext!)
            .NavigateTo(AvaliableViews.Dashboard);
    }

    public void ExecuteOpenAssessment(MainWindow window)
    {
        ((MainWindowViewModel)window.DataContext!)
            .NavigateTo(AvaliableViews.Assessment);
    }
    
    public void ExecuteOpenRisk(MainWindow window)
    {
        ((MainWindowViewModel)window.DataContext!)
            .NavigateTo(AvaliableViews.Risk);
    }
    
}
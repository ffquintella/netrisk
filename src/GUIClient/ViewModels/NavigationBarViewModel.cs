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

    #region LANGUAGE

    public string StrDashboard { get; set; }
    public string StrAssessment { get; set; }
    public string StrEntities { get; set; }
    public string StrRisks { get; set; }
    public string StrUsers { get; set; }
    public string StrDevices { get; set; }
    public string StrSettings { get; set; }
    public string StrReports { get; set; }

    
    #endregion
    
    #region FIELDS
    
    private ServerConfiguration _configuration;
    private bool _isEnabled = false;
    private bool _isAdmin = false;
    private bool _hasAssessmentPermission = false;
    private bool _hasEntitiesPermission = false;
    private bool _hasRiskPermission = false;
    private bool _hasReportsPermission = false;
    public string? _loggedUser;
    
    #endregion

    #region PROPERTIES

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
    public Boolean HasEntitiesPermission
    {
        get
        {
            if (!_isEnabled) return false;
            return _hasEntitiesPermission;
        }
        set => this.RaiseAndSetIfChanged(ref _hasEntitiesPermission, value);
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
    
    public Boolean HasReportsPermission
    {
        get
        {
            if (!_isEnabled) return false;
            return _hasReportsPermission;
        }
        set => this.RaiseAndSetIfChanged(ref _hasReportsPermission, value);
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
    public ReactiveCommand<MainWindow, Unit> BtEntitiesClicked { get; }
    public ReactiveCommand<MainWindow, Unit> BtReportsClicked { get; }
    #endregion
    
    public NavigationBarViewModel(
        ServerConfiguration configuration)
    {
        
        StrDashboard = Localizer["Dashboard"];
        StrAssessment = Localizer["Assessment"];
        StrEntities = Localizer["Entities"];
        StrRisks = Localizer["Risks"];
        StrUsers = Localizer["Users"];
        StrDevices = Localizer["Devices"];
        StrSettings = Localizer["Settings"];
        StrReports = Localizer["Reports"];
        
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
        BtEntitiesClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenEntities);
        BtReportsClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenReports);
    }

    #region METHODS

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
        if (AuthenticationService.AuthenticatedUserInfo.UserPermissions!.Contains("asset")) HasEntitiesPermission = true;
        if (AuthenticationService.AuthenticatedUserInfo.UserPermissions!.Contains("reports")) HasReportsPermission = true;
        
    }
    

    public void ExecuteOpenReports(Window sender)
    {
        var repoWin = new ReportsWindow()
        {
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        repoWin.Show(sender);
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
    
    public void ExecuteOpenEntities(MainWindow window)
    {
        ((MainWindowViewModel)window.DataContext!)
            .NavigateTo(AvaliableViews.Entities);
        
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
    
    #endregion
}
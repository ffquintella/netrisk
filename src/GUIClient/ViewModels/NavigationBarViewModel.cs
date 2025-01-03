﻿using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using ClientServices.Interfaces;
using GUIClient.Views;
using GUIClient.Models;
using Microsoft.AspNetCore.Authentication;
using Model.Authentication;
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
    public string StrVulnerabilities { get; set; }
    
    public string StrIncidents => Localizer["Incidents"];

    
    #endregion
    
    #region FIELDS
    
    private ServerConfiguration _configuration;
    private bool _isEnabled = false;
    private bool _isAdmin = false;
    private bool _hasAssessmentPermission = false;
    private bool _hasEntitiesPermission = false;
    private bool _hasRiskPermission = false;
    private bool _hasHostsPermission = false;
    private bool _hasReportsPermission = false;
    private string? _loggedUser;
    private Timer? timer;
    
    #endregion

    #region PROPERTIES

    public Thickness NameMargin {
        get
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return new Thickness(5, 0, 0, 0);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
                return new Thickness(5, 0, 0, 0);
            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                return new Thickness(5, 4, 0, 0);
            
            return new Thickness(5, 4, 0, 0);
        }
    }

    
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
    
    public Boolean HasHostsPermission
    {
        get
        {
            if (!_isEnabled) return false;
            return _hasHostsPermission;
        }
        set => this.RaiseAndSetIfChanged(ref _hasHostsPermission, value);
    }
    
    private ObservableCollection<string> _userPermissions = new();
    public ObservableCollection<string> UserPermissions
    {
        get => _userPermissions;
        set => this.RaiseAndSetIfChanged(ref _userPermissions, value);
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
    
    private AuthenticatedUserInfo? _authenticatedUserInfo;

    public AuthenticatedUserInfo? AuthenticatedUserInfo
    {
        get => _authenticatedUserInfo;
        set => this.RaiseAndSetIfChanged(ref _authenticatedUserInfo, value);
    }
    
    public String? LoggedUser
    {
        get => _loggedUser;
        set => this.RaiseAndSetIfChanged(ref _loggedUser, value);
    }
    
    private int _notificationCount = 0;
    
    public int NotificationCount
    {
        get => _notificationCount;
        set => this.RaiseAndSetIfChanged(ref _notificationCount, value);
    }
    
    private bool _hasUnreadNotifications = false;
    public bool HasUnreadNotifications
    {
        get => _hasUnreadNotifications;
        set => this.RaiseAndSetIfChanged(ref _hasUnreadNotifications, value);
    }
    
    
    #endregion
    
    #region COMMANDS
    
    public ReactiveCommand<MainWindow, Unit> BtDashboardClicked { get; }
    public ReactiveCommand<Window, Unit> BtSettingsClicked { get; }
    public ReactiveCommand<MainWindow, Unit> BtDeviceClicked { get; }
    public ReactiveCommand<MainWindow, Unit> BtAssessmentClicked { get; }
    public ReactiveCommand<MainWindow, Unit> BtRiskClicked { get; }
    public ReactiveCommand<MainWindow, Unit> BtAccountClicked { get; }
    public ReactiveCommand<MainWindow, Unit> BtEntitiesClicked { get; }
    public ReactiveCommand<MainWindow, Unit> BtReportsClicked { get; }
    public ReactiveCommand<MainWindow, Unit> BtVulnerabilityClicked { get; }
    public ReactiveCommand<MainWindow, Unit> BtNotificationsClicked { get; }
    public ReactiveCommand<MainWindow, Unit> BtIncidentsClicked { get; }

    
    #endregion
    
    #region SERVICES

    private IMessagesService MessagesService { get; } = GetService<IMessagesService>();
    
    #endregion
    
    #region CONSTRUCTOR
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
        StrVulnerabilities = Localizer["Vulnerabilities"];
        
        _configuration = configuration;

        AuthenticationService.AuthenticationSucceeded += (obj, args) =>
        {
            Initialize();
            UserPermissions = new ObservableCollection<string>(AuthenticationService.AuthenticatedUserInfo!.UserPermissions!);
        };
        
        BtDashboardClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenDashboard);
        BtSettingsClicked = ReactiveCommand.Create<Window>(ExecuteOpenSettings);
        BtDeviceClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenDevice);
        BtAssessmentClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenAssessment);
        BtRiskClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenRisk);
        BtAccountClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenAccount);
        BtEntitiesClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenEntities);
        BtReportsClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenReports);
        BtVulnerabilityClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenVulnerability);
        BtNotificationsClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenNotification);
        BtIncidentsClicked = ReactiveCommand.Create<MainWindow>(ExecuteOpenIncidents);
        
    }
    
    #endregion

    #region METHODS

    public void Initialize()
    {
        UpdateAuthenticationStatus();
        timer = new Timer(UpdateNotifications, null, 0, 10000); // 10 seconds
    }
    
    public async void UpdateAuthenticationStatus()
    {
        AuthenticatedUserInfo = AuthenticationService.AuthenticatedUserInfo;
        IsEnabled = true;
        if (AuthenticationService!.AuthenticatedUserInfo == null) await AuthenticationService.GetAuthenticatedUserInfoAsync();
        LoggedUser = AuthenticationService!.AuthenticatedUserInfo!.UserName!;
        //if (AuthenticationService.AuthenticatedUserInfo.UserRole == "Administrator") IsAdmin = true;
        if (AuthenticationService.AuthenticatedUserInfo.IsAdmin) IsAdmin = true;
        if (AuthenticationService.AuthenticatedUserInfo.UserPermissions!.Contains("assessments") || IsAdmin) HasAssessmentPermission = true;
        if (AuthenticationService.AuthenticatedUserInfo.UserPermissions!.Contains("riskmanagement") || IsAdmin) HasRiskPermission = true;
        if (AuthenticationService.AuthenticatedUserInfo.UserPermissions!.Contains("asset") || IsAdmin) HasEntitiesPermission = true;
        if (AuthenticationService.AuthenticatedUserInfo.UserPermissions!.Contains("reports") || IsAdmin) HasReportsPermission = true;
        if (AuthenticationService.AuthenticatedUserInfo.UserPermissions!.Contains("hosts") || IsAdmin) HasHostsPermission = true;
        
    }

    private async void UpdateNotifications(object? state)
    {
        NotificationCount = await MessagesService.GetCountAsync();
        HasUnreadNotifications = await MessagesService.HasUnreadMessages();
    }
    

    public void ExecuteOpenNotification(MainWindow window)
    {
        var notificationWindow = new NotificationsWindow()
        {
            DataContext = new NotificationsViewModel(),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        
        notificationWindow.Show(window);
    }
    
    public void ExecuteOpenVulnerability(Window window)
    {
        ((MainWindowViewModel)window.DataContext!)
            .NavigateTo(AvaliableViews.Vulnerabilities);
    }
    
    public void ExecuteOpenReports(Window sender)
    {
        var repoWin = new ReportsWindow()
        {
            DataContext = new ReportsViewModel(),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        repoWin.Show(sender);
    }

    public void ExecuteOpenSettings(Window sender)
    {
        var dialog = new AdminWindow()
        {
            DataContext = new AdminViewModel(),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        dialog.ShowDialog( sender );

    }

    public void ExecuteOpenAccount(MainWindow window)
    {

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

    public void ExecuteOpenIncidents(MainWindow window)
    {
        ((MainWindowViewModel)window.DataContext!)
            .NavigateTo(AvaliableViews.Incidents);
    }

    #endregion
}
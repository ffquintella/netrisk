using GUIClient.Navigation;
using System;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Resources;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using ClientServices.Interfaces;
using GUIClient.Views;
using GUIClient.Models;
using GUIClient.Tools;
using Microsoft.AspNetCore.Authentication;
using Model.Authentication;
using Model.Configuration;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

public class NavigationBarViewModel: ViewModelBase
{

    #region LANGUAGE

    public string StrDashboard { get; set; }
    public string StrMasterDashboard => Localizer["Master Dashboard"];
    public string StrAssessment { get; set; }
    public string StrEntities { get; set; }
    public string StrRisks { get; set; }
    public string StrUsers { get; set; }
    public string StrDevices { get; set; }
    public string StrAdministration { get; set; }

    private INavigationService Navigation { get; } = GetService<INavigationService>();
    private IMainWindowProvider MainWindowProvider { get; } = GetService<IMainWindowProvider>();
    public string StrReports { get; set; }
    public string StrVulnerabilities { get; set; }
    
    public string StrIncidents => Localizer["Incidents"];
    public string StrNotifications => Localizer["Notifications"];


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
    private CancellationTokenSource _cts = new CancellationTokenSource();
    
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
    
    public ReactiveCommand<RxVoid, RxVoid> BtDashboardClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtMasterDashboardClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAdministrationClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDeviceClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAssessmentClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRiskClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAccountClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtEntitiesClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtReportsClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtVulnerabilityClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtNotificationsClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtIncidentsClicked { get; }

    
    #endregion
    
    #region SERVICES

    private IMessagesService MessagesService { get; } = GetService<IMessagesService>();
    private PluginManager PluginManager { get; } = GetService<PluginManager>();
    
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
        StrAdministration = Localizer["Administration"];
        StrReports = Localizer["Reports"];
        StrVulnerabilities = Localizer["Vulnerabilities"];
        
        _configuration = configuration;

        AuthenticationService.AuthenticationSucceeded += (obj, args) =>
        {
            Initialize();
            UserPermissions = new ObservableCollection<string>(AuthenticationService.AuthenticatedUserInfo!.UserPermissions!);
        };
        
        BtDashboardClicked = ReactiveCommand.Create(ExecuteOpenDashboard);
        BtMasterDashboardClicked = ReactiveCommand.Create(ExecuteOpenMasterDashboard);
        BtAdministrationClicked = ReactiveCommand.Create(ExecuteOpenAdministration);
        BtDeviceClicked = ReactiveCommand.Create(ExecuteOpenDevice);
        BtAssessmentClicked = ReactiveCommand.Create(ExecuteOpenAssessment);
        BtRiskClicked = ReactiveCommand.Create(ExecuteOpenRisk);
        BtAccountClicked = ReactiveCommand.Create(ExecuteOpenAccount);
        BtEntitiesClicked = ReactiveCommand.Create(ExecuteOpenEntities);
        BtReportsClicked = ReactiveCommand.Create(ExecuteOpenReports);
        BtVulnerabilityClicked = ReactiveCommand.Create(ExecuteOpenVulnerability);
        BtNotificationsClicked = ReactiveCommand.Create(ExecuteOpenNotification);
        BtIncidentsClicked = ReactiveCommand.CreateFromTask(ExecuteOpenIncidentsAsync);

        BtIncidentsClicked.ThrownExceptions.Subscribe(ex =>
        {
            Logger.Error(ex, "Error while opening incidents flow.");
        });
        
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
        try
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
        catch (Exception ex)
        {
            Logger.Error("UpdateAuthenticationStatus failed: {Message}", ex.Message);
        }
    }

    private async void UpdateNotifications(object? state)
    {
        if (_cts.IsCancellationRequested) return;
        try
        {
            NotificationCount = await MessagesService.GetCountAsync();
            HasUnreadNotifications = await MessagesService.HasUnreadMessages();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Logger.Warning("UpdateNotifications failed: {Message}", ex.Message);
        }
    }

    public override void Dispose()
    {
        _cts.Cancel();
        _cts.Dispose();
        timer?.Dispose();
        timer = null;
        base.Dispose();
    }
    

    // IX-1: Notifications, Reports and Administration are auxiliary windows — modeless,
    // parented to the shell, and singletons. They previously opened a fresh unparented window
    // per click (Notifications, Reports) or blocked the whole shell modally (Administration).

    public void ExecuteOpenNotification() =>
        Navigation.ShowAuxiliaryWindow<NotificationsWindow>(() => new NotificationsViewModel());

    public void ExecuteOpenReports() =>
        Navigation.ShowAuxiliaryWindow<ReportsWindow>(() => new ReportsViewModel());

    public void ExecuteOpenAdministration() =>
        Navigation.ShowAuxiliaryWindow<AdminWindow>(() => new AdminViewModel());

    public void ExecuteOpenAccount() =>
        Navigation.ShowModalWindow<UserInfo>(() =>
            new UserInfoViewModel(AuthenticationService.AuthenticatedUserInfo!));

    public void ExecuteOpenVulnerability() => Navigation.NavigateTo(AvaliableViews.Vulnerabilities);
    
    
    public void ExecuteOpenDevice() => Navigation.NavigateTo(AvaliableViews.Devices);

    public void ExecuteOpenEntities() => Navigation.NavigateTo(AvaliableViews.Entities);

    public void ExecuteOpenDashboard() => Navigation.NavigateTo(AvaliableViews.Dashboard);

    /// <summary>Admin-only cross-entity dashboard (Track 2 milestone 2.3.3).</summary>
    public void ExecuteOpenMasterDashboard() => Navigation.NavigateTo(AvaliableViews.MasterDashboard);

    public void ExecuteOpenAssessment() => Navigation.NavigateTo(AvaliableViews.Assessment);

    public void ExecuteOpenRisk() => Navigation.NavigateTo(AvaliableViews.Risk);

    public async Task ExecuteOpenIncidentsAsync()
    {
        var requireFaceId = await PluginManager.IsFaceIdEnabledAsync();
        
        if (requireFaceId)
        {
            try
            {
                var faceIdWindow = new VerifyFaceID
                {
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };

                var faceIdViewModel = new VerifyFaceIDViewModel(faceIdWindow);

                faceIdWindow.DataContext = faceIdViewModel;

                await faceIdWindow.ShowDialog(MainWindowProvider.GetActiveWindow());

                if (!faceIdViewModel.IsFaceIdVerified)
                {
                    return; // User did not verify Face ID, do not proceed
                }
            }
            catch (MissingManifestResourceException ex)
            {
                Logger.Error(ex, "Face ID resources are missing or incorrectly embedded.");

                var msgError = MessageBoxManager.GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = "Face ID could not be initialized due to missing application resources.",
                    Icon = Icon.Error,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

                await msgError.ShowAsync();
                return;
            }
        }
        
        Navigation.NavigateTo(AvaliableViews.Incidents);
    }

    #endregion
}
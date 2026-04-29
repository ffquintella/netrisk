using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using ClientServices.Interfaces;
using Model.Authentication;
using Model.Configuration;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;
using Tools;

namespace GUIClient.ViewModels;

public class LoginViewModel : ViewModelBase
{
    #region LANGUAGE
    public Window ParentWindow { get; set; }
    public string StrNotAccepted { get; }
    public string StrLogin { get; }
    public string StrUsername { get; }
    public string StrPassword { get; }
    public string StrExit { get; }
    public string StrEnvironment { get; }
    #endregion
    
    #region PROPERTIES
    
    public AuthenticationMethod? AuthenticationMethod { get; set; }

    public bool ProgressBarVisibility
    {
        get => _progressBarVisibility;
        set
        {
            this.RaiseAndSetIfChanged(ref _progressBarVisibility, value);
        }
    }
    
    public bool EnvironmentVisible
    {
        get => _environmentVisible;
        set
        {
            this.RaiseAndSetIfChanged(ref _environmentVisible, value);
        }
    }

    public int ProgressBarValue
    {
        get => _progressBarValue;
        set
        {
            this.RaiseAndSetIfChanged(ref _progressBarValue, value);
        }
    }

    public int ProgressBarMaxValue { get; set; } = 100;
    private ServerConfiguration _serverConfiguration;
    private IMutableConfigurationService _mutableConfigurationService;

    private ObservableCollection<AuthenticationMethod> _authenticationMethods;

    public ObservableCollection<AuthenticationMethod> AuthenticationMethods
    {
        get => _authenticationMethods;
        set
        {
            if (value.Count > 2)
            {
                ParentWindow.Height = 300;
                
                EnvironmentVisible = true;
            }
            else
            {
                AuthenticationMethod = value.FirstOrDefault(am => am.Name == "Local");
                EnvironmentVisible = false;
            }
            this.RaiseAndSetIfChanged(ref _authenticationMethods, value);
        }
    }

    public ReactiveCommand<Window?, Unit> BtSsoClicked { get; }
    public ReactiveCommand<Window?, Unit> BtLoginClicked { get; }
    public ReactiveCommand<Unit, Unit> BtExitClicked { get; }
    
    #endregion
    
    public LoginViewModel(Window parentWindow)
    {
        ParentWindow = parentWindow;
        
        StrNotAccepted = Localizer["NotAccepted"];
        StrLogin = Localizer["Login"];
        StrPassword = Localizer["Password"];
        StrUsername = Localizer["Username"];
        StrExit = Localizer["Exit"];
        StrEnvironment = Localizer["Environment"];
        
        BtSsoClicked = ReactiveCommand.CreateFromTask<Window?>(ExecuteSsoLogin);
        BtLoginClicked = ReactiveCommand.CreateFromTask<Window?>(ExecuteLogin);
        BtExitClicked = ReactiveCommand.Create(ExecuteExit);

        _serverConfiguration = GetService<ServerConfiguration>();
        _mutableConfigurationService = GetService<IMutableConfigurationService>();

        _authenticationMethods = new ObservableCollection<AuthenticationMethod>(AuthenticationService.GetAuthenticationMethods());
        AuthenticationMethods = _authenticationMethods;


        /*AuthenticationService.AuthenticationSucceeded += (obj, args) =>
        {
            Initialize();
        };*/

    }

    private bool _isAccepted;
    private bool _progressBarVisibility = false;
    private int _progressBarValue = 0;
    private bool _environmentVisible = false;

    public bool IsAccepted
    {
        get => _isAccepted;
        set => this.RaiseAndSetIfChanged(ref _isAccepted, value);
    }


    public string? Username { get; set;}
    public string? Password { get; set; }
    
    private bool _loginReady = false;
    private bool _loginError = false;
    private CancellationTokenSource _samlCts = new CancellationTokenSource();

    private async Task ExecuteSsoLogin(Window? loginWindow)
    {
        //string target= "http://www.microsoft.com";

        var url = _mutableConfigurationService.GetConfigurationValue("Server");
        
        if(!url!.EndsWith('/')) url += '/';
        
        var requestId = RandomGenerator.RandomString(20);
        var target = url + $"Authentication/SAMLRequest?requestId={requestId}";

        try
        {
            Process.Start(new ProcessStartInfo(target) {UseShellExecute = true});

            ProgressBarValue = 1;
            ProgressBarVisibility = true;
            _loginError = false;
            _loginReady = false;
            
            // Reset cancellation source for new SAML attempt
            _samlCts.Cancel();
            _samlCts.Dispose();
            _samlCts = new CancellationTokenSource();
            var token = _samlCts.Token;

            // Wire cancellation to window close
            if (loginWindow != null)
                loginWindow.Closed += (_, _) => _samlCts.Cancel();

            // Background auth-check loop: polls server until accepted or timeout
            _ = Task.Run(async () =>
            {
                try
                {
                    var accepted = await AuthenticationService.CheckSamlAuthenticationAsync(requestId);
                    int i = 0;
                    while (!accepted && i < 60 * 5 && !token.IsCancellationRequested)
                    {
                        await Task.Delay(TimeSpan.FromMilliseconds(1000), token);
                        i++;
                        accepted = await AuthenticationService.CheckSamlAuthenticationAsync(requestId);
                        if (accepted)
                        {
                            _loginReady = true;
                            _loginError = false;
                        }
                        else _loginError = true;
                    }
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    Logger.Warning("SAML auth check failed: {Message}", ex.Message);
                }
            }, token);

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                int i = 1;
                while (!_loginReady && i < 60 * 10 && !token.IsCancellationRequested)
                {
                    ProgressBarValue = i;
                    i++;
                    this.RaisePropertyChanged(nameof(ProgressBarValue));
                    try { await Task.Delay(TimeSpan.FromMilliseconds(1000), token); }
                    catch (OperationCanceledException) { break; }
                }

                if (_loginReady & _loginError == false)
                {
                    ProgressBarValue = 100;
                    ProgressBarVisibility = false;
                    if (loginWindow != null)
                    {
                        loginWindow.Close();
                    }
                }else
                {
                    Logger.Error("SAML authentication timed out");
                    var messageBoxStandardWindow = MessageBoxManager
                        .GetMessageBoxStandard(new MessageBoxStandardParams
                        {
                            ContentTitle = Localizer["Warning"],
                            ContentMessage = Localizer["SAMLAuthenticationTimeoutMSG"],
                            Icon = Icon.Warning,
                        });

                    await messageBoxStandardWindow.ShowAsync();
                }
            }, DispatcherPriority.Background);

            ProgressBarValue = 100;
            ProgressBarVisibility = false;



        }
        catch (AggregateException aex)
        {
            Logger.Warning("Agregate exception received: {Message}", aex.Message);
        }
        catch (System.Exception other)
        {
            Logger.Error("Error opening browser: {Message}", other.Message);
            var messageBoxStandardWindow = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorOpeningExternalBrowserMSG"]  ,
                    Icon = Icon.Warning,
                });
                        
            await messageBoxStandardWindow.ShowAsync(); 
        }
    }
    public async Task ExecuteLogin(Window? loginWindow)
    {
        ProgressBarValue = 0;
        if (AuthenticationMethod == null)
        {
            var messageBoxStandardWindow = MessageBoxManager
                .GetMessageBoxStandard(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["SelectAuthenticationMSG"]  ,
                    Icon = Icon.Warning,
                });
                        
            await messageBoxStandardWindow.ShowAsync(); 
        }
        else
        {
            if ( AuthenticationMethod.Type == "SAML")
            {
                await ExecuteSsoLogin(loginWindow);
            }
            else
            {
                ProgressBarVisibility = true;

                var task = Task.Run(() => AuthenticationService.DoServerAuthentication(Username!, Password!));

                int i = 1;
                while(!task.IsCompleted && i < 100)
                {
                    ProgressBarValue = i;
                    i++;
                    await Task.Delay(TimeSpan.FromMilliseconds(20));
                }

                ProgressBarValue = 100;
                ProgressBarVisibility = false;
                
                var result = await task;

                if (result != 0)
                {
                    var messageBoxStandardWindow = MessageBoxManager
                        .GetMessageBoxStandard(   new MessageBoxStandardParams
                        {
                            ContentTitle = Localizer["Warning"],
                            ContentMessage = Localizer["LoginError"]  ,
                            Icon = Icon.Warning
                        });
                            
                    await messageBoxStandardWindow.ShowAsync(); 
                }
                else
                {
                    AuthenticationService.NotifyAuthenticationSucceeded();
                    if (loginWindow != null)
                    {
                        loginWindow.Close();
                    } 
                }
            }
            
        }
    }
    
    public void ExecuteExit()
    {
        Environment.Exit(0);
    }

    public override void Dispose()
    {
        _samlCts.Cancel();
        _samlCts.Dispose();
        base.Dispose();
    }
    
    //private static T GetService<T>() =>  Locator.Current.GetService<T>();
    
}
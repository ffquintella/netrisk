using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reactive;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using MessageBox.Avalonia.DTO;
using Model.Authentication;
using Model.Configuration;
using ReactiveUI;
using Tools;

namespace GUIClient.ViewModels;

public class LoginViewModel : ViewModelBase
{
    
    public string StrNotAccepted { get; }
    public string StrLogin { get; }
    public string StrUsername { get; }
    public string StrPassword { get; }
    public string StrExit { get; }
    public string StrEnvironment { get; }
    
    public AuthenticationMethod? AuthenticationMethod { get; set; }

    public bool ProgressBarVisibility
    {
        get => _progressBarVisibility;
        set
        {
            this.RaiseAndSetIfChanged(ref _progressBarVisibility, value);
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
    public List<AuthenticationMethod> AuthenticationMethods => AuthenticationService.GetAuthenticationMethods();
    public ReactiveCommand<Window?, Unit> BtSSOClicked { get; }
    public ReactiveCommand<Window?, Unit> BtLoginClicked { get; }
    public ReactiveCommand<Unit, Unit> BtExitClicked { get; }
    
    public LoginViewModel()
    {
        StrNotAccepted = Localizer["NotAccepted"];
        StrLogin = Localizer["Login"];
        StrPassword = Localizer["Password"];
        StrUsername = Localizer["Username"];
        StrExit = Localizer["Exit"];
        StrEnvironment = Localizer["Environment"];
        
        BtSSOClicked = ReactiveCommand.Create<Window?>(ExecuteSSOLogin);
        BtLoginClicked = ReactiveCommand.Create<Window?>(ExecuteLogin);
        BtExitClicked = ReactiveCommand.Create(ExecuteExit);

        _serverConfiguration = GetService<ServerConfiguration>();
        
        /*AuthenticationService.AuthenticationSucceeded += (obj, args) =>
        {
            Initialize();
        };*/
        
    }

    private bool _isAccepted;
    private bool _progressBarVisibility = false;
    private int _progressBarValue = 0;

    public bool IsAccepted
    {
        get
        {
            return _isAccepted;
        }
        set
        {
            this.RaiseAndSetIfChanged(ref _isAccepted, value);
        }
    }


    public string? Username { get; set;}
    public string? Password { get; set; }

    private async void ExecuteSSOLogin(Window? loginWindow)
    {
        //string target= "http://www.microsoft.com";

        var requestId = RandomGenerator.RandomString(20);
        var target = _serverConfiguration.Url + $"Authentication/SAMLRequest?requestId={requestId}";
        
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            
            ProgressBarValue = 1;
            ProgressBarVisibility = true;

            var task = Task.Run<bool>(() =>
            {
                var accepted = AuthenticationService.CheckSamlAuthentication(requestId);
                int i = 0;
                while (!accepted && i < 60 * 5)
                {
                    Thread.Sleep(1000);
                    i++;
                    accepted = AuthenticationService.CheckSamlAuthentication(requestId);
                    if (accepted) return true;
                }
                return false;
            });

            int i = 1;
            while(!task.IsCompleted && i < 60 * 5)
            {
                ProgressBarValue = i;
                i++;
                this.RaisePropertyChanged("Progress");
                await Task.Delay(TimeSpan.FromMilliseconds(1000));
            }

            if (task.IsCompleted && task.Result == true)
            {
                ProgressBarValue = 100;
                ProgressBarVisibility = false;
                if (loginWindow != null)
                {
                    loginWindow.Close();
                } 
            }
            else
            {
                Logger.Error("SAML authentication timeouted");
                var messageBoxStandardWindow = MessageBox.Avalonia.MessageBoxManager
                    .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Warning"],
                        ContentMessage = Localizer["SAMLAuthenticationTimeoutMSG"]  ,
                        Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                    });
                        
                await messageBoxStandardWindow.Show(); 
            }
            

            
            //System.Diagnostics.Process.Start(target);
        }
        catch (System.Exception other)
        {
            Logger.Error("Error opening browser: {0}", other.Message);
            var messageBoxStandardWindow = MessageBox.Avalonia.MessageBoxManager
                .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorOpeningExternalBrowserMSG"]  ,
                    Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                });
                        
            await messageBoxStandardWindow.Show(); 
        }
    }
    public async void ExecuteLogin(Window? loginWindow)
    {
        ProgressBarValue = 0;
        if (AuthenticationMethod == null)
        {
            var messageBoxStandardWindow = MessageBox.Avalonia.MessageBoxManager
                .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Warning"],
                    ContentMessage = Localizer["SelectAuthenticationMSG"]  ,
                    Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                });
                        
            await messageBoxStandardWindow.Show(); 
        }
        else
        {
            if ( AuthenticationMethod.Type == "SAML")
            {
                /*var messageBoxStandardWindow = MessageBox.Avalonia.MessageBoxManager
                    .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Warning"],
                        ContentMessage = Localizer["NotImplementedMSG"]  ,
                        Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                    });
                            
                await messageBoxStandardWindow.Show();*/
                ExecuteSSOLogin(loginWindow);
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
                    this.RaisePropertyChanged("Progress");
                    await Task.Delay(TimeSpan.FromMilliseconds(20));
                }

                ProgressBarValue = 100;
                ProgressBarVisibility = false;
                
                var result =  task.Result;
                
                //var result = _authenticationService.DoServerAuthentication(Username, Password);

                if (result != 0)
                {
                    var messageBoxStandardWindow = MessageBox.Avalonia.MessageBoxManager
                        .GetMessageBoxStandardWindow(   new MessageBoxStandardParams
                        {
                            ContentTitle = Localizer["Warning"],
                            ContentMessage = Localizer["LoginError"]  ,
                            Icon = MessageBox.Avalonia.Enums.Icon.Warning,
                        });
                            
                    await messageBoxStandardWindow.Show(); 
                }
                else
                {
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
    
    //private static T GetService<T>() =>  Locator.Current.GetService<T>();
    
}
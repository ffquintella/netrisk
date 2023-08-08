using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using ClientServices.Interfaces;
using GUIClient.ViewModels;
using Microsoft.Extensions.Localization;
using Model.Rest;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using Splat;
using MsBox.Avalonia.Enums;
namespace GUIClient.Views;

public partial class LoginWindow : Window
{
    private IRegistrationService _registrationService;
    private ILocalizationService _localizationService;
    private IEnvironmentService _environmentService;
    private IStringLocalizer _localizer;
    private IMutableConfigurationService _mutableConfigurationService;
    private IAuthenticationService _authenticationService;

    
    public LoginWindow()
    {
        DataContext = new LoginViewModel();
  
        _registrationService = GetService<IRegistrationService>();
        _localizationService = GetService<ILocalizationService>();
        _environmentService = GetService<IEnvironmentService>();
        _mutableConfigurationService = GetService<IMutableConfigurationService>();
        _localizer = _localizationService.GetLocalizer(typeof(LoginWindow).Assembly);
        _authenticationService = GetService<IAuthenticationService>();
        
        InitializeComponent();
#if DEBUG
        this.AttachDevTools();
#endif
        
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
    }

    private void OnOpened(object? sender, EventArgs eventArgs)
    {
        //Checking if registration was donne
        if (!_registrationService.IsRegistered)
        {
            var result = _registrationService.Register(_environmentService.DeviceID);

            if (result.Result == RequestResult.Success)
            {
                
                //var bitmap = new Bitmap(AssetLoader.Open(new Uri("avares://GUIClient/Assets/Hex-Warning.ico")));
                
                var messageBoxStandardWindow = MessageBoxManager
                    .GetMessageBoxStandard(   new MessageBoxStandardParams
                    {
                        ContentTitle = _localizer["Warning"],
                        ContentMessage = _localizer["NoRegistrationMSG"]  + " " +  result.RequestID ,
                        Icon = MsBox.Avalonia.Enums.Icon.Warning,
                    });
                        
                messageBoxStandardWindow.ShowAsync();
                
 
                
            }
            else
            {
                var messageBoxStandardWindow = MessageBoxManager
                    .GetMessageBoxStandard(_localizer["Warning"], _localizer["RegistrationErrorMSG"]);
                messageBoxStandardWindow.ShowAsync();
            }
        }
        else
        {
            
            // checking if registration was accepted
            if (!_registrationService.IsAccepted)
            {
                if (!_registrationService.CheckAcceptance(_environmentService.DeviceID))
                {
                    var messageBoxStandardWindow = MessageBoxManager
                        .GetMessageBoxStandard(_localizer["Warning"], _localizer["RegistrationNotAcceptedMSG"] 
                                                                      + " " + _mutableConfigurationService.GetConfigurationValue("RegistrationID"));
                    messageBoxStandardWindow.ShowAsync();
                }
                else
                {
                    ((LoginViewModel) DataContext!).IsAccepted = true;
                }
            }
            else
            {
                ((LoginViewModel) DataContext!).IsAccepted = true;
            }
        }
    }

    protected static T GetService<T>()
    {
        var result = Locator.Current.GetService<T>();
        if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
        return result;
    } 
}
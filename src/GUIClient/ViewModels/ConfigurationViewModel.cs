using ClientServices.Interfaces;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class ConfigurationViewModel: ViewModelBase
{
    #region LANGUAGES
        private string StrSystemConfigurations { get; } = Localizer["SystemConfigurations"];
        private string StrBackupPassword { get; } = Localizer["BackupPassword"];
        private string StrSave { get; } = Localizer["Save"];
    #endregion
    
    #region PROPERTIES
        private string _backupPassword = string.Empty;
        public string BackupPassword
        {
            get => _backupPassword;
            set => this.RaiseAndSetIfChanged(ref _backupPassword, value);
        }
        private bool _passwordSet = false;
        public bool PasswordSet
        {
            get => _passwordSet;
            set => this.RaiseAndSetIfChanged(ref _passwordSet, value);
        }
    #endregion

    #region SERVICES
        private IConfigurationsService ConfigurationsService => GetService<IConfigurationsService>();
    #endregion
    
    #region CONSTRUCTOR

    public ConfigurationViewModel()
    {
        var authenticationService = GetService<IAuthenticationService>();
        
        
        authenticationService.AuthenticationSucceeded += (_, _) =>
        {
            var PasswordSet = ConfigurationsService.BackupPasswordIsSet();
        };
    }

    #endregion
    
    #region METHODS

    public void SaveConfigurations()
    {
        ConfigurationsService.SetBackupPassword(BackupPassword);
        
        var messageBoxStandardWindow = MessageBoxManager
            .GetMessageBoxStandard(   new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Success"],
                ContentMessage = Localizer["ConfigurationsSaved"]  ,
                Icon = Icon.Success,
            });
                        
        messageBoxStandardWindow.ShowAsync(); 
    }
    #endregion
}
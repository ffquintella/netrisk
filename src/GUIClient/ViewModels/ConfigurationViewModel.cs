using ClientServices.Interfaces;
using Model.DTO;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class ConfigurationViewModel: ViewModelBase
{
    #region LANGUAGES
        public string StrSystemConfigurations { get; } = Localizer["SystemConfigurations"];
        public string StrBackupPassword { get; } = Localizer["BackupPassword"];
        public string StrWebsiteSync { get; } = Localizer["WebsiteSync"];
        public string StrWebsiteSyncUrl { get; } = Localizer["WebsiteSyncUrl"];
        public string StrWebsiteSyncInterval { get; } = Localizer["WebsiteSyncInterval"];
        public string StrWebsiteFastSyncInterval { get; } = Localizer["WebsiteFastSyncInterval"];
        public new string StrSave { get; } = Localizer["Save"];
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

        private string _websiteSyncUrl = string.Empty;
        public string WebsiteSyncUrl
        {
            get => _websiteSyncUrl;
            set => this.RaiseAndSetIfChanged(ref _websiteSyncUrl, value);
        }

        private int _websiteSyncInterval = 60;
        public int WebsiteSyncInterval
        {
            get => _websiteSyncInterval;
            set => this.RaiseAndSetIfChanged(ref _websiteSyncInterval, value);
        }

        private int _websiteFastSyncInterval = 2;
        public int WebsiteFastSyncInterval
        {
            get => _websiteFastSyncInterval;
            set => this.RaiseAndSetIfChanged(ref _websiteFastSyncInterval, value);
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
            PasswordSet = ConfigurationsService.BackupPasswordIsSet();

            var syncConfig = ConfigurationsService.GetWebsiteSyncConfig();
            WebsiteSyncUrl = syncConfig.Url;
            WebsiteSyncInterval = syncConfig.IntervalMinutes;
            WebsiteFastSyncInterval = syncConfig.FastIntervalMinutes;
        };
    }

    #endregion

    #region METHODS

    public void SaveConfigurations()
    {
        ConfigurationsService.SetBackupPassword(BackupPassword);

        ConfigurationsService.SetWebsiteSyncConfig(new WebsiteSyncConfigDto
        {
            Url = WebsiteSyncUrl,
            IntervalMinutes = WebsiteSyncInterval,
            FastIntervalMinutes = WebsiteFastSyncInterval
        });

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

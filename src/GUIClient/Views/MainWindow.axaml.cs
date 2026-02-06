using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClientServices.Interfaces;
using GUIClient.Exceptions;
using GUIClient.ViewModels;
using Microsoft.Extensions.Localization;
using Model.Configuration;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using Serilog;
using Microsoft.Extensions.DependencyInjection;

namespace GUIClient.Views
{
    public partial class MainWindow : Window
    {

        protected IStringLocalizer _localizer;
        public IStringLocalizer Localizer
        {
            get => _localizer;
            set => _localizer = value;
        }
        
        public MainWindow()
        {
            var localizationService = GetService<ILocalizationService>();
            var logger = Log.Logger;
            var localizer = localizationService.GetLocalizer(typeof(ViewModelBase).Assembly);
            if (localizer == null)
            {
                logger.Error("Error getting localizer service");
                throw new DIException("Error getting localizer service");
            }
            _localizer = localizer;
            
            DataContext = new MainWindowViewModel();
            
            InitializeComponent();
            
            WindowsManager.AllWindows.Add(this);
            
            #if DEBUG
                this.AttachDevTools();
            #endif
            
        }
        
        public void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async Task<bool> UpgradeCheck()
        {
            var systemsService = GetService<ISystemService>();
            if (await systemsService.NeedsUpgradeAsync())
            {
                var msgUpgrade = MessageBoxManager
                    .GetMessageBoxStandard(   new MessageBoxStandardParams
                    {
                        ContentTitle = Localizer["Upgrade"],
                        ContentMessage = Localizer["UpgradeNeededMSG"]  ,
                        Icon = MsBox.Avalonia.Enums.Icon.Warning,
                        WindowStartupLocation = WindowStartupLocation.CenterOwner
                    });

                await msgUpgrade.ShowAsync();
                
                var dialog = new UpgradeWindow();
                dialog.DataContext = new UpgradeViewModel();
                
                _ = dialog.ShowDialog(this);
                
                ((UpgradeViewModel)dialog.DataContext).StartUpgrade();
                return true;
            }

            return false;
        }
        
        private async void LoadCheckAsync(object? sender, EventArgs eventArgs)
        {
            var upgrading = await UpgradeCheck();

            if (!upgrading)
            {
                var authenticationService = GetService<IAuthenticationService>();
                if (authenticationService.IsAuthenticated == false)
                {
                    var result = await authenticationService.TryAuthenticateAsync();
                    if (result == false)
                    {
                        var dialog = new LoginWindow();
                        await dialog.ShowDialog( this );
                    }
                }
            }

        } 
        
        private Grid OverlayGrid => this.FindControl<Grid>("OverlayGridCtrl")!;
        
        public void ShowOverlay() => OverlayGrid.ZIndex = 1000;

        public void HideOverlay() => OverlayGrid.ZIndex = -1;
        public void btn_SettingsOnClick( object? sender, RoutedEventArgs args )
        {
            var localizationService = GetService<ILocalizationService>();
            var serverConfiguration = GetService<ServerConfiguration>();
            
            var dialog = new Settings()
            {
                DataContext = new SettingsViewModel(serverConfiguration)
            };
            dialog.ShowDialog( this );

        }

        private static T GetService<T>()
        {
            var result = Program.ServiceProvider.GetService<T>();
            if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
            return result;
        } 
    }
}
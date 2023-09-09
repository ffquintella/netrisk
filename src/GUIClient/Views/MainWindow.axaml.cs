using System;
using System.Linq;
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
using Splat;

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
            
        }
        
        public void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private async void UpgradeCheck()
        {
            var systemsService = GetService<ISystemService>();
            if (systemsService.NeedsUpgrade())
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
            }
        }
        
        private void LoadCheck(object? sender, EventArgs eventArgs)
        {
            UpgradeCheck();
            
            var authenticationService = GetService<IAuthenticationService>();
            if (authenticationService.IsAuthenticated == false)
            {
                var result = authenticationService.TryAuthenticate();
                if (result == false)
                {
                    //_logger.Debug("Starting authentication");
                    var dialog = new LoginWindow();
                    dialog.ShowDialog( this );
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
        protected static T GetService<T>()
        {
            var result = Locator.Current.GetService<T>();
            if (result == null) throw new Exception("Could not find service of class: " + typeof(T).Name);
            return result;
        } 
    }
}
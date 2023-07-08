using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using ClientServices.Interfaces;
using GUIClient.ViewModels;
using Model.Configuration;
using Splat;

namespace GUIClient.Views
{
    public partial class MainWindow : Window
    {

        public MainWindow()
        {
            DataContext = new MainWindowViewModel();
            
            InitializeComponent();
             
        }
        
        public void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }
        
        private void LoadCheck(object? sender, EventArgs eventArgs)
        {
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
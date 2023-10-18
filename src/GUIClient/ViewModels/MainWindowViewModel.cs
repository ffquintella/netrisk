using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using ClientServices.Interfaces;
using GUIClient.Models;
using GUIClient.Views;
using Model.Configuration;
using ReactiveUI;

namespace GUIClient.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {

        #region FIELDS

        private bool _viewDashboardIsVisible = true;
        private bool _viewDeviceIsVisible = false;
        private bool _assessmentIsVisible = false;
        private bool _riskIsVisible = false;
        private bool _entitiesIsVisible = false;
        private bool _usersIsVisible = false;
        private bool _vulnerabilitiesIsVisible = false;

        #endregion

        #region LANGUAGE

        public string StrApplicationMN { get; }
        public string StrExitMN { get; }
        public string StrAbout { get; } = Localizer["About"];

        #endregion

        #region PROPERTIES

        
        public Window? ParentWindow
        {
            get { return WindowsManager.AllWindows.Find(w => w is MainWindow); }
        }
        
         public bool ViewDashboardIsVisible
        {
            get => _viewDashboardIsVisible;
            set => this.RaiseAndSetIfChanged(ref _viewDashboardIsVisible, value);
        }
        
        public bool ViewDeviceIsVisible
        {
            get => _viewDeviceIsVisible;
            set => this.RaiseAndSetIfChanged(ref _viewDeviceIsVisible, value);
        }
        
        public bool AssessmentIsVisible
        {
            get => _assessmentIsVisible;
            set => this.RaiseAndSetIfChanged(ref _assessmentIsVisible, value);
        }
        
        public bool RiskIsVisible
        {
            get => _riskIsVisible;
            set => this.RaiseAndSetIfChanged(ref _riskIsVisible, value);
        }
        
        public bool EntitiesIsVisible
        {
            get => _entitiesIsVisible;
            set => this.RaiseAndSetIfChanged(ref _entitiesIsVisible, value);
        }
        
        public bool UsersIsVisible
        {
            get => _usersIsVisible;
            set => this.RaiseAndSetIfChanged(ref _usersIsVisible, value);
        }

        public bool VulnerabilitiesIsVisible
        {
            get => _vulnerabilitiesIsVisible;
            set => this.RaiseAndSetIfChanged(ref _vulnerabilitiesIsVisible, value);
        }
        
        public Thickness _appMenuMargin;

        public Thickness AppMenuMargin
        {
            get => _appMenuMargin;
            set => this.RaiseAndSetIfChanged(ref _appMenuMargin, value);
        }
        
        private DeviceViewModel _deviceViewModel = 
            new DeviceViewModel();

        public DeviceViewModel DeviceViewModel
        {
            get => _deviceViewModel;
            set =>  this.RaiseAndSetIfChanged(ref _deviceViewModel, value);
        }

        private VulnerabilitiesViewModel _vulnerabilitiesViewModel = 
            new VulnerabilitiesViewModel();

        public VulnerabilitiesViewModel VulnerabilitiesViewModel
        {
            get => _vulnerabilitiesViewModel;
            set =>  this.RaiseAndSetIfChanged(ref _vulnerabilitiesViewModel, value);
        }

        #endregion
       
        public MainWindowViewModel()
        {
            
            StrApplicationMN = Localizer["ApplicationMN"];
            StrExitMN = Localizer["ExitMN"];

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                AppMenuMargin = new Thickness(60, 0, 0, 0);
            }
            else
            {
                AppMenuMargin = new Thickness(0, 0, 0, 0);
            }
            
            
        }
        
        #region METHODS
        public void OnMenuExitCommand()
        {
            Environment.Exit(0);
        }
        
        public void OnMenuAboutCommand()
        {
            ServerConfiguration configuration = GetService<ServerConfiguration>();
            
            var dialog = new Settings()
            {
                DataContext = new SettingsViewModel(configuration),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            dialog.ShowDialog( ParentWindow! );
        }

        public void NavigateTo(AvaliableViews view)
        {
            HideAllViews();
            switch (view)
            {
                case AvaliableViews.Dashboard:
                    ViewDashboardIsVisible = true;
                    break;
                case AvaliableViews.Devices:
                    ViewDeviceIsVisible = true;
                    break;
                case AvaliableViews.Assessment:
                    AssessmentIsVisible = true;
                    break;
                case AvaliableViews.Risk:
                    RiskIsVisible = true;
                    break;
                case AvaliableViews.Entities:
                    EntitiesIsVisible = true;
                    break;
                case AvaliableViews.Users:
                    UsersIsVisible = true;
                    break;
                case AvaliableViews.Vulnerabilities:
                    VulnerabilitiesIsVisible = true;
                    break;
            }
        }

        private void HideAllViews()
        {
            ViewDashboardIsVisible = false;
            ViewDeviceIsVisible = false;
            AssessmentIsVisible = false;
            RiskIsVisible = false;
            UsersIsVisible = false;
            EntitiesIsVisible = false;
            VulnerabilitiesIsVisible = false;
        }

        #endregion

        
        
    }
}
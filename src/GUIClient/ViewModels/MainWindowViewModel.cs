using System;
using System.Runtime.InteropServices;
using Avalonia;
using ClientServices.Interfaces;
using GUIClient.Models;
using ReactiveUI;

namespace GUIClient.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {
        
        private bool _viewDashboardIsVisible = true;
        private bool _viewDeviceIsVisible = false;
        private bool _assessmentIsVisible = false;
        private bool _riskIsVisible = false;
        private bool _entitiesIsVisible = false;
        private bool _usersIsVisible = false;
        
        public string StrApplicationMN { get; }
        public string StrExitMN { get; }

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

        public void OnMenuExitCommand()
        {
            Environment.Exit(0);
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
        }
        
        
    }
}
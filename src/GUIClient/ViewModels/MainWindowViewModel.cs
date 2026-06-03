using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using ClientServices.Interfaces;
using GUIClient.Helpers;
using GUIClient.Models;
using GUIClient.Views;
using Model.Configuration;
using ReactiveUI;
using System.Reactive;
using DAL.Entities;
using Serilog;

namespace GUIClient.ViewModels
{
    public class MainWindowViewModel : ViewModelBase
    {

        #region FIELDS

        private bool _viewDashboardIsVisible = true;
        private bool _hostsIsVisible = false;
        private bool _assessmentIsVisible = false;
        private bool _riskIsVisible = false;
        private bool _entitiesIsVisible = false;
        private bool _vulnerabilitiesIsVisible = false;

        #endregion

        #region LANGUAGE

        public string StrApplicationMn { get; }
        public string StrExitMn { get; }
        public string StrAbout { get; } = Localizer["About"];
        public string StrDebug { get; } = Localizer["Debug"];

        #endregion

        #region PROPERTIES

        private MainWindow? ParentWindow
        {
            get { return (MainWindow)WindowsManager.AllWindows.Find(w => w is MainWindow)!; }
        }
        
         public bool ViewDashboardIsVisible
        {
            get => _viewDashboardIsVisible;
            set => this.RaiseAndSetIfChanged(ref _viewDashboardIsVisible, value);
        }
        
        public bool HostsIsVisible
        {
            get => _hostsIsVisible;
            set => this.RaiseAndSetIfChanged(ref _hostsIsVisible, value);
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
        
        public bool VulnerabilitiesIsVisible
        {
            get => _vulnerabilitiesIsVisible;
            set => this.RaiseAndSetIfChanged(ref _vulnerabilitiesIsVisible, value);
        }

        private bool _incidentsIsVisble;
        
        public bool IncidentsIsVisble
        {
            get => _incidentsIsVisble;
            set => this.RaiseAndSetIfChanged(ref _incidentsIsVisble, value);
        }
        
        private Thickness _appMenuMargin;
        public Thickness AppMenuMargin
        {
            get => _appMenuMargin;
            set => this.RaiseAndSetIfChanged(ref _appMenuMargin, value);
        }

        /// <summary>True when running under Apple Darwin – the in-window menu is then
        /// suppressed in favour of the macOS global menu bar (see MainWindow.axaml NativeMenu).</summary>
        public bool IsMacOS => PlatformInfo.IsMacOS;

        /// <summary>True on every platform except macOS – drives the in-window <c>Menu</c> visibility.</summary>
        public bool IsNotMacOS => PlatformInfo.IsNotMacOS;

        /// <summary>
        /// Navigation-bar inset. On macOS the in-window menu is hidden and its row collapses,
        /// so the navigation bar rises to the top of the title bar; we inset its left edge to
        /// clear the native traffic-light window controls (which Darwin renders top-left).
        /// </summary>
        public Thickness NavBarMargin => PlatformInfo.IsMacOS
            ? new Thickness(80, 2, 0, 2)
            : new Thickness(0, 2, 0, 2);
        
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

        private IncidentsViewModel? _incidentsViewModel;
        public IncidentsViewModel? IncidentsViewModel
        {
            get => _incidentsViewModel;
            set => this.RaiseAndSetIfChanged(ref _incidentsViewModel, value);
        }
        
        private bool _isDebug = false;
        
        public bool IsDebug
        {
            get => _isDebug;
            set => this.RaiseAndSetIfChanged(ref _isDebug, value);
        }
        

        #endregion

        #region BUTTONS

        public ReactiveCommand<String, Unit> BtDebugWindowClicked { get; }

        /// <summary>Global <c>Ctrl+P</c> hot-key: opens the reporting/export surface.</summary>
        public ReactiveCommand<Unit, Unit> HotKeyOpenReportsCommand { get; }

        #endregion
       
        #region CONSTRUCTOR
        public MainWindowViewModel()
        {
            
            StrApplicationMn = Localizer["ApplicationMN"];
            StrExitMn = Localizer["ExitMN"];

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                AppMenuMargin = new Thickness(60, 0, 0, 0);
            }
            else
            {
                AppMenuMargin = new Thickness(0, 0, 0, 0);
            }
            
            #if DEBUG
            IsDebug = true;
            #endif
            
            BtDebugWindowClicked = ReactiveCommand.Create<String>(ExecuteDebugCommand);
            HotKeyOpenReportsCommand = ReactiveCommand.Create(ExecuteOpenReports);

        }
        #endregion
        
        #region METHODS
        public void OnMenuExitCommand()
        {
            Environment.Exit(0);
        }
        
        private void ExecuteOpenReports()
        {
            var parent = ParentWindow;
            if (parent == null) return;

            var repoWin = new ReportsWindow()
            {
                DataContext = new ReportsViewModel(),
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            };
            repoWin.Show(parent);
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
        
        private void ExecuteDebugCommand(string command)
        {
            switch (command)
            {
                case "IRP-Create":

                    var risk = new Risk()
                    {
                        Id = 0,
                        Subject = "Debug Risk"
                    };
                    
                    var addIrpDc = new IncidentResponsePlanViewModel(risk, testOnly: true);
                    var addIrp = new IncidentResponsePlanWindow()
                    {
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        //SizeToContent = SizeToContent.WidthAndHeight,
                        Width = 1000,
                        Height = 800,
                        CanResize = true,
                        DataContext = addIrpDc
                    };
        
                    addIrp.Show();
                    
                    break;
                
                case "IRP-Task-Create":
                    
                    var plan = new IncidentResponsePlan()
                    {
                        Id = 99,
                        Name = "Debug IRP"
                    };
                    
                    var addIrpTask = new IncidentResponsePlanTaskViewModel(plan);
                    var addIrpTaskWin = new IncidentResponsePlanTaskWindow()
                    {
                        WindowStartupLocation = WindowStartupLocation.CenterOwner,
                        Width = 1000,
                        Height = 800,
                        CanResize = true,
                        DataContext = addIrpTask
                    };
        
                    addIrpTaskWin.Show();

                    break;
                
                default:
                    Log.Warning("Unknown command clicked: {command}", command);
                    break;
            }
            
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
                    HostsIsVisible = true;
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
                case AvaliableViews.Incidents:
                    if(IncidentsViewModel == null)
                    {
                        IncidentsViewModel = new IncidentsViewModel(ParentWindow!);
                    }
                    IncidentsIsVisble = true;
                    break;
                case AvaliableViews.Vulnerabilities:
                    VulnerabilitiesIsVisible = true;
                    break;
            }
        }

        private void HideAllViews()
        {
            ViewDashboardIsVisible = false;
            HostsIsVisible = false;
            AssessmentIsVisible = false;
            RiskIsVisible = false;
            EntitiesIsVisible = false;
            VulnerabilitiesIsVisible = false;
            IncidentsIsVisble = false;
        }

        #endregion
        
    }
}
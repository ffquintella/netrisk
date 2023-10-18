using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class HostsViewModel: ViewModelBase
{
    #region LANGUAGE

    public string StrHosts { get; } = Localizer["Hosts"];
    public string StrDetails { get; } = Localizer["Details"];
    public string StrServices { get; } = Localizer["Services"];
    public string StrVulnerabilities { get; } = Localizer["Vulnerabilities"];
    public string StrStatus { get; } = Localizer["Status"] + ": ";
    public string StrRegistrationDate { get; } = Localizer["RegistrationDate"] + ": ";
    public string StrResponsibleTeam { get; } = Localizer["ResponsibleTeam"] + ": ";
    public string StrOperatingSystem { get; } = Localizer["OperatingSystem"] + ": ";
    public string StrPort { get; } = Localizer["Port"] + ": ";
    public string StrTitle { get; } = Localizer["Title"] ;
    public string StrScore { get; } = Localizer["Score"] ;
    public string StrImpact { get; } = Localizer["Impact"] ;
    public string StrFirstDetection { get; } = Localizer["FirstDetection"] ;
    public string StrLastDetection { get; } = Localizer["LastDetection"] ;
    public string StrDetectionCount { get; } = Localizer["DetectionCount"] ;
    public string StrFixTeam { get; } = Localizer["FixTeam"] ;
    public string StrAnalyst { get; } = Localizer["Analyst"] ;

    #endregion

    #region PROPERTIES

        private ObservableCollection<Host> _hostsList;
        public ObservableCollection<Host> HostsList
        {
            get => _hostsList;
            set => this.RaiseAndSetIfChanged(ref _hostsList, value);
        }
        
        private Host _selectedHost;
        public Host SelectedHost
        {
            get => _selectedHost;
            set
            {
                Task.Run(() =>
                {
                    SelectedHostsServices = new ObservableCollection<HostsService>(HostsService.GetAllHostService(value.Id));
                    SelectedHostsVulnerabilities = new ObservableCollection<Vulnerability>(HostsService.GetAllHostVulnerabilities(value.Id));
                });

                this.RaiseAndSetIfChanged(ref _selectedHost, value);
            }
        }


        private ObservableCollection<HostsService> _selectedHostsServices;
        public ObservableCollection<HostsService> SelectedHostsServices
        {
            get => _selectedHostsServices;
            set => this.RaiseAndSetIfChanged(ref _selectedHostsServices, value);
        }
        
        private ObservableCollection<Vulnerability> _selectedHostsVulnerabilities;
        public ObservableCollection<Vulnerability> SelectedHostsVulnerabilities
        {
            get => _selectedHostsVulnerabilities;
            set => this.RaiseAndSetIfChanged(ref _selectedHostsVulnerabilities, value);
        }

    #endregion

    #region FIELDS

        private bool _initialized = false;

    #endregion

    #region SERVICES

        private IHostsService HostsService { get; } = GetService<IHostsService>();

    #endregion

    public HostsViewModel()
    {
        AuthenticationService.AuthenticationSucceeded += (_, _) =>
        {
            
            Initialize();
        };
    }
    
    #region METHODS

    
    private async void Initialize()
    {
        if (!_initialized)
        {
            await Task.Run(() =>
            {
                HostsList = new ObservableCollection<Host>(HostsService.GetAll().OrderBy(h => h.HostName));
            });

            _initialized = true;
        }
    }
    

    #endregion
}
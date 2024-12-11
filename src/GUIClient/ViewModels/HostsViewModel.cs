using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.Models;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
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

        private ObservableCollection<Host> _hostsList = new();
        public ObservableCollection<Host> HostsList
        {
            get => _hostsList;
            set => this.RaiseAndSetIfChanged(ref _hostsList, value);
        }
        
        private Host? _selectedHost = new ();
        public Host? SelectedHost
        {
            get => _selectedHost;
            set
            {
                Task.Run(async () =>
                {
                    if (value != null)
                    {
                        SelectedHostsServices = new ObservableCollection<HostsService>(await HostsService.GetAllHostServiceAsync(value.Id));
                        SelectedHostsVulnerabilities = new ObservableCollection<Vulnerability>(await HostsService.GetAllHostVulnerabilitiesAsync(value.Id));
                    }
                    else
                    {
                        SelectedHostsServices = new ();
                        SelectedHostsVulnerabilities = new ();
                    }
                    
                });

                this.RaiseAndSetIfChanged(ref _selectedHost, value);
            }
        }
        
        private string? _selectedHostsFilter = string.Empty;
        public string? SelectedHostsFilter
        {
            get => _selectedHostsFilter;
            set
            {
                Task.Run(async () =>
                {
                    
                    // Then perform the action
                    if (value != null)
                    {
                        this.RaiseAndSetIfChanged(ref _selectedHostsFilter, value);
                        // Wait for 2 seconds
                        await Task.Delay(TimeSpan.FromSeconds(2));
                        LoadHostsWithFilters();
                    }
                    else
                    {
                        this.RaiseAndSetIfChanged(ref _selectedHostsFilter, string.Empty);
                    }
                    
                });
            }
        }


        private ObservableCollection<HostsService> _selectedHostsServices = new ();
        public ObservableCollection<HostsService> SelectedHostsServices
        {
            get => _selectedHostsServices;
            set => this.RaiseAndSetIfChanged(ref _selectedHostsServices, value);
        }
        
        private ObservableCollection<Vulnerability> _selectedHostsVulnerabilities = new ();
        public ObservableCollection<Vulnerability> SelectedHostsVulnerabilities
        {
            get => _selectedHostsVulnerabilities;
            set => this.RaiseAndSetIfChanged(ref _selectedHostsVulnerabilities, value);
        }

    #endregion

    #region FIELDS

        private bool _initialized;
        private List<Host> _hosts = new();

    #endregion

    #region SERVICES

        private IHostsService HostsService { get; } = GetService<IHostsService>();
        private IDialogService DialogService { get; } = GetService<IDialogService>();

    #endregion

    public HostsViewModel()
    {
        AuthenticationService.AuthenticationSucceeded += (_, _) =>
        {
            if(AuthenticationService.AuthenticatedUserInfo == null) return;
            if(AuthenticationService.AuthenticatedUserInfo.UserPermissions.Contains("hosts"))
                _ = InitializeAsync();
        };
    }
    
    #region METHODS

    
    private async Task InitializeAsync()
    {
        if (!_initialized)
        {
            var h = await HostsService.GetFilteredAsync(100, 1, "");
            HostsList = new ObservableCollection<Host>(h );

            _initialized = true;
        }
    }

    public async void BtAddHostClicked()
    {
        var hostDialogParameter = new HostDialogParameter()
        {
            Operation = OperationType.Create
        };
        var dialogNewHost = await DialogService.ShowDialogAsync<HostDialogResult,HostDialogParameter>(nameof(EditHostDialogViewModel), hostDialogParameter);
        
        if(dialogNewHost == null) return;

        if (dialogNewHost.Action == ResultActions.Ok )
        {
            HostsList.Add(dialogNewHost.ResultingHost);
        }
    }
    public async void BtEditHostClicked()
    {
        var parameter = new HostDialogParameter()
        {
            Operation = OperationType.Edit,
            Host = SelectedHost
        };
        
        var editedHost = await 
            DialogService.ShowDialogAsync<HostDialogResult, HostDialogParameter>
                (nameof(EditHostDialogViewModel), parameter);
        
        if(editedHost == null) return;
        
        if (editedHost.Action == ResultActions.Ok )
        {
            var idx = HostsList.IndexOf(SelectedHost!);
            HostsList[idx] = editedHost.ResultingHost;
        }
    }

    public async void LoadHostsWithFilters()
    {
        var h = await HostsService.GetFilteredAsync(100, 1, "hostName@=" + _selectedHostsFilter );
        HostsList = new ObservableCollection<Host>(h);
    }
    
    public async void BtReloadHostsClicked()
    {
        await Task.Run(() =>
        {
            SelectedHostsFilter = string.Empty;
            
            //HostsList = new ObservableCollection<Host>(HostsService.GetAll());
        });
    }
    public async void BtDeleteHostClicked()
    {
        var msgConfirm = MessageBoxManager
            .GetMessageBoxStandard(new MessageBoxStandardParams
            {
                ContentTitle = Localizer["Confirmation"],
                ContentMessage = Localizer["AreYouSureToDeleteThisHostMSG"] ,
                Icon = Icon.Question,
                ButtonDefinitions = ButtonEnum.YesNoAbort,
                WindowStartupLocation = WindowStartupLocation.CenterOwner
            });

        var result = await msgConfirm.ShowAsync();
        
        if(result != ButtonResult.Yes) return;
        
        HostsService.Delete(SelectedHost!.Id);
        
        HostsList.Remove(SelectedHost);
    }
    

    #endregion
}
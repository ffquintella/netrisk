using System.Collections.Generic;
using System.Collections.ObjectModel;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;
using Model;
using ReactiveUI;
using System.Reactive;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using GUIClient.Models;
using GUIClient.ViewModels.Dialogs.Parameters;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;
using ReactiveUI.Validation.Extensions;
using Tools.Network;


namespace GUIClient.ViewModels;

public class EditHostDialogViewModel: ParameterizedDialogViewModelBaseAsync<HostDialogResult,HostDialogParameter>
{
    #region LANGUAGE

        public string StrTeamResponsible => Localizer["TeamResponsible"];
        public string StrSave => Localizer["Save"];
        public string StrCancel => Localizer["Cancel"];
        public string StrComments => Localizer["Comments"];
        public string StrName => Localizer["Name"];
        public string StrOperatingSystem => Localizer["OperatingSystem"];

    #endregion

    #region PROPERTIES

        public List<IntStatus> Statuses { get; } = new();    
        
        private IntStatus? _selectedStatus;
        
        public IntStatus? SelectedStatus
        {
            get => _selectedStatus;
            set => this.RaiseAndSetIfChanged(ref _selectedStatus, value);
        }
        
        private ObservableCollection<Team> _teams = new();
        public ObservableCollection<Team> Teams
        {
            get => _teams;
            set => this.RaiseAndSetIfChanged(ref _teams, value);
        }
        
        private Team? _selectedTeam;
        public Team? SelectedTeam
        {
            get => _selectedTeam;
            set => this.RaiseAndSetIfChanged(ref _selectedTeam, value);
        }
        
        private ComboBoxItem? _selectedOs;
        public ComboBoxItem? SelectedOs
        {
            get => _selectedOs;
            set => this.RaiseAndSetIfChanged(ref _selectedOs, value);
        }
        
        private int? _selectedOsIndex;
        public int? SelectedOsIndex
        {
            get => _selectedOsIndex;
            set => this.RaiseAndSetIfChanged(ref _selectedOsIndex, value);
        }

        private bool _saveEnabled = false;
        public bool SaveEnabled
        {
            get => _saveEnabled;
            set => this.RaiseAndSetIfChanged(ref _saveEnabled, value);
        }
        
        private string _hostName = string.Empty;
        public string HostName
        {
            get => _hostName;
            set => this.RaiseAndSetIfChanged(ref _hostName, value);
        }
        
        private string _hostIp = string.Empty;
        public string HostIp
        {
            get => _hostIp;
            set => this.RaiseAndSetIfChanged(ref _hostIp, value);
        }
        
        private string _fqdn = string.Empty;
        public string Fqdn
        {
            get => _fqdn;
            set => this.RaiseAndSetIfChanged(ref _fqdn, value);
        }
        
        private Host? _host;
        public Host? Host
        {
            get => _host;
            set => this.RaiseAndSetIfChanged(ref _host, value);
        }
        
        private string _comments = string.Empty;
        public string Comments
        {
            get => _comments;
            set => this.RaiseAndSetIfChanged(ref _comments, value);
        }

    #endregion

    #region SERVICES

    private ITeamsService TeamsService { get; } = GetService<ITeamsService>();
    private IHostsService HostsService { get; } = GetService<IHostsService>();

    #endregion

    #region FIELDS

        private OperationType _operation;

    #endregion
    
    #region BUTTONS
    
    public ReactiveCommand<Unit, Unit> BtSaveClicked { get; }
    public ReactiveCommand<Unit, Unit> BtCancelClicked { get; }
    
    #endregion

    public EditHostDialogViewModel()
    {
        Statuses.Add(IntStatus.Active);
        Statuses.Add(IntStatus.UnderReview);
        Statuses.Add(IntStatus.New);
        Statuses.Add(IntStatus.AwaitingFix);
        Statuses.Add(IntStatus.AwaitingFixVerification);
        Statuses.Add(IntStatus.NeedsFix);
        Statuses.Add(IntStatus.Ok);
        
        Initialize();
        
        BtSaveClicked = ReactiveCommand.Create(ExecuteSave);
        
        BtCancelClicked = ReactiveCommand.Create(() => Close(new HostDialogResult()
        {
            Action = ResultActions.Cancel
        } ));
        
        this.ValidationRule(
            viewModel => viewModel.HostName, 
            val => val != null ,
            Localizer["PleaseEnterAValidValueMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.Fqdn, 
            val => val != null && FqdnTool.IsValid(val),
            Localizer["PleaseEnterAValidFqdnMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.HostIp, 
            val => val != null && IpAddressTool.IsValid(val),
            Localizer["PleaseEnterAValidIPMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedTeam, 
            val => val != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.ValidationRule(
            viewModel => viewModel.SelectedStatus, 
            val => val != null,
            Localizer["PleaseSelectOneMSG"]);
        
        this.IsValid().Subscribe(observer =>
        {
            SaveEnabled = observer;
        });
        
    }

    #region METHODS

    private void Initialize()
    {
        Task.Run(() =>
        {
            Teams = new ObservableCollection<Team>(TeamsService.GetAll());
        });
        
    }

    private async void ExecuteSave()
    {
        if(_operation == OperationType.Create || Host == null) Host= new Host();
        
        Host!.HostName = HostName;
        Host.Ip = HostIp;
        Host.Status = (short) SelectedStatus!;
        Host.Comment = Comments;
        Host.Source = "manual";
        Host.RegistrationDate = DateTime.Now;
        Host.LastVerificationDate = DateTime.Now;
        Host.TeamId = SelectedTeam!.Value;
        Host.Fqdn = Fqdn;

        if (SelectedOsIndex != null)
        {
            switch (SelectedOsIndex.Value)
            {
                case 0:
                    Host.Os = "Windows";
                    break;
                case 1:
                    Host.Os = "Linux";
                    break;
                case 2:
                    Host.Os = "MacOS";
                    break;
            }
        }

        if( _operation == OperationType.Create) Host.Id = 0;

        try
        {
            if(_operation == OperationType.Edit) HostsService.UpdateAsync(Host);
            else if (_operation == OperationType.Create)
            {
                var newHost = await HostsService.Create(Host);

                if (newHost == null)
                {
                    var messageBoxStandardWindow = MessageBoxManager
                        .GetMessageBoxStandard(new MessageBoxStandardParams
                        {
                            ContentTitle = Localizer["Error"],
                            ContentMessage = Localizer["ErrorSavingHostMSG"],
                            Icon = Icon.Error,
                        });

                    await messageBoxStandardWindow.ShowAsync();
                }
                Close(new HostDialogResult()
                {
                    Action = ResultActions.Ok,
                    ResultingHost = newHost!
                });
            }
            

            Close(new HostDialogResult()
            {
                Action = ResultActions.Ok,
                ResultingHost = Host!
            });
        }
        catch (Exception ex)
        {
            var messageBoxStandardWindow = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Error"],
                    ContentMessage = Localizer["ErrorSavingHostMSG"] + "\n" + ex.Message,
                    Icon = Icon.Error,
                });

            await messageBoxStandardWindow.ShowAsync();
        }


    }

    #endregion

    public override Task ActivateAsync(HostDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            _operation = parameter.Operation;
            if(_operation == OperationType.Edit) Host = parameter.Host;
            HostIp = parameter.Host?.Ip ?? string.Empty;
            HostName = parameter.Host?.HostName ?? string.Empty;
            Comments = parameter.Host?.Comment ?? string.Empty;
            SelectedStatus = Statuses.FirstOrDefault(s => (short)s == parameter.Host?.Status);
            SelectedTeam = Teams.FirstOrDefault(t => t.Value == parameter.Host?.TeamId);
            Fqdn = parameter.Host?.Fqdn ?? string.Empty;
            
            if(parameter.Host != null && parameter.Host.Os != null)
                switch (parameter.Host.Os.ToLower())
                {
                    case "windows":
                        SelectedOsIndex = 0;
                        break;
                    case "linux":
                        SelectedOsIndex = 1;
                        break;
                    case "macos":
                        SelectedOsIndex = 2;
                        break;
                    default:
                        SelectedOsIndex = null;
                        break;
                }
            

            Initialize();
        });
    }
}
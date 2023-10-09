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
using ReactiveUI.Validation.Extensions;
using Tools.Network;


namespace GUIClient.ViewModels;

public class EditHostDialogViewModel: DialogViewModelBase<HostDialogResult>
{
    #region LANGUAGE

        public string StrTeamResponsible => Localizer["TeamResponsible"];
        public string StrSave => Localizer["Save"];
        public string StrCancel => Localizer["Cancel"];
        public string StrComments => Localizer["Comments"];

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

    #endregion

    #region FIELDS

    

    #endregion
    
    #region BUTTONS
    
    public ReactiveCommand<Unit, Unit> BtSaveClicked { get; }
    public ReactiveCommand<Unit, Unit> BtCancelClicked { get; }
    
    #endregion

    public EditHostDialogViewModel()
    {
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
        Teams = new ObservableCollection<Team>(TeamsService.GetAll());
    }

    private void ExecuteSave()
    {

    }

    #endregion
}
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ClientServices.Interfaces;
using DAL.Entities;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Results;
using Model;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;
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
using Tools.Network;
using GUIClient.Interfaces;
using GUIClient.Tools.Hosts;
using System.Windows.Input;


namespace GUIClient.ViewModels;

public class EditHostDialogViewModel: ParameterizedDialogViewModelBaseAsync<HostDialogResult,HostDialogParameter>, ISaveableDialog
{
    #region LANGUAGE
    public string StrTitle => Localizer["EditHost"];
    public string StrIp => Localizer["IP"];
    public string StrFqdn => Localizer["FQDN"];


        public string StrTeamResponsible => Localizer["TeamResponsible"];
        public new string StrSave => Localizer["Save"];
        public new string StrCancel => Localizer["Cancel"];
        public string StrComments => Localizer["Comments"];
        public string StrName => Localizer["Name"];
        public string StrOperatingSystem => Localizer["OperatingSystem"];
        public string StrStatus => Localizer["Status"];
        public string StrCriticality => Localizer["Criticality"];
        public string StrEnvironment => Localizer["Environment"];
        public string StrOwner => Localizer["Owner"];
        public string StrEntity => Localizer["Entity"];
        public string StrEntityTip => Localizer["HostEntityAssignmentTip"];

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
        
        /// <summary>Not set, then 1 Very low … 5 Critical (S38 §3.5).</summary>
        public List<CriticalityOption> CriticalityOptions { get; } = CriticalityScale.EditableLevels
            .Select(level => new CriticalityOption(level, level is null
                ? Localizer[CriticalityScale.LabelKey(level)].Value
                : level + " · " + Localizer[CriticalityScale.LabelKey(level)]))
            .ToList();

        private CriticalityOption? _selectedCriticality;

        /// <summary>
        /// The chosen level. "Not set" is a real option (Level null), so a null write here is the
        /// ComboBox losing its selection when it re-templates, never the user's choice — ignored, or
        /// it would silently clear a host's criticality on save.
        /// </summary>
        public CriticalityOption? SelectedCriticality
        {
            get => _selectedCriticality;
            set
            {
                if (value is null) return;
                this.RaiseAndSetIfChanged(ref _selectedCriticality, value);
            }
        }

        private string _environment = string.Empty;
        public string Environment
        {
            get => _environment;
            set => this.RaiseAndSetIfChanged(ref _environment, value);
        }

        private string _owner = string.Empty;
        public string Owner
        {
            get => _owner;
            set => this.RaiseAndSetIfChanged(ref _owner, value);
        }

        private string _comments = string.Empty;
        public string Comments
        {
            get => _comments;
            set => this.RaiseAndSetIfChanged(ref _comments, value);
        }

        /// <summary>"Unassigned", then the entities a host can belong to (M58, T282, S39 D-20).</summary>
        private ObservableCollection<EntityOption> _entityOptions = new();
        public ObservableCollection<EntityOption> EntityOptions
        {
            get => _entityOptions;
            set => this.RaiseAndSetIfChanged(ref _entityOptions, value);
        }

        private EntityOption? _selectedEntity;

        /// <summary>
        /// The host's entity. As with criticality, "Unassigned" is a real option (Id null), so a null write is the
        /// ComboBox losing its selection when it re-templates — ignored, or a save would silently unassign the host.
        /// The server records a choice made here as a manual assignment, which no sync moves.
        /// </summary>
        public EntityOption? SelectedEntity
        {
            get => _selectedEntity;
            set
            {
                if (value is null) return;
                this.RaiseAndSetIfChanged(ref _selectedEntity, value);
            }
        }

        /// <summary>The edited host's entity, held until the options have loaded.</summary>
        private int? _hostEntityId;

    #endregion

    #region SERVICES

    private ITeamsService TeamsService { get; } = GetService<ITeamsService>();
    private IHostsService HostsService { get; } = GetService<IHostsService>();
    private IEntitiesService EntitiesService { get; } = GetService<IEntitiesService>();

    #endregion

    #region FIELDS

        private OperationType _operation;

    #endregion
    
    #region BUTTONS
    
    public ReactiveCommand<RxVoid, RxVoid> BtSaveClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtCancelClicked { get; }

    /// <summary>Ctrl/Cmd+S accelerator target (see <see cref="ISaveableDialog"/>).</summary>
    public ICommand? SaveCommand => BtSaveClicked;

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

        _selectedCriticality = CriticalityOptions[0];
        
        _= InitializeAsync();
        
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

    private async Task InitializeAsync()
    {
        Teams = new ObservableCollection<Team>(await TeamsService.GetAllAsync());

        // The server returns only the entities the caller's scope covers, so the picker cannot offer one the save
        // would be refused for. A failed load leaves "Unassigned" plus the host's current entity, never an empty list.
        try
        {
            EntityOptions = new ObservableCollection<EntityOption>(
                EntityOption.Build(await EntitiesService.GetAllAsync(null, true), Localizer["HostEntityUnassigned"]));
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not load the entities for the host dialog: {Message}", ex.Message);
            EntityOptions = new ObservableCollection<EntityOption>(EntityOption.Build([], Localizer["HostEntityUnassigned"]));
        }

        SelectHostEntity();
    }

    /// <summary>Selects the edited host's entity, adding it by id if the list does not carry it.</summary>
    private void SelectHostEntity()
    {
        if (EntityOptions.Count == 0) return;

        var match = EntityOptions.FirstOrDefault(o => o.Id == _hostEntityId);
        if (match == null && _hostEntityId is { } id)
        {
            match = new EntityOption(id, $"#{id}");
            EntityOptions.Add(match);
        }

        SelectedEntity = match ?? EntityOptions[0];
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
        Host.Criticality = SelectedCriticality?.Level;
        Host.Environment = string.IsNullOrWhiteSpace(Environment) ? null : Environment.Trim();
        Host.Owner = string.IsNullOrWhiteSpace(Owner) ? null : Owner.Trim();
        // Before the options load, keep what the host had rather than reading "Unassigned" (M58, T282).
        Host.EntityId = SelectedEntity != null ? SelectedEntity.Id : _hostEntityId;

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

        // T356: the PUT carries the whole host; never send a copy older than an agent's approved write.
        if (_operation == OperationType.Edit && _agentGuard?.ConflictMessage() is { } conflict)
        {
            Toasts.Warning(conflict);
            return;
        }

        try
        {
            if (_operation == OperationType.Edit)
            {
                // T356 review: the awaited update (UpdateAsync is async void: a refused PUT was never seen here). A failure
                // reaches the catch below; only a confirmed save ends the agent's highlights on the host.
                await GUIClient.ViewModels.Agents.AgentEditGuard.SaveAsync(_agentGuard, async () =>
                {
                    await HostsService.UpdateHostAsync(Host);
                    return true;
                });
            }
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

    private GUIClient.ViewModels.Agents.AgentEditGuard? _agentGuard;

    public override Task ActivateAsync(HostDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            _operation = parameter.Operation;
            if(_operation == OperationType.Edit) Host = parameter.Host;
            // Review M63 #7: from when the screen read this copy, an agent's write to the host blocks this dialog's save.
            if (_operation == OperationType.Edit && parameter.Host is { } edited)
                _agentGuard = GUIClient.ViewModels.Agents.AgentEditGuard.ForSince(GUIClient.Agents.AgentItemKinds.Host,
                    edited.Id, parameter.LoadedAt ?? DateTimeOffset.UtcNow);
            HostIp = parameter.Host?.Ip ?? string.Empty;
            HostName = parameter.Host?.HostName ?? string.Empty;
            Comments = parameter.Host?.Comment ?? string.Empty;
            SelectedStatus = Statuses.FirstOrDefault(s => (short)s == parameter.Host?.Status);
            SelectedTeam = Teams.FirstOrDefault(t => t.Value == parameter.Host?.TeamId);
            Fqdn = parameter.Host?.Fqdn ?? string.Empty;
            SelectedCriticality = CriticalityOptions.First(o =>
                o.Level == CriticalityScale.Normalize(parameter.Host?.Criticality));
            Environment = parameter.Host?.Environment ?? string.Empty;
            Owner = parameter.Host?.Owner ?? string.Empty;
            _hostEntityId = parameter.Host?.EntityId;
            SelectHostEntity();
            
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
            

            _= InitializeAsync();
        });
    }
}
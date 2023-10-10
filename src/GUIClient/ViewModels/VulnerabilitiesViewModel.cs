using System;
using System.Collections.ObjectModel;
using System.Linq;
using ClientServices.Interfaces;
using DAL.Entities;
using Microsoft.Extensions.Localization;
using ReactiveUI;
using System.Reactive;
using Avalonia.Controls;
using Avalonia.Media;
using DynamicData;
using DynamicData.Binding;
using GUIClient.Models;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using GUIClient.Views;
using Model;
using Model.Authentication;
using Model.DTO;
using Model.Globalization;
using MsBox.Avalonia;
using MsBox.Avalonia.Dto;
using MsBox.Avalonia.Enums;

namespace GUIClient.ViewModels;

public class VulnerabilitiesViewModel: ViewModelBase
{
    #region LANGUAGE

    private string StrVulnerabilities { get;  } = Localizer["Vulnerabilities"];
    private string StrReload { get;  } = Localizer["Reload"];
    private string StrImport { get;  } = Localizer["Import"];
    private string StrFirstDetection { get;  } = Localizer["FirstDetection"];
    private string StrLastDetection { get;  } = Localizer["LastDetection"];
    private string StrStatus { get;  } = Localizer["Status"];
    private string StrDetectionCount { get;  } = Localizer["DetectionCount"];
    private string StrTitle { get;  }= Localizer["Title"];
    private string StrTechnology { get;  }= Localizer["Technology"];
    private string StrDetails { get; } = Localizer["Details"];
    private string StrAnalyst { get; } = Localizer["Analyst"];
    private string StrFixTeam { get; } = Localizer["FixTeam"];
    private string StrHost { get; } = Localizer["Host"];
    
    private string StrName { get; } = Localizer["Name"];
    
    private string StrScore { get; } = Localizer["Score"];
    private string StrImpact { get; } = Localizer["Impact"];
    private string StrTeamResponsible { get; } = Localizer["TeamResponsible"];
    private string StrRisks { get; } = Localizer["Risks"];
    private string StrSubject { get; } = Localizer["Subject"];
    private string StrCategory { get; } = Localizer["Category"];
    private string StrSource { get; } = Localizer["Source"];
    private string StrAdd {get; } = Localizer["Add"];
    private string StrVerify {get; } = Localizer["Verify"];
    private string StrDelete {get; } = Localizer["Delete"];

    #endregion
    
    #region PROPERTIES

    private string _statsRows = "Rows: 0";
    private string StatsRows {
        get => _statsRows;
        set => this.RaiseAndSetIfChanged(ref _statsRows, value);
    }

    private int _rowCount = 0;
    private int RowCount {
        get => _rowCount;
        set {
            _rowCount = value;
            StatsRows = $"Rows: {value}";
        }
    }

    private ObservableCollection<Vulnerability> _vulnerabilities = new ();
    public ObservableCollection<Vulnerability> Vulnerabilities {
        get => _vulnerabilities;
        set => this.RaiseAndSetIfChanged(ref _vulnerabilities, value);
    }
    
    private ObservableCollection<LocalizableListItem> _impacts = new ();
    public ObservableCollection<LocalizableListItem> Impacts {
        get => _impacts;
        set => this.RaiseAndSetIfChanged(ref _impacts, value);
    }

    private bool _isDetailsPanelOpen = false;

    public bool IsDetailsPanelOpen
    {
        get => _isDetailsPanelOpen;
        set => this.RaiseAndSetIfChanged(ref _isDetailsPanelOpen, value);
    }

    private RotateTransform _detailRotation = new(90);

    public RotateTransform DetailRotation
    {
        get => _detailRotation;
        set => this.RaiseAndSetIfChanged(ref _detailRotation, value);
    }
    
    private ObservableCollection<RiskScoring>? _selectedVulnerabilityRisksScores;

    public ObservableCollection<RiskScoring>? SelectedVulnerabilityRisksScores
    {
        get => _selectedVulnerabilityRisksScores;
        set => this.RaiseAndSetIfChanged(ref _selectedVulnerabilityRisksScores, value);
    }

    private Vulnerability? _selectedVulnerability;
    
    public Vulnerability? SelectedVulnerability
    {
        get => _selectedVulnerability;
        set
        {
            if (value != null)
            {
                LoadVulnerabiltyDetails(value.Id);
            }
            this.RaiseAndSetIfChanged(ref _selectedVulnerability, value);
            
            ProcessStatusButtons();
        }
    }
    
    private Host? _selectedVulnerabilityHost;

    public Host? SelectedVulnerabilityHost
    {
        get => _selectedVulnerabilityHost;
        set => this.RaiseAndSetIfChanged(ref _selectedVulnerabilityHost, value);
    }
    
    private AuthenticatedUserInfo? _authenticatedUserInfo;

    public AuthenticatedUserInfo? AuthenticatedUserInfo
    {
        get => _authenticatedUserInfo;
        set => this.RaiseAndSetIfChanged(ref _authenticatedUserInfo, value);
    }
    
    private Team? _selectedVulnerabilityFixTeam;

    public Team? SelectedVulnerabilityFixTeam
    {
        get => _selectedVulnerabilityFixTeam;
        set => this.RaiseAndSetIfChanged(ref _selectedVulnerabilityFixTeam, value);
    }
    
    private UserDto? _selectedVulnerabilityAnalyst;

    public UserDto? SelectedVulnerabilityAnalyst
    {
        get => _selectedVulnerabilityAnalyst;
        set => this.RaiseAndSetIfChanged(ref _selectedVulnerabilityAnalyst, value);
    }
    
    private ObservableCollection<Risk>? _selectedVulnerabilityRisks;
    public ObservableCollection<Risk>? SelectedVulnerabilityRisks
    {
        get => _selectedVulnerabilityRisks;
        set => this.RaiseAndSetIfChanged(ref _selectedVulnerabilityRisks, value);
    }
    
    private ObservableCollection<Tuple<Risk,RiskScoring>>? _selectedRisksTuples;
    public ObservableCollection<Tuple<Risk,RiskScoring>>? SelectedRisksTuples
    {
        get => _selectedRisksTuples;
        set => this.RaiseAndSetIfChanged(ref _selectedRisksTuples, value);
    }
    
    private bool _btVerifyEnabled = false;
    public bool BtVerifyEnabled
    {
        get => _btVerifyEnabled;
        set => this.RaiseAndSetIfChanged(ref _btVerifyEnabled, value);
    }
    
    public Window? ParentWindow
    {
        get { return WindowsManager.AllWindows.Find(w => w is MainWindow); }
    }
    
    #endregion
    
    #region SERVICES
    private IVulnerabilitiesService VulnerabilitiesService { get; } = GetService<IVulnerabilitiesService>();
    private IUsersService UsersService { get; } = GetService<IUsersService>();
    private IHostsService HostsService { get; } = GetService<IHostsService>();
    private IRisksService RisksService { get; } = GetService<IRisksService>();
    private IDialogService DialogService { get; } = GetService<IDialogService>();
    private IImpactsService ImpactsService { get; } = GetService<IImpactsService>();
    
    #endregion

    #region BUTTONS

    public ReactiveCommand<Unit, Unit> BtReloadClicked { get; } 
    public ReactiveCommand<Unit, Unit> BtDetailsClicked { get; } 
    public ReactiveCommand<Unit, Unit> BtAddClicked { get; }
    public ReactiveCommand<Unit, Unit> BtDeleteClicked { get; }
    public ReactiveCommand<Unit, Unit> BtVerifyClicked { get; }

    #endregion

    #region FIELDS

    private bool _initialized = false;

    #endregion
    
    
    public VulnerabilitiesViewModel()
    {
        DetailRotation = new RotateTransform(90);
        
        BtReloadClicked = ReactiveCommand.Create(ExecuteReload);
        BtDetailsClicked = ReactiveCommand.Create(ExecuteOpenCloseDetails);
        BtAddClicked = ReactiveCommand.Create(ExecuteAdd);
        BtDeleteClicked = ReactiveCommand.Create(ExecuteDelete);
        BtVerifyClicked = ReactiveCommand.Create(ExecuteVerify);
        
        AuthenticationService.AuthenticationSucceeded += (_, _) =>
        {
            Initialize();
        };



    }

    #region METHODS
    
    private void Initialize()
    {
        if (!_initialized)
        {
            AuthenticatedUserInfo = AuthenticationService.AuthenticatedUserInfo;
            Vulnerabilities = new ObservableCollection<Vulnerability>(VulnerabilitiesService.GetAll());
            RowCount = Vulnerabilities.Count;
            Impacts = new ObservableCollection<LocalizableListItem>(ImpactsService.GetAll());
                
            _initialized = true;
        }
    }

    private async void ExecuteAdd()
    {
        var parameter = new VulnerabilityDialogParameter()
        {
            Operation = OperationType.Create
        };
        
        var dialogNewVul = await DialogService.ShowDialogAsync<VulnerabilityDialogResult, VulnerabilityDialogParameter>(nameof(EditVulnerabilitiesDialogViewModel), parameter);
        
        if(dialogNewVul == null) return;

        if (dialogNewVul.Action == ResultActions.Ok )
        {
            Vulnerabilities.Add(dialogNewVul.ResultingVulnerability!);
        }
    }
    
    private async void ExecuteDelete()
    {
        if(SelectedVulnerability == null)
        {
            var msgOk = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Alert"],
                    ContentMessage = Localizer["PleaseSelectAVulnerabilityMSG"],
                    Icon = Icon.Info,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });

            await msgOk.ShowAsync();
            
        }
        else
        {
            var msgConfirm = MessageBoxManager
                .GetMessageBoxStandard(new MessageBoxStandardParams
                {
                    ContentTitle = Localizer["Alert"],
                    ContentMessage = Localizer["AreYouSureToDeleteVulnerabilityMSG"] + " " + SelectedVulnerability.Title,
                    Icon = Icon.Question,
                    ButtonDefinitions = ButtonEnum.YesNoAbort,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                });
            var confirmMsg = await msgConfirm.ShowWindowDialogAsync(ParentWindow);
            
            if (confirmMsg == ButtonResult.Yes)
            {
                try
                {
                    VulnerabilitiesService.Delete(SelectedVulnerability);
                    Vulnerabilities.Remove(SelectedVulnerability);
                }
                catch (Exception ex)
                {
                    var msgOk = MessageBoxManager
                        .GetMessageBoxStandard(new MessageBoxStandardParams
                        {
                            ContentTitle = Localizer["Error"],
                            ContentMessage = Localizer["ErrorDeletingVulnerabilityMSG"] + " " + ex.Message,
                            Icon = Icon.Error,
                            WindowStartupLocation = WindowStartupLocation.CenterOwner
                        });

                    await msgOk.ShowAsync();
                }
                
            }
        }
    }

    private void ExecuteVerify()
    {
        VulnerabilitiesService.UpdateStatus(SelectedVulnerability!.Id, (ushort) IntStatus.Verified);
        var idx = Vulnerabilities.IndexOf(SelectedVulnerability);
        Vulnerabilities[idx].Status = (ushort) IntStatus.Verified;

        var vulnerabilities = Vulnerabilities;
        var selected = SelectedVulnerability;
        Vulnerabilities = new ();
        Vulnerabilities = vulnerabilities;
        SelectedVulnerability = selected;
        
    }

    private void ProcessStatusButtons()
    {
        if(SelectedVulnerability == null)
        {
            BtVerifyEnabled = false;
        }
        else
        {
            switch (SelectedVulnerability.Status)
            {
                case (ushort) IntStatus.New:
                    BtVerifyEnabled = true;
                    break;
                default:
                    BtVerifyEnabled = false;
                    break;
            }
            
            
        }
    }
    
    private void ExecuteReload()
    {
        Vulnerabilities = new ObservableCollection<Vulnerability>(VulnerabilitiesService.GetAll());
        RowCount = Vulnerabilities.Count;
    }

    private void ExecuteOpenCloseDetails()
    {
        //IsDetailsPanelOpen = !IsDetailsPanelOpen;

        if (IsDetailsPanelOpen)
        {
            IsDetailsPanelOpen = false;
            DetailRotation = new RotateTransform(90);
        }
        else
        {
            IsDetailsPanelOpen = true;
            DetailRotation = new RotateTransform(0);
        }
    }
    
    private void LoadVulnerabiltyDetails(int vulnerabilityId)
    {
        var vulnerability = VulnerabilitiesService.GetOne(vulnerabilityId);
        
        SelectedVulnerabilityHost = vulnerability.Host;
        SelectedVulnerabilityFixTeam = vulnerability.FixTeam;
        if(vulnerability.AnalystId != null)
            SelectedVulnerabilityAnalyst = UsersService.GetUser(vulnerability.AnalystId.Value);
        SelectedVulnerabilityRisks = new ObservableCollection<Risk>(vulnerability.Risks);
        SelectedVulnerabilityRisksScores = new ObservableCollection<RiskScoring>(VulnerabilitiesService.GetRisksScores(vulnerabilityId));

        SelectedRisksTuples = new ObservableCollection<Tuple<Risk, RiskScoring>>();
        
        foreach (var risk in SelectedVulnerabilityRisks)
        {
            SelectedRisksTuples.Add(new Tuple<Risk, RiskScoring>(risk, SelectedVulnerabilityRisksScores.First(r => r.Id == risk.Id)));
        }
        
    }

    #endregion
}
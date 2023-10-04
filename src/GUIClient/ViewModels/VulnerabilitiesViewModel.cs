using System;
using System.Collections.ObjectModel;
using System.Linq;
using ClientServices.Interfaces;
using DAL.Entities;
using Microsoft.Extensions.Localization;
using ReactiveUI;
using System.Reactive;
using Avalonia.Media;
using Model.Authentication;
using Model.DTO;

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
    private string StrTeamResponsible { get; } = Localizer["TeamResponsible"];
    private string StrRisks { get; } = Localizer["Risks"];
    private string StrSubject { get; } = Localizer["Subject"];
    private string StrCategory { get; } = Localizer["Category"];
    private string StrSource { get; } = Localizer["Source"];
    private string StrAdd {get; } = Localizer["Add"];
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

    private ObservableCollection<Vulnerability> _vulnerabilities = new ObservableCollection<Vulnerability>();
    public ObservableCollection<Vulnerability> Vulnerabilities {
        get => _vulnerabilities;
        set => this.RaiseAndSetIfChanged(ref _vulnerabilities, value);
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
    
    #endregion
    
    #region SERVICES
    private IVulnerabilitiesService VulnerabilitiesService { get; } = GetService<IVulnerabilitiesService>();
    private IUsersService UsersService { get; } = GetService<IUsersService>();
    private IHostsService HostsService { get; } = GetService<IHostsService>();
    private IRisksService RisksService { get; } = GetService<IRisksService>();
    #endregion

    #region BUTTONS

    public ReactiveCommand<Unit, Unit> BtReloadClicked { get; } 
    public ReactiveCommand<Unit, Unit> BtDetailsClicked { get; } 
    public ReactiveCommand<Unit, Unit> BtAddClicked { get; }
    public ReactiveCommand<Unit, Unit> BtDeleteClicked { get; }

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
                
            _initialized = true;
        }
    }

    private void ExecuteAdd()
    {
        
    }
    
    private void ExecuteDelete()
    {
        
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
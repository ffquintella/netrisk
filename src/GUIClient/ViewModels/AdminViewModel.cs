using ClientServices.Interfaces;
using GUIClient.Tools.RiskIndex;
using GUIClient.ViewModels.Admin;
using GUIClient.ViewModels.Admin.RiskIndex;
using ReactiveUI;

namespace GUIClient.ViewModels;

public class AdminViewModel: ViewModelBase
{
    #region LANGUAGE
    public string StrTitle => Localizer["Administration"];


    public string StrAdmin { get; } = Localizer["Administration"];
    public string StrUsers { get; } = Localizer["Users"];
    public string StrDevices { get; } = Localizer["Devices"];
    public string StrConfiguration { get; }= Localizer["Configuration"];
    
    public string StrPlugins { get; }= Localizer["Plugins"];

    public string StrIrpTemplates { get; } = Localizer["IRP Templates"];

    public string StrEntityAccess { get; } = Localizer["Entity Access"];

    public string StrFindingsAdmin { get; } = Localizer["Deduplication"];
    public string StrGovernance { get; } = Localizer["Governance"];

    public string StrIntegrations { get; } = Localizer["Integrations"];

    // The navigation bar is icons only, so each one carries a hint naming what its section holds —
    // the section name alone does not tell an operator that, say, the API tokens live under findings.
    public string StrUsersHint { get; } = Localizer["AdminUsersHintMSG"];
    public string StrDevicesHint { get; } = Localizer["AdminDevicesHintMSG"];
    public string StrConfigurationHint { get; } = Localizer["AdminConfigurationHintMSG"];
    public string StrEntityAccessHint { get; } = Localizer["AdminEntityAccessHintMSG"];
    public string StrIrpTemplatesHint { get; } = Localizer["AdminIrpTemplatesHintMSG"];
    public string StrFindingsAdminHint { get; } = Localizer["AdminFindingsHintMSG"];
    public string StrApiTokensHint { get; } = Localizer["AdminApiTokensHintMSG"];
    public string StrIntegrationsHint { get; } = Localizer["AdminIntegrationsHintMSG"];
    public string StrGovernanceHint { get; } = Localizer["AdminGovernanceHintMSG"];
    public string StrPluginsHint { get; } = Localizer["AdminPluginsHintMSG"];
    public string StrRiskIndexHint { get; } = Localizer["AdminRiskIndexHintMSG"];

    #endregion

    #region PROPERTIES

    /// <summary>
    /// Whether the signed-in user is an administrator. The window also opens for the risk index's second line and approvers
    /// (S40 §6.0): the administration panes are then neither built nor initialized, so none of them fires a 403.
    /// </summary>
    public bool IsAdmin { get; }

    /// <summary>The Risk index entry: an administrator or a holder of any of the index's configuration permissions.</summary>
    public bool IsRiskIndexPermitted { get; }

    public UsersViewModel? UsersVM { get; set; }
    public DeviceViewModel? DeviceVM { get; set; }
    
    public ConfigurationViewModel? ConfigurationVM { get; set; }
    
    public PluginsViewModel? PluginsVM { get; set; }

    public IrpTemplatesViewModel? IrpTemplatesVM { get; set; }

    public EntityAccessViewModel? EntityAccessVM { get; set; }

    /// <summary>
    /// Track 3 (ASPM) administration: deduplication heuristics, SLA policy, risk acceptances and CI
    /// API tokens.
    /// </summary>
    public FindingsAdminViewModel? FindingsAdminVM { get; set; }

    public ApiTokensViewModel? ApiTokensVM { get; set; }

    /// <summary>Track 8 governance administration: appetite, intake triage, reviewers, violations.</summary>
    public GovernanceAdminViewModel? GovernanceVM { get; set; }

    /// <summary>
    /// Track 4 administration: notification channels and subscriptions, issue trackers, identity
    /// providers and SCIM, and the Vision One and SecurityScorecard connections.
    /// </summary>
    public IntegrationsViewModel? IntegrationsVM { get; set; }

    /// <summary>Track 10 M55 (S40 §6.0): profiles, draft, preview, approval, sources and entity risk context.</summary>
    public RiskIndexAdminViewModel? RiskIndexVM { get; }

    private bool _riskIndexIsVisible;

    public bool RiskIndexIsVisible
    {
        get => _riskIndexIsVisible;
        set => this.RaiseAndSetIfChanged(ref _riskIndexIsVisible, value);
    }
    
    private bool _usersIsVisible = true;
    public bool UsersIsVisible
    {
        get => _usersIsVisible;
        set => this.RaiseAndSetIfChanged(ref _usersIsVisible, value);
    }
    
    private bool _devicesIsVisible = false;
    public bool DevicesIsVisible
    {
        get => _devicesIsVisible;
        set => this.RaiseAndSetIfChanged(ref _devicesIsVisible, value);
    }
    
    private bool _configurationsIsVisible = false;
    public bool ConfigurationsIsVisible
    {
        get => _configurationsIsVisible;
        set => this.RaiseAndSetIfChanged(ref _configurationsIsVisible, value);
    }
    
    private bool _entityAccessIsVisible = false;

    public bool EntityAccessIsVisible
    {
        get => _entityAccessIsVisible;
        set => this.RaiseAndSetIfChanged(ref _entityAccessIsVisible, value);
    }

    private bool _irpTemplatesIsVisible = false;

    public bool IrpTemplatesIsVisible
    {
        get => _irpTemplatesIsVisible;
        set => this.RaiseAndSetIfChanged(ref _irpTemplatesIsVisible, value);
    }

    private bool _findingsAdminIsVisible = false;

    public bool FindingsAdminIsVisible
    {
        get => _findingsAdminIsVisible;
        set => this.RaiseAndSetIfChanged(ref _findingsAdminIsVisible, value);
    }

    private bool _apiTokensIsVisible = false;

    public bool ApiTokensIsVisible
    {
        get => _apiTokensIsVisible;
        set => this.RaiseAndSetIfChanged(ref _apiTokensIsVisible, value);
    }

    private bool _integrationsIsVisible = false;

    public bool IntegrationsIsVisible
    {
        get => _integrationsIsVisible;
        set => this.RaiseAndSetIfChanged(ref _integrationsIsVisible, value);
    }

    private bool _governanceIsVisible = false;

    public bool GovernanceIsVisible
    {
        get => _governanceIsVisible;
        set => this.RaiseAndSetIfChanged(ref _governanceIsVisible, value);
    }

    private bool _pluginsIsVisible = false;
    
    public bool PluginsIsVisible
    {
        get => _pluginsIsVisible;
        set => this.RaiseAndSetIfChanged(ref _pluginsIsVisible, value);
    }
    
    #endregion
    
    public AdminViewModel()
    {
        var user = AuthenticationService.AuthenticatedUserInfo;
        var rights = RiskIndexAdminRights.From(user);
        IsAdmin = user?.IsAdmin == true || rights.IsAdmin;
        IsRiskIndexPermitted = rights.CanOpenRiskIndex;
        if (IsRiskIndexPermitted) RiskIndexVM = new RiskIndexAdminViewModel(rights);

        if (!IsAdmin)
        {
            // The first pane a non-administrator may open (S40 §6.0 "Painel inicial").
            UsersIsVisible = false;
            RiskIndexIsVisible = IsRiskIndexPermitted;
            if (RiskIndexVM is not null) _ = RiskIndexVM.InitializeAsync();
            return;
        }

        ConfigurationVM = new ConfigurationViewModel();

        UsersVM = new UsersViewModel();
        _ = UsersVM.Initialize();
        
        DeviceVM = new DeviceViewModel();
        DeviceVM.Initialize();
        
        PluginsVM = new PluginsViewModel(GetService<IPluginsService>());
        PluginsVM.Initialize();

        IrpTemplatesVM = new IrpTemplatesViewModel();
        _ = IrpTemplatesVM.InitializeAsync();

        EntityAccessVM = new EntityAccessViewModel();
        _ = EntityAccessVM.InitializeAsync();

        FindingsAdminVM = new FindingsAdminViewModel();
        _ = FindingsAdminVM.InitializeAsync();

        ApiTokensVM = new ApiTokensViewModel();
        _ = ApiTokensVM.InitializeAsync();

        GovernanceVM = new GovernanceAdminViewModel();
        _ = GovernanceVM.InitializeAsync();

        IntegrationsVM = new IntegrationsViewModel();
        _ = IntegrationsVM.InitializeAsync();
    }

    #region METHODS

    private void DisableButtons()
    {
        UsersIsVisible = false;
        DevicesIsVisible = false;
        ConfigurationsIsVisible = false;
        PluginsIsVisible = false;
        IrpTemplatesIsVisible = false;
        EntityAccessIsVisible = false;
        FindingsAdminIsVisible = false;
        ApiTokensIsVisible = false;
        IntegrationsIsVisible = false;
        GovernanceIsVisible = false;
        RiskIndexIsVisible = false;
    }
    
    public void BtUsersClicked()
    {
        DisableButtons();
        UsersIsVisible = true;
    }
    public void BtDevicesClicked()
    {
        DisableButtons();
        DevicesIsVisible = true;
    }
    
    public void BtPluginsClicked()
    {
        DisableButtons();
        PluginsIsVisible = true;
    }

    public void BtIntegrationsClicked()
    {
        DisableButtons();
        IntegrationsIsVisible = true;
    }
    
    public void BtConfigurationsClicked()
    {
        DisableButtons();
        ConfigurationsIsVisible = true;
    }

    public void BtIrpTemplatesClicked()
    {
        DisableButtons();
        IrpTemplatesIsVisible = true;
    }

    public void BtEntityAccessClicked()
    {
        DisableButtons();
        EntityAccessIsVisible = true;
    }

    public void BtFindingsAdminClicked()
    {
        DisableButtons();
        FindingsAdminIsVisible = true;
    }

    public void BtApiTokensClicked()
    {
        DisableButtons();
        ApiTokensIsVisible = true;
    }

    public void BtGovernanceClicked()
    {
        DisableButtons();
        GovernanceIsVisible = true;
    }

    public void BtRiskIndexClicked()
    {
        if (RiskIndexVM is null) return;
        DisableButtons();
        RiskIndexIsVisible = true;
        _ = RiskIndexVM.InitializeAsync();
    }
    #endregion

}
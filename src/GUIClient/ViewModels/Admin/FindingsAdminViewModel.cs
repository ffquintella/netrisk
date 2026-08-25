using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.Enums;
using Model.Findings;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Admin;

/// <summary>
/// The Track 3 administration screen: deduplication heuristics per scanner (3.3.3), SLA policy
/// (3.4.1), risk acceptances (3.2.3) and CI API tokens (3.5.1).
///
/// One view model behind four tabs rather than four screens: an administrator setting up scanner
/// ingestion does all four in one sitting, and the alternative is four near-identical view models
/// with the same load/save shape.
/// </summary>
public class FindingsAdminViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrDeduplication { get; } = Localizer["Deduplication"];
    public string StrSlaPolicy { get; } = Localizer["SlaPolicy"];
    public string StrRiskAcceptances { get; } = Localizer["RiskAcceptances"];
    public string StrApiTokens { get; } = Localizer["ApiTokens"];
    public string StrStrategyChain { get; } = Localizer["StrategyChain"];
    public string StrHashFields { get; } = Localizer["HashFields"];
    public string StrAutoCloseMissing { get; } = Localizer["AutoCloseMissing"];
    public string StrPreviewMerge { get; } = Localizer["PreviewMerge"];
    public string StrChangeHistory { get; } = Localizer["ChangeHistory"];
    public string StrTriageDays { get; } = Localizer["TriageDays"];
    public string StrRemediationDays { get; } = Localizer["RemediationDays"];
    public string StrBenchmark { get; } = Localizer["Benchmark"];
    public string StrSeverity { get; } = Localizer["Severity"];
    public string StrExpiringWithin30Days { get; } = Localizer["ExpiringWithin30Days"];
    public string StrRevoke { get; } = Localizer["Revoke"];
    public string StrExpiresAt { get; } = Localizer["ExpiresAt"];
    public string StrAuthorizingManager { get; } = Localizer["AuthorizingManager"];
    public string StrScopes { get; } = Localizer["Scopes"];
    public string StrIssueToken { get; } = Localizer["IssueToken"];
    public string StrTokenShownOnce { get; } = Localizer["TokenShownOnceMSG"];
    public string StrLastUsed { get; } = Localizer["LastUsed"];
    public string StrName { get; } = Localizer["Name"];
    public string StrStatus { get; } = Localizer["Status"];
    public string StrTitle { get; } = Localizer["Title"];
    public string StrReload { get; } = Localizer["Reload"];

    #endregion

    #region SERVICES

    private IFindingsAdminService AdminService { get; } = GetService<IFindingsAdminService>();

    private IVulnerabilitiesService VulnerabilitiesService { get; } = GetService<IVulnerabilitiesService>();

    #endregion

    #region DEDUPLICATION (3.3.3)

    /// <summary>The importers the server knows about, so a plugin's configuration is editable too.</summary>
    public ObservableCollection<string> Importers { get; } = new();

    private string? _selectedImporter;
    public string? SelectedImporter
    {
        get => _selectedImporter;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedImporter, value);
            if (value != null) _ = LoadDedupConfigurationAsync(value);
        }
    }

    private string _strategyChain = "";
    public string StrategyChain
    {
        get => _strategyChain;
        set => this.RaiseAndSetIfChanged(ref _strategyChain, value);
    }

    /// <summary>
    /// The hash field set as a checkbox list. Order matters to the key, so the saved value preserves
    /// the order the options were declared in rather than the order they were clicked.
    /// </summary>
    public ObservableCollection<SelectableOption> HashFieldOptions { get; } = new();

    public ObservableCollection<string> AvailableStrategies { get; } = new();

    private bool _autoCloseMissing;
    public bool AutoCloseMissing
    {
        get => _autoCloseMissing;
        set => this.RaiseAndSetIfChanged(ref _autoCloseMissing, value);
    }

    public ObservableCollection<ScannerDedupConfigurationHistory> DedupHistory { get; } = new();

    // The preview panel's two findings. Plain properties rather than a nested view model: the form
    // is eight fields twice, and a view model per side would be ceremony.
    public PreviewFinding PreviewLeft { get; } = new();

    public PreviewFinding PreviewRight { get; } = new();

    private string _previewVerdict = "";
    public string PreviewVerdict
    {
        get => _previewVerdict;
        set => this.RaiseAndSetIfChanged(ref _previewVerdict, value);
    }

    private string _previewKeys = "";

    /// <summary>Every candidate key both sides produced, so a surprising verdict can be explained.</summary>
    public string PreviewKeys
    {
        get => _previewKeys;
        set => this.RaiseAndSetIfChanged(ref _previewKeys, value);
    }

    #endregion

    #region SLA (3.4.1)

    public ObservableCollection<SlaConfiguration> SlaConfigurations { get; } = new();

    public ObservableCollection<SlaBenchmarkView> SlaBenchmarks { get; } = new();

    private SlaConfiguration? _selectedSla;
    public SlaConfiguration? SelectedSla
    {
        get => _selectedSla;
        set => this.RaiseAndSetIfChanged(ref _selectedSla, value);
    }

    private int _slaSeverity = (int)4;
    public int SlaSeverity
    {
        get => _slaSeverity;
        set => this.RaiseAndSetIfChanged(ref _slaSeverity, value);
    }

    private int _slaTriageDays = 2;
    public int SlaTriageDays
    {
        get => _slaTriageDays;
        set => this.RaiseAndSetIfChanged(ref _slaTriageDays, value);
    }

    private int _slaRemediationDays = 15;
    public int SlaRemediationDays
    {
        get => _slaRemediationDays;
        set => this.RaiseAndSetIfChanged(ref _slaRemediationDays, value);
    }

    #endregion

    #region RISK ACCEPTANCES (3.2.3)

    public ObservableCollection<RiskAcceptance> Acceptances { get; } = new();

    private RiskAcceptance? _selectedAcceptance;
    public RiskAcceptance? SelectedAcceptance
    {
        get => _selectedAcceptance;
        set => this.RaiseAndSetIfChanged(ref _selectedAcceptance, value);
    }

    private bool _onlyExpiringSoon;

    /// <summary>
    /// The spec's headline filter. Defaults off so the view opens on the full register; an
    /// administrator who wants the deadline list asks for it.
    /// </summary>
    public bool OnlyExpiringSoon
    {
        get => _onlyExpiringSoon;
        set
        {
            this.RaiseAndSetIfChanged(ref _onlyExpiringSoon, value);
            _ = LoadAcceptancesAsync();
        }
    }

    private string _revocationReason = "";
    public string RevocationReason
    {
        get => _revocationReason;
        set => this.RaiseAndSetIfChanged(ref _revocationReason, value);
    }

    #endregion

    #region API TOKENS (3.5.1)

    public ObservableCollection<ApiTokenSummary> ApiTokens { get; } = new();

    public ObservableCollection<SelectableOption> ScopeOptions { get; } = new();

    private ApiTokenSummary? _selectedToken;
    public ApiTokenSummary? SelectedToken
    {
        get => _selectedToken;
        set => this.RaiseAndSetIfChanged(ref _selectedToken, value);
    }

    private string _newTokenName = "";
    public string NewTokenName
    {
        get => _newTokenName;
        set => this.RaiseAndSetIfChanged(ref _newTokenName, value);
    }

    private DateTimeOffset? _newTokenExpiry = DateTimeOffset.UtcNow.AddDays(90);
    public DateTimeOffset? NewTokenExpiry
    {
        get => _newTokenExpiry;
        set => this.RaiseAndSetIfChanged(ref _newTokenExpiry, value);
    }

    private string _issuedSecret = "";

    /// <summary>
    /// The freshly issued token. Held only in this field, only until the view is left: the server
    /// stores a hash and cannot produce it again, which is the point.
    /// </summary>
    public string IssuedSecret
    {
        get => _issuedSecret;
        set
        {
            this.RaiseAndSetIfChanged(ref _issuedSecret, value);
            this.RaisePropertyChanged(nameof(HasIssuedSecret));
        }
    }

    public bool HasIssuedSecret => !string.IsNullOrWhiteSpace(IssuedSecret);

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtSaveDedupClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtPreviewDedupClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSaveSlaClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRevokeAcceptanceClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtIssueTokenClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRevokeTokenClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }

    #endregion

    public FindingsAdminViewModel()
    {
        BtSaveDedupClicked = ReactiveCommand.CreateFromTask(SaveDedupConfigurationAsync);
        BtPreviewDedupClicked = ReactiveCommand.CreateFromTask(PreviewDedupAsync);
        BtSaveSlaClicked = ReactiveCommand.CreateFromTask(SaveSlaConfigurationAsync);
        BtRevokeAcceptanceClicked = ReactiveCommand.CreateFromTask(RevokeAcceptanceAsync);
        BtIssueTokenClicked = ReactiveCommand.CreateFromTask(IssueTokenAsync);
        BtRevokeTokenClicked = ReactiveCommand.CreateFromTask(RevokeTokenAsync);
        BtReloadClicked = ReactiveCommand.CreateFromTask(InitializeAsync);
    }

    public async Task InitializeAsync()
    {
        await LoadImportersAsync();
        await LoadDedupOptionsAsync();
        await LoadSlaAsync();
        await LoadAcceptancesAsync();
        await LoadApiTokensAsync();
    }

    #region DEDUP METHODS

    private async Task LoadImportersAsync()
    {
        Importers.Clear();

        try
        {
            var importers = await VulnerabilitiesService.GetImportersAsync();
            foreach (var importer in importers.OrderBy(i => i.Name, StringComparer.OrdinalIgnoreCase))
                Importers.Add(importer.Name);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the importer list: {Message}", ex.Message);
        }

        SelectedImporter = Importers.FirstOrDefault();
    }

    private async Task LoadDedupOptionsAsync()
    {
        try
        {
            var options = await AdminService.GetDedupOptionsAsync();

            AvailableStrategies.Clear();
            foreach (var strategy in options.Strategies) AvailableStrategies.Add(strategy);

            HashFieldOptions.Clear();
            foreach (var field in options.HashFields)
                HashFieldOptions.Add(new SelectableOption { Name = field });
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the dedup options: {Message}", ex.Message);
        }
    }

    private async Task LoadDedupConfigurationAsync(string importer)
    {
        try
        {
            var configuration = await AdminService.GetDedupConfigurationAsync(importer);

            StrategyChain = configuration.StrategyChain;
            AutoCloseMissing = configuration.AutoCloseMissing;

            var selected = (configuration.HashFields ?? "")
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var option in HashFieldOptions) option.IsSelected = selected.Contains(option.Name);

            DedupHistory.Clear();
            foreach (var entry in await AdminService.GetDedupHistoryAsync(importer)) DedupHistory.Add(entry);

            PreviewLeft.Tool = importer;
            PreviewRight.Tool = importer;
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the dedup configuration for {Importer}: {Message}", importer, ex.Message);
        }
    }

    private async Task SaveDedupConfigurationAsync()
    {
        if (SelectedImporter == null) return;

        try
        {
            // The declared order is preserved rather than the click order: the hash is over the
            // fields in sequence, so two configurations with the same fields in a different order
            // are different heuristics, and an operator ticking boxes does not mean to choose one.
            var fields = string.Join(",", HashFieldOptions.Where(o => o.IsSelected).Select(o => o.Name));

            await AdminService.SaveDedupConfigurationAsync(new ScannerDedupConfiguration
            {
                Importer = SelectedImporter,
                StrategyChain = StrategyChain,
                HashFields = fields,
                AutoCloseMissing = AutoCloseMissing
            });

            await LoadDedupConfigurationAsync(SelectedImporter);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save the dedup configuration for {Importer}: {Message}", SelectedImporter,
                ex.Message);
            PreviewVerdict = ex.Message;
        }
    }

    private async Task PreviewDedupAsync()
    {
        if (SelectedImporter == null) return;

        try
        {
            var preview = await AdminService.PreviewDedupAsync(SelectedImporter, PreviewLeft, PreviewRight);

            PreviewVerdict = preview.WouldMerge ? Localizer["WouldMerge"] : Localizer["WouldNotMerge"];

            PreviewKeys = string.Join("\n",
                preview.LeftKeys.Select(k => $"A  {k.Strategy}: {k.Key}")
                    .Concat(preview.RightKeys.Select(k => $"B  {k.Strategy}: {k.Key}")));
        }
        catch (Exception ex)
        {
            Logger.Error("Could not preview deduplication for {Importer}: {Message}", SelectedImporter, ex.Message);
            PreviewVerdict = ex.Message;
        }
    }

    #endregion

    #region SLA METHODS

    private async Task LoadSlaAsync()
    {
        try
        {
            SlaConfigurations.Clear();
            foreach (var configuration in await AdminService.GetSlaConfigurationsAsync())
                SlaConfigurations.Add(configuration);

            SlaBenchmarks.Clear();
            foreach (var benchmark in await AdminService.GetSlaBenchmarksAsync()) SlaBenchmarks.Add(benchmark);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the SLA policy: {Message}", ex.Message);
        }
    }

    private async Task SaveSlaConfigurationAsync()
    {
        try
        {
            await AdminService.SetSlaConfigurationAsync(new SlaConfiguration
            {
                Severity = SlaSeverity,
                MaxTriageDays = SlaTriageDays,
                MaxRemediationDays = SlaRemediationDays
            });

            await LoadSlaAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not save the SLA policy: {Message}", ex.Message);
        }
    }

    #endregion

    #region ACCEPTANCE METHODS

    private async Task LoadAcceptancesAsync()
    {
        try
        {
            Acceptances.Clear();
            foreach (var acceptance in await AdminService.GetAcceptancesAsync(OnlyExpiringSoon ? 30 : null))
                Acceptances.Add(acceptance);
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the risk acceptances: {Message}", ex.Message);
        }
    }

    private async Task RevokeAcceptanceAsync()
    {
        if (SelectedAcceptance == null) return;
        if (string.IsNullOrWhiteSpace(RevocationReason)) return;

        try
        {
            await AdminService.RevokeAcceptanceAsync(SelectedAcceptance.Id, RevocationReason);
            RevocationReason = "";
            await LoadAcceptancesAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not revoke risk acceptance {Id}: {Message}", SelectedAcceptance.Id, ex.Message);
        }
    }

    #endregion

    #region TOKEN METHODS

    private async Task LoadApiTokensAsync()
    {
        try
        {
            ApiTokens.Clear();
            foreach (var token in await AdminService.GetApiTokensAsync()) ApiTokens.Add(token);

            if (ScopeOptions.Count == 0)
                foreach (var scope in await AdminService.GetApiTokenScopesAsync())
                    ScopeOptions.Add(new SelectableOption { Name = scope });
        }
        catch (Exception ex)
        {
            Logger.Error("Could not load the API tokens: {Message}", ex.Message);
        }
    }

    private async Task IssueTokenAsync()
    {
        var scopes = string.Join(",", ScopeOptions.Where(o => o.IsSelected).Select(o => o.Name));

        if (string.IsNullOrWhiteSpace(NewTokenName) || string.IsNullOrWhiteSpace(scopes)) return;

        try
        {
            var issued = await AdminService.IssueApiTokenAsync(NewTokenName, scopes,
                NewTokenExpiry?.UtcDateTime, entityId: null);

            IssuedSecret = issued.Secret;
            NewTokenName = "";
            foreach (var option in ScopeOptions) option.IsSelected = false;

            await LoadApiTokensAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not issue an API token: {Message}", ex.Message);
        }
    }

    private async Task RevokeTokenAsync()
    {
        if (SelectedToken == null) return;

        try
        {
            await AdminService.RevokeApiTokenAsync(SelectedToken.Id);
            await LoadApiTokensAsync();
        }
        catch (Exception ex)
        {
            Logger.Error("Could not revoke API token {Id}: {Message}", SelectedToken.Id, ex.Message);
        }
    }

    #endregion
}

/// <summary>
/// A checkbox in a list — used for the hash field set and for token scopes. Reactive so ticking a
/// box updates the model without a per-list view model.
/// </summary>
public class SelectableOption : ReactiveObject
{
    public string Name { get; set; } = "";

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }
}

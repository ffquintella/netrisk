using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.Enums;
using GUIClient.Tools;
using GUIClient.Tools.RiskIndex;
using Model.Integrations;
using ReactiveUI;
using ReactiveUI.Binding;

namespace GUIClient.ViewModels.Admin.RiskIndex;

/// <summary>A choice of a combo box with its translated label.</summary>
public sealed record MappingOption<T>(T Value, string Label)
{
    public override string ToString() => Label;
}

/// <summary>One stored rule as the grid shows it.</summary>
public sealed record EntityMappingRow(VendorEntityMappingDto Rule, string Kind, string Policy, string Enabled)
{
    public int Priority => Rule.Priority;
    public string Value => Rule.Value;
    public string EntityName => Rule.EntityName;
}

/// <summary>
/// The vendor → entity rules of the Sources tab (S40 §6.6, §8.6; T263): pick a Vision One connection, list its rules, and add,
/// edit or remove one — the value offered from the tags its last sync saw (free typing for tags not seen yet) — with a live
/// preview of what the next sync would do. The API is under <c>configuration</c>, so a user without it fires no request and
/// reads why; writes and the preview also need global scope, which only the server knows, so a scoped user reads the
/// server's refusal.
/// </summary>
public class EntityMappingsViewModel : RiskAdminViewModelBase
{
    #region LANGUAGE

    public string StrIntro { get; } = Localizer["EntityMappingIntroMSG"];
    public string StrProvider { get; } = Localizer["EntityMappingProvider"];
    public string StrProviderVisionOne { get; } = Localizer["EntityMappingProviderVisionOne"];
    public string StrConnection { get; } = Localizer["EntityMappingConnection"];
    public string StrNew { get; } = Localizer["EntityMappingNew"];
    public string StrRefresh { get; } = Localizer["Reload"];
    public string StrPriority { get; } = Localizer["EntityMappingPriority"];
    public string StrKind { get; } = Localizer["EntityMappingKind"];
    public string StrValue { get; } = Localizer["EntityMappingValue"];
    public string StrValueHint { get; } = Localizer["EntityMappingValueHint"];
    public string StrEntity { get; } = Localizer["Entity"];
    public string StrPolicy { get; } = Localizer["EntityMappingPolicy"];
    public string StrEnabled { get; } = Localizer["EntityMappingEnabled"];
    public string StrMatchCard { get; } = Localizer["EntityMappingMatchCard"];
    public string StrTargetCard { get; } = Localizer["EntityMappingTargetCard"];
    public string StrPreviewCard { get; } = Localizer["EntityMappingPreviewCard"];
    public string StrPreviewNote { get; } = Localizer["EntityMappingPreviewNoteMSG"];
    public string StrSave { get; } = Localizer["Save"];
    public string StrDelete { get; } = Localizer["Delete"];

    #endregion

    private const int VisionOne = (int)IntegrationKind.TrendMicroVisionOne;
    private static readonly TimeSpan PreviewDelay = TimeSpan.FromMilliseconds(500);

    private readonly RiskIndexAdminRights _rights;
    private readonly IRiskIndexConfigService _configService;
    private readonly IIntegrationsService _integrationsService;
    private readonly IEntitiesService _entitiesService;
    private List<VendorEntityMappingDto> _rules = [];
    private CancellationTokenSource? _previewDelay;
    private bool _loadingEditor;

    public EntityMappingsViewModel(RiskIndexAdminRights rights, IRiskIndexConfigService configService,
        IIntegrationsService integrationsService, IEntitiesService entitiesService)
    {
        _rights = rights;
        _configService = configService;
        _integrationsService = integrationsService;
        _entitiesService = entitiesService;
        Kinds =
        [
            new MappingOption<VendorMatchKind>(VendorMatchKind.V1CustomTag, RiskAdminText.Text("EntityMappingKind_V1CustomTag")),
            new MappingOption<VendorMatchKind>(VendorMatchKind.V1AssetGroup, RiskAdminText.Text("EntityMappingKind_V1AssetGroup"))
        ];
        Policies =
        [
            new MappingOption<RestampPolicy>(RestampPolicy.OnlyUnassigned,
                RiskAdminText.Text("EntityMappingPolicy_OnlyUnassigned")),
            new MappingOption<RestampPolicy>(RestampPolicy.FollowMapping, RiskAdminText.Text("EntityMappingPolicy_FollowMapping"))
        ];

        this.WhenAnyValue(vm => vm.SelectedConnection).Subscribe(connection => { _ = LoadRulesAsync(); });
        this.WhenAnyValue(vm => vm.SelectedRow).Subscribe(row =>
        {
            if (row is not null) Edit(EntityMappingDraft.From(row.Rule));
        });
        this.WhenAnyValue(vm => vm.SelectedKind, vm => vm.Value, vm => vm.SelectedEntity, vm => vm.SelectedPolicy,
                vm => vm.Priority, vm => vm.Enabled)
            .Subscribe(_ => OnEditorChanged());
    }

    #region PROPERTIES

    public bool CanWrite => _rights.Integrations;

    public string ReadOnlyMessage => CanWrite ? "" : RiskAdminText.Text(EntityMappingGates.RequiresIntegrations);

    public bool HasReadOnlyMessage => !CanWrite;

    public ObservableCollection<TrendMicroConnectionView> Connections { get; } = [];

    private TrendMicroConnectionView? _selectedConnection;

    public TrendMicroConnectionView? SelectedConnection
    {
        get => _selectedConnection;
        set => this.RaiseAndSetIfChanged(ref _selectedConnection, value);
    }

    public ObservableCollection<EntityMappingRow> Rows { get; } = [];

    private EntityMappingRow? _selectedRow;

    public EntityMappingRow? SelectedRow
    {
        get => _selectedRow;
        set => this.RaiseAndSetIfChanged(ref _selectedRow, value);
    }

    public ObservableCollection<string> Candidates { get; } = [];

    public ObservableCollection<Entity> Entities { get; } = [];

    public IReadOnlyList<MappingOption<VendorMatchKind>> Kinds { get; }

    public IReadOnlyList<MappingOption<RestampPolicy>> Policies { get; }

    private bool _isEditing;

    /// <summary>Whether a rule (new or stored) is open in the editor cards.</summary>
    public bool IsEditing
    {
        get => _isEditing;
        private set => this.RaiseAndSetIfChanged(ref _isEditing, value);
    }

    private int? _editingId;

    private MappingOption<VendorMatchKind>? _selectedKind;

    public MappingOption<VendorMatchKind>? SelectedKind
    {
        get => _selectedKind;
        set => this.RaiseAndSetIfChanged(ref _selectedKind, value);
    }

    private string? _value;

    public string? Value
    {
        get => _value;
        set => this.RaiseAndSetIfChanged(ref _value, value);
    }

    private Entity? _selectedEntity;

    public Entity? SelectedEntity
    {
        get => _selectedEntity;
        set => this.RaiseAndSetIfChanged(ref _selectedEntity, value);
    }

    private MappingOption<RestampPolicy>? _selectedPolicy;

    public MappingOption<RestampPolicy>? SelectedPolicy
    {
        get => _selectedPolicy;
        set => this.RaiseAndSetIfChanged(ref _selectedPolicy, value);
    }

    private decimal? _priority = 1;

    public decimal? Priority
    {
        get => _priority;
        set => this.RaiseAndSetIfChanged(ref _priority, value);
    }

    private bool _enabled = true;

    public bool Enabled
    {
        get => _enabled;
        set => this.RaiseAndSetIfChanged(ref _enabled, value);
    }

    public int MaxPriority => EntityMappingGates.MaxPriority;

    /// <summary>The asset-group note: such a rule attributes no device (S40 §6.6), it waits for T271's indicators.</summary>
    public bool IsAssetGroup => SelectedKind?.Value == VendorMatchKind.V1AssetGroup;

    public string StrAssetGroupHint { get; } = Localizer["EntityMappingAssetGroupHintMSG"];

    private string _previewSummary = "";

    public string PreviewSummary
    {
        get => _previewSummary;
        private set => this.RaiseAndSetIfChanged(ref _previewSummary, value);
    }

    public ObservableCollection<string> PreviewSample { get; } = [];

    /// <summary>The editor's rule, or null when nothing is open.</summary>
    public EntityMappingDraft? Draft => !IsEditing || SelectedKind is null || SelectedPolicy is null
        ? null
        : new EntityMappingDraft(_editingId, SelectedKind.Value, Value ?? "", SelectedEntity?.Id,
            (int)Math.Clamp(Priority ?? 1, 1, EntityMappingGates.MaxPriority), SelectedPolicy.Value, Enabled);

    private bool IsDirty => Draft is { } d && (d.Id is null || !d.SameAs(_rules.FirstOrDefault(r => r.Id == d.Id)));

    private Gate NewGate => EntityMappingGates.New(CanWrite, SelectedConnection?.Id);
    private Gate SaveGate => EntityMappingGates.Save(CanWrite, Draft, IsDirty, _rules);
    private Gate DeleteGate => EntityMappingGates.Delete(CanWrite, Draft);

    public bool IsNewEnabled => NewGate.Enabled && !IsBusy;
    public string StrNewReason => RiskAdminText.Reason(NewGate);
    public bool IsSaveEnabled => SaveGate.Enabled && !IsBusy;
    public string StrSaveReason => RiskAdminText.Reason(SaveGate);
    public bool IsDeleteEnabled => DeleteGate.Enabled && !IsBusy;
    public string StrDeleteReason => RiskAdminText.Reason(DeleteGate);
    public bool IsRefreshEnabled => CanWrite && SelectedConnection is not null && !IsBusy;

    #endregion

    #region LOADING

    private bool _loaded;

    /// <summary>Loads the connections and the entity choices once; without <c>configuration</c> nothing is requested.</summary>
    public async Task LoadAsync()
    {
        if (_loaded || !CanWrite) return;
        _loaded = true;
        try
        {
            var connections = await _integrationsService.GetTrendMicroConnectionsAsync();
            Connections.Clear();
            foreach (var connection in connections.OrderBy(c => c.Name, StringComparer.CurrentCultureIgnoreCase))
                Connections.Add(connection);

            var entities = await _entitiesService.GetAllAsync();
            Entities.Clear();
            foreach (var entity in ConnectionEntityChoices.Filter(entities)) Entities.Add(entity);

            SelectedConnection ??= Connections.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not load the vendor mapping choices: {Message}", ex.Message);
            Toasts.Error(RiskAdminText.Explain(ex));
        }
    }

    private async Task LoadRulesAsync(int? selectId = null)
    {
        Rows.Clear();
        _rules = [];
        Candidates.Clear();
        CloseEditor();
        if (!CanWrite || SelectedConnection is not { } connection) return;

        try
        {
            _rules = (await _configService.GetMappingsAsync(VisionOne, connection.Id)).ToList();
            foreach (var rule in _rules) Rows.Add(RowOf(rule));
            foreach (var candidate in await _configService.GetMappingCandidatesAsync(VisionOne, connection.Id))
                Candidates.Add(candidate.Value);
        }
        catch (Exception ex)
        {
            Logger.Warning("Could not load the vendor mappings: {Message}", ex.Message);
            Toasts.Error(RiskAdminText.Explain(ex));
        }

        if (selectId is { } id) SelectedRow = Rows.FirstOrDefault(r => r.Rule.Id == id);
        RaiseGates();
    }

    private EntityMappingRow RowOf(VendorEntityMappingDto rule) => new(rule,
        Kinds.FirstOrDefault(k => k.Value == rule.Kind)?.Label ?? rule.Kind.ToString(),
        Policies.FirstOrDefault(p => p.Value == rule.Policy)?.Label ?? rule.Policy.ToString(),
        RiskAdminText.Text(rule.Enabled ? "Yes" : "No"));

    #endregion

    #region EDITOR

    private void Edit(EntityMappingDraft draft)
    {
        _loadingEditor = true;
        try
        {
            _editingId = draft.Id;
            IsEditing = true;
            SelectedKind = Kinds.FirstOrDefault(k => k.Value == draft.Kind) ?? Kinds[0];
            Value = draft.Value;
            SelectedEntity = Entities.FirstOrDefault(e => e.Id == draft.EntityId);
            SelectedPolicy = Policies.FirstOrDefault(p => p.Value == draft.Policy) ?? Policies[0];
            Priority = draft.Priority;
            Enabled = draft.Enabled;
        }
        finally
        {
            _loadingEditor = false;
        }

        OnEditorChanged();
    }

    private void CloseEditor()
    {
        _loadingEditor = true;
        try
        {
            _editingId = null;
            IsEditing = false;
            SelectedKind = null;
            Value = null;
            SelectedEntity = null;
            SelectedPolicy = null;
            Priority = 1;
            Enabled = true;
        }
        finally
        {
            _loadingEditor = false;
        }

        _previewDelay?.Cancel();
        PreviewSummary = "";
        PreviewSample.Clear();
        RaiseGates();
    }

    private void OnEditorChanged()
    {
        if (_loadingEditor) return;
        this.RaisePropertyChanged(nameof(IsAssetGroup));
        RaiseGates();
        SchedulePreview();
    }

    /// <summary>The preview runs 500 ms after the last edit (S40 §6.6), never on each keystroke.</summary>
    private void SchedulePreview()
    {
        _previewDelay?.Cancel();
        PreviewSample.Clear();
        var draft = Draft;
        if (!IsEditing) return;
        if (!EntityMappingGates.Preview(draft).Enabled || SelectedConnection is not { } connection)
        {
            PreviewSummary = RiskAdminText.Text(EntityMappingGates.Incomplete);
            return;
        }

        var delay = _previewDelay = new CancellationTokenSource();
        _ = PreviewAsync(draft!, connection.Id, delay.Token);
    }

    private async Task PreviewAsync(EntityMappingDraft draft, int connectionId, CancellationToken ct)
    {
        try
        {
            await Task.Delay(PreviewDelay, ct);
            var preview = await _configService.PreviewMappingAsync(draft.ToUpsert(VisionOne, connectionId), draft.Id, ct);
            if (ct.IsCancellationRequested) return;
            PreviewSummary = string.Format(CultureInfo.CurrentCulture, RiskAdminText.Text("EntityMappingPreviewFMT"),
                preview.Matched, preview.WouldAssign, preview.WouldReassign, preview.Conflicts, preview.AlreadyAssigned);
            PreviewSample.Clear();
            foreach (var row in preview.Sample)
                PreviewSample.Add(string.Format(CultureInfo.CurrentCulture, RiskAdminText.Text("EntityMappingSampleFMT"),
                    row.ObjectLabel, RiskAdminText.Dash(row.CurrentEntity), RiskAdminText.Dash(row.NewEntity),
                    RiskAdminText.Text("EntityMappingOutcome_" + row.OutcomeKey)));
        }
        catch (OperationCanceledException)
        {
            // A newer edit superseded this preview.
        }
        catch (Exception ex)
        {
            if (ct.IsCancellationRequested) return;
            Logger.Warning("The vendor mapping preview was refused: {Message}", ex.Message);
            PreviewSummary = RiskAdminText.Explain(ex);
        }
    }

    private void RaiseGates()
    {
        foreach (var name in new[]
                 {
                     nameof(IsNewEnabled), nameof(StrNewReason), nameof(IsSaveEnabled), nameof(StrSaveReason),
                     nameof(IsDeleteEnabled), nameof(StrDeleteReason), nameof(IsRefreshEnabled)
                 })
            this.RaisePropertyChanged(name);
    }

    #endregion

    #region COMMANDS

    public void BtNewMappingClicked()
    {
        if (!NewGate.Enabled) return;
        SelectedRow = null;
        Edit(EntityMappingDraft.New(EntityMappingGates.NextPriority(_rules)));
    }

    public async Task BtRefreshMappingsClicked()
    {
        if (!IsRefreshEnabled) return;
        await LoadRulesAsync(_editingId);
    }

    public async Task BtSaveMappingClicked()
    {
        if (!SaveGate.Enabled || Draft is not { } draft || SelectedConnection is not { } connection) return;
        VendorEntityMappingDto? saved = null;
        var body = draft.ToUpsert(VisionOne, connection.Id);
        if (!await RunRiskAsync(Localizer["EntityMappingSavedMSG"], async () => saved = draft.Id is { } id
                ? await _configService.UpdateMappingAsync(id, body)
                : await _configService.CreateMappingAsync(body)))
            return;
        await LoadRulesAsync(saved?.Id);
    }

    public async Task BtDeleteMappingClicked()
    {
        if (!DeleteGate.Enabled || Draft?.Id is not { } id) return;
        if (!await ConfirmationDialog.ConfirmAsync(Localizer["Warning"],
                string.Format(Localizer["EntityMappingDeleteConfirmMSG"], Value)))
            return;
        if (await RunRiskAsync(Localizer["EntityMappingDeletedMSG"], () => _configService.DeleteMappingAsync(id)))
            await LoadRulesAsync();
    }

    #endregion
}

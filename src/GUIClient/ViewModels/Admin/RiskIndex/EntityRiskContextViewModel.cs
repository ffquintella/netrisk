using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools;
using GUIClient.Tools.RiskIndex;
using Model.Entities;
using ReactiveUI;
using ReactiveUI.Binding;

namespace GUIClient.ViewModels.Admin.RiskIndex;

/// <summary>A choice in a risk-context cell: a value, or "not declared" (null).</summary>
public sealed record RiskContextOption<T>(T? Value, string Label) where T : struct
{
    public override string ToString() => Label;
}

/// <summary>One entity row of the Risk context tab, editing the tab's <see cref="RiskContextDraft"/>.</summary>
public sealed class EntityRiskContextRowViewModel : ReactiveObject
{
    private readonly EntityRiskContextViewModel _owner;
    private bool _loading;

    public EntityRiskContextRowViewModel(EntityRiskContextRowDto row, string? parentName, EntityRiskContextViewModel owner)
    {
        Row = row;
        _owner = owner;
        ParentName = RiskAdminText.Dash(parentName);
        Load();
    }

    public EntityRiskContextRowDto Row { get; }
    public int EntityId => Row.EntityId;
    public string Name => Row.Name;
    public string Type => RiskAdminText.Optional(Row.DefinitionName) ?? Row.DefinitionName;
    public string ParentName { get; }
    public bool SupportsCriticality => Row.SupportsCriticality && _owner.CanEdit;
    public bool SupportsInternetFacing => Row.SupportsInternetFacing && _owner.CanEdit;
    public bool SupportsClassification => Row.SupportsSecurityClassification && _owner.CanEdit;
    public bool ShowCriticality => Row.SupportsCriticality;
    public bool ShowInternetFacing => Row.SupportsInternetFacing;
    public bool ShowClassification => Row.SupportsSecurityClassification;
    public IReadOnlyList<RiskContextOption<int>> CriticalityOptions => _owner.CriticalityOptions;
    public IReadOnlyList<RiskContextOption<bool>> InternetOptions => _owner.InternetOptions;
    public IReadOnlyList<RiskContextOption<int>> ClassificationOptions => _owner.ClassificationOptions;

    /// <summary>"declared, not BIA" while M41 does not provide the BIA (S40 §6.7 "Origem").</summary>
    public string Origin => Row.SupportsCriticality && Row.Criticality is not null ? RiskAdminText.Text("RiskContextDeclared") : "—";

    public string LastChange => Row.LastChangedAtUtc is null
        ? "—"
        : RiskAdminText.Dash(Row.LastChangedBy) + " " + RiskAdminText.Day(Row.LastChangedAtUtc);

    private RiskContextOption<int>? _criticality;

    public RiskContextOption<int>? Criticality
    {
        get => _criticality;
        set
        {
            this.RaiseAndSetIfChanged(ref _criticality, value);
            Push();
        }
    }

    private RiskContextOption<bool>? _internetFacing;

    public RiskContextOption<bool>? InternetFacing
    {
        get => _internetFacing;
        set
        {
            this.RaiseAndSetIfChanged(ref _internetFacing, value);
            Push();
        }
    }

    private RiskContextOption<int>? _classification;

    public RiskContextOption<int>? Classification
    {
        get => _classification;
        set
        {
            this.RaiseAndSetIfChanged(ref _classification, value);
            Push();
        }
    }

    public bool IsChanged => _owner.Draft.IsChanged(EntityId);

    public void Load()
    {
        _loading = true;
        try
        {
            var values = _owner.Draft.Current(EntityId);
            Criticality = CriticalityOptions.FirstOrDefault(o => o.Value == values.Criticality) ?? CriticalityOptions[0];
            InternetFacing = InternetOptions.FirstOrDefault(o => o.Value == values.InternetFacing) ?? InternetOptions[0];
            Classification = ClassificationOptions.FirstOrDefault(o => o.Value == values.SecurityClassificationId)
                             ?? ClassificationOptions[0];
        }
        finally
        {
            _loading = false;
        }

        this.RaisePropertyChanged(nameof(IsChanged));
    }

    private void Push()
    {
        if (_loading) return;
        _owner.Draft.Set(EntityId, new RiskContextValues(Criticality?.Value, InternetFacing?.Value, Classification?.Value));
        this.RaisePropertyChanged(nameof(IsChanged));
        _owner.OnRowEdited();
    }
}

/// <summary>A change in the selected entity's history (S40 §6.7 "Histórico").</summary>
public sealed record EntityHistoryRow(string When, string Actor, string Property, string Change, string Justification);

/// <summary>
/// The Risk context tab (S40 §6.7; T264): criticality, internet exposure and data classification of the entity map edited
/// side by side under <c>entity_risk_context</c>, saved as one all-or-nothing batch with a mandatory justification, the
/// change history of the selected entity, and new activities under a process (<c>entities_manage</c>).
/// </summary>
public class EntityRiskContextViewModel : RiskAdminViewModelBase
{
    #region LANGUAGE

    public string StrSearch { get; } = Localizer["RiskContextSearch"];
    public string StrType { get; } = Localizer["RiskContextType"];
    public string StrOnlyUndeclared { get; } = Localizer["RiskContextOnlyUndeclared"];
    public string StrEntity { get; } = Localizer["Entity"];
    public string StrParent { get; } = Localizer["RiskContextParent"];
    public string StrCriticality { get; } = Localizer["RiskContextCriticality"];
    public string StrOrigin { get; } = Localizer["RiskContextOrigin"];
    public string StrInternet { get; } = Localizer["RiskContextInternetFacing"];
    public string StrClassification { get; } = Localizer["RiskContextClassification"];
    public string StrLastChange { get; } = Localizer["RiskContextLastChange"];
    public string StrJustification { get; } = Localizer["RiskAdminJustification"];
    public string StrSaveContext { get; } = Localizer["RiskContextSaveChanges"];
    public string StrDiscard { get; } = Localizer["RiskAdminDiscardChanges"];
    public string StrRefresh { get; } = Localizer["Reload"];
    public string StrHistory { get; } = Localizer["RiskAdminHistory"];
    public string StrWhen { get; } = Localizer["RiskAdminWhen"];
    public string StrActor { get; } = Localizer["RiskAdminActor"];
    public string StrProperty { get; } = Localizer["RiskAdminField"];
    public string StrChange { get; } = Localizer["RiskAdminChange"];
    public string StrNewActivity { get; } = Localizer["RiskContextNewActivity"];
    public string StrActivityName { get; } = Localizer["RiskContextActivityName"];
    public string StrNotApplicable { get; } = Localizer["RiskContextNotApplicable"];
    public string StrReadOnly { get; } = Localizer["RiskContextReadOnlyMSG"];

    #endregion

    private readonly IEntitiesService _entitiesService;
    private readonly RiskIndexAdminRights _rights;
    private List<EntityRiskContextRowViewModel> _all = [];

    public EntityRiskContextViewModel(RiskIndexAdminRights rights, IEntitiesService entitiesService)
    {
        _rights = rights;
        _entitiesService = entitiesService;
        CriticalityOptions = new[] { new RiskContextOption<int>(null, RiskAdminText.Text("RiskContextNotDeclared")) }
            .Concat(Enumerable.Range(EntityRiskContextRules.MinimumCriticality, EntityRiskContextRules.MaximumCriticality)
                .Select(c => new RiskContextOption<int>(c, RiskAdminText.Text("RiskContextCriticality_" + c))))
            .ToList();
        InternetOptions =
        [
            new RiskContextOption<bool>(null, RiskAdminText.Text("RiskContextNotDeclared")),
            new RiskContextOption<bool>(true, RiskAdminText.Text("Yes")),
            new RiskContextOption<bool>(false, RiskAdminText.Text("No"))
        ];
        this.WhenAnyValue(vm => vm.SearchText, vm => vm.SelectedType, vm => vm.OnlyUndeclared).Subscribe(_ => ApplyFilter());
        this.WhenAnyValue(vm => vm.SelectedRow).Subscribe(row =>
        {
            _ = LoadHistoryAsync(row);
            RaiseGates();
        });
        this.WhenAnyValue(vm => vm.Justification, vm => vm.NewActivityName).Subscribe(_ => RaiseGates());
    }

    #region PROPERTIES

    public RiskContextDraft Draft { get; } = new();

    public bool CanEdit => _rights.RiskContext;

    public bool IsReadOnly => !CanEdit;

    public IReadOnlyList<RiskContextOption<int>> CriticalityOptions { get; }

    public IReadOnlyList<RiskContextOption<bool>> InternetOptions { get; }

    public IReadOnlyList<RiskContextOption<int>> ClassificationOptions { get; private set; } = [];

    public ObservableCollection<EntityRiskContextRowViewModel> Rows { get; } = [];

    public ObservableCollection<string> Types { get; } = [];

    private string? _selectedType;

    public string? SelectedType
    {
        get => _selectedType;
        set => this.RaiseAndSetIfChanged(ref _selectedType, value);
    }

    private string? _searchText;

    public string? SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

    private bool _onlyUndeclared;

    public bool OnlyUndeclared
    {
        get => _onlyUndeclared;
        set => this.RaiseAndSetIfChanged(ref _onlyUndeclared, value);
    }

    private EntityRiskContextRowViewModel? _selectedRow;

    public EntityRiskContextRowViewModel? SelectedRow
    {
        get => _selectedRow;
        set => this.RaiseAndSetIfChanged(ref _selectedRow, value);
    }

    private string? _justification;

    public string? Justification
    {
        get => _justification;
        set => this.RaiseAndSetIfChanged(ref _justification, value);
    }

    public string ChangedSummary => RiskAdminText.Format("RiskContextChangedFMT", Draft.ChangedCount);

    public ObservableCollection<EntityHistoryRow> History { get; } = [];

    private string _historyMessage = "";

    public string HistoryMessage
    {
        get => _historyMessage;
        private set => this.RaiseAndSetIfChanged(ref _historyMessage, value);
    }

    private string? _newActivityName;

    public string? NewActivityName
    {
        get => _newActivityName;
        set => this.RaiseAndSetIfChanged(ref _newActivityName, value);
    }

    private string? _errorMessage;

    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    // ---------- gates ----------

    private Gate SaveGate
    {
        get
        {
            if (!CanEdit) return Gate.Closed(RiskIndexAdminAccess.RequiresRiskContext);
            var (_, reason) = Draft.Batch(Justification);
            return reason is null ? Gate.Open : Gate.Closed(reason);
        }
    }

    public bool IsSaveEnabled => SaveGate.Enabled && !IsBusy;
    public string StrSaveReason => RiskAdminText.Reason(SaveGate);

    public bool IsDiscardEnabled => Draft.IsDirty;
    public string StrDiscardReason => IsDiscardEnabled ? "" : RiskAdminText.Text("RiskAdminNoChanges");

    public bool IsRefreshEnabled => !IsBusy && !Draft.IsDirty;
    public string StrRefreshReason => IsRefreshEnabled ? "" : RiskAdminText.Text("RiskContextSaveOrDiscardFirst");

    private Gate NewActivityGate => !_rights.EntitiesManage ? Gate.Closed("RiskContextRequiresEntitiesManage")
        : SelectedRow?.Row.DefinitionName != "businessProcess" ? Gate.Closed("RiskContextSelectProcess")
        : string.IsNullOrWhiteSpace(NewActivityName) ? Gate.Closed("RiskAdminNameRequired")
        : Gate.Open;

    public bool IsNewActivityEnabled => NewActivityGate.Enabled;
    public string StrNewActivityReason => RiskAdminText.Reason(NewActivityGate);

    #endregion

    #region COMMANDS

    public async Task BtSaveContextClicked()
    {
        var (request, _) = Draft.Batch(Justification);
        if (!SaveGate.Enabled || request is null) return;

        var lines = Draft.Changes().Take(15).Select(c => "• " + c.Row.Name + ": " + Describe(c.Before) + " → " + Describe(c.After));
        var message = string.Format(Localizer["RiskContextSaveConfirmMSG"], Draft.ChangedCount) + "\n\n" + string.Join("\n", lines) +
                      (Draft.ChangedCount > 15 ? "\n…" : "");
        if (!await ConfirmationDialog.ConfirmAsync(Localizer["Warning"], message)) return;

        EntityRiskContextBatchResultDto? result = null;
        await RunRiskAsync("", async () => result = await _entitiesService.SaveRiskContextAsync(request));
        if (result is null) return; // nothing changed on the server: the rows stay marked (S40 §6.7)

        Toasts.Success(string.Format(Localizer["RiskContextSavedMSG"], result.UpdatedEntities));
        Justification = null;
        await LoadAsync();
    }

    public async Task BtDiscardContextClicked()
    {
        if (!IsDiscardEnabled) return;
        if (!await ConfirmationDialog.ConfirmAsync(Localizer["Warning"],
                string.Format(Localizer["RiskContextDiscardConfirmMSG"], Draft.ChangedCount)))
            return;
        Draft.Discard();
        foreach (var row in _all) row.Load();
        OnRowEdited();
    }

    public Task BtRefreshContextClicked() => IsRefreshEnabled ? LoadAsync() : Task.CompletedTask;

    /// <summary>Creates an activity under the selected process (S40 §6.7 "Nova atividade"), as the Entities screen creates one.</summary>
    public async Task BtNewActivityClicked()
    {
        if (!NewActivityGate.Enabled || SelectedRow is not { } process) return;
        var name = NewActivityName!.Trim();
        var created = false;
        await RunRiskAsync(string.Format(Localizer["RiskContextActivityCreatedMSG"], name), async () =>
        {
            var properties = await _entitiesService.GetMandatoryPropertiesAsync("activity");
            var nameProperty = properties.FirstOrDefault(p => p.Type == "name")
                               ?? throw new InvalidOperationException("The activity definition has no name property.");
            nameProperty.Value = name;
            foreach (var property in properties) property.Name += name;
            var dto = new EntityDto
            {
                Id = 0, Parent = process.EntityId, DefinitionName = "activity", Status = "active", EntitiesProperties = properties
            };
            created = await Task.Run(() => _entitiesService.CreateEntity(dto)) is not null;
        });
        if (!created) return;
        NewActivityName = null;
        if (!Draft.IsDirty) await LoadAsync();
    }

    #endregion

    #region METHODS

    public async Task LoadAsync()
    {
        await WithBusyAsync(async () =>
        {
            try
            {
                var levels = await _entitiesService.GetAllAsync("securityClassificationLevel");
                ClassificationOptions = new[] { new RiskContextOption<int>(null, RiskAdminText.Text("RiskContextNotDeclared")) }
                    .Concat(levels.OrderBy(l => l.DisplayName, StringComparer.CurrentCultureIgnoreCase)
                        .Select(l => new RiskContextOption<int>(l.Id, l.DisplayName)))
                    .ToList();
                this.RaisePropertyChanged(nameof(ClassificationOptions));

                var rows = await _entitiesService.GetRiskContextAsync();
                Draft.Load(rows);
                var names = rows.ToDictionary(r => r.EntityId, r => r.Name);
                _all = rows.OrderBy(r => TypeOrder(r.DefinitionName)).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase)
                    .Select(r => new EntityRiskContextRowViewModel(r,
                        r.ParentId is { } p && names.TryGetValue(p, out var parent) ? parent : null, this))
                    .ToList();

                var selectedType = SelectedType;
                Types.Clear();
                Types.Add(RiskAdminText.Text("RiskContextAllTypes"));
                foreach (var type in rows.Select(r => r.DefinitionName).Distinct().OrderBy(TypeOrder))
                    Types.Add(RiskAdminText.Optional(type) ?? type);
                SelectedType = Types.Contains(selectedType ?? "") ? selectedType : Types[0];
                ErrorMessage = null;
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error loading the entity risk context");
                ErrorMessage = RiskAdminText.Explain(ex);
            }
        });
        ApplyFilter();
        OnRowEdited();
    }

    internal void OnRowEdited()
    {
        this.RaisePropertyChanged(nameof(ChangedSummary));
        RaiseGates();
    }

    private void ApplyFilter()
    {
        var all = RiskAdminText.Text("RiskContextAllTypes");
        var text = SearchText?.Trim();
        Rows.Clear();
        foreach (var row in _all.Where(r =>
                     (string.IsNullOrEmpty(text) || r.Name.Contains(text, StringComparison.CurrentCultureIgnoreCase)) &&
                     (SelectedType is null || SelectedType == all || r.Type == SelectedType) &&
                     (!OnlyUndeclared || (r.Row.SupportsCriticality && Draft.Current(r.EntityId).Criticality is null))))
            Rows.Add(row);
    }

    private async Task LoadHistoryAsync(EntityRiskContextRowViewModel? row)
    {
        History.Clear();
        HistoryMessage = "";
        if (row is null) return;
        try
        {
            var entries = await _entitiesService.GetHistoryAsync(row.EntityId, 200);
            if (!ReferenceEquals(row, SelectedRow)) return;
            foreach (var entry in entries)
                History.Add(new EntityHistoryRow(RiskAdminText.Moment(entry.OccurredAtUtc), entry.Actor, entry.Property,
                    RiskAdminText.Dash(entry.OldValue) + " → " + RiskAdminText.Dash(entry.NewValue),
                    RiskAdminText.Dash(entry.Justification)));
            if (History.Count == 0) HistoryMessage = RiskAdminText.Text("RiskContextNoHistory");
        }
        catch (Exception ex)
        {
            HistoryMessage = RiskAdminText.Explain(ex);
        }
    }

    private string Describe(RiskContextValues values)
    {
        var parts = new List<string>();
        parts.Add(StrCriticality + " " + (values.Criticality?.ToString(CultureInfo.CurrentCulture) ?? "—"));
        if (values.InternetFacing is { } inet) parts.Add(StrInternet + " " + RiskAdminText.Text(inet ? "Yes" : "No"));
        if (values.SecurityClassificationId is { } id)
            parts.Add(StrClassification + " " + (ClassificationOptions.FirstOrDefault(o => o.Value == id)?.Label ?? "#" + id));
        return string.Join(", ", parts);
    }

    private static int TypeOrder(string type)
    {
        var index = EntityRiskContextRules.DefaultTypes.ToList().IndexOf(type);
        return index < 0 ? int.MaxValue : index;
    }

    private void RaiseGates()
    {
        foreach (var name in new[]
                 {
                     nameof(IsSaveEnabled), nameof(StrSaveReason), nameof(IsDiscardEnabled), nameof(StrDiscardReason),
                     nameof(IsRefreshEnabled), nameof(StrRefreshReason), nameof(IsNewActivityEnabled), nameof(StrNewActivityReason)
                 })
            this.RaisePropertyChanged(name);
    }

    #endregion
}

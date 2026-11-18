using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using DAL.Enums;
using GUIClient.Tools;
using GUIClient.Tools.RiskIndex;
using Model.RiskIndex;
using ReactiveUI;

namespace GUIClient.ViewModels.Admin.RiskIndex;

/// <summary>A group in the editor's left list, with its change mark (S40 §6.2).</summary>
public sealed class RiskParameterGroupItem(string key, string label) : ReactiveObject
{
    public string Key { get; } = key;
    public string Label { get; } = label;

    private bool _isChanged;

    public bool IsChanged
    {
        get => _isChanged;
        set => this.RaiseAndSetIfChanged(ref _isChanged, value);
    }
}

/// <summary>
/// One editable parameter row (S40 §6.2.2): the active value, the draft's control, the Δ, the lock and the inline issues of
/// the server's rules. Writes go to the shared <see cref="RiskProfileDraftSession"/>.
/// </summary>
public sealed class RiskParameterRowViewModel : ReactiveObject
{
    private readonly RiskProfileDraftSession _session;

    public RiskParameterRowViewModel(ProfileParameterRowSpec spec, RiskProfileDraftSession session)
    {
        Spec = spec;
        _session = session;
        Label = RiskAdminText.Text(spec.LabelKey) + (spec.SubLabel is null ? "" : " · " + spec.SubLabel);
        Help = RiskAdminText.Optional(spec.HelpKey) ?? "";
        LockedReason = spec.LockedReasonKey is null ? "" : RiskAdminText.Text(spec.LockedReasonKey);
        Reload();
    }

    public ProfileParameterRowSpec Spec { get; }
    public string Label { get; }
    public string Help { get; }
    public string LockedReason { get; }
    public bool IsLocked => Spec.Locked;
    public bool IsNumber => Spec.Kind is ProfileParameterKind.Decimal or ProfileParameterKind.Integer;
    public bool IsBoolean => Spec.Kind == ProfileParameterKind.Boolean;
    public bool IsChoice => Spec.Kind == ProfileParameterKind.Choice;
    public bool IsList => Spec.Kind is ProfileParameterKind.TextList or ProfileParameterKind.IntegerList or ProfileParameterKind.CidrList;
    public decimal Minimum => (decimal)Spec.Minimum;
    public decimal Maximum => (decimal)Spec.Maximum;
    public decimal Increment => (decimal)Spec.Increment;
    public string FormatString => Spec.Kind == ProfileParameterKind.Integer ? "0" : "0.00";
    public IReadOnlyList<string> Choices => Spec.Choices;
    public bool IsEditable => _session.IsEditable && !Spec.Locked;
    public string Range => IsNumber ? $"[{Spec.Minimum.ToString("0.##", CultureInfo.CurrentCulture)}; {Spec.Maximum.ToString("0.##", CultureInfo.CurrentCulture)}]" : "";

    private bool _reloading;

    private decimal? _number;

    public decimal? Number
    {
        get => _number;
        set
        {
            this.RaiseAndSetIfChanged(ref _number, value);
            if (_reloading || value is not { } v) return;
            _session.Edit(p => ProfileParameterRows.WithNumber(p, Spec, (double)v));
        }
    }

    private bool _flag;

    public bool Flag
    {
        get => _flag;
        set
        {
            this.RaiseAndSetIfChanged(ref _flag, value);
            if (!_reloading) _session.Edit(p => ProfileParameterRows.WithBoolean(p, Spec, value));
        }
    }

    private string? _choice;

    public string? Choice
    {
        get => _choice;
        set
        {
            this.RaiseAndSetIfChanged(ref _choice, value);
            if (!_reloading && value is not null) _session.Edit(p => ProfileParameterRows.WithChoice(p, Spec, value));
        }
    }

    private string _text = "";

    public string Text
    {
        get => _text;
        set
        {
            this.RaiseAndSetIfChanged(ref _text, value);
            if (_reloading) return;
            var edited = ProfileParameterRows.WithList(_session.Edited, Spec, value);
            _session.MarkField(Spec.Key, edited is not null);
            if (edited is not null) _session.Edit(_ => edited);
            ParseError = edited is null ? RiskAdminText.Text("RiskAdminListItemInvalid") : "";
        }
    }

    private string _parseError = "";

    public string ParseError
    {
        get => _parseError;
        private set
        {
            this.RaiseAndSetIfChanged(ref _parseError, value);
            this.RaisePropertyChanged(nameof(HasParseError));
        }
    }

    public bool HasParseError => ParseError.Length > 0;

    public string ActiveValue => _session.Active is { } active
        ? ProfileParameterRows.Display(ProfileParameterRows.Read(active.Parameters, Spec.Path))
        : "—";

    public string Delta
    {
        get
        {
            if (!IsNumber || _session.Active is null) return "";
            var before = ProfileParameterRows.ReadNumber(_session.Active.Parameters, Spec.Path);
            var after = ProfileParameterRows.ReadNumber(_session.Edited, Spec.Path);
            if (before is null || after is null || Math.Abs(after.Value - before.Value) < 1e-9) return "";
            return (after.Value - before.Value).ToString("+0.##;−0.##", CultureInfo.CurrentCulture);
        }
    }

    private string _issues = "";

    public string Issues
    {
        get => _issues;
        private set
        {
            this.RaiseAndSetIfChanged(ref _issues, value);
            this.RaisePropertyChanged(nameof(HasIssues));
        }
    }

    public bool HasIssues => Issues.Length > 0;

    /// <summary>Reads the row back from the session (after a load, a discard or a preset) without writing it.</summary>
    public void Reload()
    {
        _reloading = true;
        try
        {
            var parameters = _session.Edited;
            switch (Spec.Kind)
            {
                case ProfileParameterKind.Decimal:
                case ProfileParameterKind.Integer:
                    Number = ProfileParameterRows.ReadNumber(parameters, Spec.Path) is { } n ? (decimal)n : null;
                    break;
                case ProfileParameterKind.Boolean:
                    Flag = ProfileParameterRows.ReadBoolean(parameters, Spec.Path);
                    break;
                case ProfileParameterKind.Choice:
                    Choice = ProfileParameterRows.ReadText(parameters, Spec.Path);
                    break;
                default:
                    Text = ProfileParameterRows.ReadText(parameters, Spec.Path);
                    ParseError = "";
                    break;
            }
        }
        finally
        {
            _reloading = false;
        }

        RefreshDerived();
    }

    public void RefreshDerived()
    {
        var validation = _session.LiveValidation;
        Issues = validation is null
            ? ""
            : string.Join("\n", ProfileParameterRows.IssuesForRow(Spec.Key, validation.Errors.Concat(validation.Warnings))
                .Select(i => ProfileIssueText.Message(i, RiskAdminText.Optional)));
        this.RaisePropertyChanged(nameof(Delta));
        this.RaisePropertyChanged(nameof(ActiveValue));
        this.RaisePropertyChanged(nameof(IsEditable));
    }
}

/// <summary>A communication band in the bands grid: both labels and the inclusive lower bound (S40 §6.2.2 group 7).</summary>
public sealed class RiskBandRowViewModel(Action changed) : ReactiveObject
{
    private string _labelPtBr = "";

    public string LabelPtBr
    {
        get => _labelPtBr;
        set { this.RaiseAndSetIfChanged(ref _labelPtBr, value); changed(); }
    }

    private string _labelEnUs = "";

    public string LabelEnUs
    {
        get => _labelEnUs;
        set { this.RaiseAndSetIfChanged(ref _labelEnUs, value); changed(); }
    }

    private decimal? _lower;

    public decimal? Lower
    {
        get => _lower;
        set { this.RaiseAndSetIfChanged(ref _lower, value); changed(); }
    }

    public void Load(RiskIndexBandDefinition band)
    {
        _labelPtBr = band.LabelPtBr;
        _labelEnUs = band.LabelEnUs;
        _lower = band.LowerInclusive;
        this.RaisePropertyChanged(nameof(LabelPtBr));
        this.RaisePropertyChanged(nameof(LabelEnUs));
        this.RaisePropertyChanged(nameof(Lower));
    }

    public RiskIndexBandDefinition ToDefinition() =>
        new() { LabelPtBr = LabelPtBr.Trim(), LabelEnUs = LabelEnUs.Trim(), LowerInclusive = (int)(Lower ?? 0) };
}

/// <summary>
/// The draft editor (S40 §6.2; T260): the parameters by group with the active value, the draft's control, the Δ and the
/// inline issues of the server's own rules — the joint tail lock among them — the bands grid with Portuguese and English
/// labels, presets, Save under <c>If-Match</c>, Discard, and the server's validation.
/// </summary>
public class RiskProfileEditorViewModel : RiskAdminViewModelBase
{
    #region LANGUAGE

    public string StrName { get; } = Localizer["RiskProfileName"];
    public string StrMode { get; } = Localizer["RiskProfileMode"];
    public string StrGroups { get; } = Localizer["RiskAdminGroups"];
    public string StrParameter { get; } = Localizer["RiskProfileParameterKey"];
    public string StrActive { get; } = Localizer["RiskAdminActiveValue"];
    public string StrDraft { get; } = Localizer["RiskAdminDraftValue"];
    public string StrDelta { get; } = Localizer["RiskAdminDelta"];
    public string StrRange { get; } = Localizer["RiskAdminRange"];
    public string StrPreset { get; } = Localizer["RiskAdminStartingPreset"];
    public string StrApplyPreset { get; } = Localizer["RiskAdminApplyPreset"];
    public string StrSaveDraft { get; } = Localizer["RiskAdminSaveDraft"];
    public string StrReset { get; } = Localizer["RiskAdminDiscardChanges"];
    public string StrValidate { get; } = Localizer["RiskAdminValidate"];
    public string StrGoToPreview { get; } = Localizer["RiskAdminGoToPreview"];
    public string StrBands { get; } = Localizer["RiskParamGroup_bands"];
    public string StrBandLabelPt { get; } = Localizer["RiskAdminBandLabelPt"];
    public string StrBandLabelEn { get; } = Localizer["RiskAdminBandLabelEn"];
    public string StrBandLower { get; } = Localizer["RiskAdminBandLower"];
    public string StrAddBand { get; } = Localizer["RiskAdminAddBand"];
    public string StrRemoveBand { get; } = Localizer["RiskAdminRemoveBand"];
    public string StrRegisterBands { get; } = Localizer["RiskAdminRegisterBands"];
    public string StrNoDraft { get; } = Localizer["RiskAdminNoDraftMSG"];
    public string StrServerValidation { get; } = Localizer["RiskAdminServerValidation"];

    #endregion

    private readonly RiskProfileDraftSession _session;
    private readonly Action _openPreviewTab;
    private List<ProfileParameterRowSpec> _specs = [];
    private bool _loadingBands;

    public RiskProfileEditorViewModel(RiskProfileDraftSession session, Action openPreviewTab)
    {
        _session = session;
        _openPreviewTab = openPreviewTab;
        foreach (var group in ProfileParameterRows.GroupOrder)
            Groups.Add(new RiskParameterGroupItem(group, RiskAdminText.Text(ProfileParameterRows.GroupLabelKey(group))));
        _selectedGroup = Groups[0];
        _session.SessionChanged += OnSessionChanged;
        this.WhenAnyValue(vm => vm.SelectedGroup).Subscribe(_ => BuildRows());
    }

    #region PROPERTIES

    public ObservableCollection<RiskParameterGroupItem> Groups { get; } = [];

    private RiskParameterGroupItem? _selectedGroup;

    public RiskParameterGroupItem? SelectedGroup
    {
        get => _selectedGroup;
        set => this.RaiseAndSetIfChanged(ref _selectedGroup, value);
    }

    public ObservableCollection<RiskParameterRowViewModel> Rows { get; } = [];

    public bool IsBandsGroup => SelectedGroup?.Key == "bands";

    public ObservableCollection<RiskBandRowViewModel> Bands { get; } = [];

    private RiskBandRowViewModel? _selectedBand;

    public RiskBandRowViewModel? SelectedBand
    {
        get => _selectedBand;
        set => this.RaiseAndSetIfChanged(ref _selectedBand, value);
    }

    public bool HasDraft => _session.HasDraft;

    public bool IsEditable => _session.IsEditable;

    public string DraftName
    {
        get => _session.EditedName;
        set
        {
            _session.EditedName = value;
            this.RaisePropertyChanged();
        }
    }

    /// <summary>The two modes of S39 §15.1 (M59): the draft chooses one; a change is a rebaseline the validation reports.</summary>
    public IReadOnlyList<RiskModeItem> ModeOptions { get; } =
    [
        new(RiskIndexMode.Interim, Localizer[RiskOverviewTopBar.ModeKey(RiskIndexMode.Interim)].Value,
            Localizer[RiskOverviewTopBar.ModeHintKey(RiskIndexMode.Interim)].Value),
        new(RiskIndexMode.Full, Localizer[RiskOverviewTopBar.ModeKey(RiskIndexMode.Full)].Value,
            Localizer[RiskOverviewTopBar.ModeHintKey(RiskIndexMode.Full)].Value)
    ];

    public RiskModeItem? SelectedMode
    {
        get => ModeOptions.FirstOrDefault(m => m.Mode == _session.EditedMode);
        set
        {
            if (value is null) return;
            _session.EditedMode = value.Mode;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(ModeHint));
        }
    }

    public string ModeHint => SelectedMode?.Hint ?? "";

    public string Header => _session.Draft is { } d
        ? RiskAdminText.Format("RiskAdminDraftHeaderFMT", d.Version, RiskAdminText.Text(RiskOverviewTopBar.ProfileStatusKey(d.Status)),
            RiskAdminText.Dash(d.CreatedBy), RiskAdminText.Moment(d.CreatedAtUtc))
        : "";

    /// <summary>The top summary: the live result of the server's rules, or the server's own after Validate (S40 §6.2.1).</summary>
    public ObservableCollection<string> SummaryErrors { get; } = [];

    public ObservableCollection<string> SummaryWarnings { get; } = [];

    public string SummaryHeadline
    {
        get
        {
            var validation = _session.ServerValidation ?? _session.LiveValidation;
            if (validation is null) return "";
            var source = RiskAdminText.Text(_session.ServerValidation is null ? "RiskAdminLiveValidation" : "RiskAdminServerValidation");
            return validation.IsValid
                ? RiskAdminText.Format("RiskAdminValidSummaryFMT", source, validation.ChangedKeys.Count)
                : RiskAdminText.Format("RiskAdminInvalidSummaryFMT", source, validation.Errors.Count);
        }
    }

    public bool IsSummaryValid => (_session.ServerValidation ?? _session.LiveValidation)?.IsValid ?? false;

    /// <summary>The issues of the selected group that no row shows (Σω, the tail lock, the bands, a whole anchor table).</summary>
    public ObservableCollection<string> GroupIssues { get; } = [];

    /// <summary>The joint tail lock with its numbers, live (groups 2 and 7): "τ·τ_G·L_top = 39.2 ≥ L_2 = 31".</summary>
    public string TailLock
    {
        get
        {
            var p = _session.Edited;
            if (p.Bands.Count < 2) return "";
            var product = p.Tau * p.TauG * p.Bands[^1].LowerInclusive;
            var second = p.Bands[1].LowerInclusive;
            return RiskAdminText.Format(product + 1e-6 >= second ? "RiskAdminTailLockOkFMT" : "RiskAdminTailLockFailFMT",
                product.ToString("0.#", CultureInfo.CurrentCulture), second);
        }
    }

    public bool ShowTailLock => SelectedGroup?.Key is "aggregation" or "bands";

    public ObservableCollection<RiskPresetItem> PresetOptions { get; } = [];

    private RiskPresetItem? _selectedPreset;

    public RiskPresetItem? SelectedPreset
    {
        get => _selectedPreset;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedPreset, value);
            RaiseGates();
        }
    }

    // ---------- gates ----------

    private Gate SaveGate => ProfileWorkflowGates.SaveDraft(_session.Rights.Configure, _session.IsDirty, IsBusy || _session.IsBusy,
        _session.StructurallyValid, _session.Draft?.Status);

    public bool IsSaveEnabled => SaveGate.Enabled;
    public string StrSaveReason => RiskAdminText.Reason(SaveGate);

    public bool IsResetEnabled => _session.IsDirty;
    public string StrResetReason => IsResetEnabled ? "" : RiskAdminText.Text("RiskAdminNoChanges");

    private Gate ValidateGate => ProfileWorkflowGates.ValidateOnServer(_session.Rights.Configure, _session.HasDraft, _session.IsDirty);
    public bool IsValidateEnabled => ValidateGate.Enabled;
    public string StrValidateReason => RiskAdminText.Reason(ValidateGate);

    private Gate ApplyPresetGate => !_session.IsEditable ? Gate.Closed("RiskAdminNoDraft")
        : SelectedPreset is null ? Gate.Closed("RiskAdminChoosePreset")
        : Gate.Open;

    public bool IsApplyPresetEnabled => ApplyPresetGate.Enabled;
    public string StrApplyPresetReason => RiskAdminText.Reason(ApplyPresetGate);

    public bool IsGoToPreviewEnabled => _session.HasDraft && !_session.IsDirty;
    public string StrGoToPreviewReason => IsGoToPreviewEnabled ? "" : RiskAdminText.Text("RiskAdminSaveDraftFirst");

    public bool IsBandEditable => _session.IsEditable;

    #endregion

    #region COMMANDS

    public async Task BtSaveDraftClicked()
    {
        if (SaveGate.Enabled) await _session.SaveAsync();
    }

    public async Task BtResetClicked()
    {
        if (!IsResetEnabled) return;
        if (!await ConfirmationDialog.ConfirmAsync(Localizer["Warning"],
                string.Format(Localizer["RiskAdminDiscardConfirmMSG"], _session.UnsavedChangesSummary)))
            return;
        _session.Discard();
    }

    public Task BtValidateClicked() => ValidateGate.Enabled ? _session.ValidateOnServerAsync() : Task.CompletedTask;

    public void BtApplyPresetClicked()
    {
        if (!ApplyPresetGate.Enabled || SelectedPreset is null) return;
        var preset = _session.Presets.FirstOrDefault(p => p.Key == SelectedPreset.Key);
        if (preset is null) return;
        // The sources and windows are this installation's, not the preset's: a preset never re-enables a source by accident.
        _session.Edit(current => preset.Parameters with
        {
            EnabledSources = current.EnabledSources, FreshnessDays = current.FreshnessDays,
            AssessableTypes = current.AssessableTypes
        });
        ReloadAll();
    }

    public void BtGoToPreviewClicked()
    {
        if (IsGoToPreviewEnabled) _openPreviewTab();
    }

    public void BtAddBandClicked()
    {
        if (!_session.IsEditable || Bands.Count >= 5) return;
        var last = Bands.LastOrDefault()?.Lower ?? 0;
        var band = new RiskBandRowViewModel(PushBands);
        band.Load(new RiskIndexBandDefinition { LowerInclusive = (int)Math.Min(95, last + 10) });
        Bands.Add(band);
        PushBands();
    }

    public void BtRemoveBandClicked()
    {
        if (!_session.IsEditable || SelectedBand is null || Bands.Count <= 2) return;
        Bands.Remove(SelectedBand);
        PushBands();
    }

    /// <summary>The register's preset (S40 §6.2.2 group 7): 0 / 40 / 70 / 90.</summary>
    public void BtRegisterBandsClicked()
    {
        if (!_session.IsEditable) return;
        _session.Edit(p => p with
        {
            Bands =
            [
                new RiskIndexBandDefinition { LabelPtBr = "Baixo", LabelEnUs = "Low", LowerInclusive = 0 },
                new RiskIndexBandDefinition { LabelPtBr = "Médio", LabelEnUs = "Medium", LowerInclusive = 40 },
                new RiskIndexBandDefinition { LabelPtBr = "Alto", LabelEnUs = "High", LowerInclusive = 70 },
                new RiskIndexBandDefinition { LabelPtBr = "Muito alto", LabelEnUs = "Very high", LowerInclusive = 90 }
            ]
        });
        LoadBands();
    }

    #endregion

    #region METHODS

    private void OnSessionChanged(bool reloaded)
    {
        if (PresetOptions.Count == 0)
            foreach (var preset in _session.Presets.Where(p => p.Activatable))
                PresetOptions.Add(new RiskPresetItem(preset.Key, RiskAdminText.Text(preset.NameKey)));

        if (reloaded || _specs.Count == 0) ReloadAll();
        else RefreshDerived();
    }

    /// <summary>Rebuilds the rows from the session (load, discard, save, preset), keeping the selected group.</summary>
    private void ReloadAll()
    {
        if (_session.Schema is null) return;
        _specs = ProfileParameterRows.Build(_session.Schema, _session.Edited).ToList();
        BuildRows();
        LoadBands();
        RefreshDerived();
        this.RaisePropertyChanged(nameof(DraftName));
        this.RaisePropertyChanged(nameof(SelectedMode));
        this.RaisePropertyChanged(nameof(ModeHint));
    }

    private void BuildRows()
    {
        Rows.Clear();
        if (SelectedGroup is not { } group) return;
        foreach (var spec in _specs.Where(s => s.Group == group.Key)) Rows.Add(new RiskParameterRowViewModel(spec, _session));
        this.RaisePropertyChanged(nameof(IsBandsGroup));
        this.RaisePropertyChanged(nameof(ShowTailLock));
        RefreshGroupIssues();
    }

    private void LoadBands()
    {
        _loadingBands = true;
        try
        {
            Bands.Clear();
            foreach (var definition in _session.Edited.Bands)
            {
                var row = new RiskBandRowViewModel(PushBands);
                row.Load(definition);
                Bands.Add(row);
            }
        }
        finally
        {
            _loadingBands = false;
        }
    }

    private void PushBands()
    {
        if (_loadingBands || !_session.IsEditable) return;
        _session.Edit(p => p with { Bands = Bands.Select(b => b.ToDefinition()).ToList() });
    }

    private void RefreshDerived()
    {
        foreach (var row in Rows) row.RefreshDerived();

        var changed = _session.Draft is { } draft
            ? ProfileParameterRows.ChangedGroups(draft.Parameters, _session.Edited)
            : new HashSet<string>();
        foreach (var group in Groups) group.IsChanged = changed.Contains(group.Key);

        var validation = _session.ServerValidation ?? _session.LiveValidation;
        SummaryErrors.Clear();
        SummaryWarnings.Clear();
        if (validation is not null)
        {
            foreach (var issue in validation.Errors) SummaryErrors.Add(ProfileIssueText.Line(issue, RiskAdminText.Optional));
            foreach (var issue in validation.Warnings) SummaryWarnings.Add(ProfileIssueText.Line(issue, RiskAdminText.Optional));
        }

        RefreshGroupIssues();
        RaiseGates();
        foreach (var name in new[]
                 {
                     nameof(HasDraft), nameof(IsEditable), nameof(Header), nameof(SummaryHeadline), nameof(IsSummaryValid),
                     nameof(TailLock), nameof(IsBandEditable)
                 })
            this.RaisePropertyChanged(name);
    }

    private void RefreshGroupIssues()
    {
        GroupIssues.Clear();
        var validation = _session.ServerValidation ?? _session.LiveValidation;
        if (validation is null || SelectedGroup is not { } group) return;
        var rowKeys = Rows.Select(r => r.Spec.Key).ToList();
        foreach (var issue in ProfileParameterRows.IssuesForGroup(group.Key, validation.Errors.Concat(validation.Warnings), rowKeys))
            GroupIssues.Add(ProfileIssueText.Line(issue, RiskAdminText.Optional));
    }

    private void RaiseGates()
    {
        foreach (var name in new[]
                 {
                     nameof(IsSaveEnabled), nameof(StrSaveReason), nameof(IsResetEnabled), nameof(StrResetReason),
                     nameof(IsValidateEnabled), nameof(StrValidateReason), nameof(IsApplyPresetEnabled),
                     nameof(StrApplyPresetReason), nameof(IsGoToPreviewEnabled), nameof(StrGoToPreviewReason)
                 })
            this.RaisePropertyChanged(name);
    }

    #endregion
}

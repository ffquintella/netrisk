using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools;
using GUIClient.Tools.RiskIndex;
using Model.DTO;
using Model.RiskIndex;
using ReactiveUI;

namespace GUIClient.ViewModels.Admin.RiskIndex;

/// <summary>A version in the Profiles list (S40 §6.1).</summary>
public sealed class RiskProfileListRow(RiskIndexProfileSummaryDto summary)
{
    public RiskIndexProfileSummaryDto Summary { get; } = summary;
    public int Id => Summary.Id;
    public string Version => "v" + Summary.Version;
    public string Name => Summary.Name;
    public string Preset => Summary.Preset is { } p ? RiskAdminText.Text("RiskPreset_" + p) : "—";
    public string Status => RiskAdminText.Text(RiskOverviewTopBar.ProfileStatusKey(Summary.Status));
    public string Effective => Summary.EffectiveFrom is null
        ? "—"
        : RiskAdminText.Day(Summary.EffectiveFrom) + " → " + (Summary.RetiredAt is null ? "" : RiskAdminText.Day(Summary.RetiredAt));
    public string Author => RiskAdminText.Dash(Summary.CreatedBy);
    public string Approver => RiskAdminText.Dash(Summary.ApprovedBy);
    public string Rebaseline => Summary.Status is RiskIndexProfileStatus.Draft or RiskIndexProfileStatus.Rejected
        ? "—"
        : RiskAdminText.Text(Summary.IsRebaseline ? "Yes" : "No");
    public string Committee => RiskAdminText.Dash(Summary.CommitteeDecisionRef);
    public override string ToString() => $"{Version} · {Name} · {Status}";
}

/// <summary>A parameter of the diff between two versions, or of one version's detail.</summary>
public sealed record RiskProfileDiffRow(string Group, string Parameter, string Key, string A, string B, bool Changed);

/// <summary>A row of a profile's audit trail.</summary>
public sealed record RiskProfileHistoryRow(string When, string Actor, string Field, string Change);

/// <summary>A preset offered by the new-draft form.</summary>
public sealed record RiskPresetItem(string Key, string Label)
{
    public override string ToString() => Label;
}

/// <summary>A profile mode in the draft editor's chooser (S39 §15.1; M59): the mode, its label and its hint.</summary>
public sealed record RiskModeItem(DAL.Enums.RiskIndexMode Mode, string Label, string Hint)
{
    public override string ToString() => Label;
}

/// <summary>Where a new draft starts from (S40 §6.1 "Novo rascunho").</summary>
public enum RiskDraftOrigin
{
    Active = 0,
    Preset = 1,
    Copy = 2
}

/// <summary>
/// The Profiles tab (S40 §6.1; T259): every version with status, effective dates and approvals, the detail of the selected
/// one, a parameter diff between any two versions, and the draft commands — new (from the active profile, a preset or a
/// copy), open and delete.
/// </summary>
public class RiskProfilesViewModel : RiskAdminViewModelBase
{
    #region LANGUAGE

    public string StrVersion { get; } = Localizer["RiskProfileVersion"];
    public string StrName { get; } = Localizer["RiskProfileName"];
    public string StrPreset { get; } = Localizer["RiskProfilePreset"];
    public string StrStatus { get; } = Localizer["RiskProfileStatus"];
    public string StrEffective { get; } = Localizer["RiskAdminEffective"];
    public string StrAuthor { get; } = Localizer["RiskProfileCreatedBy"];
    public string StrApprover { get; } = Localizer["RiskProfileApprovedBy"];
    public string StrRebaseline { get; } = Localizer["RiskAdminRebaseline"];
    public string StrCommittee { get; } = Localizer["RiskProfileCommitteeRef"];
    public string StrDetail { get; } = Localizer["RiskAdminDetail"];
    public string StrJustification { get; } = Localizer["RiskAdminJustification"];
    public string StrBoardRef { get; } = Localizer["RiskAdminBoardRef"];
    public string StrRejectedReason { get; } = Localizer["RiskAdminRejectedReason"];
    public string StrRetireJustification { get; } = Localizer["RiskAdminRetireJustification"];
    public string StrGrantSource { get; } = Localizer["RiskAdminGrantSource"];
    public string StrEvidence { get; } = Localizer["RiskAdminEvidence"];
    public string StrHistory { get; } = Localizer["RiskAdminHistory"];
    public string StrParameters { get; } = Localizer["RiskProfileParameters"];
    public string StrGroup { get; } = Localizer["RiskProfileParameterGroup"];
    public string StrParameter { get; } = Localizer["RiskProfileParameterKey"];
    public string StrCompare { get; } = Localizer["RiskAdminCompare"];
    public string StrCompareA { get; } = Localizer["RiskAdminCompareA"];
    public string StrCompareB { get; } = Localizer["RiskAdminCompareB"];
    public string StrOnlyChanged { get; } = Localizer["RiskAdminOnlyChanged"];
    public string StrNewDraft { get; } = Localizer["RiskAdminNewDraft"];
    public string StrOrigin { get; } = Localizer["RiskAdminDraftOrigin"];
    public string StrCreate { get; } = Localizer["RiskAdminCreateDraft"];
    public string StrOpenDraft { get; } = Localizer["RiskAdminOpenDraft"];
    public string StrRefresh { get; } = Localizer["Reload"];
    public string StrDeleteDraft { get; } = Localizer["RiskAdminDeleteDraft"];
    public string StrWhen { get; } = Localizer["RiskAdminWhen"];
    public string StrActor { get; } = Localizer["RiskAdminActor"];
    public string StrField { get; } = Localizer["RiskAdminField"];
    public string StrChange { get; } = Localizer["RiskAdminChange"];
    public string StrNoDifferences { get; } = Localizer["RiskProfileNoDifferences"];
    public string StrFileName { get; } = Localizer["RiskAdminFileName"];

    public IReadOnlyList<string> OriginOptions { get; } =
    [
        Localizer["RiskAdminOriginActive"], Localizer["RiskAdminOriginPreset"], Localizer["RiskAdminOriginCopy"]
    ];

    #endregion

    private readonly RiskProfileDraftSession _session;
    private readonly IRiskIndexService _readService;
    private readonly IRiskIndexConfigService _configService;
    private readonly Action _openDraftTab;

    public RiskProfilesViewModel(RiskProfileDraftSession session, IRiskIndexService readService,
        IRiskIndexConfigService configService, Action openDraftTab)
    {
        _session = session;
        _readService = readService;
        _configService = configService;
        _openDraftTab = openDraftTab;
        _session.SessionChanged += reloaded => { if (reloaded) Refresh(); else RaiseGates(); };
        _session.ProfilesChanged += () => _ = LoadDetailAsync(SelectedProfile);

        this.WhenAnyValue(vm => vm.SelectedProfile).Subscribe(row =>
        {
            _ = LoadDetailAsync(row);
            RaiseGates();
        });
        this.WhenAnyValue(vm => vm.CompareA, vm => vm.CompareB, vm => vm.OnlyChanged)
            .Subscribe(_ => { var pending = CompareAsync(); });
        this.WhenAnyValue(vm => vm.NewDraftName, vm => vm.NewDraftOrigin, vm => vm.NewDraftPreset, vm => vm.NewDraftSource)
            .Subscribe(_ => RaiseGates());
    }

    #region PROPERTIES

    public ObservableCollection<RiskProfileListRow> Profiles { get; } = [];

    private RiskProfileListRow? _selectedProfile;

    public RiskProfileListRow? SelectedProfile
    {
        get => _selectedProfile;
        set => this.RaiseAndSetIfChanged(ref _selectedProfile, value);
    }

    private RiskIndexProfileDto? _detail;

    public RiskIndexProfileDto? Detail
    {
        get => _detail;
        private set
        {
            this.RaiseAndSetIfChanged(ref _detail, value);
            this.RaisePropertyChanged(nameof(HasDetail));
        }
    }

    public bool HasDetail => Detail is not null;

    public string DetailTitle => Detail is null ? "" : RiskAdminText.Format("RiskAdminDetailFMT", Detail.Version, Detail.Name);
    public string DetailJustification => RiskAdminText.Dash(Detail?.Justification);
    public string DetailCommittee => RiskAdminText.Dash(Detail?.CommitteeDecisionRef);
    public string DetailBoard => RiskAdminText.Dash(Detail?.BoardApprovalRef);
    public string DetailRejected => RiskAdminText.Dash(Detail?.RejectedReason);
    public string DetailRetire => Detail?.RetireJustification is null
        ? "—"
        : Detail.RetireJustification + " (" + RiskAdminText.Dash(Detail.RetireDecisionRef) + ")";
    public string DetailGrantSource => RiskAdminText.Dash(Detail?.ApprovalGrantSource);

    public ObservableCollection<RiskProfileDiffRow> DetailParameters { get; } = [];

    public ObservableCollection<FileListing> DetailEvidence { get; } = [];

    public ObservableCollection<RiskProfileHistoryRow> DetailHistory { get; } = [];

    private string? _historyMessage;

    /// <summary>Why the trail is not shown (it needs unrestricted scope, S40 §6.1), or null.</summary>
    public string? HistoryMessage
    {
        get => _historyMessage;
        private set => this.RaiseAndSetIfChanged(ref _historyMessage, value);
    }

    private RiskProfileListRow? _compareA;

    public RiskProfileListRow? CompareA
    {
        get => _compareA;
        set => this.RaiseAndSetIfChanged(ref _compareA, value);
    }

    private RiskProfileListRow? _compareB;

    public RiskProfileListRow? CompareB
    {
        get => _compareB;
        set => this.RaiseAndSetIfChanged(ref _compareB, value);
    }

    private bool _onlyChanged = true;

    public bool OnlyChanged
    {
        get => _onlyChanged;
        set => this.RaiseAndSetIfChanged(ref _onlyChanged, value);
    }

    public ObservableCollection<RiskProfileDiffRow> DiffRows { get; } = [];

    private bool _hasNoDifferences;

    public bool HasNoDifferences
    {
        get => _hasNoDifferences;
        private set => this.RaiseAndSetIfChanged(ref _hasNoDifferences, value);
    }

    private bool _isNewDraftFormOpen;

    public bool IsNewDraftFormOpen
    {
        get => _isNewDraftFormOpen;
        set => this.RaiseAndSetIfChanged(ref _isNewDraftFormOpen, value);
    }

    private string _newDraftName = "";

    public string NewDraftName
    {
        get => _newDraftName;
        set => this.RaiseAndSetIfChanged(ref _newDraftName, value);
    }

    private int _newDraftOrigin;

    /// <summary>Index into <see cref="OriginOptions"/>: the active profile, a preset or a copy of a version.</summary>
    public int NewDraftOrigin
    {
        get => _newDraftOrigin;
        set
        {
            this.RaiseAndSetIfChanged(ref _newDraftOrigin, value);
            this.RaisePropertyChanged(nameof(IsPresetOrigin));
            this.RaisePropertyChanged(nameof(IsCopyOrigin));
        }
    }

    public bool IsPresetOrigin => NewDraftOrigin == (int)RiskDraftOrigin.Preset;

    public bool IsCopyOrigin => NewDraftOrigin == (int)RiskDraftOrigin.Copy;

    public ObservableCollection<RiskPresetItem> PresetOptions { get; } = [];

    private RiskPresetItem? _newDraftPreset;

    public RiskPresetItem? NewDraftPreset
    {
        get => _newDraftPreset;
        set => this.RaiseAndSetIfChanged(ref _newDraftPreset, value);
    }

    private RiskProfileListRow? _newDraftSource;

    public RiskProfileListRow? NewDraftSource
    {
        get => _newDraftSource;
        set => this.RaiseAndSetIfChanged(ref _newDraftSource, value);
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

    private Gate NewDraftGate => ProfileWorkflowGates.NewDraft(_session.Rights.Configure,
        ProfileWorkflowGates.OpenDraft(_session.Profiles));

    public bool IsNewDraftEnabled => NewDraftGate.Enabled;
    public string StrNewDraftReason => RiskAdminText.Reason(NewDraftGate);

    private Gate CreateGate
    {
        get
        {
            var gate = NewDraftGate;
            if (!gate.Enabled) return gate;
            if (string.IsNullOrWhiteSpace(NewDraftName)) return Gate.Closed("RiskAdminNameRequired");
            // Without an active profile the first draft starts from a preset (Stage A1, S39 §12.3).
            if (_session.Active is null && NewDraftOrigin == (int)RiskDraftOrigin.Active)
                return Gate.Closed("RiskAdminPresetRequiredFirst");
            if (IsPresetOrigin && NewDraftPreset is null) return Gate.Closed("RiskAdminChoosePreset");
            if (IsCopyOrigin && NewDraftSource is null) return Gate.Closed("RiskAdminChooseVersion");
            return Gate.Open;
        }
    }

    public bool IsCreateEnabled => CreateGate.Enabled;
    public string StrCreateReason => RiskAdminText.Reason(CreateGate);

    private Gate DeleteGate => ProfileWorkflowGates.DeleteDraft(_session.Rights.Configure, SelectedProfile?.Summary);
    public bool IsDeleteDraftEnabled => DeleteGate.Enabled;
    public string StrDeleteDraftReason => RiskAdminText.Reason(DeleteGate);

    public bool IsOpenDraftEnabled => SelectedProfile?.Summary.Status is RiskIndexProfileStatus.Draft
        or RiskIndexProfileStatus.PendingApproval;
    public string StrOpenDraftReason => IsOpenDraftEnabled ? "" : RiskAdminText.Text("RiskAdminSelectDraft");

    public bool IsRefreshEnabled => !IsBusy;

    #endregion

    #region COMMANDS

    public void BtNewDraftClicked() => IsNewDraftFormOpen = !IsNewDraftFormOpen;

    public async Task BtCreateDraftClicked()
    {
        if (!CreateGate.Enabled) return;
        var request = (RiskDraftOrigin)NewDraftOrigin switch
        {
            RiskDraftOrigin.Preset => new RiskIndexProfileCreateRequest(NewDraftName.Trim(), null, NewDraftPreset!.Key),
            RiskDraftOrigin.Copy => new RiskIndexProfileCreateRequest(NewDraftName.Trim(), NewDraftSource!.Id, null),
            _ => new RiskIndexProfileCreateRequest(NewDraftName.Trim(), _session.Active?.Id, null)
        };

        var created = false;
        await RunRiskAsync(Localizer["RiskAdminDraftCreatedMSG"], async () =>
        {
            await _configService.CreateDraftAsync(request);
            created = true;
        });
        if (!created) return;
        IsNewDraftFormOpen = false;
        NewDraftName = "";
        await _session.RefreshAfterLifecycleAsync();
        _openDraftTab();
    }

    public void BtOpenDraftClicked()
    {
        if (IsOpenDraftEnabled) _openDraftTab();
    }

    public Task BtRefreshClicked() => _session.RefreshAfterLifecycleAsync();

    public async Task BtDeleteDraftClicked()
    {
        if (!DeleteGate.Enabled || SelectedProfile is not { } row) return;
        if (!await ConfirmationDialog.ConfirmDeleteAsync(row.Version + " · " + row.Name)) return;

        var deleted = false;
        await RunRiskAsync(string.Format(Localizer["RiskAdminDraftDeletedMSG"], row.Version), async () =>
        {
            await _configService.DeleteDraftAsync(row.Id);
            deleted = true;
        });
        if (deleted) await _session.RefreshAfterLifecycleAsync();
    }

    #endregion

    #region METHODS

    /// <summary>Rebuilds the list from the session, keeping the selection and the compare choices by id.</summary>
    private void Refresh()
    {
        var selected = SelectedProfile?.Id;
        var a = CompareA?.Id;
        var b = CompareB?.Id;

        Profiles.Clear();
        foreach (var profile in _session.Profiles.OrderByDescending(p => p.Version)) Profiles.Add(new RiskProfileListRow(profile));

        if (PresetOptions.Count == 0)
            foreach (var preset in _session.Presets.Where(p => p.Activatable))
                PresetOptions.Add(new RiskPresetItem(preset.Key, RiskAdminText.Text(preset.NameKey)));

        SelectedProfile = Profiles.FirstOrDefault(p => p.Id == selected) ?? Profiles.FirstOrDefault();
        CompareA = Profiles.FirstOrDefault(p => p.Id == a) ?? Profiles.Skip(1).FirstOrDefault();
        CompareB = Profiles.FirstOrDefault(p => p.Id == b) ?? Profiles.FirstOrDefault();
        if (_session.Active is null && NewDraftOrigin == (int)RiskDraftOrigin.Active) NewDraftOrigin = (int)RiskDraftOrigin.Preset;
        ErrorMessage = _session.LoadError;
        RaiseGates();
    }

    private void RaiseGates()
    {
        foreach (var name in new[]
                 {
                     nameof(IsNewDraftEnabled), nameof(StrNewDraftReason), nameof(IsCreateEnabled), nameof(StrCreateReason),
                     nameof(IsDeleteDraftEnabled), nameof(StrDeleteDraftReason), nameof(IsOpenDraftEnabled),
                     nameof(StrOpenDraftReason)
                 })
            this.RaisePropertyChanged(name);
    }

    private async Task LoadDetailAsync(RiskProfileListRow? row)
    {
        DetailParameters.Clear();
        DetailEvidence.Clear();
        DetailHistory.Clear();
        HistoryMessage = null;
        Detail = null;
        RaiseDetail();
        if (row is null) return;

        try
        {
            var detail = await _readService.GetProfileAsync(row.Id);
            if (!ReferenceEquals(row, SelectedProfile)) return;
            Detail = detail;
            RaiseDetail();

            var all = await _readService.GetProfileDiffAsync(row.Id, row.Id);
            foreach (var change in all.Changes)
                DetailParameters.Add(DiffRow(change, change.ValueA, null));

            foreach (var file in await _configService.GetEvidenceAsync(row.Id)) DetailEvidence.Add(file);
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error reading risk index profile {Id}", row.Id);
            ErrorMessage = RiskAdminText.Explain(ex);
            return;
        }

        try
        {
            foreach (var entry in await _configService.GetHistoryAsync(row.Id, 200))
                DetailHistory.Add(new RiskProfileHistoryRow(RiskAdminText.Moment(entry.OccurredAt), entry.Actor, entry.Field,
                    RiskAdminText.Dash(entry.OldValue) + " → " + RiskAdminText.Dash(entry.NewValue)));
        }
        catch (Exception ex)
        {
            // The trail carries values of other units: it needs unrestricted scope (S40 §6.1), and saying so beats a blank.
            HistoryMessage = RiskAdminText.Explain(ex);
        }
    }

    private void RaiseDetail()
    {
        foreach (var name in new[]
                 {
                     nameof(DetailTitle), nameof(DetailJustification), nameof(DetailCommittee), nameof(DetailBoard),
                     nameof(DetailRejected), nameof(DetailRetire), nameof(DetailGrantSource)
                 })
            this.RaisePropertyChanged(name);
    }

    private async Task CompareAsync()
    {
        DiffRows.Clear();
        HasNoDifferences = false;
        if (CompareA is not { } a || CompareB is not { } b || a.Id == b.Id) return;

        try
        {
            var diff = await _readService.GetProfileDiffAsync(a.Id, b.Id);
            if (!ReferenceEquals(a, CompareA) || !ReferenceEquals(b, CompareB)) return;
            DiffRows.Clear();
            foreach (var change in diff.Changes.Where(c => c.Changed || !OnlyChanged))
                DiffRows.Add(DiffRow(change, change.ValueA, change.ValueB));
            HasNoDifferences = DiffRows.Count == 0;
        }
        catch (Exception ex)
        {
            Logger.Error(ex, "Error comparing risk index profiles {A} and {B}", a.Id, b.Id);
            ErrorMessage = RiskAdminText.Explain(ex);
        }
    }

    internal static RiskProfileDiffRow DiffRow(RiskIndexParameterChangeDto change, string? a, string? b) =>
        new(RiskAdminText.Text(ProfileParameterRows.GroupLabelKey(change.Group)),
            ProfileIssueText.Label(change.Key, RiskAdminText.Optional) ?? change.Key, change.Key, a ?? "—", b ?? "—",
            change.Changed);

    #endregion
}

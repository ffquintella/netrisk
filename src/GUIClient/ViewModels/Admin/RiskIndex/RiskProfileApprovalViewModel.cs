using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools;
using GUIClient.Tools.RiskIndex;
using Model.DTO;
using Model.File;
using Model.RiskIndex;
using ReactiveUI;

namespace GUIClient.ViewModels.Admin.RiskIndex;

/// <summary>A segregation-of-duties check as the server computed it (S40 §6.4): passed or not, and its sentence.</summary>
public sealed record RiskApprovalCheckRow(string Glyph, string Message, bool Passed);

/// <summary>A node of the last activation's bridge (S39 §9.5): the value under the old and the new version.</summary>
public sealed record RiskBridgeRow(string Node, string Category, string Old, string New, string Delta);

/// <summary>
/// The approval tab (S40 §6.4; T262): the profile awaiting approval with its differences and justification, the server's
/// segregation-of-duties checks (<c>GET …/ApprovalEligibility</c>, the same method <c>Approve</c> refuses with), the
/// committee reference and minutes, the Board reference when the bands change or there is no active profile, the explicit
/// rebaseline confirmation, approve and reject; the last activation's bridge; and publication — publish or suspend.
/// </summary>
public class RiskProfileApprovalViewModel : RiskAdminViewModelBase
{
    #region LANGUAGE

    public string StrSummary { get; } = Localizer["RiskAdminSummary"];
    public string StrDifferences { get; } = Localizer["RiskAdminDifferencesFromActive"];
    public string StrJustification { get; } = Localizer["RiskAdminJustification"];
    public string StrSegregation { get; } = Localizer["RiskAdminSegregation"];
    public string StrSegregationForApprover { get; } = Localizer["RiskAdminSegregationForApprover"];
    public string StrReferences { get; } = Localizer["RiskAdminReferences"];
    public string StrCommitteeRef { get; } = Localizer["RiskAdminCommitteeDecision"];
    public string StrBoardRef { get; } = Localizer["RiskAdminBoardRef"];
    public string StrEvidence { get; } = Localizer["RiskAdminEvidence"];
    public string StrAttach { get; } = Localizer["RiskAdminAttachMinutes"];
    public string StrConfirmRebaseline { get; } = Localizer["RiskAdminConfirmRebaselineLabel"];
    public string StrApprove { get; } = Localizer["RiskAdminApprove"];
    public string StrReject { get; } = Localizer["RiskAdminReject"];
    public string StrRejectReason { get; } = Localizer["RiskAdminRejectReason"];
    public string StrLastActivation { get; } = Localizer["RiskAdminLastActivation"];
    public string StrPublication { get; } = Localizer["RiskAdminPublication"];
    public string StrPublish { get; } = Localizer["RiskAdminPublish"];
    public string StrSuspend { get; } = Localizer["RiskAdminSuspend"];
    public string StrSuspendJustification { get; } = Localizer["RiskAdminSuspendJustification"];
    public string StrSuspendCommitteeRef { get; } = Localizer["RiskAdminSuspendCommitteeRef"];
    public string StrNoPending { get; } = Localizer["RiskAdminNoPendingMSG"];
    public string StrParameter { get; } = Localizer["RiskProfileParameterKey"];
    public string StrGroup { get; } = Localizer["RiskProfileParameterGroup"];
    public string StrNode { get; } = Localizer["RiskAdminNode"];
    public string StrCategory { get; } = Localizer["RiskAdminCategory"];
    public string StrOld { get; } = Localizer["RiskAdminOldValue"];
    public string StrNew { get; } = Localizer["RiskAdminNewValue"];
    public string StrDelta { get; } = Localizer["RiskAdminDelta"];
    public string StrFileName { get; } = Localizer["RiskAdminFileName"];
    public string StrRefresh { get; } = Localizer["Reload"];

    #endregion

    private readonly RiskProfileDraftSession _session;
    private readonly IRiskIndexService _readService;
    private readonly IRiskIndexConfigService _configService;
    private readonly IFilesService _filesService;

    public RiskProfileApprovalViewModel(RiskProfileDraftSession session, IRiskIndexService readService,
        IRiskIndexConfigService configService, IFilesService filesService)
    {
        _session = session;
        _readService = readService;
        _configService = configService;
        _filesService = filesService;
        _session.SessionChanged += reloaded =>
        {
            if (reloaded) _ = LoadAsync();
        };
        this.WhenAnyValue(vm => vm.CommitteeRef, vm => vm.BoardRef, vm => vm.ConfirmRebaseline, vm => vm.RejectReason)
            .Subscribe(_ => RaiseGates());
        this.WhenAnyValue(vm => vm.SuspendJustification, vm => vm.SuspendCommitteeRef).Subscribe(_ => RaiseGates());
    }

    #region PROPERTIES

    public RiskIndexProfileDto? Pending => _session.Draft is { Status: RiskIndexProfileStatus.PendingApproval } p ? p : null;

    public bool HasPending => Pending is not null;

    public bool CanApprove => _session.Rights.Approve;

    public bool ShowChecks => CanApprove && HasPending;

    public bool ShowChecksNote => !CanApprove && HasPending;

    public string PendingHeader => Pending is { } p
        ? RiskAdminText.Format("RiskAdminPendingHeaderFMT", p.Version, p.Name, RiskAdminText.Dash(p.CreatedBy),
            RiskAdminText.Moment(p.SubmittedAtUtc))
        : "";

    public string PendingJustification => RiskAdminText.Dash(Pending?.Justification);

    public ObservableCollection<RiskProfileDiffRow> Differences { get; } = [];

    private RiskIndexApprovalEligibilityDto? _eligibility;

    public ObservableCollection<RiskApprovalCheckRow> Checks { get; } = [];

    public string GrantSourceText => _eligibility?.GrantSource is { } source
        ? RiskAdminText.Format("RiskAdminGrantSourceFMT", source)
        : "";

    public bool RequiresBoardRef => _eligibility?.RequiresBoardRef ?? Pending is not null && _session.Active is null;

    public string BoardHint => RiskAdminText.Text(RequiresBoardRef ? "RiskAdminBoardRequiredHint" : "RiskAdminBoardOptionalHint");

    public string RebaselineTriggersText => _eligibility is { RebaselineTriggers.Count: > 0 } e
        ? string.Join(" · ", e.RebaselineTriggers.Select(t => RiskAdminText.Text("RiskRebaseline_" + t)))
        : RiskAdminText.Text("RiskAdminNoRebaselineTrigger");

    public bool HasRebaselineTriggers => _eligibility?.RebaselineTriggers.Count > 0;

    /// <summary>"Effective from the 05:00 UTC snapshot of ‹day›" — the next snapshot instant (S40 §6.4 "Ativação").</summary>
    public string EffectiveNote
    {
        get
        {
            var now = DateTime.UtcNow;
            var next = new DateTime(now.Year, now.Month, now.Day, 5, 0, 0, DateTimeKind.Utc);
            if (next <= now) next = next.AddDays(1);
            return RiskAdminText.Format("RiskAdminEffectiveNoteFMT", next.ToString("d", CultureInfo.CurrentCulture));
        }
    }

    private string? _committeeRef;

    public string? CommitteeRef
    {
        get => _committeeRef;
        set => this.RaiseAndSetIfChanged(ref _committeeRef, value);
    }

    private string? _boardRef;

    public string? BoardRef
    {
        get => _boardRef;
        set => this.RaiseAndSetIfChanged(ref _boardRef, value);
    }

    private bool _confirmRebaseline;

    public bool ConfirmRebaseline
    {
        get => _confirmRebaseline;
        set => this.RaiseAndSetIfChanged(ref _confirmRebaseline, value);
    }

    private string? _rejectReason;

    public string? RejectReason
    {
        get => _rejectReason;
        set => this.RaiseAndSetIfChanged(ref _rejectReason, value);
    }

    public ObservableCollection<FileListing> Evidence { get; } = [];

    public ObservableCollection<RiskBridgeRow> Bridge { get; } = [];

    private string _bridgeMessage = "";

    /// <summary>Why the bridge is not shown: none yet, or it needs unrestricted scope (S40 §6.4).</summary>
    public string BridgeMessage
    {
        get => _bridgeMessage;
        private set => this.RaiseAndSetIfChanged(ref _bridgeMessage, value);
    }

    private RiskIndexPublicationDto? _publication;

    public string PublicationText => _publication is { } p
        ? RiskAdminText.Format("RiskAdminPublicationStateFMT", RiskAdminText.Text("RiskPublication_" + p.State),
            RiskAdminText.Moment(p.ChangedAtUtc), RiskAdminText.Dash(p.ChangedBy))
        : "—";

    public string ActiveText => _session.Active is { } a
        ? RiskAdminText.Format("RiskAdminActiveProfileFMT", a.Version, a.Name, RiskAdminText.Day(a.EffectiveFrom))
        : RiskAdminText.Text("RiskAdminNoActiveProfile");

    private string? _suspendJustification;

    public string? SuspendJustification
    {
        get => _suspendJustification;
        set => this.RaiseAndSetIfChanged(ref _suspendJustification, value);
    }

    private string? _suspendCommitteeRef;

    public string? SuspendCommitteeRef
    {
        get => _suspendCommitteeRef;
        set => this.RaiseAndSetIfChanged(ref _suspendCommitteeRef, value);
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

    private Gate ApproveGate => ProfileWorkflowGates.Approve(CanApprove, Pending?.Status, _eligibility, CommitteeRef, Evidence.Count,
        BoardRef, ConfirmRebaseline);

    public bool IsApproveEnabled => ApproveGate.Enabled;
    public string StrApproveReason => RiskAdminText.Reason(ApproveGate);

    private Gate RejectGate => ProfileWorkflowGates.Reject(CanApprove, Pending?.Status, RejectReason);
    public bool IsRejectEnabled => RejectGate.Enabled;
    public string StrRejectReasonTip => RiskAdminText.Reason(RejectGate);

    private Gate AttachGate => ProfileWorkflowGates.AttachEvidence(CanApprove, Pending?.Status);
    public bool IsAttachEnabled => AttachGate.Enabled;
    public string StrAttachReason => RiskAdminText.Reason(AttachGate);

    private Gate PublishGate => ProfileWorkflowGates.Publish(CanApprove, _publication?.State, _session.Active);
    public bool IsPublishEnabled => PublishGate.Enabled;
    public string StrPublishReason => RiskAdminText.Reason(PublishGate);

    private Gate SuspendGate => ProfileWorkflowGates.Suspend(CanApprove, _session.Active, SuspendJustification, SuspendCommitteeRef);
    public bool IsSuspendEnabled => SuspendGate.Enabled;
    public string StrSuspendReason => RiskAdminText.Reason(SuspendGate);

    #endregion

    #region COMMANDS

    public async Task BtApproveClicked()
    {
        if (!ApproveGate.Enabled || Pending is not { } pending) return;
        var approved = false;
        await RunRiskAsync(string.Format(Localizer["RiskAdminApprovedMSG"], pending.Version), async () =>
        {
            await _configService.ApproveAsync(pending.Id, new RiskIndexApproveRequest(CommitteeRef!.Trim(),
                string.IsNullOrWhiteSpace(BoardRef) ? null : BoardRef.Trim(), ConfirmRebaseline, null));
            approved = true;
        });
        if (!approved)
        {
            // A refusal may come from a check that changed since the tab opened: show the server's current view.
            await LoadEligibilityAsync();
            return;
        }

        ClearInputs();
        await _session.RefreshAfterLifecycleAsync();
    }

    public async Task BtRejectClicked()
    {
        if (!RejectGate.Enabled || Pending is not { } pending) return;
        if (!await ConfirmationDialog.ConfirmAsync(Localizer["Warning"],
                string.Format(Localizer["RiskAdminRejectConfirmMSG"], pending.Version)))
            return;
        var rejected = false;
        await RunRiskAsync(string.Format(Localizer["RiskAdminRejectedMSG"], pending.Version), async () =>
        {
            await _configService.RejectAsync(pending.Id, new RiskIndexRejectRequest(RejectReason!.Trim()));
            rejected = true;
        });
        if (!rejected) return;
        ClearInputs();
        await _session.RefreshAfterLifecycleAsync();
    }

    /// <summary>Uploads the minutes as the caller's own parentless file and links it through the profile route (S40 §8.4).</summary>
    public async Task BtAttachEvidenceClicked()
    {
        if (!AttachGate.Enabled || Pending is not { } pending) return;
        var storage = StorageProviderAccessor.Current;
        if (storage is null) return;
        var picked = await storage.OpenFilePickerAsync(new FilePickerOpenOptions { Title = StrAttach, AllowMultiple = false });
        if (picked.Count == 0) return;
        var userId = AuthenticationService.AuthenticatedUserInfo?.UserId;
        if (userId is null) return;

        await RunRiskAsync(Localizer["RiskAdminMinutesAttachedMSG"], async () =>
        {
            var uploaded = await _filesService.UploadFileAsync(picked[0].Path, 0, userId.Value,
                FileCollectionType.RiskIndexProfileEvidence);
            if (uploaded.Id is not { } fileId)
                throw new InvalidOperationException("The upload did not return the file id.");
            await _configService.AttachEvidenceAsync(pending.Id, fileId);
        });
        await LoadEvidenceAsync(pending.Id);
        await LoadEligibilityAsync();
    }

    public async Task BtPublishClicked()
    {
        if (!PublishGate.Enabled) return;
        if (!await ConfirmationDialog.ConfirmAsync(Localizer["Warning"], Localizer["RiskAdminPublishConfirmMSG"])) return;
        await RunRiskAsync(Localizer["RiskAdminPublishedMSG"], async () =>
            _publication = await _configService.SetPublicationAsync(RiskIndexPublicationState.Published));
        RaiseAll();
    }

    public async Task BtSuspendClicked()
    {
        if (!SuspendGate.Enabled || _session.Active is not { } active) return;
        if (!await ConfirmationDialog.ConfirmAsync(Localizer["Warning"],
                string.Format(Localizer["RiskAdminSuspendConfirmMSG"], active.Version)))
            return;
        var retired = false;
        await RunRiskAsync(Localizer["RiskAdminSuspendedMSG"], async () =>
        {
            await _configService.RetireAsync(active.Id, new RiskIndexRetireRequest(SuspendJustification!.Trim(),
                SuspendCommitteeRef!.Trim()));
            retired = true;
        });
        if (!retired) return;
        SuspendJustification = null;
        SuspendCommitteeRef = null;
        await _session.RefreshAfterLifecycleAsync();
    }

    public Task BtRefreshClicked() => _session.RefreshAfterLifecycleAsync();

    #endregion

    #region METHODS

    public async Task LoadAsync()
    {
        Differences.Clear();
        Evidence.Clear();
        Checks.Clear();
        Bridge.Clear();
        _eligibility = null;
        ErrorMessage = null;

        try
        {
            _publication = await _readService.GetPublicationAsync();
        }
        catch (Exception ex)
        {
            ErrorMessage = RiskAdminText.Explain(ex);
        }

        if (Pending is { } pending)
        {
            try
            {
                var against = _session.Active?.Id ?? pending.Id;
                var diff = await _readService.GetProfileDiffAsync(against, pending.Id);
                foreach (var change in diff.Changes.Where(c => c.Changed || against == pending.Id))
                    Differences.Add(RiskProfilesViewModel.DiffRow(change, change.ValueA, change.ValueB));
            }
            catch (Exception ex)
            {
                ErrorMessage = RiskAdminText.Explain(ex);
            }

            await LoadEvidenceAsync(pending.Id);
            await LoadEligibilityAsync();
        }

        await LoadBridgeAsync();
        RaiseAll();
    }

    private async Task LoadEvidenceAsync(int id)
    {
        Evidence.Clear();
        try
        {
            foreach (var file in await _configService.GetEvidenceAsync(id)) Evidence.Add(file);
        }
        catch (Exception ex)
        {
            ErrorMessage = RiskAdminText.Explain(ex);
        }

        RaiseGates();
    }

    /// <summary>Only the approver asks: a configure-only reader sees the note instead of a 403 (S40 §6.4).</summary>
    private async Task LoadEligibilityAsync()
    {
        Checks.Clear();
        _eligibility = null;
        if (!CanApprove || Pending is not { } pending)
        {
            RaiseAll();
            return;
        }

        try
        {
            _eligibility = await _configService.GetApprovalEligibilityAsync(pending.Id);
            foreach (var check in _eligibility.Checks)
                Checks.Add(new RiskApprovalCheckRow(check.Passed ? "✓" : "✕",
                    string.IsNullOrWhiteSpace(check.Message) ? RiskAdminText.Text("RiskApprovalCheck_" + check.Code) : check.Message,
                    check.Passed));
        }
        catch (Exception ex)
        {
            ErrorMessage = RiskAdminText.Explain(ex);
        }

        RaiseAll();
    }

    private async Task LoadBridgeAsync()
    {
        Bridge.Clear();
        if (_session.Active is not { } active)
        {
            BridgeMessage = RiskAdminText.Text("RiskAdminNoBridge");
            return;
        }

        try
        {
            var bridge = await _configService.GetBridgeAsync(active.Id);
            if (bridge is null)
            {
                BridgeMessage = RiskAdminText.Text("RiskAdminNoBridge");
                return;
            }

            var culture = CultureInfo.CurrentCulture;
            foreach (var node in bridge.Nodes)
                Bridge.Add(new RiskBridgeRow(node.Node.Label, node.Category.ToString(), node.Old.ToString("0.0", culture),
                    node.New.ToString("0.0", culture), (node.New - node.Old).ToString("+0.0;−0.0;0", culture)));
            BridgeMessage = RiskAdminText.Format(bridge.OverlayPartial ? "RiskAdminBridgePartialFMT" : "RiskAdminBridgeFMT",
                bridge.FromVersion, bridge.ToVersion, bridge.BridgeDate.ToString("d", culture));
        }
        catch (Exception ex)
        {
            BridgeMessage = RiskAdminText.Explain(ex);
        }
    }

    private void ClearInputs()
    {
        CommitteeRef = null;
        BoardRef = null;
        ConfirmRebaseline = false;
        RejectReason = null;
    }

    private void RaiseAll()
    {
        foreach (var name in new[]
                 {
                     nameof(Pending), nameof(HasPending), nameof(CanApprove), nameof(ShowChecks), nameof(ShowChecksNote),
                     nameof(PendingHeader), nameof(PendingJustification), nameof(GrantSourceText), nameof(RequiresBoardRef),
                     nameof(BoardHint), nameof(RebaselineTriggersText), nameof(HasRebaselineTriggers), nameof(EffectiveNote),
                     nameof(PublicationText), nameof(ActiveText)
                 })
            this.RaisePropertyChanged(name);
        RaiseGates();
    }

    private void RaiseGates()
    {
        foreach (var name in new[]
                 {
                     nameof(IsApproveEnabled), nameof(StrApproveReason), nameof(IsRejectEnabled), nameof(StrRejectReasonTip),
                     nameof(IsAttachEnabled), nameof(StrAttachReason), nameof(IsPublishEnabled), nameof(StrPublishReason),
                     nameof(IsSuspendEnabled), nameof(StrSuspendReason)
                 })
            this.RaisePropertyChanged(name);
    }

    #endregion
}

using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools.Track9;
using Model.DecisionCycle;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Track9;

public sealed class CommitteeDecisionRow(RiskCommitteeDecisionDto item, string statusLabel) : ReactiveObject
{
    private RiskCommitteeDecisionDto _item = item;
    private string _statusLabel = statusLabel;
    public RiskCommitteeDecisionDto Item => _item;
    public int Id => Item.Id;
    public int CommitteeId => Item.CommitteeId;
    public int RiskId => Item.RiskId;
    public string RiskSubject => Item.RiskSubject;
    public string StatusLabel => _statusLabel;

    public void Update(RiskCommitteeDecisionDto value, string label)
    {
        _item = value;
        _statusLabel = label;
        foreach (var property in new[] { nameof(Item), nameof(Id), nameof(CommitteeId), nameof(RiskId),
                     nameof(RiskSubject), nameof(StatusLabel) })
            this.RaisePropertyChanged(property);
    }
}

/// <summary>Committee constitution, membership, submission, voting and withdrawal (T307).</summary>
public sealed class RiskCommitteesViewModel : ViewModelBase
{
    private readonly IDecisionCycleService _decisions = GetService<IDecisionCycleService>();
    private readonly IRiskGovernanceService _governance = GetService<IRiskGovernanceService>();
    private readonly IUsersService _users = GetService<IUsersService>();
    private readonly IEntitiesService _entities = GetService<IEntitiesService>();
    private readonly IRisksService _risks = GetService<IRisksService>();
    private readonly Track9MonitoringLoadGate _listGate = new();
    private readonly Track9MonitoringLoadGate _committeeGate = new();
    private readonly Track9MonitoringLoadGate _decisionGate = new();
    private readonly Track9MonitoringLoadGate _acceptanceGate = new();
    private bool _loaded;
    private string _loadError = string.Empty;
    private ObservableCollection<RiskCommitteeDto> _committees = [];
    private ObservableCollection<CommitteeDecisionRow> _committeeDecisions = [];
    private RiskCommitteeDto? _selectedCommittee;
    private CommitteeDecisionRow? _selectedDecision;
    private Track9LookupItem? _selectedDecisionRisk;

    public string StrTitle { get; } = Localizer["Track9RiskCommitteesTitle"];
    public string StrReload { get; } = Localizer["Reload"];
    public string StrNoItems { get; } = Localizer["Track9NoItems"];
    public string StrThirdLine { get; } = Localizer["Track9ReadOnlyThirdLine"];
    public string StrShowRetired { get; } = Localizer["Track9ShowRetired"];
    public string StrOpenOnly { get; } = Localizer["Track9OpenOnly"];
    public string StrCommittees { get; } = Localizer["Track9Committees"];
    public string StrDecisions { get; } = Localizer["Track9Decisions"];
    public string StrDefinition { get; } = Localizer["Track9CommitteeDefinition"];
    public string StrName { get; } = Localizer["Name"];
    public string StrMandate { get; } = Localizer["Track9Mandate"];
    public string StrEntityId { get; } = Localizer["Track9EntityId"];
    public string StrRequiredApprovals { get; } = Localizer["Track9RequiredApprovals"];
    public string StrCreate { get; } = Localizer["Create"];
    public string StrUpdate { get; } = Localizer["Update"];
    public string StrRetire { get; } = Localizer["Track9RetireCommittee"];
    public string StrMembers { get; } = Localizer["Members"];
    public string StrMemberId { get; } = Localizer["Track9MemberUserId"];
    public string StrAddMember { get; } = Localizer["Track9AddMember"];
    public string StrRemoveMember { get; } = Localizer["Track9RemoveMember"];
    public string StrSubmit { get; } = Localizer["Track9SubmitDecision"];
    public string StrKind { get; } = Localizer["Track9DecisionKind"];
    public string StrRiskId { get; } = Localizer["Track9RiskId"];
    public string StrRenewsId { get; } = Localizer["Track9RenewedAcceptanceId"];
    public string StrBusinessJustification { get; } = Localizer["Track9BusinessJustification"];
    public string StrControls { get; } = Localizer["Track9CompensatingControls"];
    public string StrExpires { get; } = Localizer["Track9ExpiresAt"];
    public string StrMinutes { get; } = Localizer["Track9MinutesReference"];
    public string StrVote { get; } = Localizer["Track9Vote"];
    public string StrVoteChoice { get; } = Localizer["Track9VoteChoice"];
    public string StrComment { get; } = Localizer["Track9Comment"];
    public string StrWithdraw { get; } = Localizer["Track9Withdraw"];
    public string StrReason { get; } = Localizer["Reason"];
    public string StrStatus { get; } = Localizer["Status"];

    public ObservableCollection<Track9Choice<RiskCommitteeDecisionKind>> DecisionKinds { get; } =
    [
        new(RiskCommitteeDecisionKind.Accept, Localizer["Track9DecisionAccept"]),
        new(RiskCommitteeDecisionKind.Renew, Localizer["Track9DecisionRenew"])
    ];

    public ObservableCollection<Track9Choice<RiskCommitteeVoteChoice>> VoteChoices { get; } =
    [
        new(RiskCommitteeVoteChoice.Approve, Localizer["Track9VoteApprove"]),
        new(RiskCommitteeVoteChoice.Reject, Localizer["Track9VoteReject"]),
        new(RiskCommitteeVoteChoice.Abstain, Localizer["Track9VoteAbstain"])
    ];
    public ObservableCollection<Track9LookupItem> UserChoices { get; } = [];
    public ObservableCollection<Track9LookupItem> EntityChoices { get; } = [];
    public ObservableCollection<Track9LookupItem> RiskChoices { get; } = [];
    public ObservableCollection<Track9LookupItem> AcceptanceChoices { get; } = [];

    public ObservableCollection<RiskCommitteeDto> Committees
    {
        get => _committees;
        private set
        {
            this.RaiseAndSetIfChanged(ref _committees, value);
            this.RaisePropertyChanged(nameof(IsEmpty));
        }
    }

    public ObservableCollection<CommitteeDecisionRow> CommitteeDecisions
    {
        get => _committeeDecisions;
        private set
        {
            this.RaiseAndSetIfChanged(ref _committeeDecisions, value);
            this.RaisePropertyChanged(nameof(IsEmpty));
        }
    }

    public RiskCommitteeDto? SelectedCommittee
    {
        get => _selectedCommittee;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedCommittee, value);
            foreach (var property in new[] { nameof(CanRetire), nameof(CanManageMembers), nameof(CanSubmit) })
                this.RaisePropertyChanged(property);
            if (value is null)
            {
                _committeeGate.DiscardOutstanding();
                ResetCommitteeEditor();
            }
            else
            {
                ResetCommitteeEditor();
                _ = LoadCommitteeAsync(value.Id);
            }
        }
    }

    public CommitteeDecisionRow? SelectedDecision
    {
        get => _selectedDecision;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedDecision, value);
            foreach (var property in new[] { nameof(CanVote), nameof(CanWithdraw) })
                this.RaisePropertyChanged(property);
            if (value is null)
                _decisionGate.DiscardOutstanding();
            else
                _ = LoadDecisionAsync(value.Id);
        }
    }

    public string LoadError
    {
        get => _loadError;
        private set
        {
            this.RaiseAndSetIfChanged(ref _loadError, value);
            this.RaisePropertyChanged(nameof(HasLoadError));
            this.RaisePropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IncludeRetired { get; set; }
    public bool OpenOnly { get; set; }
    public string CommitteeName { get; set; } = string.Empty;
    public string Mandate { get; set; } = string.Empty;
    public Track9LookupItem? SelectedEntity { get; set; }
    public decimal? RequiredApprovals { get; set; } = DecisionCycleLimits.MinRequiredApprovals;
    public Track9LookupItem? SelectedUserToAdd { get; set; }
    public RiskCommitteeMemberDto? SelectedMember { get; set; }
    public Track9Choice<RiskCommitteeDecisionKind>? DecisionKind { get; set; }
    public Track9LookupItem? SelectedDecisionRisk
    {
        get => _selectedDecisionRisk;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedDecisionRisk, value);
            SelectedAcceptanceToRenew = null;
            this.RaisePropertyChanged(nameof(SelectedAcceptanceToRenew));
            _ = LoadAcceptancesAsync(value?.Id);
        }
    }
    public Track9LookupItem? SelectedAcceptanceToRenew { get; set; }
    public string DecisionName { get; set; } = string.Empty;
    public string BusinessJustification { get; set; } = string.Empty;
    public string CompensatingControls { get; set; } = string.Empty;
    public DateTimeOffset? ExpiresAt { get; set; } = DateTimeOffset.Now.AddMonths(3);
    public string MinutesReference { get; set; } = string.Empty;
    public Track9Choice<RiskCommitteeVoteChoice>? VoteChoice { get; set; }
    public string VoteComment { get; set; } = string.Empty;
    public string WithdrawalReason { get; set; } = string.Empty;

    public bool IsEmpty => _loaded && Committees.Count == 0 && CommitteeDecisions.Count == 0 && !HasLoadError;
    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);
    public bool IsThirdLine => Track9MonitoringPermissions.IsThirdLine(AuthenticationService.AuthenticatedUserInfo);
    public bool CanDefine => Track9MonitoringPermissions.CanDefineKriOrCommittee(AuthenticationService.AuthenticatedUserInfo);
    public bool CanRetire => CanDefine && SelectedCommittee is { RetiredAt: null };
    public bool CanManageMembers => CanRetire;
    public bool CanSubmit => SelectedCommittee is { RetiredAt: null }
                             && Track9MonitoringPermissions.CanReviewRisk(AuthenticationService.AuthenticatedUserInfo);
    public bool CanWithdraw => SelectedDecision?.Item.Status == RiskCommitteeDecisionStatus.Open
                               && Track9MonitoringPermissions.CanReviewRisk(AuthenticationService.AuthenticatedUserInfo);
    public bool CanVote
    {
        get
        {
            if (SelectedDecision?.Item.Status != RiskCommitteeDecisionStatus.Open) return false;
            var committee = Committees.FirstOrDefault(x => x.Id == SelectedDecision.CommitteeId);
            var userId = AuthenticationService.AuthenticatedUserInfo?.UserId;
            var member = userId is not null && committee?.Members.Any(x => x.UserId == userId) == true;
            return Track9MonitoringPermissions.CanVote(AuthenticationService.AuthenticatedUserInfo, member);
        }
    }

    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtNewClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSaveClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRetireClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAddMemberClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRemoveMemberClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtSubmitClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtVoteClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtWithdrawClicked { get; }

    public RiskCommitteesViewModel()
    {
        DecisionKind = DecisionKinds[0];
        VoteChoice = VoteChoices[0];
        BtReloadClicked = ReactiveCommand.CreateFromTask(ReloadAsync);
        BtNewClicked = ReactiveCommand.Create(ClearCommitteeEditor);
        BtSaveClicked = ReactiveCommand.CreateFromTask(SaveCommitteeAsync);
        BtRetireClicked = ReactiveCommand.CreateFromTask(RetireCommitteeAsync);
        BtAddMemberClicked = ReactiveCommand.CreateFromTask(AddMemberAsync);
        BtRemoveMemberClicked = ReactiveCommand.CreateFromTask(RemoveMemberAsync);
        BtSubmitClicked = ReactiveCommand.CreateFromTask(SubmitAsync);
        BtVoteClicked = ReactiveCommand.CreateFromTask(VoteAsync);
        BtWithdrawClicked = ReactiveCommand.CreateFromTask(WithdrawAsync);
    }

    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : ReloadAsync();
    public Task ReloadAsync() => LoadAsync();

    private async Task LoadAsync()
    {
        var token = _listGate.Begin();
        try
        {
            await WithBusyAsync(async () =>
            {
                var committeeId = SelectedCommittee?.Id;
                var decisionId = SelectedDecision?.Id;
                var committeesTask = _decisions.GetCommitteesAsync(IncludeRetired);
                var decisionsTask = _decisions.GetCommitteeDecisionsAsync(committeeId, null, OpenOnly);
                var usersTask = _users.GetAllAsync();
                var entitiesTask = _entities.GetAllAsync();
                var risksTask = _risks.GetAllRisksAsync(true);
                await Task.WhenAll(committeesTask, decisionsTask, usersTask, entitiesTask, risksTask);
                if (!_listGate.IsCurrent(token)) return;
                Committees = new ObservableCollection<RiskCommitteeDto>(await committeesTask);
                CommitteeDecisions = new ObservableCollection<CommitteeDecisionRow>((await decisionsTask)
                    .Select(x => new CommitteeDecisionRow(x, Localizer[DecisionStatusKey(x.Status)])));
                Replace(UserChoices, (await usersTask).Select(x => new Track9LookupItem(x.Id, x.Name)));
                Replace(EntityChoices, [new Track9LookupItem(null, Localizer["Track9OrganizationWide"]),
                    .. (await entitiesTask).Select(x => new Track9LookupItem(x.Id, x.DisplayName))]);
                Replace(RiskChoices, (await risksTask).Select(x => new Track9LookupItem(x.Id, x.Subject)));
                LoadError = string.Empty;
                _loaded = true;
                SelectedCommittee = committeeId is null ? null : Committees.FirstOrDefault(x => x.Id == committeeId);
                SelectedDecision = decisionId is null ? null : CommitteeDecisions.FirstOrDefault(x => x.Id == decisionId);
                this.RaisePropertyChanged(nameof(IsEmpty));
                this.RaisePropertyChanged(nameof(CanVote));
            });
        }
        catch (Exception ex)
        {
            if (!_listGate.IsCurrent(token)) return;
            Logger.Error(ex, "Error loading the Track 9 risk committees");
            LoadError = ExplainError(ex);
        }
    }

    private async Task LoadCommitteeAsync(int? id)
    {
        var token = _committeeGate.Begin();
        if (id is null) return;
        try
        {
            var detail = await _decisions.GetCommitteeAsync(id.Value);
            if (!_committeeGate.IsCurrent(token)) return;
            var selected = Committees.FirstOrDefault(x => x.Id == detail.Id);
            if (selected is null) return;
            selected.Name = detail.Name;
            selected.Mandate = detail.Mandate;
            selected.EntityId = detail.EntityId;
            selected.RequiredApprovals = detail.RequiredApprovals;
            selected.RetiredAt = detail.RetiredAt;
            selected.UpdatedAt = detail.UpdatedAt;
            selected.UpdatedById = detail.UpdatedById;
            selected.Members = detail.Members;
            _selectedCommittee = selected;
            this.RaisePropertyChanged(nameof(SelectedCommittee));
            PopulateCommittee(detail);
            this.RaisePropertyChanged(nameof(CanVote));
        }
        catch (Exception ex)
        {
            if (!_committeeGate.IsCurrent(token)) return;
            Logger.Error(ex, "Error loading risk committee {CommitteeId}", id);
            LoadError = ExplainError(ex);
        }
    }

    private async Task LoadDecisionAsync(int? id)
    {
        var token = _decisionGate.Begin();
        if (id is null) return;
        try
        {
            var detail = await _decisions.GetCommitteeDecisionAsync(id.Value);
            if (!_decisionGate.IsCurrent(token)) return;
            var row = CommitteeDecisions.FirstOrDefault(x => x.Id == detail.Id);
            if (row is null) return;
            row.Update(detail, Localizer[DecisionStatusKey(detail.Status)]);
            _selectedDecision = row;
            this.RaisePropertyChanged(nameof(SelectedDecision));
            this.RaisePropertyChanged(nameof(CanVote));
            this.RaisePropertyChanged(nameof(CanWithdraw));
        }
        catch (Exception ex)
        {
            if (!_decisionGate.IsCurrent(token)) return;
            Logger.Error(ex, "Error loading committee decision {DecisionId}", id);
            LoadError = ExplainError(ex);
        }
    }

    private async Task LoadAcceptancesAsync(int? riskId)
    {
        var token = _acceptanceGate.Begin();
        AcceptanceChoices.Clear();
        if (riskId is null) return;

        try
        {
            var acceptances = await _governance.GetAcceptancesAsync(riskId.Value);
            if (!_acceptanceGate.IsCurrent(token) || SelectedDecisionRisk?.Id != riskId) return;
            Replace(AcceptanceChoices, acceptances.Select(x =>
                new Track9LookupItem(x.Id, $"{x.Name} (#{x.Id})")));
            LoadError = string.Empty;
        }
        catch (Exception ex)
        {
            if (!_acceptanceGate.IsCurrent(token) || SelectedDecisionRisk?.Id != riskId) return;
            Logger.Error(ex, "Error loading acceptances for committee risk {RiskId}", riskId);
            LoadError = ExplainError(ex);
        }
    }

    private void PopulateCommittee(RiskCommitteeDto committee)
    {
        CommitteeName = committee.Name;
        Mandate = committee.Mandate ?? string.Empty;
        SelectedEntity = EntityChoices.FirstOrDefault(x => x.Id == committee.EntityId);
        RequiredApprovals = committee.RequiredApprovals;
        foreach (var property in new[] { nameof(CommitteeName), nameof(Mandate), nameof(SelectedEntity),
                     nameof(RequiredApprovals), nameof(CanRetire), nameof(CanManageMembers), nameof(CanSubmit) })
            this.RaisePropertyChanged(property);
    }

    private void ClearCommitteeEditor()
    {
        if (!CanDefine) return;
        _committeeGate.DiscardOutstanding();
        _selectedCommittee = null;
        ResetCommitteeEditor();
        foreach (var property in new[] { nameof(SelectedCommittee), nameof(CanRetire),
                     nameof(CanManageMembers), nameof(CanSubmit) })
            this.RaisePropertyChanged(property);
    }

    private void ResetCommitteeEditor()
    {
        CommitteeName = Mandate = string.Empty;
        SelectedEntity = EntityChoices.FirstOrDefault(x => x.Id is null);
        RequiredApprovals = DecisionCycleLimits.MinRequiredApprovals;
        foreach (var property in new[] { nameof(CommitteeName), nameof(Mandate), nameof(SelectedEntity),
                     nameof(RequiredApprovals) })
            this.RaisePropertyChanged(property);
    }

    private RiskCommitteeRequest CommitteeRequest() => new()
    {
        Name = CommitteeName,
        Mandate = Mandate,
        EntityId = SelectedEntity?.Id,
        RequiredApprovals = RequiredApprovals is { } approvals ? decimal.ToInt32(approvals) : null
    };

    private Task SaveCommitteeAsync() => !CanDefine
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9CommitteeSaved"], async () =>
    {
        var saved = SelectedCommittee is null
            ? await _decisions.CreateCommitteeAsync(CommitteeRequest())
            : await _decisions.UpdateCommitteeAsync(SelectedCommittee.Id, CommitteeRequest());
        await ReloadAsync();
        SelectedCommittee = Committees.FirstOrDefault(x => x.Id == saved.Id);
    });

    private Task RetireCommitteeAsync() => !CanRetire || SelectedCommittee is null
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9CommitteeRetired"], async () =>
        {
            await _decisions.RetireCommitteeAsync(SelectedCommittee.Id);
            await ReloadAsync();
        });

    private Task AddMemberAsync() => !CanManageMembers || SelectedCommittee is null || SelectedUserToAdd?.Id is not { } user
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9MemberAdded"], async () =>
        {
            _selectedCommittee = await _decisions.AddCommitteeMemberAsync(SelectedCommittee.Id, user);
            this.RaisePropertyChanged(nameof(SelectedCommittee));
            await ReloadAsync();
        });

    private Task RemoveMemberAsync() => !CanManageMembers || SelectedCommittee is null || SelectedMember is null
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9MemberRemoved"], async () =>
        {
            await _decisions.RemoveCommitteeMemberAsync(SelectedCommittee.Id, SelectedMember.UserId);
            _selectedCommittee = await _decisions.GetCommitteeAsync(SelectedCommittee.Id);
            this.RaisePropertyChanged(nameof(SelectedCommittee));
            await ReloadAsync();
        });

    private Task SubmitAsync() => !CanSubmit || SelectedCommittee is null
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9DecisionSubmitted"], async () =>
        {
            await _decisions.OpenCommitteeDecisionAsync(SelectedCommittee.Id, new RiskCommitteeDecisionRequest
            {
                RiskId = SelectedDecisionRisk?.Id,
                Kind = DecisionKind?.Value,
                RenewsAcceptanceId = SelectedAcceptanceToRenew?.Id,
                Name = DecisionName,
                BusinessJustification = BusinessJustification,
                CompensatingControls = CompensatingControls,
                ExpiresAt = ExpiresAt?.UtcDateTime,
                MinutesReference = MinutesReference
            });
            await ReloadAsync();
        });

    private Task VoteAsync() => !CanVote || SelectedDecision is null
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9VoteRecorded"], async () =>
        {
            var detail = await _decisions.VoteAsync(SelectedDecision.Id,
                new RiskCommitteeVoteRequest { Choice = VoteChoice?.Value, Comment = VoteComment });
            _selectedDecision = new CommitteeDecisionRow(detail, Localizer[DecisionStatusKey(detail.Status)]);
            this.RaisePropertyChanged(nameof(SelectedDecision));
            await ReloadAsync();
        });

    private Task WithdrawAsync() => !CanWithdraw || SelectedDecision is null
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9DecisionWithdrawn"], async () =>
        {
            var detail = await _decisions.WithdrawCommitteeDecisionAsync(SelectedDecision.Id,
                new RiskCommitteeWithdrawRequest { Reason = WithdrawalReason });
            _selectedDecision = new CommitteeDecisionRow(detail, Localizer[DecisionStatusKey(detail.Status)]);
            this.RaisePropertyChanged(nameof(SelectedDecision));
            await ReloadAsync();
        });

    private static void Replace<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private static string DecisionStatusKey(RiskCommitteeDecisionStatus status) => status switch
    {
        RiskCommitteeDecisionStatus.Open => "Track9DecisionOpen",
        RiskCommitteeDecisionStatus.Approved => "Track9DecisionApproved",
        RiskCommitteeDecisionStatus.Rejected => "Track9DecisionRejected",
        RiskCommitteeDecisionStatus.Withdrawn => "Track9DecisionWithdrawnState",
        _ => "Track9DecisionOpen"
    };
}

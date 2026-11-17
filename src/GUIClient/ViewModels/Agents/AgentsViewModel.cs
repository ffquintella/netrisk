using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using GUIClient.Agents;
using GUIClient.Tools;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Agents;

/// <summary>One registered agent as the Agents window lists it.</summary>
public class AgentRow
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string Client { get; init; } = "";
    public string Fingerprint { get; init; } = "";
    public string CreatedAt { get; init; } = "";
    public string LastUsed { get; init; } = "";
    public string Lease { get; init; } = "";
    public bool HasLease { get; init; }
    public bool IsPaused { get; init; }
}

/// <summary>One data class in the Agents window's exclusion list (T362).</summary>
public class AgentDataClassRow : ReactiveObject
{
    private readonly Action _changed;

    public AgentDataClassRow(AgentDataClass dataClass, string label, bool isExcluded, bool isEditable, Action changed)
    {
        Class = dataClass;
        Label = label;
        _isExcluded = isExcluded;
        IsEditable = isEditable;
        _changed = changed;
    }

    public AgentDataClass Class { get; }
    public string Label { get; }
    public bool IsEditable { get; }

    private bool _isExcluded;
    public bool IsExcluded
    {
        get => _isExcluded;
        set
        {
            if (_isExcluded == value) return;
            this.RaiseAndSetIfChanged(ref _isExcluded, value);
            _changed();
        }
    }
}

/// <summary>
/// The Agents window (T347, T361, T362): the agent co-work settings — the off-by-default switch for the loopback server
/// with its LGPD disclosure (asked once before the first start), and the data classes withheld from agents (changed only
/// by a user holding the data-catalogue permission) — then the user's registered agents with rename and revoke, and the
/// active lease of the selected agent with pause, resume and end.
/// </summary>
public class AgentsViewModel : ViewModelBase
{
    public string StrTitle { get; } = Localizer["AgentsTitle"];
    public string StrServerSection { get; } = Localizer["AgentServerSection"];
    public string StrServerEnable { get; } = Localizer["AgentServerEnable"];
    public string StrServerHint { get; } = Localizer["AgentServerHintMSG"];
    public string StrRegistrationsSection { get; } = Localizer["AgentRegistrationsSection"];
    public string StrName { get; } = Localizer["AgentName"];
    public string StrClient { get; } = Localizer["AgentClient"];
    public string StrFingerprint { get; } = Localizer["AgentFingerprint"];
    public string StrCreatedAt { get; } = Localizer["AgentCreatedAt"];
    public string StrLastUsed { get; } = Localizer["AgentLastUsed"];
    public string StrLease { get; } = Localizer["AgentLease"];
    public string StrRename { get; } = Localizer["AgentRename"];
    public string StrRevoke { get; } = Localizer["AgentRevoke"];
    public string StrPause { get; } = Localizer["AgentPause"];
    public string StrResume { get; } = Localizer["AgentResume"];
    public string StrEndAccess { get; } = Localizer["AgentEndAccess"];
    public string StrNoneRegistered { get; } = Localizer["AgentNoneRegistered"];
    public string StrDataDisclosure { get; } = Localizer["AgentDataDisclosureMSG"];
    public string StrDataClassesSection { get; } = Localizer["AgentDataClassesSection"];
    public string StrDataClassesHint { get; } = Localizer["AgentDataClassesHintMSG"];
    public string StrDataClassesLocked { get; } = Localizer["AgentDataClassesLockedMSG"];
    public string StrDataClassesFloor { get; } = Localizer["AgentDataClassesFloorMSG"];
    public string StrDataClassesSave { get; } = Localizer["AgentDataClassesSave"];
    public string StrConnect { get; } = Localizer["AgentConnectButton"];

    /// <summary>M65: opens "Connect an agent", which registers the server in the AI clients on this computer.</summary>
    public ReactiveCommand<RxVoid, RxVoid> BtConnectClicked { get; } = ReactiveCommand.Create(() =>
        GetService<GUIClient.Navigation.INavigationService>()
            .ShowAuxiliaryWindow<GUIClient.Views.Agents.AgentConnectWindow>(() => new AgentConnectViewModel()));

    /// <summary>T362: one row per data class, checked when withheld from agents.</summary>
    public ObservableCollection<AgentDataClassRow> DataClasses { get; } = new();

    private bool _isPolicyLocked = true;
    /// <summary>True for a user without the data-catalogue permission: the list is shown, not editable.</summary>
    public bool IsPolicyLocked
    {
        get => _isPolicyLocked;
        set => this.RaiseAndSetIfChanged(ref _isPolicyLocked, value);
    }

    private bool _isSavePolicyEnabled;
    public bool IsSavePolicyEnabled
    {
        get => _isSavePolicyEnabled;
        set => this.RaiseAndSetIfChanged(ref _isSavePolicyEnabled, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> BtSavePolicyClicked { get; }

    private readonly AgentCoworkService _service;
    private readonly DispatcherTimer _clock;
    private bool _loading;

    public ObservableCollection<AgentRow> Agents { get; } = new();

    private AgentRow? _selectedAgent;
    public AgentRow? SelectedAgent
    {
        get => _selectedAgent;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedAgent, value);
            UpdateEnabledFlags();
        }
    }

    private bool _isServerEnabled;
    public bool IsServerEnabled
    {
        get => _isServerEnabled;
        set
        {
            if (_isServerEnabled == value) return;
            this.RaiseAndSetIfChanged(ref _isServerEnabled, value);
            if (!_loading) _ = WithBusyAsync(() => SwitchServerAsync(value));
        }
    }

    private string _serverStatus = "";
    public string ServerStatus
    {
        get => _serverStatus;
        set => this.RaiseAndSetIfChanged(ref _serverStatus, value);
    }

    private bool _hasNoAgents = true;
    public bool HasNoAgents
    {
        get => _hasNoAgents;
        set => this.RaiseAndSetIfChanged(ref _hasNoAgents, value);
    }

    private bool _isRenameEnabled;
    public bool IsRenameEnabled
    {
        get => _isRenameEnabled;
        set => this.RaiseAndSetIfChanged(ref _isRenameEnabled, value);
    }

    private bool _isRevokeEnabled;
    public bool IsRevokeEnabled
    {
        get => _isRevokeEnabled;
        set => this.RaiseAndSetIfChanged(ref _isRevokeEnabled, value);
    }

    private bool _isPauseEnabled;
    public bool IsPauseEnabled
    {
        get => _isPauseEnabled;
        set => this.RaiseAndSetIfChanged(ref _isPauseEnabled, value);
    }

    private bool _isResumeEnabled;
    public bool IsResumeEnabled
    {
        get => _isResumeEnabled;
        set => this.RaiseAndSetIfChanged(ref _isResumeEnabled, value);
    }

    private bool _isEndAccessEnabled;
    public bool IsEndAccessEnabled
    {
        get => _isEndAccessEnabled;
        set => this.RaiseAndSetIfChanged(ref _isEndAccessEnabled, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> BtRenameClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRevokeClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtPauseClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtResumeClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtEndAccessClicked { get; }

    public AgentsViewModel()
    {
        _service = GetService<AgentCoworkService>();

        BtRenameClicked = ReactiveCommand.CreateFromTask(ExecuteRenameAsync);
        BtRevokeClicked = ReactiveCommand.CreateFromTask(ExecuteRevokeAsync);
        BtPauseClicked = ReactiveCommand.Create(() => OnSelectedAgent(id => _service.Runtime.Leases.Pause(id)));
        BtResumeClicked = ReactiveCommand.Create(() => OnSelectedAgent(id => _service.Runtime.Leases.Resume(id)));
        BtEndAccessClicked = ReactiveCommand.Create(() =>
            OnSelectedAgent(id => _service.Runtime.Leases.End(id, LeaseEndReason.EndedByUser)));
        BtSavePolicyClicked = ReactiveCommand.Create(ExecuteSavePolicy);
        LoadPolicy();

        _service.StateChanged += OnModelChanged;
        _service.Runtime.Registry.Changed += OnModelChanged;
        _service.Runtime.Leases.Changed += OnModelChanged;

        // The remaining minutes move on their own; so does an expiry nobody triggered.
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(15), DispatcherPriority.Background, (_, _) =>
        {
            _service.Runtime.Sweep();
            Refresh();
        });
        _clock.Start();

        Refresh();
    }

    public override void Dispose()
    {
        _clock.Stop();
        _service.StateChanged -= OnModelChanged;
        _service.Runtime.Registry.Changed -= OnModelChanged;
        _service.Runtime.Leases.Changed -= OnModelChanged;
        base.Dispose();
    }

    private void OnModelChanged() => Dispatcher.UIThread.Post(Refresh);

    /// <summary>
    /// T361: switching the server on asks once (per disclosure version) for the LGPD disclosure; declining leaves it off.
    /// Off stops the server at once, every lease ending. Either way the server starts or stops live, no restart needed.
    /// </summary>
    private async Task SwitchServerAsync(bool enable)
    {
        if (enable && !_service.DisclosureAccepted)
        {
            var accepted = await ConfirmationDialog.ConfirmAsync(Localizer["AgentServerDisclosureTitle"],
                Localizer["AgentServerDisclosureMSG"]);
            if (!accepted)
            {
                Toasts.Info(Localizer["AgentServerDisclosureDeclinedMSG"]);
                Dispatcher.UIThread.Post(Refresh); // back to off, without calling the service
                return;
            }
            _service.AcceptDisclosure();
        }
        await _service.SetEnabledAsync(enable);
    }

    /// <summary>T362: the current policy, editable only for a user who may change it.</summary>
    private void LoadPolicy()
    {
        var policy = _service.ExclusionPolicy;
        var editable = AgentExclusionPolicyAccess.CanChange(AuthenticationService.IsAuthenticated
            ? AuthenticationService.AuthenticatedUserInfo
            : null);
        IsPolicyLocked = !editable;
        DataClasses.Clear();
        // The organisation minimum (AgentExclusionPolicy.Floor) is shown checked and cannot be unchecked.
        foreach (var dataClass in Enum.GetValues<AgentDataClass>())
            DataClasses.Add(new AgentDataClassRow(dataClass, Localizer[AgentDataClasses.LabelKey(dataClass)],
                policy.Excluded.Contains(dataClass), editable && !AgentExclusionPolicy.Floor.Contains(dataClass),
                OnPolicyEdited));
        IsSavePolicyEnabled = false;
    }

    private void OnPolicyEdited() => IsSavePolicyEnabled = !IsPolicyLocked;

    private void ExecuteSavePolicy()
    {
        var policy = AgentExclusionPolicy.Of(DataClasses.Where(r => r.IsExcluded).Select(r => r.Class));
        if (_service.SetExclusionPolicy(policy)) Toasts.Success(Localizer["AgentDataClassesSavedMSG"]);
        else Toasts.Warning(Localizer["AgentDataClassesNotSavedMSG"]);
        LoadPolicy();
    }

    private void Refresh()
    {
        _loading = true;
        try
        {
            IsServerEnabled = _service.IsEnabled;
        }
        finally
        {
            _loading = false;
        }

        var runtime = _service.Runtime;
        ServerStatus = !IsServerEnabled ? Localizer["AgentServerStatusOff"]
            : _service.StartFailed ? Localizer["AgentServerStatusError"]
            : runtime.Port is { } port ? string.Format(Localizer["AgentServerStatusOn"], port)
            : Localizer["AgentServerStatusSignedOut"];

        var now = DateTimeOffset.UtcNow;
        var leases = runtime.Leases.ActiveLeases().ToDictionary(l => l.AgentId);
        var selectedId = SelectedAgent?.Id;
        Agents.Clear();
        foreach (var agent in runtime.Registry.List())
        {
            leases.TryGetValue(agent.Id, out var lease);
            Agents.Add(new AgentRow
            {
                Id = agent.Id,
                Name = agent.Name,
                Client = (agent.ClientName + " " + agent.ClientVersion).Trim(),
                Fingerprint = agent.Fingerprint,
                CreatedAt = agent.CreatedAt.ToLocalTime().ToString("g", CultureInfo.CurrentCulture),
                LastUsed = agent.LastUsedAt?.ToLocalTime().ToString("g", CultureInfo.CurrentCulture)
                           ?? Localizer["AgentNever"],
                Lease = LeaseText(lease, now),
                HasLease = lease is not null,
                IsPaused = lease?.IsPaused == true
            });
        }
        HasNoAgents = Agents.Count == 0;
        SelectedAgent = Agents.FirstOrDefault(a => a.Id == selectedId);
    }

    private static string LeaseText(AgentLease? lease, DateTimeOffset now)
    {
        if (lease is null) return Localizer["AgentLeaseNone"];
        var minutes = (int)Math.Ceiling(lease.Remaining(now).TotalMinutes);
        var mode = lease.Mode == AgentAccessMode.ReadWrite ? Localizer["AgentModeReadWrite"] : Localizer["AgentModeRead"];
        return lease.IsPaused
            ? string.Format(Localizer["AgentLeasePaused"], minutes)
            : string.Format(Localizer["AgentLeaseActive"], mode, minutes);
    }

    private void UpdateEnabledFlags()
    {
        var row = SelectedAgent;
        IsRenameEnabled = row is not null;
        IsRevokeEnabled = row is not null;
        IsPauseEnabled = row is { HasLease: true, IsPaused: false };
        IsResumeEnabled = row is { HasLease: true, IsPaused: true };
        IsEndAccessEnabled = row is { HasLease: true };
    }

    private void OnSelectedAgent(Func<string, bool> action)
    {
        if (SelectedAgent is { } row) action(row.Id);
    }

    private async Task ExecuteRenameAsync()
    {
        if (SelectedAgent is not { } row) return;

        var result = await GetService<IDialogService>().ShowDialogAsync<StringDialogResult, StringDialogParameter>(
            nameof(EditSingleStringDialogViewModel),
            new StringDialogParameter { Title = StrRename, FieldName = StrName, Value = row.Name });
        if (result is not { Action: ResultActions.Ok }) return;

        if (!_service.Runtime.Registry.Rename(row.Id, result.Result))
            Toasts.Warning(string.Format(Localizer["AgentRenameInvalidMSG"], AgentText.MaxNameLength));
    }

    private async Task ExecuteRevokeAsync()
    {
        if (SelectedAgent is not { } row) return;

        var confirmed = await ConfirmationDialog.ConfirmAsync(Localizer["Warning"],
            string.Format(Localizer["AgentRevokeConfirmMSG"], row.Name));
        if (confirmed) _service.Runtime.Registry.Revoke(row.Id);
    }
}

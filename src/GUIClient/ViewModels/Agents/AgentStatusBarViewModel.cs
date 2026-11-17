using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using GUIClient.Agents;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Agents;

/// <summary>One agent's active lease in the status bar (T355): who, how long is left, what it did last, and its controls.</summary>
public class AgentLeaseBarRow : ReactiveObject
{
    public AgentLeaseBarRow(string agentId, Action<string> pause, Action<string> resume, Action<string> end,
        Action<string> moreTime)
    {
        AgentId = agentId;
        BtPauseClicked = ReactiveCommand.Create(() => pause(AgentId));
        BtResumeClicked = ReactiveCommand.Create(() => resume(AgentId));
        BtEndClicked = ReactiveCommand.Create(() => end(AgentId));
        BtMoreTimeClicked = ReactiveCommand.Create(() => moreTime(AgentId),
            this.WhenAnyValue(x => x.IsExtensionPending, pending => !pending));
    }

    public string AgentId { get; }

    private string _statusText = "";
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    private string _lastActivityText = "";
    public string LastActivityText
    {
        get => _lastActivityText;
        set => this.RaiseAndSetIfChanged(ref _lastActivityText, value);
    }

    private bool _isPaused;
    public bool IsPaused
    {
        get => _isPaused;
        set
        {
            this.RaiseAndSetIfChanged(ref _isPaused, value);
            this.RaisePropertyChanged(nameof(IsRunning));
        }
    }

    public bool IsRunning => !IsPaused;

    private bool _isExpiring;
    /// <summary>The last minute of the lease: the warning and "Grant another 10 min" show.</summary>
    public bool IsExpiring
    {
        get => _isExpiring;
        set => this.RaiseAndSetIfChanged(ref _isExpiring, value);
    }

    private bool _isExtensionPending;
    /// <summary>A request for more time is waiting in the approval dialog; nothing is extended until it is granted.</summary>
    public bool IsExtensionPending
    {
        get => _isExtensionPending;
        set => this.RaiseAndSetIfChanged(ref _isExtensionPending, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> BtPauseClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtResumeClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtEndClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtMoreTimeClicked { get; }
}

/// <summary>
/// The status-bar indicator (T355, S55 "Co-work experience"): for every active lease the agent's name, mode and
/// remaining time, its last call, Pause/Resume and End access always visible, the activity panel one click away, and in
/// the last minute a warning with "Grant another 10 min" — which asks the user through the normal access dialog.
/// </summary>
public class AgentStatusBarViewModel : ViewModelBase
{
    public string StrActivity { get; } = Localizer["AgentActivity"];
    public string StrPause { get; } = Localizer["AgentPause"];
    public string StrResume { get; } = Localizer["AgentResume"];
    public string StrEndAccess { get; } = Localizer["AgentEndAccess"];
    public string StrExpiring { get; } = Localizer["AgentExpiringMSG"];
    public string StrMoreTime { get; } = Localizer["AgentMoreTime"];
    public string StrMoreTimePending { get; } = Localizer["AgentMoreTimePendingMSG"];

    private readonly AgentCoworkService _service;
    private readonly DispatcherTimer _clock;

    public ObservableCollection<AgentLeaseBarRow> Leases { get; } = new();

    private bool _hasLeases;
    public bool HasLeases
    {
        get => _hasLeases;
        set => this.RaiseAndSetIfChanged(ref _hasLeases, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> BtActivityClicked { get; }

    public AgentStatusBarViewModel()
    {
        _service = GetService<AgentCoworkService>();
        BtActivityClicked = ReactiveCommand.Create(OpenActivity);

        _service.Runtime.Leases.Changed += OnModelChanged;
        _service.Runtime.Activity.Added += OnActivity;
        _service.StateChanged += OnModelChanged;

        // The countdown and the one-minute warning move on their own.
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) => Refresh());
        _clock.Start();
        Refresh();
    }

    public override void Dispose()
    {
        _clock.Stop();
        _service.Runtime.Leases.Changed -= OnModelChanged;
        _service.Runtime.Activity.Added -= OnActivity;
        _service.StateChanged -= OnModelChanged;
        base.Dispose();
    }

    private void OnModelChanged() => Dispatcher.UIThread.Post(Refresh);

    private void OnActivity(AgentActivityEntry _) => Dispatcher.UIThread.Post(Refresh);

    private void Refresh()
    {
        var runtime = _service.Runtime;
        var now = DateTimeOffset.UtcNow;
        var leases = runtime.Leases.ActiveLeases().OrderBy(l => l.AgentName, StringComparer.CurrentCulture).ToList();
        var entries = runtime.Activity.Entries();

        foreach (var gone in Leases.Where(r => leases.All(l => l.AgentId != r.AgentId)).ToList()) Leases.Remove(gone);
        foreach (var lease in leases)
        {
            var row = Leases.FirstOrDefault(r => r.AgentId == lease.AgentId);
            if (row is null)
            {
                row = new AgentLeaseBarRow(lease.AgentId, id => runtime.Leases.Pause(id), id => runtime.Leases.Resume(id),
                    id => runtime.Leases.End(id, LeaseEndReason.EndedByUser), RequestMoreTime);
                Leases.Add(row);
            }

            var mode = lease.Mode == AgentAccessMode.ReadWrite
                ? (lease.ConfirmEachWrite ? Localizer["AgentModeReadWriteConfirmed"] : Localizer["AgentModeReadWrite"])
                : Localizer["AgentModeRead"];
            row.StatusText = string.Format(lease.IsPaused ? Localizer["AgentBarPausedMSG"] : Localizer["AgentBarActiveMSG"],
                lease.AgentName, mode, AgentLeaseClock.Remaining(lease, now));
            row.IsPaused = lease.IsPaused;
            row.IsExpiring = AgentLeaseClock.IsExpiring(lease, now);
            row.IsExtensionPending = runtime.Leases.GetPendingFor(lease.AgentId) is { UserInitiated: true };
            var last = entries.LastOrDefault(e => e.AgentId == lease.AgentId);
            row.LastActivityText = last is null ? "" : ActivityText(last);
        }
        HasLeases = Leases.Count > 0;
    }

    /// <summary>"update_risk on Risk 12 (updated)" — the tool, the item and the status; no data.</summary>
    public static string ActivityText(AgentActivityEntry entry) =>
        string.Format(Localizer["AgentLastActivityMSG"], entry.Tool,
            entry.Item is null ? Localizer["AgentNoItem"] : AgentWriteDialogViewModel.KindLabel(entry.Item.Kind) + " " + entry.Item.Id,
            entry.Result);

    private void RequestMoreTime(string agentId)
    {
        if (!_service.Runtime.RequestMoreTime(agentId)) Toasts.Warning(Localizer["AgentMoreTimeFailedMSG"]);
        Refresh();
    }

    private static void OpenActivity() =>
        GetService<GUIClient.Navigation.INavigationService>()
            .ShowAuxiliaryWindow<GUIClient.Views.Agents.AgentActivityWindow>(() => new AgentActivityViewModel());
}

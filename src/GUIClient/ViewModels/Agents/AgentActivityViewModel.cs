using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Threading;
using GUIClient.Agents;
using ReactiveUI;
using ReactiveUI.Binding;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Agents;

/// <summary>One call in the activity panel: time, agent, tool, item, read or edit, result. No data.</summary>
public class AgentActivityRow
{
    public string Time { get; init; } = "";
    public string Agent { get; init; } = "";
    public string Tool { get; init; } = "";
    public string Item { get; init; } = "";
    public string Kind { get; init; } = "";
    public string Result { get; init; } = "";
}

/// <summary>One item an agent is on now (T356 presence).</summary>
public class AgentPresenceRow
{
    public string Item { get; init; } = "";
    public string Agent { get; init; } = "";
    public string Mode { get; init; } = "";
    public string LastSeen { get; init; } = "";
}

/// <summary>
/// The activity panel (T355): the live list of the agents' calls, newest first, the items they are on (T356), and Pause,
/// Resume and End access for every active lease — always visible.
/// </summary>
public class AgentActivityViewModel : ViewModelBase
{
    public string StrTitle { get; } = Localizer["AgentActivityTitle"];
    public string StrCalls { get; } = Localizer["AgentActivityCalls"];
    public string StrPresence { get; } = Localizer["AgentActivityPresence"];
    public string StrTime { get; } = Localizer["AgentActivityTime"];
    public string StrAgent { get; } = Localizer["AgentName"];
    public string StrTool { get; } = Localizer["AgentWriteTool"];
    public string StrItem { get; } = Localizer["AgentWriteItem"];
    public string StrKind { get; } = Localizer["AgentActivityKind"];
    public string StrResult { get; } = Localizer["AgentActivityResult"];
    public string StrMode { get; } = Localizer["AgentActivityMode"];
    public string StrLastSeen { get; } = Localizer["AgentActivityLastSeen"];
    public string StrPauseAll { get; } = Localizer["AgentPause"];
    public string StrResumeAll { get; } = Localizer["AgentResume"];
    public string StrEndAll { get; } = Localizer["AgentEndAccess"];
    public string StrNoLease { get; } = Localizer["AgentLeaseNone"];

    private readonly AgentCoworkService _service;
    private readonly DispatcherTimer _clock;

    public ObservableCollection<AgentActivityRow> Entries { get; } = new();
    public ObservableCollection<AgentPresenceRow> Present { get; } = new();

    private string _leaseText = "";
    public string LeaseText
    {
        get => _leaseText;
        set => this.RaiseAndSetIfChanged(ref _leaseText, value);
    }

    private bool _hasLease;
    public bool HasLease
    {
        get => _hasLease;
        set => this.RaiseAndSetIfChanged(ref _hasLease, value);
    }

    public ReactiveCommand<RxVoid, RxVoid> BtPauseClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtResumeClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtEndClicked { get; }

    public AgentActivityViewModel()
    {
        _service = GetService<AgentCoworkService>();
        var leases = _service.Runtime.Leases;
        var hasLease = this.WhenAnyValue(x => x.HasLease);
        BtPauseClicked = ReactiveCommand.Create(() => ForEachLease(id => leases.Pause(id)), hasLease);
        BtResumeClicked = ReactiveCommand.Create(() => ForEachLease(id => leases.Resume(id)), hasLease);
        BtEndClicked = ReactiveCommand.Create(() => ForEachLease(id => leases.End(id, LeaseEndReason.EndedByUser)), hasLease);

        _service.Runtime.Activity.Added += OnAdded;
        _service.Runtime.Presence.Changed += OnChanged;
        leases.Changed += OnChanged;
        _clock = new DispatcherTimer(TimeSpan.FromSeconds(5), DispatcherPriority.Background, (_, _) => Refresh());
        _clock.Start();
        Refresh();
    }

    public override void Dispose()
    {
        _clock.Stop();
        _service.Runtime.Activity.Added -= OnAdded;
        _service.Runtime.Presence.Changed -= OnChanged;
        _service.Runtime.Leases.Changed -= OnChanged;
        base.Dispose();
    }

    private void OnAdded(AgentActivityEntry _) => Dispatcher.UIThread.Post(Refresh);

    private void OnChanged() => Dispatcher.UIThread.Post(Refresh);

    private void ForEachLease(Func<string, bool> action)
    {
        foreach (var lease in _service.Runtime.Leases.ActiveLeases()) action(lease.AgentId);
        Refresh();
    }

    private void Refresh()
    {
        var runtime = _service.Runtime;
        var now = DateTimeOffset.UtcNow;

        var leases = runtime.Leases.ActiveLeases();
        HasLease = leases.Count > 0;
        LeaseText = HasLease
            ? string.Join("   ", leases.Select(l => string.Format(
                l.IsPaused ? Localizer["AgentBarPausedMSG"] : Localizer["AgentBarActiveMSG"], l.AgentName,
                l.Mode == AgentAccessMode.ReadWrite ? Localizer["AgentModeReadWrite"] : Localizer["AgentModeRead"],
                AgentLeaseClock.Remaining(l, now))))
            : StrNoLease;

        Entries.Clear();
        foreach (var e in runtime.Activity.Entries().AsEnumerable().Reverse())
            Entries.Add(new AgentActivityRow
            {
                Time = e.At.ToLocalTime().ToString("T", CultureInfo.CurrentCulture),
                Agent = e.AgentName,
                Tool = e.Tool,
                Item = e.Item is null ? "" : AgentWriteDialogViewModel.KindLabel(e.Item.Kind) + " " + e.Item.Id,
                Kind = e.IsEdit ? Localizer["AgentActivityEdit"] : Localizer["AgentActivityRead"],
                Result = e.Result
            });

        Present.Clear();
        foreach (var p in runtime.Presence.Active(now))
            Present.Add(new AgentPresenceRow
            {
                Item = AgentWriteDialogViewModel.KindLabel(p.Item.Kind) + " " + p.Item.Id,
                Agent = p.AgentName,
                Mode = p.Editing ? Localizer["AgentPresenceEditing"] : Localizer["AgentPresenceReading"],
                LastSeen = p.LastSeen.ToLocalTime().ToString("T", CultureInfo.CurrentCulture)
            });
    }
}

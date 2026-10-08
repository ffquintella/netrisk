using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools.Track9;
using Model.Governance;
using Model.Monitoring;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Track9;

public sealed class RiskMonitoringKriRow(KriGateDto kri, string stateLabel)
{
    public int Id { get; } = kri.KriId;
    public string Name { get; } = kri.Name;
    public string StateLabel { get; } = stateLabel;
    public string ValueText { get; } = kri.Value?.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
}

public sealed class RiskMonitoringTriggerRow(ReassessmentTriggerDto trigger, string stateLabel)
{
    public string Title { get; } = trigger.EventTitle;
    public string StateLabel { get; } = stateLabel;
    public DateTime RaisedAt { get; } = trigger.RaisedAt;
}

/// <summary>Risk-detail block for KRI links, explicit stale/no-reading state, Gate B and reassessment triggers.</summary>
public sealed class RiskMonitoringPanelViewModel : ViewModelBase
{
    private readonly IMonitoringService _monitoring = GetService<IMonitoringService>();
    private readonly IRiskGovernanceService _governance = GetService<IRiskGovernanceService>();
    private readonly Track9MonitoringLoadGate _gate = new();
    private int? _riskId;
    private bool _loaded;
    private string _loadError = string.Empty;
    private AppetiteEvaluation? _appetite;

    public string StrTitle { get; } = Localizer["Track9RiskMonitoringTitle"];
    public string StrReload { get; } = Localizer["Reload"];
    public string StrNoItems { get; } = Localizer["Track9NoItems"];
    public string StrThirdLine { get; } = Localizer["Track9ReadOnlyThirdLine"];
    public string StrGateB { get; } = Localizer["Track9GateB"];
    public string StrKris { get; } = Localizer["Track9Kris"];
    public string StrTriggers { get; } = Localizer["Track9Triggers"];
    public string StrName { get; } = Localizer["Name"];
    public string StrStatus { get; } = Localizer["Status"];
    public string StrValue { get; } = Localizer["Value"];
    public string StrOccurredAt { get; } = Localizer["Track9OccurredAt"];
    public string StrLink { get; } = Localizer["Track9LinkRisk"];
    public string StrUnlink { get; } = Localizer["Track9UnlinkRisk"];

    public AppetiteEvaluation? Appetite
    {
        get => _appetite;
        private set
        {
            this.RaiseAndSetIfChanged(ref _appetite, value);
            this.RaisePropertyChanged(nameof(GateBText));
        }
    }

    public ObservableCollection<RiskMonitoringKriRow> LinkedKris { get; } = [];
    public ObservableCollection<RiskMonitoringTriggerRow> Triggers { get; } = [];
    public ObservableCollection<Track9LookupItem> AvailableKris { get; } = [];
    public RiskMonitoringKriRow? SelectedLinkedKri { get; set; }
    public Track9LookupItem? SelectedKriToLink { get; set; }

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

    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);
    public bool IsEmpty => _loaded && _riskId is not null && LinkedKris.Count == 0 && Triggers.Count == 0 && !HasLoadError;
    public bool IsThirdLine => Track9MonitoringPermissions.IsThirdLine(AuthenticationService.AuthenticatedUserInfo);
    public bool CanWrite => _riskId is not null
                            && Track9MonitoringPermissions.CanSubmitRiskData(AuthenticationService.AuthenticatedUserInfo);
    public string GateBText => Appetite is null
        ? string.Empty
        : Localizer[Appetite.Indicators.State switch
        {
            IndicatorAppetiteState.NotConfigured => "Track9GateBNotConfigured",
            IndicatorAppetiteState.NotAssessable => "Track9GateBNotAssessable",
            IndicatorAppetiteState.WithinTolerance => "Track9GateBWithin",
            IndicatorAppetiteState.ExceedsTolerance => "Track9GateBExceeded",
            _ => "Track9GateBNotAssessable"
        }];

    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtLinkClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtUnlinkClicked { get; }

    public RiskMonitoringPanelViewModel()
    {
        BtReloadClicked = ReactiveCommand.CreateFromTask(ReloadAsync);
        BtLinkClicked = ReactiveCommand.CreateFromTask(LinkAsync);
        BtUnlinkClicked = ReactiveCommand.CreateFromTask(UnlinkAsync);
    }

    public Task EnsureLoadedAsync() => _riskId is null ? Task.CompletedTask : ReloadAsync();
    public Task ReloadAsync() => _riskId is null ? Task.CompletedTask : LoadAsync(_riskId.Value);

    public Task LoadRiskAsync(int? riskId)
    {
        if (riskId is null)
        {
            Clear();
            return Task.CompletedTask;
        }
        _gate.DiscardOutstanding();
        _riskId = riskId;
        ResetContent();
        LoadError = string.Empty;
        this.RaisePropertyChanged(nameof(CanWrite));
        return LoadAsync(riskId.Value);
    }

    public void Clear()
    {
        _gate.DiscardOutstanding();
        _riskId = null;
        ResetContent();
        LoadError = string.Empty;
        this.RaisePropertyChanged(nameof(CanWrite));
    }

    private void ResetContent()
    {
        _loaded = false;
        Appetite = null;
        LinkedKris.Clear();
        Triggers.Clear();
        AvailableKris.Clear();
        SelectedLinkedKri = null;
        SelectedKriToLink = null;
        foreach (var property in new[] { nameof(SelectedLinkedKri), nameof(SelectedKriToLink) })
            this.RaisePropertyChanged(property);
        this.RaisePropertyChanged(nameof(IsEmpty));
    }

    private async Task LoadAsync(int riskId)
    {
        var token = _gate.Begin();
        try
        {
            await WithBusyAsync(async () =>
            {
                var appetiteTask = _governance.GetAppetiteEvaluationAsync(riskId);
                var triggersTask = _monitoring.GetTriggersAsync(riskId);
                var krisTask = _monitoring.GetKrisAsync();
                await Task.WhenAll(appetiteTask, triggersTask, krisTask);
                if (!_gate.IsCurrent(token) || _riskId != riskId) return;

                Appetite = await appetiteTask;
                LinkedKris.Clear();
                foreach (var kri in Appetite.Indicators.Kris)
                    LinkedKris.Add(new RiskMonitoringKriRow(kri, KriStateLabel(kri.State)));

                Triggers.Clear();
                foreach (var trigger in await triggersTask)
                    Triggers.Add(new RiskMonitoringTriggerRow(trigger, TriggerStateLabel(trigger.State)));

                var linkedIds = Appetite.Indicators.Kris.Select(x => x.KriId).ToHashSet();
                AvailableKris.Clear();
                foreach (var kri in (await krisTask).Where(x => x.RetiredAt is null && !linkedIds.Contains(x.Id)))
                    AvailableKris.Add(new Track9LookupItem(kri.Id, kri.Name));

                LoadError = string.Empty;
                _loaded = true;
                this.RaisePropertyChanged(nameof(IsEmpty));
                this.RaisePropertyChanged(nameof(CanWrite));
            });
        }
        catch (Exception ex)
        {
            if (!_gate.IsCurrent(token) || _riskId != riskId) return;
            Logger.Error(ex, "Error loading Track 9 monitoring for risk {RiskId}", riskId);
            LoadError = ExplainError(ex);
        }
    }

    private Task LinkAsync() => !CanWrite || _riskId is null || SelectedKriToLink?.Id is not { } kriId
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9RiskLinked"], async () =>
        {
            await _monitoring.LinkRiskAsync(kriId, _riskId.Value);
            await ReloadAsync();
        });

    private Task UnlinkAsync() => !CanWrite || _riskId is null || SelectedLinkedKri is null
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9RiskUnlinked"], async () =>
        {
            await _monitoring.UnlinkRiskAsync(SelectedLinkedKri.Id, _riskId.Value);
            await ReloadAsync();
        });

    private static string KriStateLabel(KriState state) => Localizer[state switch
    {
        KriState.NoReading => "Track9NoReading",
        KriState.Stale => "Track9Stale",
        KriState.WithinTolerance => "Track9WithinTolerance",
        KriState.Warning => "Warning",
        KriState.Breached => "Track9Breached",
        KriState.Retired => "Retired",
        _ => "Track9NoReading"
    }];

    private static string TriggerStateLabel(ReassessmentTriggerState state) => Localizer[state switch
    {
        ReassessmentTriggerState.Pending => "Track9Pending",
        ReassessmentTriggerState.Answered => "Track9Answered",
        ReassessmentTriggerState.RiskClosed => "Track9RiskClosed",
        _ => "Track9Pending"
    }];
}

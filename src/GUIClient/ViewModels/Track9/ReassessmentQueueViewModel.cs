using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools.Track9;
using Model.Monitoring;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Track9;

public sealed class ReassessmentEventRow(ReassessmentEventDto item, string typeLabel)
{
    public int Id => item.Id;
    public DateTime OccurredAt => item.OccurredAt;
    public string Title => item.Title;
    public string TypeLabel { get; } = typeLabel;
}

public sealed class ReassessmentTriggerRow(ReassessmentTriggerDto item, string stateLabel)
{
    public int RiskId => item.RiskId;
    public string RiskSubject => item.RiskSubject;
    public string StateLabel { get; } = stateLabel;
}

/// <summary>The six Phase 7 reassessment events and the pending/answered queue (T306).</summary>
public sealed class ReassessmentQueueViewModel : ViewModelBase
{
    private readonly IMonitoringService _monitoring = GetService<IMonitoringService>();
    private readonly IRisksService _risks = GetService<IRisksService>();
    private readonly IIncidentsService _incidents = GetService<IIncidentsService>();
    private readonly Track9MonitoringLoadGate _gate = new();
    private bool _loaded;
    private int? _riskScope;
    private string _loadError = string.Empty;
    private ObservableCollection<ReassessmentEventRow> _events = [];
    private ObservableCollection<ReassessmentTriggerRow> _triggers = [];

    public string StrTitle { get; } = Localizer["Track9ReassessmentQueueTitle"];
    public string StrReload { get; } = Localizer["Reload"];
    public string StrNoItems { get; } = Localizer["Track9NoItems"];
    public string StrThirdLine { get; } = Localizer["Track9ReadOnlyThirdLine"];
    public string StrEvents { get; } = Localizer["Track9Events"];
    public string StrTriggers { get; } = Localizer["Track9Triggers"];
    public string StrPendingOnly { get; } = Localizer["Track9PendingOnly"];
    public string StrEventType { get; } = Localizer["Track9EventType"];
    public string StrTitleField { get; } = Localizer["Track9EventTitle"];
    public string StrDescription { get; } = Localizer["Description"];
    public string StrOccurredAt { get; } = Localizer["Track9OccurredAt"];
    public string StrIncidentId { get; } = Localizer["Track9IncidentId"];
    public string StrRiskId { get; } = Localizer["Track9RiskId"];
    public string StrAdd { get; } = Localizer["Add"];
    public string StrRemove { get; } = Localizer["Remove"];
    public string StrDeclare { get; } = Localizer["Track9DeclareEvent"];
    public string StrApplyRisk { get; } = Localizer["Track9ApplyRisk"];
    public string StrStatus { get; } = Localizer["Status"];

    public ObservableCollection<Track9Choice<ReassessmentTriggerType>> TriggerTypes { get; } =
    [
        new(ReassessmentTriggerType.ArchitectureOrTechnologyChange, Localizer["Track9TriggerArchitecture"]),
        new(ReassessmentTriggerType.SupplierAcquisitionOrMigration, Localizer["Track9TriggerSupplier"]),
        new(ReassessmentTriggerType.SignificantIncidentOrNearMiss, Localizer["Track9TriggerIncident"]),
        new(ReassessmentTriggerType.NewRegulation, Localizer["Track9TriggerRegulation"]),
        new(ReassessmentTriggerType.NewAiModel, Localizer["Track9TriggerAiModel"]),
        new(ReassessmentTriggerType.NewDataOrKriBreach, Localizer["Track9TriggerDataOrKri"])
    ];
    public ObservableCollection<Track9LookupItem> RiskChoices { get; } = [];
    public ObservableCollection<Track9LookupItem> IncidentChoices { get; } = [];

    public ObservableCollection<ReassessmentEventRow> Events
    {
        get => _events;
        private set
        {
            this.RaiseAndSetIfChanged(ref _events, value);
            this.RaisePropertyChanged(nameof(IsEmpty));
        }
    }

    public ObservableCollection<ReassessmentTriggerRow> Triggers
    {
        get => _triggers;
        private set
        {
            this.RaiseAndSetIfChanged(ref _triggers, value);
            this.RaisePropertyChanged(nameof(IsEmpty));
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

    public Track9Choice<ReassessmentTriggerType>? FilterType { get; set; }
    public bool PendingOnly { get; set; }
    public Track9LookupItem? FilterRisk { get; set; }
    public ReassessmentEventRow? SelectedEvent { get; set; }
    public Track9Choice<ReassessmentTriggerType>? EventType { get; set; }
    public string EventTitle { get; set; } = string.Empty;
    public string EventDescription { get; set; } = string.Empty;
    public DateTimeOffset? OccurredAt { get; set; } = DateTimeOffset.Now;
    public Track9LookupItem? SelectedIncident { get; set; }
    public Track9LookupItem? DraftRisk { get; set; }
    public int? SelectedDraftRiskId { get; set; }
    public ObservableCollection<int> DraftRiskIds { get; } = [];
    public Track9LookupItem? AdditionalRisk { get; set; }

    public bool IsEmpty => _loaded && Events.Count == 0 && Triggers.Count == 0 && !HasLoadError;
    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);
    public bool IsRiskScoped => _riskScope is not null;
    public bool IsGlobalMode => !IsRiskScoped;
    public bool IsThirdLine => Track9MonitoringPermissions.IsThirdLine(AuthenticationService.AuthenticatedUserInfo);
    public bool CanWrite => Track9MonitoringPermissions.CanSubmitRiskData(AuthenticationService.AuthenticatedUserInfo);

    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAddDraftRiskClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRemoveDraftRiskClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtDeclareClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtApplyRiskClicked { get; }

    public ReassessmentQueueViewModel()
    {
        EventType = TriggerTypes[0];
        BtReloadClicked = ReactiveCommand.CreateFromTask(ReloadAsync);
        BtAddDraftRiskClicked = ReactiveCommand.Create(AddDraftRisk);
        BtRemoveDraftRiskClicked = ReactiveCommand.Create(RemoveDraftRisk);
        BtDeclareClicked = ReactiveCommand.CreateFromTask(DeclareAsync);
        BtApplyRiskClicked = ReactiveCommand.CreateFromTask(ApplyRiskAsync);
    }

    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : ReloadAsync();
    public Task ReloadAsync() => LoadAsync();

    public Task LoadRiskAsync(int? riskId)
    {
        if (riskId is null)
        {
            Clear();
            return Task.CompletedTask;
        }

        _gate.DiscardOutstanding();
        _riskScope = riskId;
        Events = [];
        Triggers = [];
        SelectedEvent = null;
        AdditionalRisk = null;
        DraftRiskIds.Clear();
        _loaded = false;
        FilterRisk = RiskChoices.FirstOrDefault(x => x.Id == riskId)
                     ?? new Track9LookupItem(riskId, $"#{riskId}");
        foreach (var property in new[] { nameof(FilterRisk), nameof(SelectedEvent), nameof(AdditionalRisk),
                     nameof(IsRiskScoped), nameof(IsGlobalMode) })
            this.RaisePropertyChanged(property);
        return ReloadAsync();
    }

    public void Clear()
    {
        _gate.DiscardOutstanding();
        _riskScope = null;
        FilterRisk = null;
        Events = [];
        Triggers = [];
        SelectedEvent = null;
        AdditionalRisk = null;
        DraftRiskIds.Clear();
        _loaded = false;
        LoadError = string.Empty;
        foreach (var property in new[] { nameof(FilterRisk), nameof(SelectedEvent), nameof(AdditionalRisk),
                     nameof(IsRiskScoped), nameof(IsGlobalMode) })
            this.RaisePropertyChanged(property);
    }

    private async Task LoadAsync()
    {
        var token = _gate.Begin();
        try
        {
            await WithBusyAsync(async () =>
            {
                var eventsTask = _monitoring.GetEventsAsync(FilterType?.Value);
                var triggersTask = _monitoring.GetTriggersAsync(
                    _riskScope ?? FilterRisk?.Id, PendingOnly);
                var risksTask = _risks.GetAllRisksAsync();
                var incidentsTask = _incidents.GetAllAsync();
                await Task.WhenAll(eventsTask, triggersTask, risksTask, incidentsTask);
                if (!_gate.IsCurrent(token)) return;
                Events = new ObservableCollection<ReassessmentEventRow>((await eventsTask)
                    .Select(x => new ReassessmentEventRow(x, TriggerLabel(x.TriggerType))));
                Triggers = new ObservableCollection<ReassessmentTriggerRow>((await triggersTask)
                    .Select(x => new ReassessmentTriggerRow(x, TriggerStateLabel(x.State))));
                Replace(RiskChoices, (await risksTask).Select(x => new Track9LookupItem(x.Id, x.Subject)));
                Replace(IncidentChoices, (await incidentsTask).Select(x => new Track9LookupItem(x.Id, x.Name)));
                if (_riskScope is { } scopedRisk)
                {
                    FilterRisk = RiskChoices.FirstOrDefault(x => x.Id == scopedRisk)
                                 ?? new Track9LookupItem(scopedRisk, $"#{scopedRisk}");
                    this.RaisePropertyChanged(nameof(FilterRisk));
                }
                LoadError = string.Empty;
                _loaded = true;
                this.RaisePropertyChanged(nameof(IsEmpty));
            });
        }
        catch (Exception ex)
        {
            if (!_gate.IsCurrent(token)) return;
            Logger.Error(ex, "Error loading the Track 9 reassessment queue");
            LoadError = ExplainError(ex);
        }
    }

    private void AddDraftRisk()
    {
        if (!CanWrite) return;
        if (DraftRisk?.Id is not { } value) return;
        var ids = Track9MonitoringInputRules.PositiveDistinctIds(DraftRiskIds.Append(value));
        DraftRiskIds.Clear();
        foreach (var id in ids) DraftRiskIds.Add(id);
        DraftRisk = null;
        this.RaisePropertyChanged(nameof(DraftRisk));
    }

    private void RemoveDraftRisk()
    {
        if (!CanWrite) return;
        if (SelectedDraftRiskId is { } id) DraftRiskIds.Remove(id);
    }

    private Task DeclareAsync() => !CanWrite
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9EventDeclared"], async () =>
    {
        await _monitoring.DeclareEventAsync(new ReassessmentEventRequest
        {
            Type = EventType?.Value,
            Title = EventTitle,
            Description = EventDescription,
            OccurredAt = OccurredAt?.UtcDateTime,
            IncidentId = SelectedIncident?.Id,
            RiskIds = _riskScope is { } scopedRisk ? [scopedRisk] : [.. DraftRiskIds]
        });
        await ReloadAsync();
    });

    private Task ApplyRiskAsync() => !CanWrite || SelectedEvent is null
        || (_riskScope ?? AdditionalRisk?.Id) is not { } risk
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9EventRiskAdded"], async () =>
        {
            await _monitoring.AddEventRisksAsync(SelectedEvent.Id,
                new ReassessmentRisksRequest { RiskIds = [risk] });
            await ReloadAsync();
        });

    private static void Replace<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private string TriggerLabel(ReassessmentTriggerType type) =>
        TriggerTypes.First(x => x.Value == type).Label;

    private static string TriggerStateLabel(ReassessmentTriggerState state) => Localizer[state switch
    {
        ReassessmentTriggerState.Pending => "Track9Pending",
        ReassessmentTriggerState.Answered => "Track9Answered",
        ReassessmentTriggerState.RiskClosed => "Track9RiskClosed",
        _ => "Track9Pending"
    }];
}

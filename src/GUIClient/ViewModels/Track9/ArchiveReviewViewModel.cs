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

public sealed class Track9ArchiveConditionDraft(ReassessmentTriggerType triggerType, string label) : ReactiveObject
{
    private bool _isSelected;
    private string _description = string.Empty;
    public ReassessmentTriggerType TriggerType { get; } = triggerType;
    public string Label { get; } = label;
    public bool IsSelected { get => _isSelected; set => this.RaiseAndSetIfChanged(ref _isSelected, value); }
    public string Description { get => _description; set => this.RaiseAndSetIfChanged(ref _description, value); }
}

public sealed class ArchiveReviewRow(RiskArchiveDto item, string stateLabel)
{
    public RiskArchiveDto Item { get; } = item;
    public int Id => Item.Id;
    public int RiskId => Item.RiskId;
    public string RiskSubject => Item.RiskSubject;
    public DateTime NextReviewDueAt => Item.NextReviewDueAt;
    public string StateLabel { get; } = stateLabel;
}

/// <summary>Archive conditions, quarterly reviews and manual reopening (T307).</summary>
public sealed class ArchiveReviewViewModel : ViewModelBase
{
    private readonly IDecisionCycleService _decisions = GetService<IDecisionCycleService>();
    private readonly IRisksService _risks = GetService<IRisksService>();
    private readonly Track9MonitoringLoadGate _gate = new();
    private bool _loaded;
    private int? _riskScope;
    private string _loadError = string.Empty;
    private ObservableCollection<ArchiveReviewRow> _archives = [];
    private ArchiveReviewRow? _selectedArchive;

    public string StrTitle { get; } = Localizer["Track9ArchiveReviewTitle"];
    public string StrReload { get; } = Localizer["Reload"];
    public string StrNoItems { get; } = Localizer["Track9NoItems"];
    public string StrThirdLine { get; } = Localizer["Track9ReadOnlyThirdLine"];
    public string StrDueOnly { get; } = Localizer["Track9DueOnly"];
    public string StrIncludeEnded { get; } = Localizer["Track9IncludeEnded"];
    public string StrDecision { get; } = Localizer["Track9ArchiveDecision"];
    public string StrRiskId { get; } = Localizer["Track9RiskId"];
    public string StrJustification { get; } = Localizer["Track9Justification"];
    public string StrCloseReason { get; } = Localizer["Track9CloseReason"];
    public string StrConditions { get; } = Localizer["Track9ReopeningConditions"];
    public string StrDescription { get; } = Localizer["Description"];
    public string StrArchive { get; } = Localizer["Track9ArchiveRisk"];
    public string StrReview { get; } = Localizer["Track9QuarterlyReview"];
    public string StrReviewNote { get; } = Localizer["Track9ReviewNote"];
    public string StrKeep { get; } = Localizer["Track9KeepArchived"];
    public string StrReopen { get; } = Localizer["Reopen"];
    public string StrManualReopen { get; } = Localizer["Track9ManualReopen"];
    public string StrReason { get; } = Localizer["Reason"];
    public string StrStatus { get; } = Localizer["Status"];

    public ObservableCollection<Track9ArchiveConditionDraft> Conditions { get; } =
    [
        new(ReassessmentTriggerType.ArchitectureOrTechnologyChange, Localizer["Track9TriggerArchitecture"]),
        new(ReassessmentTriggerType.SupplierAcquisitionOrMigration, Localizer["Track9TriggerSupplier"]),
        new(ReassessmentTriggerType.SignificantIncidentOrNearMiss, Localizer["Track9TriggerIncident"]),
        new(ReassessmentTriggerType.NewRegulation, Localizer["Track9TriggerRegulation"]),
        new(ReassessmentTriggerType.NewAiModel, Localizer["Track9TriggerAiModel"]),
        new(ReassessmentTriggerType.NewDataOrKriBreach, Localizer["Track9TriggerDataOrKri"])
    ];

    public ObservableCollection<Track9Choice<RiskArchiveReviewOutcome>> ReviewOutcomes { get; } =
    [
        new(RiskArchiveReviewOutcome.KeepArchived, Localizer["Track9KeepArchived"]),
        new(RiskArchiveReviewOutcome.Reopen, Localizer["Reopen"])
    ];
    public ObservableCollection<Track9LookupItem> RiskChoices { get; } = [];
    public ObservableCollection<Track9LookupItem> CloseReasonChoices { get; } = [];

    public ObservableCollection<ArchiveReviewRow> Archives
    {
        get => _archives;
        private set
        {
            this.RaiseAndSetIfChanged(ref _archives, value);
            this.RaisePropertyChanged(nameof(IsEmpty));
        }
    }

    public ArchiveReviewRow? SelectedArchive
    {
        get => _selectedArchive;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedArchive, value);
            if (value is not null)
            {
                SelectedRisk = RiskChoices.FirstOrDefault(x => x.Id == value.RiskId)
                               ?? new Track9LookupItem(value.RiskId, value.RiskSubject);
                this.RaisePropertyChanged(nameof(SelectedRisk));
            }
            foreach (var property in new[] { nameof(CanReview), nameof(CanReopen) })
                this.RaisePropertyChanged(property);
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

    public bool DueOnly { get; set; }
    public bool IncludeEnded { get; set; }
    public Track9LookupItem? SelectedRisk { get; set; }
    public string Justification { get; set; } = string.Empty;
    public Track9LookupItem? SelectedCloseReason { get; set; }
    public Track9Choice<RiskArchiveReviewOutcome>? ReviewOutcome { get; set; }
    public string ReviewNote { get; set; } = string.Empty;
    public string ReopenReason { get; set; } = string.Empty;

    public bool IsEmpty => _loaded && Archives.Count == 0 && !HasLoadError;
    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);
    public bool IsRiskScoped => _riskScope is not null;
    public bool IsGlobalMode => !IsRiskScoped;
    public bool IsThirdLine => Track9MonitoringPermissions.IsThirdLine(AuthenticationService.AuthenticatedUserInfo);
    public bool CanArchive => Track9MonitoringPermissions.CanCloseRisk(AuthenticationService.AuthenticatedUserInfo);
    public bool CanReview => SelectedArchive?.Item.State == RiskArchiveState.Live
                             && Track9MonitoringPermissions.CanReviewRisk(AuthenticationService.AuthenticatedUserInfo);
    public bool CanReopen => SelectedArchive?.Item.State == RiskArchiveState.Live
                             && Track9MonitoringPermissions.CanCloseRisk(AuthenticationService.AuthenticatedUserInfo);

    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtArchiveClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtReviewClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtReopenClicked { get; }

    public ArchiveReviewViewModel()
    {
        ReviewOutcome = ReviewOutcomes[0];
        BtReloadClicked = ReactiveCommand.CreateFromTask(ReloadAsync);
        BtArchiveClicked = ReactiveCommand.CreateFromTask(ArchiveAsync);
        BtReviewClicked = ReactiveCommand.CreateFromTask(ReviewAsync);
        BtReopenClicked = ReactiveCommand.CreateFromTask(ReopenAsync);
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
        Archives = [];
        SelectedArchive = null;
        ResetDraft();
        _loaded = false;
        SelectedRisk = RiskChoices.FirstOrDefault(x => x.Id == riskId)
                       ?? new Track9LookupItem(riskId, $"#{riskId}");
        foreach (var property in new[] { nameof(SelectedRisk), nameof(IsRiskScoped), nameof(IsGlobalMode) })
            this.RaisePropertyChanged(property);
        return ReloadAsync();
    }

    public void Clear()
    {
        _gate.DiscardOutstanding();
        _riskScope = null;
        SelectedRisk = null;
        Archives = [];
        SelectedArchive = null;
        ResetDraft();
        LoadError = string.Empty;
        _loaded = false;
        foreach (var property in new[] { nameof(SelectedRisk), nameof(IsRiskScoped), nameof(IsGlobalMode) })
            this.RaisePropertyChanged(property);
    }

    private async Task LoadAsync()
    {
        var token = _gate.Begin();
        try
        {
            await WithBusyAsync(async () =>
            {
                var selectedId = SelectedArchive?.Id;
                var archivesTask = _riskScope is { } riskId
                    ? _decisions.GetRiskArchivesAsync(riskId)
                    : _decisions.GetArchivesAsync(DueOnly, IncludeEnded);
                var risksTask = _risks.GetAllRisksAsync(true);
                await Task.WhenAll(archivesTask, risksTask);
                if (!_gate.IsCurrent(token)) return;
                Archives = new ObservableCollection<ArchiveReviewRow>((await archivesTask)
                    .Select(x => new ArchiveReviewRow(x, Localizer[ArchiveStateKey(x.State)])));
                Replace(RiskChoices, (await risksTask).Select(x => new Track9LookupItem(x.Id, x.Subject)));
                Replace(CloseReasonChoices, _risks.GetRiskCloseReasons()
                    .Select(x => new Track9LookupItem(x.Value, x.Name)));
                if (_riskScope is { } scopedRisk)
                {
                    SelectedRisk = RiskChoices.FirstOrDefault(x => x.Id == scopedRisk)
                                   ?? new Track9LookupItem(scopedRisk, $"#{scopedRisk}");
                    this.RaisePropertyChanged(nameof(SelectedRisk));
                }
                SelectedArchive = selectedId is null ? null : Archives.FirstOrDefault(x => x.Id == selectedId);
                LoadError = string.Empty;
                _loaded = true;
                this.RaisePropertyChanged(nameof(IsEmpty));
            });
        }
        catch (Exception ex)
        {
            if (!_gate.IsCurrent(token)) return;
            Logger.Error(ex, "Error loading the Track 9 archive review queue");
            LoadError = ExplainError(ex);
        }
    }

    private Task ArchiveAsync() => !CanArchive || SelectedRisk?.Id is not { } risk
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9ArchiveCreated"], async () =>
        {
            await _decisions.ArchiveAsync(risk, new RiskArchiveRequest
            {
                Justification = Justification,
                CloseReason = SelectedCloseReason?.Id,
                Conditions = Conditions.Where(x => x.IsSelected)
                    .Select(x => new RiskArchiveConditionRequest
                        { TriggerType = x.TriggerType, Description = x.Description })
                    .ToList()
            });
            await ReloadAsync();
        });

    private void ResetDraft()
    {
        Justification = ReviewNote = ReopenReason = string.Empty;
        SelectedCloseReason = null;
        ReviewOutcome = ReviewOutcomes[0];
        foreach (var condition in Conditions)
        {
            condition.IsSelected = false;
            condition.Description = string.Empty;
        }
        foreach (var property in new[] { nameof(Justification), nameof(ReviewNote), nameof(ReopenReason),
                     nameof(SelectedCloseReason), nameof(ReviewOutcome) })
            this.RaisePropertyChanged(property);
    }

    private static void Replace<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private Task ReviewAsync() => !CanReview || SelectedArchive is null
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9ArchiveReviewed"], async () =>
        {
            await _decisions.ReviewArchiveAsync(SelectedArchive.RiskId, new RiskArchiveReviewRequest
            {
                Outcome = ReviewOutcome?.Value,
                Note = ReviewNote
            });
            await ReloadAsync();
        });

    private Task ReopenAsync() => !CanReopen || SelectedArchive is null
        ? Task.CompletedTask
        : RunAsync(Localizer["Track9ArchiveReopened"], async () =>
        {
            await _decisions.ReopenArchiveAsync(SelectedArchive.RiskId,
                new RiskArchiveReopenRequest { Reason = ReopenReason });
            await ReloadAsync();
        });

    private static string ArchiveStateKey(RiskArchiveState state) => state switch
    {
        RiskArchiveState.Live => "Track9ArchiveLive",
        RiskArchiveState.Reopened => "Track9ArchiveReopenedState",
        RiskArchiveState.Superseded => "Track9ArchiveSuperseded",
        _ => "Track9ArchiveSuperseded"
    };
}

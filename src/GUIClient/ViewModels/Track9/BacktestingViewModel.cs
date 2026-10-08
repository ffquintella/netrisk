using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools.Track9;
using Model.DecisionCycle;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Track9;

public sealed class BacktestingIncidentRow(BacktestIncidentDto item, string outcomeLabel)
{
    public int IncidentId => item.IncidentId;
    public string IncidentName => item.IncidentName;
    public DateTime OccurredAt => item.OccurredAt;
    public string OutcomeLabel { get; } = outcomeLabel;
}

/// <summary>Period report and typed incident/near-miss backtesting assessment (T307).</summary>
public sealed class BacktestingViewModel : ViewModelBase
{
    private readonly IDecisionCycleService _decisions = GetService<IDecisionCycleService>();
    private readonly IEntitiesService _entities = GetService<IEntitiesService>();
    private readonly IRisksService _risks = GetService<IRisksService>();
    private readonly Track9MonitoringLoadGate _reportGate = new();
    private readonly Track9MonitoringLoadGate _incidentGate = new();
    private bool _loaded;
    private string _loadError = string.Empty;
    private BacktestReportDto? _report;
    private BacktestIncidentDto? _selectedIncident;
    private BacktestingIncidentRow? _selectedReportItem;

    public string StrTitle { get; } = Localizer["Track9BacktestingTitle"];
    public string StrReload { get; } = Localizer["Reload"];
    public string StrNoItems { get; } = Localizer["Track9NoItems"];
    public string StrThirdLine { get; } = Localizer["Track9ReadOnlyThirdLine"];
    public string StrFilters { get; } = Localizer["Track9ReportFilters"];
    public string StrFrom { get; } = Localizer["Track9From"];
    public string StrTo { get; } = Localizer["Track9To"];
    public string StrEntityId { get; } = Localizer["Track9EntityId"];
    public string StrGenerate { get; } = Localizer["Track9GenerateReport"];
    public string StrIncidents { get; } = Localizer["Incidents"];
    public string StrAssessed { get; } = Localizer["Track9Assessed"];
    public string StrNotAssessed { get; } = Localizer["Track9NotAssessed"];
    public string StrUnforeseenRate { get; } = Localizer["Track9UnforeseenRate"];
    public string StrFalseNegativeRate { get; } = Localizer["Track9FalseNegativeRate"];
    public string StrAssessment { get; } = Localizer["Track9Assessment"];
    public string StrNoScenario { get; } = Localizer["Track9NoCorrespondingScenario"];
    public string StrMatchedRisks { get; } = Localizer["Track9MatchedRiskIds"];
    public string StrRiskId { get; } = Localizer["Track9RiskId"];
    public string StrAddRisk { get; } = Localizer["Track9AddMatchedRisk"];
    public string StrRemoveRisk { get; } = Localizer["Track9RemoveMatchedRisk"];
    public string StrNotes { get; } = Localizer["Notes"];
    public string StrAssess { get; } = Localizer["Track9AssessIncident"];

    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    public Track9LookupItem? SelectedEntity { get; set; }
    public Track9LookupItem? DraftRisk { get; set; }
    public int? SelectedDraftRiskId { get; set; }
    public ObservableCollection<int> DraftRiskIds { get; } = [];
    public ObservableCollection<Track9LookupItem> EntityChoices { get; } = [];
    public ObservableCollection<Track9LookupItem> RiskChoices { get; } = [];
    public bool NoCorrespondingScenario { get; set; }
    public string AssessmentNote { get; set; } = string.Empty;

    public BacktestReportDto? Report
    {
        get => _report;
        private set
        {
            this.RaiseAndSetIfChanged(ref _report, value);
            Items = new ObservableCollection<BacktestingIncidentRow>((value?.Items ?? [])
                .Select(x => new BacktestingIncidentRow(x, Localizer[BacktestOutcomeKey(x.Outcome)])));
            this.RaisePropertyChanged(nameof(Items));
            foreach (var property in new[] { nameof(HasReport), nameof(IsEmpty), nameof(IncidentCountText),
                         nameof(AssessedText), nameof(NotAssessedText), nameof(UnforeseenRateText),
                         nameof(FalseNegativeRateText) })
                this.RaisePropertyChanged(property);
        }
    }

    public ObservableCollection<BacktestingIncidentRow> Items { get; private set; } = [];
    public BacktestIncidentDto? SelectedIncident
    {
        get => _selectedIncident;
        private set
        {
            this.RaiseAndSetIfChanged(ref _selectedIncident, value);
            this.RaisePropertyChanged(nameof(HasSelectedIncident));
        }
    }

    public BacktestingIncidentRow? SelectedReportItem
    {
        get => _selectedReportItem;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedReportItem, value);
            SelectedIncident = null;
            DraftRiskIds.Clear();
            NoCorrespondingScenario = false;
            AssessmentNote = string.Empty;
            this.RaisePropertyChanged(nameof(NoCorrespondingScenario));
            this.RaisePropertyChanged(nameof(AssessmentNote));
            _ = LoadIncidentAsync(value?.IncidentId);
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

    public bool HasReport => Report is not null;
    public bool HasSelectedIncident => SelectedIncident is not null;
    public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);
    public bool IsEmpty => _loaded && Items.Count == 0 && !HasLoadError;
    public bool IsThirdLine => Track9MonitoringPermissions.IsThirdLine(AuthenticationService.AuthenticatedUserInfo);
    public bool CanWrite => Track9MonitoringPermissions.CanSubmitRiskData(AuthenticationService.AuthenticatedUserInfo);
    public string IncidentCountText => Report?.Incidents.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    public string AssessedText => Report?.Assessed.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    public string NotAssessedText => Report?.NotAssessed.ToString(CultureInfo.CurrentCulture) ?? string.Empty;
    public string UnforeseenRateText => Rate(Report?.UnforeseenRate);
    public string FalseNegativeRateText => Rate(Report?.FalseNegativeRate);

    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAddRiskClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRemoveRiskClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAssessClicked { get; }

    public BacktestingViewModel()
    {
        BtReloadClicked = ReactiveCommand.CreateFromTask(ReloadAsync);
        BtAddRiskClicked = ReactiveCommand.Create(AddRisk);
        BtRemoveRiskClicked = ReactiveCommand.Create(RemoveRisk);
        BtAssessClicked = ReactiveCommand.CreateFromTask(AssessAsync);
    }

    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : ReloadAsync();
    public Task ReloadAsync() => LoadAsync();

    public async Task LoadIncidentAsync(int? incidentId)
    {
        var token = _incidentGate.Begin();
        if (incidentId is null)
        {
            SelectedIncident = null;
            return;
        }

        try
        {
            var detail = await _decisions.GetIncidentBacktestAsync(incidentId.Value);
            if (!_incidentGate.IsCurrent(token)) return;
            SelectedIncident = detail;
            DraftRiskIds.Clear();
            foreach (var risk in detail.Risks) DraftRiskIds.Add(risk.RiskId);
            NoCorrespondingScenario = detail.AssessedAt is not null && detail.Risks.Count == 0 && detail.HiddenRiskCount == 0;
            AssessmentNote = detail.Note ?? string.Empty;
            this.RaisePropertyChanged(nameof(NoCorrespondingScenario));
            this.RaisePropertyChanged(nameof(AssessmentNote));
        }
        catch (Exception ex)
        {
            if (!_incidentGate.IsCurrent(token)) return;
            Logger.Error(ex, "Error loading incident backtest {IncidentId}", incidentId);
            LoadError = ExplainError(ex);
        }
    }

    public void Clear()
    {
        _reportGate.DiscardOutstanding();
        _incidentGate.DiscardOutstanding();
        Report = null;
        SelectedReportItem = null;
        SelectedIncident = null;
        LoadError = string.Empty;
        _loaded = false;
    }

    private async Task LoadAsync()
    {
        var token = _reportGate.Begin();
        try
        {
            await WithBusyAsync(async () =>
            {
                var selectedId = SelectedIncident?.IncidentId;
                var reportTask = _decisions.GetBacktestingReportAsync(
                    From?.UtcDateTime, To?.UtcDateTime, SelectedEntity?.Id);
                var entitiesTask = _entities.GetAllAsync();
                var risksTask = _risks.GetAllRisksAsync(true);
                await Task.WhenAll(reportTask, entitiesTask, risksTask);
                if (!_reportGate.IsCurrent(token)) return;
                Report = await reportTask;
                Replace(EntityChoices, [new Track9LookupItem(null, Localizer["Track9OrganizationWide"]),
                    .. (await entitiesTask).Select(x => new Track9LookupItem(x.Id, x.DisplayName))]);
                Replace(RiskChoices, (await risksTask).Select(x => new Track9LookupItem(x.Id, x.Subject)));
                LoadError = string.Empty;
                _loaded = true;
                SelectedReportItem = selectedId is null ? null : Items.FirstOrDefault(x => x.IncidentId == selectedId);
                this.RaisePropertyChanged(nameof(IsEmpty));
            });
        }
        catch (Exception ex)
        {
            if (!_reportGate.IsCurrent(token)) return;
            Logger.Error(ex, "Error loading the Track 9 backtesting report");
            LoadError = ExplainError(ex);
        }
    }

    private void AddRisk()
    {
        if (!CanWrite) return;
        if (DraftRisk?.Id is not { } value) return;
        var ids = Track9MonitoringInputRules.PositiveDistinctIds(DraftRiskIds.Append(value));
        DraftRiskIds.Clear();
        foreach (var id in ids) DraftRiskIds.Add(id);
        DraftRisk = null;
        NoCorrespondingScenario = false;
        this.RaisePropertyChanged(nameof(DraftRisk));
        this.RaisePropertyChanged(nameof(NoCorrespondingScenario));
    }

    private void RemoveRisk()
    {
        if (!CanWrite) return;
        if (SelectedDraftRiskId is { } id) DraftRiskIds.Remove(id);
    }

    private Task AssessAsync() => !CanWrite || SelectedIncident is null
        || !Track9MonitoringInputRules.CanAssessBacktest(DraftRiskIds, NoCorrespondingScenario)
            ? Task.CompletedTask
            : RunAsync(Localizer["Track9IncidentAssessed"], async () =>
            {
                _selectedIncident = await _decisions.AssessIncidentAsync(SelectedIncident.IncidentId,
                    new BacktestAssessmentRequest
                    {
                        RiskIds = Track9MonitoringInputRules.PositiveDistinctIds(DraftRiskIds),
                        NoCorrespondingScenario = NoCorrespondingScenario,
                        Note = AssessmentNote
                    });
                this.RaisePropertyChanged(nameof(SelectedIncident));
                await ReloadAsync();
            });

    private static string Rate(double? value) => value is null
        ? string.Empty
        : value.Value.ToString("P1", CultureInfo.CurrentCulture);

    private static void Replace<T>(ObservableCollection<T> target, System.Collections.Generic.IEnumerable<T> values)
    {
        target.Clear();
        foreach (var value in values) target.Add(value);
    }

    private static string BacktestOutcomeKey(BacktestOutcome outcome) => outcome switch
    {
        BacktestOutcome.NotAssessed => "Track9BacktestNotAssessed",
        BacktestOutcome.NotForeseen => "Track9BacktestNotForeseen",
        BacktestOutcome.RegisteredAfterOccurrence => "Track9BacktestRegisteredAfter",
        BacktestOutcome.ForeseenTreated => "Track9BacktestForeseenTreated",
        BacktestOutcome.ForeseenDismissed => "Track9BacktestForeseenDismissed",
        _ => "Track9BacktestNotAssessed"
    };
}

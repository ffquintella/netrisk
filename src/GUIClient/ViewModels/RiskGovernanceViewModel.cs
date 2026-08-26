using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Entities;
using DAL.Enums;
using Model.DTO;
using Model.Governance;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels;

/// <summary>
/// One risk's governance record (Track 8): the acceptance in force and its history (8.1.4), both
/// scores with the delta (8.2.2), the counter-signature a threshold-crossing review is waiting for
/// (8.3.4), the treatment tasks (8.5.3), quantitative scoring (8.7.2), and the field-level change
/// history (8.4).
///
/// One dialog rather than six panels bolted onto the risk editor. The risk editor is already the
/// largest view in the application, and these six things are read together — "is this accepted, by
/// whom, until when, and what is anybody doing about it" is one question.
/// </summary>
public class RiskGovernanceViewModel
    : ParameterizedDialogViewModelBaseAsync<RiskGovernanceDialogResult, RiskGovernanceDialogParameter>
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["Governance"];
    public string StrAcceptances { get; } = Localizer["Acceptances"];
    public string StrAcceptRisk { get; } = Localizer["AcceptRisk"];
    public string StrRenew { get; } = Localizer["RenewAcceptance"];
    public string StrRevoke { get; } = Localizer["RevokeAcceptance"];
    public string StrBusinessJustification { get; } = Localizer["BusinessJustification"];
    public string StrExpiresAt { get; } = Localizer["ExpiresAt"];
    public string StrAcceptedUntil { get; } = Localizer["AcceptedUntil"];
    public string StrReason { get; } = Localizer["Reason"];
    public string StrStatus { get; } = Localizer["Status"];
    public string StrInherent { get; } = Localizer["Inherent"];
    public string StrResidual { get; } = Localizer["Residual"];
    public string StrResidualDelta { get; } = Localizer["ResidualDelta"];
    public string StrCounterSign { get; } = Localizer["CounterSign"];
    public string StrMitigationTasks { get; } = Localizer["MitigationTasks"];
    public string StrAddTask { get; } = Localizer["AddTask"];
    public string StrTaskTitle { get; } = Localizer["TaskTitle"];
    public string StrOwner { get; } = Localizer["Owner"];
    public string StrDueDate { get; } = Localizer["DueDate"];
    public string StrQuantitativeScoring { get; } = Localizer["QuantitativeScoring"];
    public string StrLossEventFrequency { get; } = Localizer["LossEventFrequency"];
    public string StrLossMagnitude { get; } = Localizer["LossMagnitude"];
    public string StrMinimum { get; } = Localizer["Minimum"];
    public string StrMostLikely { get; } = Localizer["MostLikely"];
    public string StrMaximum { get; } = Localizer["Maximum"];
    public string StrRunSimulation { get; } = Localizer["RunSimulation"];
    public string StrAnnualisedLossExposure { get; } = Localizer["AnnualisedLossExposure"];
    public string StrLossExceedanceCurve { get; } = Localizer["LossExceedanceCurve"];
    public string StrAuditTrail { get; } = Localizer["AuditTrail"];
    public string StrField { get; } = Localizer["Field"];
    public string StrOldValue { get; } = Localizer["OldValue"];
    public string StrNewValue { get; } = Localizer["NewValue"];
    public string StrActor { get; } = Localizer["Actor"];
    public string StrOccurredAt { get; } = Localizer["OccurredAt"];
    public string StrRequestReview { get; } = Localizer["RequestReview"];

    #endregion

    #region SERVICES

    private IRiskGovernanceService GovernanceService { get; } = GetService<IRiskGovernanceService>();

    private IRisksService RisksService { get; } = GetService<IRisksService>();

    #endregion

    #region PROPERTIES

    private int _riskId;

    public int RiskId
    {
        get => _riskId;
        set => this.RaiseAndSetIfChanged(ref _riskId, value);
    }

    private string _riskSubject = string.Empty;

    public string RiskSubject
    {
        get => _riskSubject;
        set => this.RaiseAndSetIfChanged(ref _riskSubject, value);
    }

    public ObservableCollection<RiskAcceptance> Acceptances { get; } = [];

    private RiskAcceptance? _activeAcceptance;

    /// <summary>The acceptance in force, or null. Null is an ordinary state, not an error.</summary>
    public RiskAcceptance? ActiveAcceptance
    {
        get => _activeAcceptance;
        set
        {
            this.RaiseAndSetIfChanged(ref _activeAcceptance, value);
            this.RaisePropertyChanged(nameof(IsAccepted));
            this.RaisePropertyChanged(nameof(CanAccept));
        }
    }

    public bool IsAccepted => ActiveAcceptance is not null;

    /// <summary>
    /// False when the risk already carries a live acceptance or the appetite refuses one. Two
    /// separate reasons, both surfaced in <see cref="AppetiteExplanation"/> rather than left as a
    /// greyed-out button with no explanation (IX-4).
    /// </summary>
    public bool CanAccept => ActiveAcceptance is null && Appetite?.ExceedsCeiling != true;

    private AppetiteEvaluation? _appetite;

    public AppetiteEvaluation? Appetite
    {
        get => _appetite;
        set
        {
            this.RaiseAndSetIfChanged(ref _appetite, value);
            this.RaisePropertyChanged(nameof(AppetiteExplanation));
            this.RaisePropertyChanged(nameof(CanAccept));
        }
    }

    public string AppetiteExplanation => Appetite?.Explanation ?? string.Empty;

    private string? _justification;

    public string? Justification
    {
        get => _justification;
        set => this.RaiseAndSetIfChanged(ref _justification, value);
    }

    private DateTimeOffset? _expiresAt = DateTimeOffset.UtcNow.AddDays(90);

    public DateTimeOffset? ExpiresAt
    {
        get => _expiresAt;
        set => this.RaiseAndSetIfChanged(ref _expiresAt, value);
    }

    private string? _revocationReason;

    public string? RevocationReason
    {
        get => _revocationReason;
        set => this.RaiseAndSetIfChanged(ref _revocationReason, value);
    }

    private float? _inherent;

    public float? Inherent
    {
        get => _inherent;
        set
        {
            this.RaiseAndSetIfChanged(ref _inherent, value);
            this.RaisePropertyChanged(nameof(ResidualDelta));
        }
    }

    private float? _residual;

    public float? Residual
    {
        get => _residual;
        set
        {
            this.RaiseAndSetIfChanged(ref _residual, value);
            this.RaisePropertyChanged(nameof(ResidualDelta));
        }
    }

    /// <summary>
    /// Inherent minus residual. Null rather than zero when the residual has never been assessed:
    /// "nobody has looked at the treatment" and "the treatment achieves nothing" are opposite
    /// statements and an auditor cares which.
    /// </summary>
    public float? ResidualDelta => Inherent is null || Residual is null ? null : Inherent - Residual;

    public ObservableCollection<MgmtReview> Reviews { get; } = [];

    private MgmtReview? _selectedReview;

    public MgmtReview? SelectedReview
    {
        get => _selectedReview;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedReview, value);
            this.RaisePropertyChanged(nameof(CanCounterSign));
        }
    }

    /// <summary>
    /// A review can be counter-signed only if it asked for one and has not had it. The server
    /// enforces the rest — a distinct approver holding the top band, and not the risk's own
    /// owner — and its refusal is what the user sees.
    /// </summary>
    public bool CanCounterSign =>
        SelectedReview is { RequiresCountersignature: true, SecondReviewerId: null };

    public ObservableCollection<MitigationTask> Tasks { get; } = [];

    private MitigationTask? _selectedTask;

    public MitigationTask? SelectedTask
    {
        get => _selectedTask;
        set => this.RaiseAndSetIfChanged(ref _selectedTask, value);
    }

    private string? _newTaskTitle;

    public string? NewTaskTitle
    {
        get => _newTaskTitle;
        set => this.RaiseAndSetIfChanged(ref _newTaskTitle, value);
    }

    private int? _newTaskOwnerId;

    public int? NewTaskOwnerId
    {
        get => _newTaskOwnerId;
        set => this.RaiseAndSetIfChanged(ref _newTaskOwnerId, value);
    }

    private DateTimeOffset? _newTaskDueDate = DateTimeOffset.UtcNow.AddDays(30);

    public DateTimeOffset? NewTaskDueDate
    {
        get => _newTaskDueDate;
        set => this.RaiseAndSetIfChanged(ref _newTaskDueDate, value);
    }

    private int? _mitigationId;

    /// <summary>
    /// The treatment plan tasks hang off. Null when the risk has none, which is why the add button
    /// is disabled rather than failing: a task with no plan would be unreachable from the editor.
    /// </summary>
    public int? MitigationId
    {
        get => _mitigationId;
        set
        {
            this.RaiseAndSetIfChanged(ref _mitigationId, value);
            this.RaisePropertyChanged(nameof(CanAddTask));
        }
    }

    public bool CanAddTask => MitigationId is not null;

    // --- 8.7.2 quantitative -------------------------------------------------------------------

    private double _lefMin = 0.1;
    public double LefMin { get => _lefMin; set => this.RaiseAndSetIfChanged(ref _lefMin, value); }

    private double _lefMostLikely = 0.5;
    public double LefMostLikely
    {
        get => _lefMostLikely;
        set => this.RaiseAndSetIfChanged(ref _lefMostLikely, value);
    }

    private double _lefMax = 2;
    public double LefMax { get => _lefMax; set => this.RaiseAndSetIfChanged(ref _lefMax, value); }

    private double _lossMin = 10_000;
    public double LossMin { get => _lossMin; set => this.RaiseAndSetIfChanged(ref _lossMin, value); }

    private double _lossMostLikely = 50_000;
    public double LossMostLikely
    {
        get => _lossMostLikely;
        set => this.RaiseAndSetIfChanged(ref _lossMostLikely, value);
    }

    private double _lossMax = 500_000;
    public double LossMax { get => _lossMax; set => this.RaiseAndSetIfChanged(ref _lossMax, value); }

    private QuantitativeRiskResult? _quantitative;

    public QuantitativeRiskResult? Quantitative
    {
        get => _quantitative;
        set
        {
            this.RaiseAndSetIfChanged(ref _quantitative, value);
            this.RaisePropertyChanged(nameof(HasQuantitative));

            LossExceedanceCurve.Clear();
            foreach (var point in value?.LossExceedanceCurve ?? []) LossExceedanceCurve.Add(point);
        }
    }

    public bool HasQuantitative => Quantitative is not null;

    public ObservableCollection<LossExceedancePointDto> LossExceedanceCurve { get; } = [];

    public ObservableCollection<AuditLog> AuditTrail { get; } = [];

    private bool _acceptanceChanged;

    private bool _scoresChanged;

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtAcceptClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRenewClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRevokeClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtCounterSignClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtAddTaskClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRunSimulationClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRequestReviewClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtReloadClicked { get; }

    #endregion

    public RiskGovernanceViewModel()
    {
        BtAcceptClicked = ReactiveCommand.CreateFromTask(AcceptAsync);
        BtRenewClicked = ReactiveCommand.CreateFromTask(RenewAsync);
        BtRevokeClicked = ReactiveCommand.CreateFromTask(RevokeAsync);
        BtCounterSignClicked = ReactiveCommand.CreateFromTask(CounterSignAsync);
        BtAddTaskClicked = ReactiveCommand.CreateFromTask(AddTaskAsync);
        BtRunSimulationClicked = ReactiveCommand.CreateFromTask(RunSimulationAsync);
        BtRequestReviewClicked = ReactiveCommand.CreateFromTask(RequestReviewAsync);
        BtReloadClicked = ReactiveCommand.CreateFromTask(LoadAsync);
    }

    public override async Task ActivateAsync(RiskGovernanceDialogParameter parameter,
        CancellationToken cancellationToken = default)
    {
        RiskId = parameter.RiskId;
        RiskSubject = parameter.RiskSubject;

        await LoadAsync();
    }

    private async Task LoadAsync()
    {
        await WithBusyAsync(async () =>
        {
            var acceptances = await GovernanceService.GetAcceptancesAsync(RiskId);

            Acceptances.Clear();
            foreach (var acceptance in acceptances) Acceptances.Add(acceptance);

            ActiveAcceptance = await GovernanceService.GetActiveAcceptanceAsync(RiskId);
            Appetite = await GovernanceService.GetAppetiteEvaluationAsync(RiskId);

            var scores = await GovernanceService.GetScorePairsAsync([RiskId]);
            var pair = scores.FirstOrDefault(s => s.RiskId == RiskId);
            Inherent = pair?.Inherent;
            Residual = pair?.Residual;

            var reviews = RisksService.GetRiskMgmtReviews(RiskId);
            Reviews.Clear();
            foreach (var review in reviews.OrderByDescending(r => r.SubmissionDate)) Reviews.Add(review);

            var tasks = await GovernanceService.GetTasksByRiskAsync(RiskId);
            Tasks.Clear();
            foreach (var task in tasks) Tasks.Add(task);
            MitigationId = tasks.FirstOrDefault()?.MitigationId ?? MitigationId;

            Quantitative = await GovernanceService.GetQuantitativeAsync(RiskId);

            var trail = await GovernanceService.GetRiskAuditTrailAsync(RiskId, 200);
            AuditTrail.Clear();
            foreach (var entry in trail) AuditTrail.Add(entry);
        });
    }

    // --- 8.1.4 acceptance ---------------------------------------------------------------------

    private RiskAcceptanceRequest? BuildAcceptanceRequest()
    {
        if (string.IsNullOrWhiteSpace(Justification) || ExpiresAt is null ||
            ExpiresAt.Value.UtcDateTime.Date <= DateTime.UtcNow.Date)
        {
            Toasts.Error(Localizer["AcceptanceNeedsJustificationMSG"]);
            return null;
        }

        return new RiskAcceptanceRequest
        {
            BusinessJustification = Justification!.Trim(),
            // End of the chosen day: a manager picking a date means "until the end of it", and
            // midnight would expire the acceptance a day early.
            ExpiresAt = DateTime.SpecifyKind(
                ExpiresAt.Value.UtcDateTime.Date.AddDays(1).AddSeconds(-1), DateTimeKind.Utc)
        };
    }

    private async Task AcceptAsync()
    {
        var request = BuildAcceptanceRequest();
        if (request is null) return;

        await RunAsync(Localizer["AcceptanceRecordedMSG"], async () =>
        {
            await GovernanceService.CreateAcceptanceAsync(RiskId, request);
            _acceptanceChanged = true;
            Justification = null;
            await LoadAsync();
        });
    }

    private async Task RenewAsync()
    {
        if (ActiveAcceptance is null) return;

        var request = BuildAcceptanceRequest();
        if (request is null) return;

        await RunAsync(Localizer["AcceptanceRenewedMSG"], async () =>
        {
            await GovernanceService.RenewAcceptanceAsync(RiskId, ActiveAcceptance.Id, request);
            _acceptanceChanged = true;
            Justification = null;
            await LoadAsync();
        });
    }

    private async Task RevokeAsync()
    {
        if (ActiveAcceptance is null) return;

        if (string.IsNullOrWhiteSpace(RevocationReason))
        {
            Toasts.Error(Localizer["DismissalNeedsAReasonMSG"]);
            return;
        }

        await RunAsync(Localizer["AcceptanceRevokedMSG"], async () =>
        {
            await GovernanceService.RevokeAcceptanceAsync(RiskId, ActiveAcceptance.Id,
                RevocationReason!.Trim());

            _acceptanceChanged = true;
            RevocationReason = null;
            await LoadAsync();
        });
    }

    // --- 8.3.4 counter-signature --------------------------------------------------------------

    private async Task CounterSignAsync()
    {
        if (SelectedReview is null) return;

        await RunAsync(Localizer["CounterSignedMSG"], async () =>
        {
            await GovernanceService.CountersignAsync(RiskId, SelectedReview.Id);
            await LoadAsync();
        });
    }

    // --- 8.5.3 treatment tasks ----------------------------------------------------------------

    private async Task AddTaskAsync()
    {
        if (MitigationId is null || string.IsNullOrWhiteSpace(NewTaskTitle)) return;

        await RunAsync(Localizer["TaskSavedMSG"], async () =>
        {
            await GovernanceService.CreateTaskAsync(new MitigationTaskRequest
            {
                MitigationId = MitigationId.Value,
                Title = NewTaskTitle!.Trim(),
                OwnerId = NewTaskOwnerId,
                DueDate = NewTaskDueDate?.UtcDateTime.Date
            });

            NewTaskTitle = null;
            await LoadAsync();
        });
    }

    // --- 8.7.2 quantitative -------------------------------------------------------------------

    private async Task RunSimulationAsync()
    {
        await RunAsync(Localizer["SimulationCompletedMSG"], async () =>
        {
            Quantitative = await GovernanceService.ComputeQuantitativeAsync(RiskId,
                new QuantitativeRiskInput
                {
                    LossEventFrequencyMin = LefMin,
                    LossEventFrequencyMostLikely = LefMostLikely,
                    LossEventFrequencyMax = LefMax,
                    LossMagnitudeMin = LossMin,
                    LossMagnitudeMostLikely = LossMostLikely,
                    LossMagnitudeMax = LossMax
                });

            // The simulation rewrites the mapped 0–10 score and the residual, so the caller has to
            // refresh the row rather than keep showing the matrix numbers.
            _scoresChanged = true;

            await LoadAsync();
        });
    }

    // --- 8.5.1 out-of-cadence review ----------------------------------------------------------

    private async Task RequestReviewAsync()
    {
        await RunAsync(Localizer["ReviewRequestedMSG"], async () =>
        {
            await GovernanceService.RequestReviewAsync(RiskId,
                Localizer["RequestReview"] + " — " + RiskSubject);
        });
    }

    /// <summary>
    /// Closes the dialog reporting what changed, so the risk list refreshes the affected row instead
    /// of reloading the whole register (IX-2: the caller updates its own state from a typed result).
    /// </summary>
    public void CloseWithResult() => Close(new RiskGovernanceDialogResult
    {
        Action = _acceptanceChanged || _scoresChanged ? ResultActions.Ok : ResultActions.Cancel,
        AcceptanceChanged = _acceptanceChanged,
        ScoresChanged = _scoresChanged
    });

    public ReactiveCommand<RxVoid, RxVoid> BtCloseClicked =>
        ReactiveCommand.Create<RxVoid, RxVoid>(_ =>
        {
            CloseWithResult();
            return default;
        });
}

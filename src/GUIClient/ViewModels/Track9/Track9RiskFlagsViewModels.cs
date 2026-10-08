using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools.Track9;
using Model.RiskFlags;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Track9;

public sealed class RiskFlagRow
{
    public RiskFlagRow(RiskFlagStateDto value, Func<RiskFlagCode, string> label, string yes, string no)
    {
        Value = value;
        Name = label(value.Code);
        Number = value.Number?.ToString(CultureInfo.CurrentCulture) ?? "—";
        IsSet = value.IsSet ? yes : no;
        Declared = value.Declared ? yes : no;
        Derived = value.Derived ? yes : no;
    }

    public RiskFlagStateDto Value { get; }
    public string Number { get; }
    public string Name { get; }
    public string IsSet { get; }
    public string Declared { get; }
    public string Derived { get; }
    public string? DeclaredReason => Value.DeclaredReason;
    public string? DerivedBasis => Value.DerivedBasis;
}

public sealed class RiskDecisionRow(RiskDecisionDto value, string decision)
{
    public RiskDecisionDto Value { get; } = value;
    public string Decision { get; } = decision;
    public string? Reason => Value.Reason;
    public DateTime DecidedAt => Value.DecidedAt;
}

public sealed class RiskFlagsPanelViewModel : Track9RiskViewModelBase
{
    private readonly IRiskFlagsService _service = GetService<IRiskFlagsService>();
    private int? _riskId;
    private int _loadVersion;
    private RiskFlagsStateDto? _state;
    private RiskFlagRow? _selectedFlag;
    private string? _flagReason;
    private Track9Choice? _selectedDecision;
    private string? _decisionReason;

    public RiskFlagsPanelViewModel()
    {
        DecisionOptions = Enum.GetValues<RiskDecisionKind>()
            .Select(v => new Track9Choice(v, LocalizedEnum(v))).ToList();
        _selectedDecision = DecisionOptions.FirstOrDefault();

        ReloadCommand = ReactiveCommand.CreateFromTask(() => LoadRiskAsync(_riskId));
        RefreshCommand = ReactiveCommand.CreateFromTask(RefreshAsync);
        DeclareCommand = ReactiveCommand.CreateFromTask(DeclareAsync);
        WithdrawCommand = ReactiveCommand.CreateFromTask(WithdrawAsync);
        RecordDecisionCommand = ReactiveCommand.CreateFromTask(RecordDecisionAsync);
    }

    public string StrTitle { get; } = Localizer["Track9RiskFlags"];
    public string StrGateA { get; } = Localizer["Track9GateA"];
    public string StrFlag { get; } = Localizer["Track9Flag"];
    public string StrSet { get; } = Localizer["Track9Set"];
    public string StrDeclared { get; } = Localizer["Track9Declared"];
    public string StrDerived { get; } = Localizer["Track9Derived"];
    public string StrReason { get; } = Localizer["Track9Reason"];
    public string StrDeclare { get; } = Localizer["Track9Declare"];
    public string StrWithdraw { get; } = Localizer["Track9Withdraw"];
    public string StrRefreshDerived { get; } = Localizer["Track9RefreshDerived"];
    public string StrCurrentDecision { get; } = Localizer["Track9CurrentDecision"];
    public string StrDecisionHistory { get; } = Localizer["Track9DecisionHistory"];
    public string StrRecordDecision { get; } = Localizer["Track9RecordDecision"];

    public ObservableCollection<RiskFlagRow> Flags { get; } = [];
    public ObservableCollection<RiskDecisionRow> Decisions { get; } = [];
    public List<Track9Choice> DecisionOptions { get; }

    public bool HasRisk => _riskId is not null;
    public RiskFlagsStateDto? State
    {
        get => _state;
        private set
        {
            this.RaiseAndSetIfChanged(ref _state, value);
            this.RaisePropertyChanged(nameof(GateAText));
            this.RaisePropertyChanged(nameof(GateAExplanation));
            this.RaisePropertyChanged(nameof(CurrentDecisionText));
        }
    }

    public string GateAText => State is null ? StrNotAvailable : State.GateA.Holds ? Localizer["Yes"].Value : Localizer["No"].Value;
    public string GateAExplanation => State?.GateA.Explanation ?? StrNotAvailable;
    public string CurrentDecisionText => State?.CurrentDecision is { } decision
        ? $"{LocalizedEnum(decision.Decision)} — {decision.Reason}"
        : StrNotAvailable;

    public RiskFlagRow? SelectedFlag
    {
        get => _selectedFlag;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedFlag, value);
            FlagReason = string.Empty;
            RaiseWriteState();
        }
    }

    public string? FlagReason
    {
        get => _flagReason;
        set
        {
            this.RaiseAndSetIfChanged(ref _flagReason, value);
            RaiseWriteState();
        }
    }

    public Track9Choice? SelectedDecision
    {
        get => _selectedDecision;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedDecision, value);
            RaiseWriteState();
        }
    }

    public string? DecisionReason
    {
        get => _decisionReason;
        set
        {
            this.RaiseAndSetIfChanged(ref _decisionReason, value);
            RaiseWriteState();
        }
    }

    public bool CanDeclare => SelectedFlag is not null && !SelectedFlag.Value.Declared &&
                              Track9RiskPresentation.CanDeclareFlags(User) && FlagError is null;
    public bool CanRefresh => HasRisk && Track9RiskPresentation.CanDeclareFlags(User);
    public bool CanEditFlag => Track9RiskPresentation.CanDeclareFlags(User) || Track9RiskPresentation.CanReview(User);
    public bool CanEditDecision => Track9RiskPresentation.CanReview(User);
    public bool CanWithdraw => SelectedFlag?.Value.Declared == true && Track9RiskPresentation.CanReview(User) && FlagError is null;
    public bool CanRecordDecision => SelectedDecision is not null && Track9RiskPresentation.CanReview(User) && DecisionError is null;
    public string? FlagError => Track9RiskPresentation.ReasonError(FlagReason, RiskFlagCatalogue.MaxReasonLength,
        "Track9ReasonRequired", "Track9ReasonTooLong") is { } key ? Localizer[key].Value : null;
    public string? DecisionError => Track9RiskPresentation.ReasonError(DecisionReason, RiskFlagCatalogue.MaxDecisionReasonLength,
        "Track9ReasonRequired", "Track9DecisionReasonTooLong") is { } key ? Localizer[key].Value : null;

    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RefreshCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeclareCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> WithdrawCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> RecordDecisionCommand { get; }

    public async Task LoadRiskAsync(int? riskId)
    {
        var loadVersion = ++_loadVersion;
        _riskId = riskId;
        LoadError = null;
        this.RaisePropertyChanged(nameof(HasRisk));
        this.RaisePropertyChanged(nameof(CanRefresh));
        Flags.Clear();
        Decisions.Clear();
        State = null;
        SelectedFlag = null;
        if (riskId is not { } id || !Track9RiskPresentation.CanReadRisks(User)) return;

        bool IsCurrent() => Track9RiskPresentation.IsLatestLoad(loadVersion, _loadVersion) &&
                            Track9RiskPresentation.IsCurrentSelection(id, _riskId);

        await ReadAsync("risk flags", async () =>
        {
            var state = await _service.GetRiskFlagsAsync(id);
            var decisions = await _service.GetDecisionsAsync(id);
            if (!IsCurrent()) return;
            ApplyState(state);
            foreach (var decision in decisions.OrderByDescending(d => d.DecidedAt))
                Decisions.Add(new RiskDecisionRow(decision, LocalizedEnum(decision.Decision)));
        }, IsCurrent);
    }

    public async Task RefreshAsync()
    {
        if (_riskId is not { } id) return;
        var operationVersion = ++_loadVersion;
        await RunAsync(Localizer["Track9Saved"], async () =>
        {
            var saved = await _service.RefreshAsync(id);
            if (Track9RiskPresentation.IsLatestLoad(operationVersion, _loadVersion) &&
                Track9RiskPresentation.IsCurrentSelection(id, _riskId)) ApplyState(saved);
        });
    }

    public async Task DeclareAsync()
    {
        if (_riskId is not { } id || SelectedFlag is null || !CanDeclare) return;
        var operationVersion = ++_loadVersion;
        var code = SelectedFlag.Value.Code;
        var reason = FlagReason!.Trim();
        await RunAsync(Localizer["Track9Saved"], async () =>
        {
            var saved = await _service.DeclareAsync(id, code, reason);
            if (!Track9RiskPresentation.IsLatestLoad(operationVersion, _loadVersion) ||
                !Track9RiskPresentation.IsCurrentSelection(id, _riskId)) return;
            ApplyState(saved);
            FlagReason = string.Empty;
        });
    }

    public async Task WithdrawAsync()
    {
        if (_riskId is not { } id || SelectedFlag is null || !CanWithdraw) return;
        var operationVersion = ++_loadVersion;
        var code = SelectedFlag.Value.Code;
        var reason = FlagReason!.Trim();
        await RunAsync(Localizer["Track9Saved"], async () =>
        {
            var saved = await _service.WithdrawAsync(id, code, reason);
            if (!Track9RiskPresentation.IsLatestLoad(operationVersion, _loadVersion) ||
                !Track9RiskPresentation.IsCurrentSelection(id, _riskId)) return;
            ApplyState(saved);
            FlagReason = string.Empty;
        });
    }

    public async Task RecordDecisionAsync()
    {
        if (_riskId is not { } id || SelectedDecision?.Value is not RiskDecisionKind decision || !CanRecordDecision) return;
        var operationVersion = ++_loadVersion;
        var reason = DecisionReason!.Trim();
        await RunAsync(Localizer["Track9Saved"], async () =>
        {
            var saved = await _service.RecordDecisionAsync(id, decision, reason);
            if (!Track9RiskPresentation.IsLatestLoad(operationVersion, _loadVersion) ||
                !Track9RiskPresentation.IsCurrentSelection(id, _riskId)) return;
            Decisions.Insert(0, new RiskDecisionRow(saved, LocalizedEnum(saved.Decision)));
            if (State is not null) State.CurrentDecision = saved;
            this.RaisePropertyChanged(nameof(CurrentDecisionText));
            DecisionReason = string.Empty;
        });
    }

    private void ApplyState(RiskFlagsStateDto state)
    {
        State = state;
        Flags.Clear();
        foreach (var flag in state.Flags.Concat([state.NoLegitimateAcceptance]))
            Flags.Add(new RiskFlagRow(flag, code => LocalizedEnum(code), Localizer["Yes"], Localizer["No"]));
        SelectedFlag = Flags.FirstOrDefault();
    }

    private void RaiseWriteState()
    {
        foreach (var property in new[] { nameof(CanDeclare), nameof(CanRefresh), nameof(CanEditFlag), nameof(CanEditDecision), nameof(CanWithdraw), nameof(CanRecordDecision), nameof(FlagError), nameof(DecisionError) })
            this.RaisePropertyChanged(property);
    }
}

public sealed class FlaggedRiskRow(FlaggedRiskDto value, Func<RiskFlagCode, string> label)
{
    public FlaggedRiskDto Value { get; } = value;
    public int RiskId => Value.RiskId;
    public string ReferenceId => Value.ReferenceId;
    public string Subject => Value.Subject;
    public string Status => Value.Status;
    public string Flags => string.Join(", ", Value.Flags.Select(label));
    public bool GateA => Value.GateA;
}

public sealed class RiskFlagFilterViewModel : Track9RiskViewModelBase
{
    private readonly IRiskFlagsService _service = GetService<IRiskFlagsService>();
    private bool _loaded;
    private int _loadVersion;
    private Track9Choice? _selectedFlag;
    private Track9Choice? _selectedGateA;

    public RiskFlagFilterViewModel()
    {
        GateAOptions =
        [
            new Track9Choice(string.Empty, Localizer["Track9AllGateAStates"]),
            new Track9Choice(true, Localizer["Track9GateAOnly"]),
            new Track9Choice(false, Localizer["Track9WithoutGateA"])
        ];
        _selectedGateA = GateAOptions[0];
        ReloadCommand = ReactiveCommand.CreateFromTask(LoadAsync);
    }

    public string StrTitle { get; } = Localizer["Track9FlaggedRisks"];
    public string StrFlag { get; } = Localizer["Track9Flag"];
    public string StrGateA { get; } = Localizer["Track9GateA"];
    public string StrRisk { get; } = Localizer["Track9Risk"];
    public string StrFlags { get; } = Localizer["Track9Flags"];
    public List<Track9Choice> FlagOptions { get; } = [];
    public List<Track9Choice> GateAOptions { get; }
    public ObservableCollection<FlaggedRiskRow> Rows { get; } = [];

    public Track9Choice? SelectedFlag
    {
        get => _selectedFlag;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedFlag, value);
            if (_loaded) _ = LoadAsync();
        }
    }

    public Track9Choice? SelectedGateA
    {
        get => _selectedGateA;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedGateA, value);
            if (_loaded) _ = LoadAsync();
        }
    }

    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }

    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : LoadAsync();

    public async Task LoadAsync()
    {
        var loadVersion = ++_loadVersion;
        LoadError = null;
        if (!Track9RiskPresentation.CanReadRisks(User)) return;
        bool IsCurrent() => Track9RiskPresentation.IsLatestLoad(loadVersion, _loadVersion);
        await ReadAsync("flagged risks", async () =>
        {
            if (FlagOptions.Count == 0)
            {
                var catalogue = await _service.GetCatalogueAsync();
                if (!IsCurrent()) return;
                FlagOptions.Add(new Track9Choice(string.Empty, Localizer["Track9AllFlags"]));
                foreach (var flag in catalogue)
                    FlagOptions.Add(new Track9Choice(flag.Code, LocalizedEnum(flag.Code)));
                SelectedFlag ??= FlagOptions[0];
            }

            RiskFlagCode? flagCode = SelectedFlag?.Value is RiskFlagCode code ? code : null;
            bool? gateA = SelectedGateA?.Value is bool value ? value : null;
            var answer = await _service.GetFlaggedAsync(flagCode, gateA);
            if (!IsCurrent()) return;
            Rows.Clear();
            foreach (var risk in answer) Rows.Add(new FlaggedRiskRow(risk, c => LocalizedEnum(c)));
            _loaded = true;
        }, IsCurrent);
    }
}

public sealed class TopRiskRow(TopRiskDto value, Func<RiskFlagCode, string> flagLabel,
    Func<RiskDecisionKind, string> decisionLabel, Func<NextDecisionKind, string> nextDecisionLabel,
    string overdue, string notAvailable)
{
    public int Rank => value.Rank;
    public int RiskId => value.RiskId;
    public string ReferenceId => value.ReferenceId;
    public string Subject => value.Subject;
    public string Status => value.Status;
    public bool GateA => value.GateA;
    public string Flags => string.Join(", ", value.Flags.Select(flagLabel));
    public string Decision => value.Decision is { } decision ? decisionLabel(decision) : notAvailable;
    public string ExpectedAnnualLoss => Track9RiskPresentation.Money(value.ExpectedAnnualLoss, notAvailable);
    public string NextDecision
    {
        get
        {
            var text = nextDecisionLabel(value.NextDecision.Kind);
            if (value.NextDecision.DueAt is { } dueAt) text = $"{text} — {dueAt:g}";
            return value.NextDecision.Overdue ? $"{text} — {overdue}" : text;
        }
    }
}

public sealed class TopRisksViewModel : Track9RiskViewModelBase
{
    private readonly IRiskFlagsService _service = GetService<IRiskFlagsService>();
    private bool _loaded;
    private int _loadVersion;
    private decimal _limit = 10;

    public TopRisksViewModel() => ReloadCommand = ReactiveCommand.CreateFromTask(ReloadAsync);

    public string StrTitle { get; } = Localizer["Track9TopRisks"];
    public string StrRank { get; } = Localizer["Track9Rank"];
    public string StrRisk { get; } = Localizer["Track9Risk"];
    public string StrGateA { get; } = Localizer["Track9GateA"];
    public string StrFlags { get; } = Localizer["Track9Flags"];
    public string StrDecision { get; } = Localizer["Track9Decision"];
    public string StrExpectedAnnualLoss { get; } = Localizer["Track9ExpectedAnnualLoss"];
    public string StrNextDecision { get; } = Localizer["Track9NextDecision"];
    public string StrOverdue { get; } = Localizer["Track9Overdue"];
    public ObservableCollection<TopRiskRow> Rows { get; } = [];

    public decimal Limit
    {
        get => _limit;
        set => this.RaiseAndSetIfChanged(ref _limit, Math.Clamp(value, 1, 50));
    }

    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }
    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : ReloadAsync();

    public async Task ReloadAsync()
    {
        var loadVersion = ++_loadVersion;
        LoadError = null;
        if (!Track9RiskPresentation.CanReadRisks(User)) return;
        bool IsCurrent() => Track9RiskPresentation.IsLatestLoad(loadVersion, _loadVersion);
        var limit = (int)Limit;
        await ReadAsync("top risks", async () =>
        {
            var answer = await _service.GetTopRisksAsync(limit);
            if (!IsCurrent()) return;
            Rows.Clear();
            foreach (var risk in answer.Items)
                Rows.Add(new TopRiskRow(risk, c => LocalizedEnum(c), d => LocalizedEnum(d),
                    next => LocalizedEnum(next), StrOverdue, StrNotAvailable));
            _loaded = true;
        }, IsCurrent);
    }
}

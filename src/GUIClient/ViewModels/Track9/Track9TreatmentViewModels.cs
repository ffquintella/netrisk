using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools.Track9;
using Model.TreatmentEconomics;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Track9;

public sealed class MitigationEconomicsViewModel : Track9RiskViewModelBase
{
    private readonly ITreatmentEconomicsService _service = GetService<ITreatmentEconomicsService>();
    private int? _mitigationId;
    private int _loadVersion;
    private MitigationEconomicsDto? _result;
    private Track9Choice? _selectedOption;
    private string? _transferCounterparty;
    private decimal? _oneTime;
    private decimal? _annual;
    private decimal? _sideEffectsAnnual;
    private decimal? _horizonYears;
    private string? _costBasis;
    private decimal? _effortPersonDays;
    private decimal? _durationDays;

    public MitigationEconomicsViewModel()
    {
        OptionChoices = Enum.GetValues<TreatmentOption>().Select(v => new Track9Choice(v, LocalizedEnum(v))).ToList();
        ReloadCommand = ReactiveCommand.CreateFromTask(() => LoadMitigationAsync(_mitigationId));
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
    }

    public string StrTitle { get; } = Localizer["Track9TreatmentEconomics"];
    public string StrOption { get; } = Localizer["Track9Option"];
    public string StrTransferCounterparty { get; } = Localizer["Track9TransferCounterparty"];
    public string StrOneTimeCost { get; } = Localizer["Track9OneTimeCost"];
    public string StrAnnualCost { get; } = Localizer["Track9AnnualCost"];
    public string StrAnnualSideEffects { get; } = Localizer["Track9AnnualSideEffects"];
    public string StrHorizonYears { get; } = Localizer["Track9HorizonYears"];
    public string StrCostBasis { get; } = Localizer["Track9CostBasis"];
    public string StrEffortPersonDays { get; } = Localizer["Track9EffortPersonDays"];
    public string StrDurationDays { get; } = Localizer["Track9DurationDays"];
    public string StrPrerequisites { get; } = Localizer["Track9Prerequisites"];
    public string StrGateC { get; } = Localizer["Track9GateC"];
    public string StrBenefit { get; } = Localizer["Track9Benefit"];
    public string StrAnnualizedCost { get; } = Localizer["Track9AnnualizedCost"];
    public string StrNetBenefit { get; } = Localizer["Track9NetBenefit"];
    public string StrBenefitCostRatio { get; } = Localizer["Track9BenefitCostRatio"];
    public string StrGordonLoeb { get; } = Localizer["Track9GordonLoeb"];
    public string StrStale { get; } = Localizer["Track9Stale"];

    public List<Track9Choice> OptionChoices { get; }
    public ObservableCollection<Track9RiskChoice> PrerequisiteChoices { get; } = [];
    public bool HasMitigation => _mitigationId is not null;
    public bool CanWrite => Track9RiskPresentation.CanPlanMitigations(User);

    public MitigationEconomicsDto? Result
    {
        get => _result;
        private set
        {
            this.RaiseAndSetIfChanged(ref _result, value);
            foreach (var name in new[] { nameof(GateCText), nameof(BenefitText), nameof(AnnualizedCostText), nameof(NetBenefitText), nameof(BenefitCostRatioText), nameof(GordonLoebText), nameof(IsStale) })
                this.RaisePropertyChanged(name);
        }
    }

    public string GateCText => Result is null ? StrNotAvailable : $"{LocalizedEnum(Result.GateC.Outcome)} — {Result.GateC.Explanation}";
    public string BenefitText => Track9RiskPresentation.Money(Result?.GateC.Benefit, StrNotAvailable);
    public string AnnualizedCostText => Track9RiskPresentation.Money(Result?.GateC.AnnualizedCost, StrNotAvailable);
    public string NetBenefitText => Track9RiskPresentation.Money(Result?.GateC.NetBenefit, StrNotAvailable);
    public string BenefitCostRatioText => Result?.GateC.BenefitCostRatio?.ToString("N2") ?? StrNotAvailable;
    public string GordonLoebText => Track9RiskPresentation.Money(Result?.GateC.GordonLoebReference, StrNotAvailable);
    public bool IsStale => Result?.GateC.Stale == true;

    public Track9Choice? SelectedOption { get => _selectedOption; set { this.RaiseAndSetIfChanged(ref _selectedOption, value); Validate(); } }
    public string? TransferCounterparty { get => _transferCounterparty; set { this.RaiseAndSetIfChanged(ref _transferCounterparty, value); Validate(); } }
    public decimal? OneTime { get => _oneTime; set { this.RaiseAndSetIfChanged(ref _oneTime, value); Validate(); } }
    public decimal? Annual { get => _annual; set { this.RaiseAndSetIfChanged(ref _annual, value); Validate(); } }
    public decimal? SideEffectsAnnual { get => _sideEffectsAnnual; set { this.RaiseAndSetIfChanged(ref _sideEffectsAnnual, value); Validate(); } }
    public decimal? HorizonYears { get => _horizonYears; set { this.RaiseAndSetIfChanged(ref _horizonYears, value); Validate(); } }
    public string? CostBasis { get => _costBasis; set { this.RaiseAndSetIfChanged(ref _costBasis, value); Validate(); } }
    public decimal? EffortPersonDays { get => _effortPersonDays; set { this.RaiseAndSetIfChanged(ref _effortPersonDays, value); Validate(); } }
    public decimal? DurationDays { get => _durationDays; set { this.RaiseAndSetIfChanged(ref _durationDays, value); Validate(); } }
    public bool SaveEnabled => HasMitigation && CanWrite && ValidationMessage is null;

    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }

    public async Task LoadMitigationAsync(int? mitigationId)
    {
        var loadVersion = ++_loadVersion;
        _mitigationId = mitigationId;
        LoadError = null;
        this.RaisePropertyChanged(nameof(HasMitigation));
        Result = null;
        PrerequisiteChoices.Clear();
        ClearEditor();
        if (mitigationId is not { } id || !Track9RiskPresentation.CanReadMitigations(User))
        {
            ValidationMessage = null;
            this.RaisePropertyChanged(nameof(SaveEnabled));
            return;
        }

        bool IsCurrent() => Track9RiskPresentation.IsLatestLoad(loadVersion, _loadVersion) &&
                            Track9RiskPresentation.IsCurrentSelection(id, _mitigationId);

        await ReadAsync("mitigation economics", async () =>
        {
            var answer = await _service.GetMitigationAsync(id);
            var risks = await RisksService.GetAllRisksAsync(true);
            if (!IsCurrent()) return;
            Result = answer;
            SelectedOption = answer.Option is { } option ? OptionChoices.First(c => (TreatmentOption)c.Value == option) : null;
            TransferCounterparty = answer.TransferCounterparty;
            OneTime = answer.Cost?.OneTime;
            Annual = answer.Cost?.Annual;
            SideEffectsAnnual = answer.Cost?.SideEffectsAnnual;
            HorizonYears = answer.Cost?.HorizonYears;
            CostBasis = answer.CostBasis;
            EffortPersonDays = answer.EffortPersonDays;
            DurationDays = answer.DurationDays;
            foreach (var risk in ToRiskChoices(risks).Where(r => r.MitigationId is not null && r.MitigationId != id))
            {
                risk.IsSelected = answer.PrerequisiteMitigationIds.Contains(risk.MitigationId!.Value);
                PrerequisiteChoices.Add(risk);
            }
            Validate();
        }, IsCurrent);
    }

    public async Task SaveAsync()
    {
        Validate();
        if (_mitigationId is not { } id || !SaveEnabled || SelectedOption?.Value is not TreatmentOption option) return;
        var operationVersion = ++_loadVersion;
        var costDeclared = OneTime is not null || Annual is not null || SideEffectsAnnual is not null;
        var request = new MitigationEconomicsRequest
        {
            Option = option,
            TransferCounterparty = TransferCounterparty?.Trim(),
            Cost = costDeclared ? new TreatmentCostRequest
            {
                OneTime = OneTime,
                Annual = Annual,
                SideEffectsAnnual = SideEffectsAnnual,
                HorizonYears = HorizonYears is { } horizon ? (int)horizon : null
            } : null,
            CostBasis = CostBasis?.Trim(),
            EffortPersonDays = EffortPersonDays,
            DurationDays = DurationDays is { } duration ? (int)duration : null,
            PrerequisiteMitigationIds = PrerequisiteChoices.Where(r => r.IsSelected).Select(r => r.MitigationId!.Value).ToList()
        };
        await RunAsync(Localizer["Track9Saved"], async () =>
        {
            var saved = await _service.SaveMitigationAsync(id, request);
            if (Track9RiskPresentation.IsLatestLoad(operationVersion, _loadVersion) &&
                Track9RiskPresentation.IsCurrentSelection(id, _mitigationId)) Result = saved;
        });
    }

    private MitigationEconomicsDraft Draft() => new(
        SelectedOption?.Value is TreatmentOption option ? option : null,
        TransferCounterparty, OneTime, Annual, SideEffectsAnnual,
        HorizonYears is { } h ? (int)h : null, CostBasis, EffortPersonDays,
        DurationDays is { } d ? (int)d : null,
        PrerequisiteChoices.Where(r => r.IsSelected).Select(r => r.MitigationId!.Value).ToList());

    private void ClearEditor()
    {
        SelectedOption = null;
        TransferCounterparty = null;
        OneTime = Annual = SideEffectsAnnual = null;
        HorizonYears = null;
        CostBasis = null;
        EffortPersonDays = DurationDays = null;
    }

    private void Validate()
    {
        ValidationMessage = Track9RiskPresentation.MitigationError(Draft()) is { } key ? Localizer[key].Value : null;
        this.RaisePropertyChanged(nameof(SaveEnabled));
    }
}

public sealed class RiskTargetViewModel : Track9RiskViewModelBase
{
    private readonly ITreatmentEconomicsService _service = GetService<ITreatmentEconomicsService>();
    private int? _riskId;
    private int _loadVersion;
    private RiskTreatmentEconomicsDto? _result;
    private decimal? _targetScore;
    private decimal? _targetExpectedLoss;
    private DateTimeOffset? _targetDate;
    private string? _rationale;

    public RiskTargetViewModel()
    {
        ReloadCommand = ReactiveCommand.CreateFromTask(() => LoadRiskAsync(_riskId));
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        DeleteCommand = ReactiveCommand.CreateFromTask(DeleteAsync);
    }

    public string StrTitle { get; } = Localizer["Track9RiskTarget"];
    public string StrTargetScore { get; } = Localizer["Track9TargetScore"];
    public string StrTargetExpectedLoss { get; } = Localizer["Track9TargetExpectedLoss"];
    public string StrTargetDate { get; } = Localizer["Track9TargetDate"];
    public string StrRationale { get; } = Localizer["Track9Rationale"];
    public string StrCurrent { get; } = Localizer["Track9Current"];
    public string StrTargetStatus { get; } = Localizer["Track9TargetStatus"];
    public string StrRemoveTarget { get; } = Localizer["Track9RemoveTarget"];
    public bool HasRisk => _riskId is not null;
    public bool HasTarget => Result?.Target is not null;
    public bool CanWrite => Track9RiskPresentation.CanPlanMitigations(User);

    public RiskTreatmentEconomicsDto? Result
    {
        get => _result;
        private set
        {
            this.RaiseAndSetIfChanged(ref _result, value);
            this.RaisePropertyChanged(nameof(HasTarget));
            this.RaisePropertyChanged(nameof(CurrentText));
            this.RaisePropertyChanged(nameof(StatusText));
        }
    }

    public string CurrentText => Result?.Target?.Status is { } state
        ? $"{Track9RiskPresentation.Money(state.CurrentScore, StrNotAvailable)} / {Track9RiskPresentation.Money(state.CurrentExpectedLoss, StrNotAvailable)}"
        : StrNotAvailable;
    public string StatusText => Result?.Target?.Status.Explanation ?? StrNotAvailable;

    public decimal? TargetScore { get => _targetScore; set { this.RaiseAndSetIfChanged(ref _targetScore, value); Validate(); } }
    public decimal? TargetExpectedLoss { get => _targetExpectedLoss; set { this.RaiseAndSetIfChanged(ref _targetExpectedLoss, value); Validate(); } }
    public DateTimeOffset? TargetDate { get => _targetDate; set { this.RaiseAndSetIfChanged(ref _targetDate, value); Validate(); } }
    public string? Rationale { get => _rationale; set { this.RaiseAndSetIfChanged(ref _rationale, value); Validate(); } }
    public bool SaveEnabled => HasRisk && CanWrite && ValidationMessage is null;
    public bool DeleteEnabled => HasTarget && CanWrite;

    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

    public async Task LoadRiskAsync(int? riskId)
    {
        var loadVersion = ++_loadVersion;
        _riskId = riskId;
        LoadError = null;
        this.RaisePropertyChanged(nameof(HasRisk));
        Result = null;
        TargetScore = TargetExpectedLoss = null;
        TargetDate = null;
        Rationale = null;
        if (riskId is not { } id || !Track9RiskPresentation.CanReadRisks(User))
        {
            ValidationMessage = null;
            this.RaisePropertyChanged(nameof(SaveEnabled));
            this.RaisePropertyChanged(nameof(DeleteEnabled));
            return;
        }
        bool IsCurrent() => Track9RiskPresentation.IsLatestLoad(loadVersion, _loadVersion) &&
                            Track9RiskPresentation.IsCurrentSelection(id, _riskId);
        await ReadAsync("risk target", async () =>
        {
            var answer = await _service.GetRiskAsync(id);
            if (!IsCurrent()) return;
            Result = answer;
            TargetScore = answer.Target?.TargetScore;
            TargetExpectedLoss = answer.Target?.TargetExpectedLoss;
            TargetDate = answer.Target?.TargetDate is { } date
                ? new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero) : null;
            Rationale = answer.Target?.Rationale;
            Validate();
        }, IsCurrent);
    }

    public async Task SaveAsync()
    {
        Validate();
        if (_riskId is not { } id || !SaveEnabled) return;
        var operationVersion = ++_loadVersion;
        var request = new RiskTargetRequest
        {
            TargetScore = TargetScore,
            TargetExpectedLoss = TargetExpectedLoss,
            TargetDate = TargetDate is { } date ? DateOnly.FromDateTime(date.Date) : null,
            Rationale = Rationale?.Trim()
        };
        await RunAsync(Localizer["Track9Saved"], async () =>
        {
            var saved = await _service.SaveTargetAsync(id, request);
            if (!Track9RiskPresentation.IsLatestLoad(operationVersion, _loadVersion) ||
                !Track9RiskPresentation.IsCurrentSelection(id, _riskId)) return;
            Result ??= new RiskTreatmentEconomicsDto { RiskId = id };
            Result.Target = saved;
            this.RaisePropertyChanged(nameof(Result));
            this.RaisePropertyChanged(nameof(HasTarget));
            this.RaisePropertyChanged(nameof(CurrentText));
            this.RaisePropertyChanged(nameof(StatusText));
        });
    }

    public async Task DeleteAsync()
    {
        if (_riskId is not { } id || !DeleteEnabled) return;
        var operationVersion = ++_loadVersion;
        await RunAsync(Localizer["Track9Deleted"], async () =>
        {
            await _service.DeleteTargetAsync(id);
            if (!Track9RiskPresentation.IsLatestLoad(operationVersion, _loadVersion) ||
                !Track9RiskPresentation.IsCurrentSelection(id, _riskId)) return;
            if (Result is not null) Result.Target = null;
            TargetScore = null;
            TargetExpectedLoss = null;
            TargetDate = null;
            Rationale = null;
            this.RaisePropertyChanged(nameof(HasTarget));
            this.RaisePropertyChanged(nameof(CurrentText));
            this.RaisePropertyChanged(nameof(StatusText));
        });
    }

    private void Validate()
    {
        ValidationMessage = Track9RiskPresentation.RiskTargetError(TargetScore, TargetExpectedLoss, TargetDate, Rationale)
            is { } key ? Localizer[key].Value : null;
        this.RaisePropertyChanged(nameof(SaveEnabled));
        this.RaisePropertyChanged(nameof(DeleteEnabled));
    }
}

public sealed class TreatmentPortfolioRow(PortfolioItemDto value, Func<Enum, string> enumLabel, string notAvailable)
{
    public int MitigationId => value.MitigationId;
    public string Risk => value.RiskSubject;
    public string Option => value.Option is { } option ? enumLabel(option) : notAvailable;
    public string Tier => enumLabel(value.Tier);
    public string Status => enumLabel(value.Status);
    public string FirstYearCost => Track9RiskPresentation.Money(value.FirstYearCost, notAvailable);
    public string EffortPersonDays => value.EffortPersonDays?.ToString("N2") ?? notAvailable;
    public string Reasons => string.Join(Environment.NewLine, value.Reasons);
    public bool RequiresEscalation => value.RequiresEscalation;
}

public sealed class TreatmentPortfolioViewModel : Track9RiskViewModelBase
{
    private readonly ITreatmentEconomicsService _service = GetService<ITreatmentEconomicsService>();
    private bool _loaded;
    private int _loadVersion;
    private int _calculationVersion;
    private decimal? _budget;
    private decimal? _peopleCapacity;
    private DateTimeOffset? _startDate = DateTimeOffset.Now;
    private DateTimeOffset? _deadline;
    private PortfolioSelectionDto? _result;

    public TreatmentPortfolioViewModel()
    {
        ReloadCommand = ReactiveCommand.CreateFromTask(LoadCandidatesAsync);
        CalculateCommand = ReactiveCommand.CreateFromTask(CalculateAsync);
    }

    public string StrTitle { get; } = Localizer["Track9TreatmentPortfolio"];
    public string StrBudget { get; } = Localizer["Track9Budget"];
    public string StrPeopleCapacity { get; } = Localizer["Track9PeopleCapacity"];
    public string StrStartDate { get; } = Localizer["Track9StartDate"];
    public string StrDeadline { get; } = Localizer["Track9Deadline"];
    public string StrCandidates { get; } = Localizer["Track9Candidates"];
    public string StrPortfolioSummary { get; } = Localizer["Track9PortfolioSummary"];
    public string StrBudgetUsed { get; } = Localizer["Track9BudgetUsed"];
    public string StrPeopleUsed { get; } = Localizer["Track9PeopleUsed"];
    public string StrExpectedReduction { get; } = Localizer["Track9ExpectedReduction"];
    public string StrEscalations { get; } = Localizer["Track9Escalations"];
    public string StrStatus { get; } = Localizer["Track9Status"];
    public string StrReason { get; } = Localizer["Track9Reason"];
    public string StrRisk { get; } = Localizer["Track9Risk"];
    public string StrOption { get; } = Localizer["Track9Option"];
    public string StrGateD { get; } = Localizer["Track9GateD"];

    public ObservableCollection<Track9RiskChoice> Candidates { get; } = [];
    public ObservableCollection<TreatmentPortfolioRow> Rows { get; } = [];

    public decimal? Budget { get => _budget; set { this.RaiseAndSetIfChanged(ref _budget, value); Validate(); } }
    public decimal? PeopleCapacity { get => _peopleCapacity; set { this.RaiseAndSetIfChanged(ref _peopleCapacity, value); Validate(); } }
    public DateTimeOffset? StartDate { get => _startDate; set { this.RaiseAndSetIfChanged(ref _startDate, value); Validate(); } }
    public DateTimeOffset? Deadline { get => _deadline; set { this.RaiseAndSetIfChanged(ref _deadline, value); Validate(); } }
    public PortfolioSelectionDto? Result
    {
        get => _result;
        private set
        {
            this.RaiseAndSetIfChanged(ref _result, value);
            foreach (var name in new[] { nameof(BudgetUsedText), nameof(PeopleUsedText), nameof(ExpectedReductionText), nameof(EscalationsText) })
                this.RaisePropertyChanged(name);
        }
    }
    public string BudgetUsedText => Track9RiskPresentation.Money(Result?.BudgetUsed, StrNotAvailable);
    public string PeopleUsedText => Track9RiskPresentation.Money(Result?.PeopleUsed, StrNotAvailable);
    public string ExpectedReductionText => Track9RiskPresentation.Money(Result?.ExpectedReduction, StrNotAvailable);
    public string EscalationsText => Result?.EscalationsRequired.ToString() ?? StrNotAvailable;
    public bool CalculateEnabled => ValidationMessage is null;
    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CalculateCommand { get; }

    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : LoadCandidatesAsync();

    private async Task LoadCandidatesAsync()
    {
        var loadVersion = ++_loadVersion;
        LoadError = null;
        if (!Track9RiskPresentation.CanReadRisks(User)) return;
        bool IsCurrent() => Track9RiskPresentation.IsLatestLoad(loadVersion, _loadVersion);
        await ReadAsync("treatment portfolio candidates", async () =>
        {
            var risks = await RisksService.GetAllRisksAsync();
            if (!IsCurrent()) return;
            Candidates.Clear();
            foreach (var risk in ToRiskChoices(risks).Where(r => r.MitigationId is not null))
            {
                risk.IsSelected = true;
                risk.PropertyChanged += (_, _) => Validate();
                Candidates.Add(risk);
            }
            _loaded = true;
            Validate();
        }, IsCurrent);
    }

    public async Task CalculateAsync()
    {
        Validate();
        if (!CalculateEnabled || !Track9RiskPresentation.CanReadRisks(User)) return;
        var calculationVersion = ++_calculationVersion;
        bool IsCurrent() => Track9RiskPresentation.IsLatestLoad(calculationVersion, _calculationVersion);
        var request = new PortfolioSelectionRequest
        {
            Budget = Budget,
            PeopleCapacityPersonDays = PeopleCapacity,
            StartDate = StartDate is { } start ? DateOnly.FromDateTime(start.Date) : null,
            Deadline = Deadline is { } end ? DateOnly.FromDateTime(end.Date) : null,
            MitigationIds = Candidates.Where(r => r.IsSelected).Select(r => r.MitigationId!.Value).ToList()
        };
        await ReadAsync("treatment portfolio", async () =>
        {
            var answer = await _service.SelectPortfolioAsync(request);
            if (!IsCurrent()) return;
            Result = answer;
            Rows.Clear();
            foreach (var item in Result.Items)
                Rows.Add(new TreatmentPortfolioRow(item,
                    value => Localizer[$"Track9{value.GetType().Name}{value}"].Value, StrNotAvailable));
        }, IsCurrent);
    }

    private void Validate()
    {
        ValidationMessage = Budget is null or < 0 or > TreatmentEconomicsLimits.MaxAmount
            ? Localizer["Track9AmountOutOfRange"].Value
            : PeopleCapacity is < 0 or > TreatmentEconomicsLimits.MaxEffortPersonDays
                ? Localizer["Track9PeopleCapacityOutOfRange"].Value
                : Deadline is { } deadline && StartDate is { } start && deadline.Date < start.Date
                    ? Localizer["Track9DeadlineBeforeStart"].Value
                    : Candidates.Count(r => r.IsSelected) > TreatmentEconomicsLimits.MaxPortfolioMitigations
                        ? Localizer["Track9TooManyCandidates"].Value : null;
        this.RaisePropertyChanged(nameof(CalculateEnabled));
    }
}

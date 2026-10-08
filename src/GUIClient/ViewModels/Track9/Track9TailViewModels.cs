using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools.Track9;
using Model.Exceptions;
using Model.TailRisk;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.Track9;

public sealed class LossComponentEditRow : ReactiveObject
{
    private bool _included;
    private double? _minimum;
    private double? _mostLikely;
    private double? _maximum;
    private string? _basis;

    public LossComponentEditRow(LossComponent component, string name)
    {
        Component = component;
        Name = name;
    }

    public LossComponent Component { get; }
    public string Name { get; }
    public bool Included { get => _included; set => this.RaiseAndSetIfChanged(ref _included, value); }
    public double? Minimum { get => _minimum; set => this.RaiseAndSetIfChanged(ref _minimum, value); }
    public double? MostLikely { get => _mostLikely; set => this.RaiseAndSetIfChanged(ref _mostLikely, value); }
    public double? Maximum { get => _maximum; set => this.RaiseAndSetIfChanged(ref _maximum, value); }
    public string? Basis { get => _basis; set => this.RaiseAndSetIfChanged(ref _basis, value); }
}

public sealed class LossComponentsViewModel : Track9RiskViewModelBase
{
    private readonly ITailRiskService _service = GetService<ITailRiskService>();
    private int? _riskId;
    private int _loadVersion;
    private RiskTailDto? _result;

    public LossComponentsViewModel()
    {
        foreach (var component in Enum.GetValues<LossComponent>())
        {
            var row = new LossComponentEditRow(component, LocalizedEnum(component));
            row.PropertyChanged += (_, _) => Validate();
            Components.Add(row);
        }
        ReloadCommand = ReactiveCommand.CreateFromTask(() => LoadRiskAsync(_riskId));
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        DeleteCommand = ReactiveCommand.CreateFromTask(DeleteAsync);
    }

    public string StrTitle { get; } = Localizer["Track9LossComponents"];
    public string StrInclude { get; } = Localizer["Track9Include"];
    public string StrComponent { get; } = Localizer["Track9Component"];
    public string StrMinimum { get; } = Localizer["Track9Minimum"];
    public string StrMostLikely { get; } = Localizer["Track9MostLikely"];
    public string StrMaximum { get; } = Localizer["Track9Maximum"];
    public string StrBasis { get; } = Localizer["Track9Basis"];
    public string StrRemoveComponents { get; } = Localizer["Track9RemoveComponents"];
    public ObservableCollection<LossComponentEditRow> Components { get; } = [];
    public bool HasRisk => _riskId is not null;
    public bool CanWrite => Track9RiskPresentation.CanSubmitRisk(User);
    public bool HasDeclaredComponents => Result?.DeclaredComponents.Count > 0;
    public bool SaveEnabled => HasRisk && CanWrite && ValidationMessage is null;
    public bool DeleteEnabled => HasDeclaredComponents && CanWrite;

    public RiskTailDto? Result
    {
        get => _result;
        private set
        {
            this.RaiseAndSetIfChanged(ref _result, value);
            this.RaisePropertyChanged(nameof(HasDeclaredComponents));
            this.RaisePropertyChanged(nameof(DeleteEnabled));
        }
    }

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
        foreach (var row in Components)
        {
            row.Included = false;
            row.Minimum = row.MostLikely = row.Maximum = null;
            row.Basis = null;
        }
        if (riskId is not { } id || !Track9RiskPresentation.CanReadRisks(User))
        {
            ValidationMessage = null;
            this.RaisePropertyChanged(nameof(SaveEnabled));
            return;
        }

        bool IsCurrent() => Track9RiskPresentation.IsLatestLoad(loadVersion, _loadVersion) &&
                            Track9RiskPresentation.IsCurrentSelection(id, _riskId);

        await ReadAsync("loss components", async () =>
        {
            var answer = await _service.GetRiskAsync(id);
            if (!IsCurrent()) return;
            Result = answer;
            foreach (var declared in answer.DeclaredComponents)
            {
                var row = Components.First(c => c.Component == declared.Component);
                row.Included = true;
                row.Minimum = declared.Min;
                row.MostLikely = declared.MostLikely;
                row.Maximum = declared.Max;
                row.Basis = declared.Basis;
            }
            Validate();
        }, IsCurrent);
    }

    public async Task SaveAsync()
    {
        Validate();
        if (_riskId is not { } id || !SaveEnabled) return;
        var operationVersion = ++_loadVersion;
        var request = new LossComponentsRequest
        {
            Components = Components.Where(c => c.Included).Select(c => new LossComponentRequest
            {
                Component = c.Component,
                Min = c.Minimum,
                MostLikely = c.MostLikely,
                Max = c.Maximum,
                Basis = c.Basis?.Trim()
            }).ToList()
        };
        await RunAsync(Localizer["Track9Saved"], async () =>
        {
            var saved = await _service.SaveLossComponentsAsync(id, request);
            if (Track9RiskPresentation.IsLatestLoad(operationVersion, _loadVersion) &&
                Track9RiskPresentation.IsCurrentSelection(id, _riskId)) Result = saved;
        });
    }

    public async Task DeleteAsync()
    {
        if (_riskId is not { } id || !DeleteEnabled) return;
        var operationVersion = ++_loadVersion;
        await RunAsync(Localizer["Track9Deleted"], async () =>
        {
            var saved = await _service.DeleteLossComponentsAsync(id);
            if (!Track9RiskPresentation.IsLatestLoad(operationVersion, _loadVersion) ||
                !Track9RiskPresentation.IsCurrentSelection(id, _riskId)) return;
            Result = saved;
            foreach (var row in Components) row.Included = false;
            Validate();
        });
    }

    private void Validate()
    {
        var drafts = Components.Where(c => c.Included)
            .Select(c => new LossComponentDraft(c.Component, c.Minimum, c.MostLikely, c.Maximum, c.Basis)).ToList();
        ValidationMessage = Track9RiskPresentation.LossComponentsError(drafts) is { } key ? Localizer[key].Value : null;
        this.RaisePropertyChanged(nameof(SaveEnabled));
    }
}

public sealed class TailRunRow(TailStatisticsDto value, string name, string notAvailable)
{
    public string Run { get; } = name;
    public string ExpectedLoss { get; } = Track9RiskPresentation.Money(value.ExpectedLoss, notAvailable);
    public string P95 { get; } = Track9RiskPresentation.Money(value.P95, notAvailable);
    public string Cvar95 { get; } = Track9RiskPresentation.Money(value.Cvar95, notAvailable);
    public string ProbabilityOfLoss { get; } = Track9RiskPresentation.Percent(value.ProbabilityOfLoss, notAvailable);
    public DateTime ComputedAt { get; } = value.ComputedAt;
}

public sealed class TailContributionRow(LossComponentContributionDto value, string name, string notAvailable)
{
    public string Component { get; } = name;
    public string ExpectedLoss { get; } = Track9RiskPresentation.Money(value.ExpectedLoss, notAvailable);
    public string Cvar95 { get; } = Track9RiskPresentation.Money(value.Cvar95, notAvailable);
}

public sealed class RiskTailPanelViewModel : Track9RiskViewModelBase
{
    private readonly ITailRiskService _service = GetService<ITailRiskService>();
    private int? _riskId;
    private int _loadVersion;
    private RiskTailDto? _result;

    public RiskTailPanelViewModel() => ReloadCommand = ReactiveCommand.CreateFromTask(() => LoadRiskAsync(_riskId));

    public string StrTitle { get; } = Localizer["Track9TailRisk"];
    public string StrGateB { get; } = Localizer["Track9GateB"];
    public string StrInherent { get; } = Localizer["Track9Inherent"];
    public string StrResidual { get; } = Localizer["Track9Residual"];
    public string StrExpectedLoss { get; } = Localizer["Track9ExpectedLoss"];
    public string StrP95 { get; } = Localizer["Track9P95"];
    public string StrCvar95 { get; } = Localizer["Track9Cvar95"];
    public string StrProbabilityOfLoss { get; } = Localizer["Track9ProbabilityOfLoss"];
    public string StrComponent { get; } = Localizer["Track9Component"];
    public string StrContribution { get; } = Localizer["Track9Contribution"];
    public string StrComputedAt { get; } = Localizer["Track9ComputedAt"];
    public ObservableCollection<TailRunRow> Runs { get; } = [];
    public ObservableCollection<TailContributionRow> Contributions { get; } = [];

    public RiskTailDto? Result
    {
        get => _result;
        private set
        {
            this.RaiseAndSetIfChanged(ref _result, value);
            this.RaisePropertyChanged(nameof(GateBText));
        }
    }

    public string GateBText => Result is null
        ? StrNotAvailable
        : $"{LocalizedEnum(Result.Appetite.State)} — {Result.Appetite.Explanation}";
    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }

    public async Task LoadRiskAsync(int? riskId)
    {
        var loadVersion = ++_loadVersion;
        _riskId = riskId;
        LoadError = null;
        Result = null;
        Runs.Clear();
        Contributions.Clear();
        if (riskId is not { } id || !Track9RiskPresentation.CanReadRisks(User)) return;
        bool IsCurrent() => Track9RiskPresentation.IsLatestLoad(loadVersion, _loadVersion) &&
                            Track9RiskPresentation.IsCurrentSelection(id, _riskId);
        await ReadAsync("risk tail", async () =>
        {
            var answer = await _service.GetRiskAsync(id);
            if (!IsCurrent()) return;
            Result = answer;
            if (answer.Inherent is { } inherent)
            {
                Runs.Add(new TailRunRow(inherent, StrInherent, StrNotAvailable));
                foreach (var component in inherent.Components)
                    Contributions.Add(new TailContributionRow(component, LocalizedEnum(component.Component), StrNotAvailable));
            }
            if (answer.Residual is { } residual)
            {
                Runs.Add(new TailRunRow(residual, StrResidual, StrNotAvailable));
                foreach (var component in residual.Components)
                    Contributions.Add(new TailContributionRow(component, $"{StrResidual} — {LocalizedEnum(component.Component)}", StrNotAvailable));
            }
        }, IsCurrent);
    }
}

public sealed class CorrelationRow(RiskCorrelationDto value, IReadOnlyDictionary<int, string> riskNames, string notAvailable)
{
    public RiskCorrelationDto Value { get; } = value;
    public int Id => Value.Id;
    public string RiskA => riskNames.TryGetValue(Value.RiskAId, out var name) ? name : notAvailable;
    public string RiskB => riskNames.TryGetValue(Value.RiskBId, out var name) ? name : notAvailable;
    public decimal Coefficient => Value.Coefficient;
    public string Rationale => Value.Rationale;
}

public sealed class CorrelationEditorViewModel : Track9RiskViewModelBase
{
    private readonly ITailRiskService _service = GetService<ITailRiskService>();
    private bool _loaded;
    private int _loadVersion;
    private Track9RiskChoice? _selectedRiskA;
    private Track9RiskChoice? _selectedRiskB;
    private decimal? _coefficient;
    private string? _rationale;
    private CorrelationRow? _selectedCorrelation;

    public CorrelationEditorViewModel()
    {
        ReloadCommand = ReactiveCommand.CreateFromTask(LoadAsync);
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        DeleteCommand = ReactiveCommand.CreateFromTask(DeleteAsync);
        NewCommand = ReactiveCommand.Create(ClearEditor);
    }

    public string StrTitle { get; } = Localizer["Track9Correlations"];
    public string StrRiskA { get; } = Localizer["Track9RiskA"];
    public string StrRiskB { get; } = Localizer["Track9RiskB"];
    public string StrCoefficient { get; } = Localizer["Track9Coefficient"];
    public string StrRationale { get; } = Localizer["Track9Rationale"];
    public string StrNew { get; } = Localizer["New"];
    public ObservableCollection<Track9RiskChoice> Risks { get; } = [];
    public ObservableCollection<CorrelationRow> Rows { get; } = [];
    public bool CanWrite => Track9RiskPresentation.CanSubmitRisk(User);

    public Track9RiskChoice? SelectedRiskA { get => _selectedRiskA; set { this.RaiseAndSetIfChanged(ref _selectedRiskA, value); Validate(); } }
    public Track9RiskChoice? SelectedRiskB { get => _selectedRiskB; set { this.RaiseAndSetIfChanged(ref _selectedRiskB, value); Validate(); } }
    public decimal? Coefficient { get => _coefficient; set { this.RaiseAndSetIfChanged(ref _coefficient, value); Validate(); } }
    public string? Rationale { get => _rationale; set { this.RaiseAndSetIfChanged(ref _rationale, value); Validate(); } }
    public CorrelationRow? SelectedCorrelation
    {
        get => _selectedCorrelation;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedCorrelation, value);
            if (value is null) return;
            SelectedRiskA = Risks.FirstOrDefault(r => r.Id == value.Value.RiskAId);
            SelectedRiskB = Risks.FirstOrDefault(r => r.Id == value.Value.RiskBId);
            Coefficient = value.Value.Coefficient;
            Rationale = value.Value.Rationale;
            this.RaisePropertyChanged(nameof(DeleteEnabled));
        }
    }
    public bool SaveEnabled => CanWrite && ValidationMessage is null;
    public bool DeleteEnabled => CanWrite && SelectedCorrelation is not null;
    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> NewCommand { get; }
    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : LoadAsync();

    private async Task LoadAsync()
    {
        var loadVersion = ++_loadVersion;
        LoadError = null;
        if (!Track9RiskPresentation.CanReadRisks(User)) return;
        bool IsCurrent() => Track9RiskPresentation.IsLatestLoad(loadVersion, _loadVersion);
        await ReadAsync("risk correlations", async () =>
        {
            var risks = ToRiskChoices(await RisksService.GetAllRisksAsync(true));
            var correlations = await _service.GetCorrelationsAsync();
            if (!IsCurrent()) return;
            Risks.Clear();
            foreach (var risk in risks) Risks.Add(risk);
            var names = risks.ToDictionary(r => r.Id, r => r.DisplayName);
            Rows.Clear();
            foreach (var item in correlations) Rows.Add(new CorrelationRow(item, names, StrNotAvailable));
            _loaded = true;
            ClearEditor();
        }, IsCurrent);
    }

    public async Task SaveAsync()
    {
        Validate();
        if (!SaveEnabled) return;
        _loadVersion++;
        var request = new RiskCorrelationRequest
        {
            RiskAId = SelectedRiskA!.Id,
            RiskBId = SelectedRiskB!.Id,
            Coefficient = Coefficient,
            Rationale = Rationale?.Trim()
        };
        await RunAsync(Localizer["Track9Saved"], async () =>
        {
            await _service.SaveCorrelationAsync(request);
            await LoadAsync();
        });
    }

    public async Task DeleteAsync()
    {
        if (!DeleteEnabled) return;
        _loadVersion++;
        var id = SelectedCorrelation!.Id;
        await RunAsync(Localizer["Track9Deleted"], async () =>
        {
            await _service.DeleteCorrelationAsync(id);
            await LoadAsync();
        });
    }

    private void ClearEditor()
    {
        SelectedCorrelation = null;
        SelectedRiskA = Risks.FirstOrDefault();
        SelectedRiskB = Risks.Skip(1).FirstOrDefault();
        Coefficient = 0;
        Rationale = string.Empty;
        this.RaisePropertyChanged(nameof(DeleteEnabled));
    }

    private void Validate()
    {
        ValidationMessage = Track9RiskPresentation.CorrelationError(SelectedRiskA?.Id, SelectedRiskB?.Id, Coefficient, Rationale)
            is { } key ? Localizer[key].Value : null;
        this.RaisePropertyChanged(nameof(SaveEnabled));
    }
}

public sealed class AppetiteTailLimitsViewModel : Track9RiskViewModelBase
{
    private readonly ITailRiskService _service = GetService<ITailRiskService>();
    private int? _appetiteId;
    private int _loadVersion;
    private RiskAppetiteTailLimitsDto? _result;
    private decimal? _maxScenarioExpectedLoss;
    private decimal? _maxScenarioP95;
    private decimal? _maxScenarioCvar95;
    private decimal? _maxPortfolioExpectedLoss;
    private decimal? _maxPortfolioP95;
    private decimal? _maxPortfolioCvar95;
    private string? _rationale;

    public AppetiteTailLimitsViewModel()
    {
        ReloadCommand = ReactiveCommand.CreateFromTask(() => LoadAppetiteAsync(_appetiteId));
        SaveCommand = ReactiveCommand.CreateFromTask(SaveAsync);
        DeleteCommand = ReactiveCommand.CreateFromTask(DeleteAsync);
    }

    public string StrTitle { get; } = Localizer["Track9TailLimits"];
    public string StrScenarioExpectedLoss { get; } = Localizer["Track9ScenarioExpectedLossLimit"];
    public string StrScenarioP95 { get; } = Localizer["Track9ScenarioP95Limit"];
    public string StrScenarioCvar95 { get; } = Localizer["Track9ScenarioCvar95Limit"];
    public string StrPortfolioExpectedLoss { get; } = Localizer["Track9PortfolioExpectedLossLimit"];
    public string StrPortfolioP95 { get; } = Localizer["Track9PortfolioP95Limit"];
    public string StrPortfolioCvar95 { get; } = Localizer["Track9PortfolioCvar95Limit"];
    public string StrRationale { get; } = Localizer["Track9Rationale"];
    public string StrRemoveLimits { get; } = Localizer["Track9RemoveLimits"];
    public string StrNotConfigured { get; } = Localizer["Track9NotConfigured"];
    public bool HasAppetite => _appetiteId is not null;
    public bool HasLimits => Result is not null;
    public bool CanWrite => Track9RiskPresentation.CanAdministerAppetite(User);
    public bool SaveEnabled => HasAppetite && CanWrite && ValidationMessage is null;
    public bool DeleteEnabled => HasLimits && CanWrite;
    public RiskAppetiteTailLimitsDto? Result { get => _result; private set { this.RaiseAndSetIfChanged(ref _result, value); this.RaisePropertyChanged(nameof(HasLimits)); } }
    public decimal? MaxScenarioExpectedLoss { get => _maxScenarioExpectedLoss; set { this.RaiseAndSetIfChanged(ref _maxScenarioExpectedLoss, value); Validate(); } }
    public decimal? MaxScenarioP95 { get => _maxScenarioP95; set { this.RaiseAndSetIfChanged(ref _maxScenarioP95, value); Validate(); } }
    public decimal? MaxScenarioCvar95 { get => _maxScenarioCvar95; set { this.RaiseAndSetIfChanged(ref _maxScenarioCvar95, value); Validate(); } }
    public decimal? MaxPortfolioExpectedLoss { get => _maxPortfolioExpectedLoss; set { this.RaiseAndSetIfChanged(ref _maxPortfolioExpectedLoss, value); Validate(); } }
    public decimal? MaxPortfolioP95 { get => _maxPortfolioP95; set { this.RaiseAndSetIfChanged(ref _maxPortfolioP95, value); Validate(); } }
    public decimal? MaxPortfolioCvar95 { get => _maxPortfolioCvar95; set { this.RaiseAndSetIfChanged(ref _maxPortfolioCvar95, value); Validate(); } }
    public string? Rationale { get => _rationale; set { this.RaiseAndSetIfChanged(ref _rationale, value); Validate(); } }
    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> SaveCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> DeleteCommand { get; }

    public async Task LoadAppetiteAsync(int? appetiteId)
    {
        var loadVersion = ++_loadVersion;
        _appetiteId = appetiteId;
        LoadError = null;
        this.RaisePropertyChanged(nameof(HasAppetite));
        Result = null;
        ClearEditor();
        if (appetiteId is not { } id || !Track9RiskPresentation.CanReadAppetite(User))
        {
            ValidationMessage = null;
            this.RaisePropertyChanged(nameof(SaveEnabled));
            this.RaisePropertyChanged(nameof(DeleteEnabled));
            return;
        }
        bool IsCurrent() => Track9RiskPresentation.IsLatestLoad(loadVersion, _loadVersion) &&
                            Track9RiskPresentation.IsCurrentSelection(id, _appetiteId);
        await ReadAsync("appetite tail limits", async () =>
        {
            RiskAppetiteTailLimitsDto answer;
            try
            {
                answer = await _service.GetAppetiteLimitsAsync(id);
            }
            catch (DataNotFoundException)
            {
                if (IsCurrent()) Validate();
                return;
            }
            if (!IsCurrent()) return;
            Result = answer;
            MaxScenarioExpectedLoss = answer.MaxScenarioExpectedLoss;
            MaxScenarioP95 = answer.MaxScenarioP95;
            MaxScenarioCvar95 = answer.MaxScenarioCvar95;
            MaxPortfolioExpectedLoss = answer.MaxPortfolioExpectedLoss;
            MaxPortfolioP95 = answer.MaxPortfolioP95;
            MaxPortfolioCvar95 = answer.MaxPortfolioCvar95;
            Rationale = answer.Rationale;
            Validate();
        }, IsCurrent);
    }

    public async Task SaveAsync()
    {
        Validate();
        if (_appetiteId is not { } id || !SaveEnabled) return;
        var operationVersion = ++_loadVersion;
        var request = new RiskAppetiteTailLimitsRequest
        {
            MaxScenarioExpectedLoss = MaxScenarioExpectedLoss,
            MaxScenarioP95 = MaxScenarioP95,
            MaxScenarioCvar95 = MaxScenarioCvar95,
            MaxPortfolioExpectedLoss = MaxPortfolioExpectedLoss,
            MaxPortfolioP95 = MaxPortfolioP95,
            MaxPortfolioCvar95 = MaxPortfolioCvar95,
            Rationale = Rationale?.Trim()
        };
        await RunAsync(Localizer["Track9Saved"], async () =>
        {
            var saved = await _service.SaveAppetiteLimitsAsync(id, request);
            if (Track9RiskPresentation.IsLatestLoad(operationVersion, _loadVersion) &&
                Track9RiskPresentation.IsCurrentSelection(id, _appetiteId)) Result = saved;
        });
    }

    public async Task DeleteAsync()
    {
        if (_appetiteId is not { } id || !DeleteEnabled) return;
        var operationVersion = ++_loadVersion;
        await RunAsync(Localizer["Track9Deleted"], async () =>
        {
            await _service.DeleteAppetiteLimitsAsync(id);
            if (!Track9RiskPresentation.IsLatestLoad(operationVersion, _loadVersion) ||
                !Track9RiskPresentation.IsCurrentSelection(id, _appetiteId)) return;
            Result = null;
            MaxScenarioExpectedLoss = MaxScenarioP95 = MaxScenarioCvar95 = null;
            MaxPortfolioExpectedLoss = MaxPortfolioP95 = MaxPortfolioCvar95 = null;
            Rationale = null;
        });
    }

    private void ClearEditor()
    {
        MaxScenarioExpectedLoss = MaxScenarioP95 = MaxScenarioCvar95 = null;
        MaxPortfolioExpectedLoss = MaxPortfolioP95 = MaxPortfolioCvar95 = null;
        Rationale = null;
    }

    private void Validate()
    {
        ValidationMessage = Track9RiskPresentation.TailLimitsError(
            [MaxScenarioExpectedLoss, MaxScenarioP95, MaxScenarioCvar95, MaxPortfolioExpectedLoss, MaxPortfolioP95, MaxPortfolioCvar95], Rationale)
            is { } key ? Localizer[key].Value : null;
        this.RaisePropertyChanged(nameof(SaveEnabled));
        this.RaisePropertyChanged(nameof(DeleteEnabled));
    }
}

public sealed class TailPortfolioMemberRow(PortfolioMemberDto value, Func<Enum, string> enumLabel, string notAvailable)
{
    public string Risk => value.Subject;
    public string Run => enumLabel(value.Run);
    public string ExpectedLoss => Track9RiskPresentation.Money(value.ExpectedLoss, notAvailable);
    public string P95 => Track9RiskPresentation.Money(value.P95, notAvailable);
    public string Cvar95 => Track9RiskPresentation.Money(value.Cvar95, notAvailable);
    public string Contribution => Track9RiskPresentation.Money(value.Cvar95Contribution, notAvailable);
    public string Appetite => enumLabel(value.ScenarioAppetite);
}

public sealed class TailPortfolioExcludedRow(PortfolioExcludedRiskDto value, Func<Enum, string> enumLabel)
{
    public string Risk => value.Subject;
    public string Reason => enumLabel(value.Reason);
}

public sealed class TailPortfolioViewModel : Track9RiskViewModelBase
{
    private readonly ITailRiskService _service = GetService<ITailRiskService>();
    private bool _loaded;
    private int _loadVersion;
    private int _calculationVersion;
    private Track9Choice? _selectedBasis;
    private decimal _seed = TailRiskLimits.DefaultPortfolioSeed;
    private PortfolioTailDto? _result;

    public TailPortfolioViewModel()
    {
        BasisOptions = Enum.GetValues<PortfolioBasis>().Select(v => new Track9Choice(v, LocalizedEnum(v))).ToList();
        _selectedBasis = BasisOptions[0];
        ReloadCommand = ReactiveCommand.CreateFromTask(LoadCandidatesAsync);
        CalculateCommand = ReactiveCommand.CreateFromTask(CalculateAsync);
    }

    public string StrTitle { get; } = Localizer["Track9TailPortfolio"];
    public string StrRisk { get; } = Localizer["Track9Risk"];
    public string StrPortfolioBasis { get; } = Localizer["Track9PortfolioBasis"];
    public string StrSeed { get; } = Localizer["Track9Seed"];
    public string StrMembers { get; } = Localizer["Track9Members"];
    public string StrExcluded { get; } = Localizer["Track9Excluded"];
    public string StrExpectedLoss { get; } = Localizer["Track9ExpectedLoss"];
    public string StrP95 { get; } = Localizer["Track9P95"];
    public string StrCvar95 { get; } = Localizer["Track9Cvar95"];
    public string StrContribution { get; } = Localizer["Track9Contribution"];
    public string StrGateB { get; } = Localizer["Track9GateB"];
    public string StrDiversification { get; } = Localizer["Track9Diversification"];
    public string StrDependence { get; } = Localizer["Track9Dependence"];
    public string StrReason { get; } = Localizer["Track9Reason"];
    public ObservableCollection<Track9RiskChoice> Risks { get; } = [];
    public ObservableCollection<TailPortfolioMemberRow> Members { get; } = [];
    public ObservableCollection<TailPortfolioExcludedRow> Excluded { get; } = [];
    public List<Track9Choice> BasisOptions { get; }
    public Track9Choice? SelectedBasis { get => _selectedBasis; set => this.RaiseAndSetIfChanged(ref _selectedBasis, value); }
    public decimal Seed { get => _seed; set => this.RaiseAndSetIfChanged(ref _seed, value); }
    public PortfolioTailDto? Result
    {
        get => _result;
        private set
        {
            this.RaiseAndSetIfChanged(ref _result, value);
            this.RaisePropertyChanged(nameof(GateBText));
            this.RaisePropertyChanged(nameof(DependenceText));
            this.RaisePropertyChanged(nameof(DiversificationText));
        }
    }
    public string GateBText => Result is null ? StrNotAvailable : $"{LocalizedEnum(Result.Appetite.State)} — {Result.Appetite.Explanation}";
    public string DependenceText => Result is null ? StrNotAvailable : LocalizedEnum(Result.Dependence);
    public string DiversificationText => Track9RiskPresentation.Money(Result?.Diversification, StrNotAvailable);
    public ReactiveCommand<RxVoid, RxVoid> ReloadCommand { get; }
    public ReactiveCommand<RxVoid, RxVoid> CalculateCommand { get; }
    public Task EnsureLoadedAsync() => _loaded ? Task.CompletedTask : LoadCandidatesAsync();

    private async Task LoadCandidatesAsync()
    {
        var loadVersion = ++_loadVersion;
        LoadError = null;
        if (!Track9RiskPresentation.CanReadRisks(User)) return;
        bool IsCurrent() => Track9RiskPresentation.IsLatestLoad(loadVersion, _loadVersion);
        await ReadAsync("tail portfolio candidates", async () =>
        {
            var risks = ToRiskChoices(await RisksService.GetAllRisksAsync());
            if (!IsCurrent()) return;
            Risks.Clear();
            foreach (var risk in risks)
            {
                risk.IsSelected = true;
                Risks.Add(risk);
            }
            _loaded = true;
        }, IsCurrent);
    }

    public async Task CalculateAsync()
    {
        if (SelectedBasis?.Value is not PortfolioBasis basis || !Track9RiskPresentation.CanReadRisks(User)) return;
        var calculationVersion = ++_calculationVersion;
        bool IsCurrent() => Track9RiskPresentation.IsLatestLoad(calculationVersion, _calculationVersion);
        var request = new PortfolioTailRequest
        {
            RiskIds = Risks.Where(r => r.IsSelected).Select(r => r.Id).ToList(),
            Basis = basis,
            Seed = (int)Seed
        };
        await ReadAsync("tail portfolio", async () =>
        {
            var answer = await _service.AggregatePortfolioAsync(request);
            if (!IsCurrent()) return;
            Result = answer;
            Members.Clear();
            Excluded.Clear();
            string Label(Enum value) => Localizer[$"Track9{value.GetType().Name}{value}"].Value;
            foreach (var member in Result.Members) Members.Add(new TailPortfolioMemberRow(member, Label, StrNotAvailable));
            foreach (var excluded in Result.NotQuantified) Excluded.Add(new TailPortfolioExcludedRow(excluded, Label));
        }, IsCurrent);
    }
}

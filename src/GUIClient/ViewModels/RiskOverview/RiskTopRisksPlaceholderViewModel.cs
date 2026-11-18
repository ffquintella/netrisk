using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools.RiskIndex;
using Model.Exceptions;
using Model.RiskIndex;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>One Top Risks row with its "›" to the scenario's detail, which carries the navigation to the register.</summary>
public sealed class RiskTopRiskRowViewModel(TopRiskRow row, ReactiveCommand<TopRiskRow, RxVoid> open)
{
    public TopRiskRow Row { get; } = row;
    public string Rank => Row.Rank.ToString(CultureInfo.CurrentCulture);
    public string Scenario => Row.Scenario;
    public string GateA => Row.GateA;
    public string Scores => Row.Scores;
    public string Loss => Row.Loss;
    public string Confidence => Row.Confidence;
    public string NextDecision => Row.NextDecision;
    public bool HasGateA => Row.HasGateA;
    public ReactiveCommand<TopRiskRow, RxVoid> Open { get; } = open;
}

/// <summary>
/// The Top Risks space (S40 §5.11): in interim mode only the placeholder — no approximation (S39 §1.2 item 5); in full mode
/// M43's list (T172) for the node from <c>GET /RiskIndex/TopRisks</c>, read-only, each row opening the scenario (M59, T286).
/// </summary>
public class RiskTopRisksPlaceholderViewModel : ViewModelBase
{
    public string StrTitle { get; } = Localizer["RiskTopRisksTitle"];
    public string StrPlaceholder { get; } = Localizer["RiskTopRisksPlaceholder"];
    public string StrColRank { get; } = Localizer["RiskTopRisks_ColRank"];
    public string StrColScenario { get; } = Localizer["RiskTopRisks_ColScenario"];
    public string StrColGateA { get; } = Localizer["RiskTopRisks_ColGateA"];
    public string StrColScores { get; } = Localizer["RiskTopRisks_ColScores"];
    public string StrColLoss { get; } = Localizer["RiskTopRisks_ColLoss"];
    public string StrColConfidence { get; } = Localizer["RiskTopRisks_ColConfidence"];
    public string StrColNext { get; } = Localizer["RiskTopRisks_ColNext"];
    public string StrOpenHint { get; } = Localizer["RiskObjectsOpenDetailHint"];
    public string StrRetry { get; } = Localizer["RiskOverviewRetry"];
    private string StrLoadError { get; } = Localizer["RiskOverviewLoadError"];

    private static TextLookup Text => RiskHeadlineViewModel.Text;

    private IRiskIndexService RiskIndexService { get; } = GetService<IRiskIndexService>();

    private readonly Func<RiskIndexObjectLabelDto, Task>? _openDetail;
    private string? _node;
    private DateOnly _date;
    private SemaphoreSlim _gate = new(1, 1);
    private CancellationToken _token;
    private int _generation;

    public RiskTopRisksPlaceholderViewModel() : this(null)
    {
    }

    /// <param name="openDetail">Opens a scenario's object detail (its navigation to the register needs <c>riskmanagement</c>).</param>
    public RiskTopRisksPlaceholderViewModel(Func<RiskIndexObjectLabelDto, Task>? openDetail)
    {
        _openDetail = openDetail;
        BtRetryClicked = ReactiveCommand.CreateFromTask(ReadAsync);
        OpenRow = ReactiveCommand.CreateFromTask<TopRiskRow>(row =>
            _openDetail is null ? Task.CompletedTask : _openDetail(row.Object));
    }

    public ReactiveCommand<RxVoid, RxVoid> BtRetryClicked { get; }

    public ReactiveCommand<TopRiskRow, RxVoid> OpenRow { get; }

    public ObservableCollection<RiskTopRiskRowViewModel> Rows { get; } = [];

    public bool HasRows => Rows.Count > 0;

    private string? _stateText;
    /// <summary>The space's sentence when there is no list: the interim placeholder, or "no open scenario here".</summary>
    public string? StateText
    {
        get => _stateText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _stateText, value);
            this.RaisePropertyChanged(nameof(HasState));
        }
    }

    public bool HasState => !string.IsNullOrEmpty(StateText);

    private bool _isActive;
    public bool IsActive { get => _isActive; set => this.RaiseAndSetIfChanged(ref _isActive, value); }

    private bool _isExpanded;
    /// <summary>Closed by default (§5.0: "▸ Top Risks").</summary>
    public bool IsExpanded { get => _isExpanded; set => this.RaiseAndSetIfChanged(ref _isExpanded, value); }

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    public void Clear()
    {
        _generation++;
        _node = null;
        Rows.Clear();
        this.RaisePropertyChanged(nameof(HasRows));
        StateText = StrPlaceholder;
        ErrorMessage = null;
        IsBusy = false;
    }

    public Task LoadAsync(RiskIndexOverviewDto overview, SemaphoreSlim gate, CancellationToken token)
    {
        Clear();
        _node = overview.Node.Key;
        _date = overview.SnapshotDate;
        _gate = gate;
        _token = token;
        return ReadAsync();
    }

    private async Task ReadAsync()
    {
        if (_node is not { } node) return;
        var generation = ++_generation;
        var token = _token;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _gate.WaitAsync(token);
            RiskIndexTopRisksDto top;
            try
            {
                top = await RiskIndexService.GetTopRisksAsync(node, _date, 10, token);
            }
            finally
            {
                _gate.Release();
            }

            if (generation != _generation || token.IsCancellationRequested) return;
            Rows.Clear();
            foreach (var row in FullModeFormatter.TopRisks(top, Text, CultureInfo.CurrentCulture))
                Rows.Add(new RiskTopRiskRowViewModel(row, OpenRow));
            this.RaisePropertyChanged(nameof(HasRows));
            StateText = FullModeFormatter.TopRisksState(top, Text);
        }
        catch (Exception ex) when (generation == _generation && !token.IsCancellationRequested)
        {
            Logger.Error(ex, "Error loading the risk index Top Risks");
            ErrorMessage = ex is InvalidHttpRequestException { ErrorCode: { Length: > 0 } code } refusal
                ? RiskApiErrorText.Translate(code, refusal.Message, Text, StrLoadError)
                : StrLoadError;
        }
        catch (Exception ex)
        {
            Logger.Debug("A superseded read of the Top Risks ended: {Message}", ex.Message);
        }
        finally
        {
            if (generation == _generation) IsBusy = false;
        }
    }
}

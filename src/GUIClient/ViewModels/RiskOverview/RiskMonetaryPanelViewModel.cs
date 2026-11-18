using System;
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

/// <summary>
/// "Painel monetário — fora do índice" (S40 §5.10): Σ E[L] residual and inherent with "n of N" and the share of REG's weight
/// quantified — never a bare total — and, in full mode, M45's portfolio P95/CVaR with Gate B on the portfolio (M59, T287),
/// from <c>GET /RiskIndex/Monetary</c>. Until the read answers, each line shows the spec's unavailable state.
/// </summary>
public class RiskMonetaryPanelViewModel : ViewModelBase
{
    public string StrTitle { get; } = Localizer["RiskMonetaryTitle"];
    public string StrResidualLabel { get; } = Localizer["RiskMonetary_ResidualLabel"];
    public string StrInherentLabel { get; } = Localizer["RiskMonetary_InherentLabel"];
    public string StrP95Label { get; } = Localizer["RiskMonetary_P95Label"];
    public string StrNote { get; } = Localizer["RiskMonetaryNote"];
    public string StrRetry { get; } = Localizer["RiskOverviewRetry"];
    private string StrLoadError { get; } = Localizer["RiskOverviewLoadError"];

    private static TextLookup Text => RiskHeadlineViewModel.Text;

    private IRiskIndexService RiskIndexService { get; } = GetService<IRiskIndexService>();

    private string? _node;
    private DateOnly _date;
    private SemaphoreSlim _gate = new(1, 1);
    private CancellationToken _token;
    private int _generation;

    public RiskMonetaryPanelViewModel()
    {
        BtRetryClicked = ReactiveCommand.CreateFromTask(ReadAsync);
        Reset();
    }

    public ReactiveCommand<RxVoid, RxVoid> BtRetryClicked { get; }

    private string _residualState = "";
    public string StrResidualState { get => _residualState; private set => this.RaiseAndSetIfChanged(ref _residualState, value); }

    private string _inherentState = "";
    public string StrInherentState { get => _inherentState; private set => this.RaiseAndSetIfChanged(ref _inherentState, value); }

    private string _p95State = "";
    public string StrP95State { get => _p95State; private set => this.RaiseAndSetIfChanged(ref _p95State, value); }

    private string? _gateB;
    /// <summary>Gate B on the portfolio (M45, T186), when the portfolio was aggregated.</summary>
    public string? StrGateB
    {
        get => _gateB;
        private set
        {
            this.RaiseAndSetIfChanged(ref _gateB, value);
            this.RaisePropertyChanged(nameof(HasGateB));
        }
    }

    public bool HasGateB => !string.IsNullOrEmpty(StrGateB);

    private bool _isActive;
    /// <summary>A node is shown ("Meu escopo" and the §5.13 states have none).</summary>
    public bool IsActive { get => _isActive; set => this.RaiseAndSetIfChanged(ref _isActive, value); }

    private bool _isExpanded;
    /// <summary>Closed by default (§5.0: "▸ Painel monetário").</summary>
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

    private void Reset()
    {
        StrResidualState = Localizer["RiskMonetary_ResidualUnavailable"];
        StrInherentState = Localizer["RiskMonetary_InherentUnavailable"];
        StrP95State = Localizer["RiskMonetary_P95Unavailable"];
        StrGateB = null;
    }

    public void Clear()
    {
        _generation++;
        _node = null;
        Reset();
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
            RiskIndexMonetaryDto monetary;
            try
            {
                monetary = await RiskIndexService.GetMonetaryAsync(node, _date, token);
            }
            finally
            {
                _gate.Release();
            }

            if (generation != _generation || token.IsCancellationRequested) return;
            var lines = FullModeFormatter.Monetary(monetary, Text, CultureInfo.CurrentCulture);
            StrResidualState = lines.Residual;
            StrInherentState = lines.Inherent;
            StrP95State = lines.Portfolio;
            StrGateB = lines.GateB;
        }
        catch (Exception ex) when (generation == _generation && !token.IsCancellationRequested)
        {
            Logger.Error(ex, "Error loading the risk index monetary panel");
            ErrorMessage = ex is InvalidHttpRequestException { ErrorCode: { Length: > 0 } code } refusal
                ? RiskApiErrorText.Translate(code, refusal.Message, Text, StrLoadError)
                : StrLoadError;
        }
        catch (Exception ex)
        {
            Logger.Debug("A superseded read of the monetary panel ended: {Message}", ex.Message);
        }
        finally
        {
            if (generation == _generation) IsBusy = false;
        }
    }
}

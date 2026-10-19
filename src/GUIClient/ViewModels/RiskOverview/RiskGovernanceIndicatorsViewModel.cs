using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Models;
using GUIClient.Tools.Navigation;
using GUIClient.Tools.RiskIndex;
using Model.Exceptions;
using Model.RiskIndex;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>One indicator with its "›" to the owning module, disabled with the reason when the user may not open it.</summary>
public sealed class RiskGovernanceIndicatorViewModel(GovernanceIndicator indicator,
    ReactiveCommand<GovernanceIndicator, RxVoid> open)
{
    public GovernanceIndicator Indicator { get; } = indicator;
    public string Text => Indicator.Text;
    public bool HasModule => Indicator.HasModule;
    public bool CanOpen => Indicator.CanOpen;
    public string? Hint => Indicator.Hint;
    public ReactiveCommand<GovernanceIndicator, RxVoid> Open { get; } = open;
}

/// <summary>
/// "Governança e revisões — indicadores (fora do ICR)" (S40 §5.2.5; T256): the always-visible strip below the quality strip,
/// from <c>GET /RiskIndex/GovernanceIndicators</c>. Volume, not risk; never in the number nor in any order. The server counts
/// them live (T249), so the strip says they do not follow a past snapshot date. A "›" opens the owning module.
/// </summary>
public class RiskGovernanceIndicatorsViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["RiskGovernanceTitle"];
    public string StrVolumeNote { get; } = Localizer["RiskGovernanceVolumeNote"];
    public string StrLiveNote { get; } = Localizer["RiskGovernanceLiveNote"];
    public string StrPartial { get; } = Localizer["RiskGovernancePartial"];
    public string StrMyScope { get; } = Localizer["RiskGovernanceMyScope"];
    public string StrRetry { get; } = Localizer["RiskOverviewRetry"];
    private string StrLoadError { get; } = Localizer["RiskOverviewLoadError"];

    #endregion

    private static TextLookup Text => RiskHeadlineViewModel.Text;

    private IRiskIndexService RiskIndexService { get; } = GetService<IRiskIndexService>();

    private readonly Action<AvaliableViews> _openModule;
    private string? _node;
    private DateOnly _date;
    private SemaphoreSlim _gate = new(1, 1);
    private CancellationToken _token;
    private int _generation;

    /// <param name="openModule">Switches the shell to a module (navigation only; the module applies its own permission).</param>
    public RiskGovernanceIndicatorsViewModel(Action<AvaliableViews> openModule)
    {
        _openModule = openModule;
        BtRetryClicked = ReactiveCommand.CreateFromTask(ReadAsync);
        OpenModule = ReactiveCommand.Create<GovernanceIndicator>(indicator =>
        {
            if (indicator is { CanOpen: true, Module: { } module }) _openModule(module);
        });
    }

    public ReactiveCommand<RxVoid, RxVoid> BtRetryClicked { get; }

    public ReactiveCommand<GovernanceIndicator, RxVoid> OpenModule { get; }

    #region PROPERTIES

    public ObservableCollection<RiskGovernanceIndicatorViewModel> Items { get; } = [];

    private bool _isPartial;
    public bool IsPartial { get => _isPartial; private set => this.RaiseAndSetIfChanged(ref _isPartial, value); }

    private bool _isMyScope;
    public bool IsMyScope { get => _isMyScope; private set => this.RaiseAndSetIfChanged(ref _isMyScope, value); }

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

    #endregion

    public void Clear()
    {
        _generation++;
        _node = null;
        Items.Clear();
        IsPartial = IsMyScope = false;
        ErrorMessage = null;
        IsBusy = false;
    }

    /// <summary>"Meu escopo" (§5.1): the indicators are counted per node; the strip says to open an entity.</summary>
    public void ShowMyScope()
    {
        Clear();
        IsMyScope = true;
    }

    public Task LoadAsync(RiskIndexOverviewDto overview, SemaphoreSlim gate, CancellationToken token)
    {
        Clear();
        _node = overview.Node.Key;
        _date = overview.SnapshotDate;
        _gate = gate;
        _token = token;
        IsPartial = overview.Provenance == RiskIndexProvenance.PartialRestricted;
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
            RiskIndexGovernanceIndicatorsDto indicators;
            try
            {
                indicators = await RiskIndexService.GetGovernanceIndicatorsAsync(node, _date, token);
            }
            finally
            {
                _gate.Release();
            }

            if (generation != _generation || token.IsCancellationRequested) return;
            var user = AuthenticationService.AuthenticatedUserInfo;
            Items.Clear();
            foreach (var indicator in GovernanceIndicatorsFormatter.Items(indicators, view => ModuleAccess.CanOpen(view, user), Text,
                         CultureInfo.CurrentCulture))
                Items.Add(new RiskGovernanceIndicatorViewModel(indicator, OpenModule));
        }
        catch (Exception ex) when (generation == _generation && !token.IsCancellationRequested)
        {
            Logger.Error(ex, "Error loading the risk index governance indicators");
            ErrorMessage = ex is InvalidHttpRequestException { ErrorCode: { Length: > 0 } code } refusal
                ? RiskApiErrorText.Translate(code, refusal.Message, Text, StrLoadError)
                : StrLoadError;
        }
        catch (Exception ex)
        {
            Logger.Debug("A superseded read of the governance indicators ended: {Message}", ex.Message);
        }
        finally
        {
            if (generation == _generation) IsBusy = false;
        }
    }
}

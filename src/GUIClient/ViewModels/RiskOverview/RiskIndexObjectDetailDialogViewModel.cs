using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Navigation;
using GUIClient.Tools.Navigation;
using GUIClient.Tools.RiskIndex;
using GUIClient.ViewModels.Dialogs;
using GUIClient.ViewModels.Dialogs.Parameters;
using GUIClient.ViewModels.Dialogs.Results;
using Model.Exceptions;
using Model.RiskIndex;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>A navigation button of the detail: enabled by the navigation bar's own rule, or disabled with its reason.</summary>
public sealed class RiskDetailDestinationViewModel(ObjectDetailDestination destination,
    ReactiveCommand<RiskDetailDestinationViewModel, RxVoid> open)
{
    public string Label => destination.Label;
    public bool IsEnabled => destination.IsEnabled;
    public string? DisabledReason => destination.DisabledReason;
    public NavigationTarget? Target => destination.Target;
    public ReactiveCommand<RiskDetailDestinationViewModel, RxVoid> Open { get; } = open;
}

/// <summary>
/// The object detail (S40 §5.9.2; T255): a read-only utility dialog over <c>GET /RiskIndex/Objects/{kind}/{reference}</c> —
/// header, per-category values, factor readings with their freshness, signals, the register's inputs and the decision
/// state, redacted per module (§11.3). It closes with the destination chosen (IX-2) and the caller navigates; Esc closes.
/// </summary>
public class RiskIndexObjectDetailDialogViewModel
    : ParameterizedDialogViewModelBaseAsync<RiskIndexObjectDetailDialogResult, RiskIndexObjectDetailDialogParameter>
{
    #region LANGUAGE

    public string StrByCategory { get; } = Localizer["RiskObjectDetailByCategory"];
    public string StrFactors { get; } = Localizer["RiskObjectDetailFactors"];
    public string StrSignals { get; } = Localizer["RiskObjectDetailSignals"];
    public string StrScenario { get; } = Localizer["RiskObjectDetailScenario"];
    public string StrDecision { get; } = Localizer["RiskObjectDetailDecision"];
    public string StrChips { get; } = Localizer["RiskObjectDetailChips"];
    public string StrNavigation { get; } = Localizer["RiskObjectDetailNavigation"];
    public string StrNoSignals { get; } = Localizer["RiskObjectDetailNoSignals"];
    public string StrNoChips { get; } = Localizer["RiskObjectDetailNoChips"];
    public string StrNoDestinations { get; } = Localizer["RiskObjectDetailNoDestinations"];
    public string StrColCategory { get; } = Localizer["RiskObjectDetailColCategory"];
    public string StrColR { get; } = Localizer["RiskObjectDetailColR"];
    public string StrColState { get; } = Localizer["RiskObjectDetailColState"];
    public string StrColPopulation { get; } = Localizer["RiskObjectDetailColPopulation"];
    public string StrColWeight { get; } = Localizer["RiskObjectDetailColWeight"];
    public string StrColBranch { get; } = Localizer["RiskObjectDetailColBranch"];
    public string StrColFactor { get; } = Localizer["RiskObjectDetailColFactor"];
    public string StrColType { get; } = Localizer["RiskObjectDetailColType"];
    public string StrColX { get; } = Localizer["RiskObjectDetailColX"];
    public string StrColV { get; } = Localizer["RiskObjectDetailColV"];
    public string StrColUF { get; } = Localizer["RiskObjectDetailColUF"];
    public string StrColFEff { get; } = Localizer["RiskObjectDetailColFEff"];
    public string StrColReading { get; } = Localizer["RiskObjectDetailColReading"];
    public string StrColSource { get; } = Localizer["RiskObjectDetailColSource"];
    private string StrLoading { get; } = Localizer["RiskObjectDetailLoading"];
    private string StrLoadError { get; } = Localizer["RiskOverviewLoadError"];

    #endregion

    private static TextLookup Text => RiskHeadlineViewModel.Text;

    private IRiskIndexService RiskIndexService { get; } = GetService<IRiskIndexService>();

    public RiskIndexObjectDetailDialogViewModel()
    {
        _title = StrLoading;
        BtCloseClicked = ReactiveCommand.Create(() => Close(new RiskIndexObjectDetailDialogResult()));
        OpenDestination = ReactiveCommand.Create<RiskDetailDestinationViewModel>(destination =>
        {
            // Defence in depth: a disabled destination never closes with a target.
            if (destination is { IsEnabled: true, Target: { } target })
                Close(new RiskIndexObjectDetailDialogResult { Target = target });
        });
    }

    public ReactiveCommand<RxVoid, RxVoid> BtCloseClicked { get; }

    public ReactiveCommand<RiskDetailDestinationViewModel, RxVoid> OpenDestination { get; }

    #region PROPERTIES

    private string _title;
    public string Title { get => _title; private set => this.RaiseAndSetIfChanged(ref _title, value); }

    private string _snapshot = "";
    public string Snapshot { get => _snapshot; private set => this.RaiseAndSetIfChanged(ref _snapshot, value); }

    private string _kindAndClass = "";
    public string KindAndClass { get => _kindAndClass; private set => this.RaiseAndSetIfChanged(ref _kindAndClass, value); }

    private string _nodes = "";
    public string Nodes { get => _nodes; private set => this.RaiseAndSetIfChanged(ref _nodes, value); }

    private string _criticality = "";
    public string Criticality { get => _criticality; private set => this.RaiseAndSetIfChanged(ref _criticality, value); }

    private string _criticalityOrigin = "";
    public string CriticalityOrigin
    {
        get => _criticalityOrigin;
        private set => this.RaiseAndSetIfChanged(ref _criticalityOrigin, value);
    }

    private string? _internetFacing;
    public string? InternetFacing { get => _internetFacing; private set => this.RaiseAndSetIfChanged(ref _internetFacing, value); }

    private string? _vendorReference;
    /// <summary>A vendor object with no screen in NetRisk: its id, as selectable text (§5.9.2 [N]).</summary>
    public string? VendorReference { get => _vendorReference; private set => this.RaiseAndSetIfChanged(ref _vendorReference, value); }

    private string? _redactionNote;
    /// <summary>"Requer a permissão ‹módulo›" in place of the register's content (§11.3); null when readable.</summary>
    public string? RedactionNote { get => _redactionNote; private set => this.RaiseAndSetIfChanged(ref _redactionNote, value); }

    public ObservableCollection<ObjectDetailCategoryRow> CategoryRows { get; } = [];
    public ObservableCollection<ObjectDetailFactorRow> FactorRows { get; } = [];
    public ObservableCollection<string> Signals { get; } = [];
    public ObservableCollection<string> ScenarioLines { get; } = [];
    public ObservableCollection<string> Chips { get; } = [];
    public ObservableCollection<RiskDetailDestinationViewModel> Destinations { get; } = [];

    private bool _hasDetail;
    public bool HasDetail { get => _hasDetail; private set => this.RaiseAndSetIfChanged(ref _hasDetail, value); }

    private bool _isScenario;
    public bool IsScenario { get => _isScenario; private set => this.RaiseAndSetIfChanged(ref _isScenario, value); }

    private string _decisionText = "";
    public string DecisionText { get => _decisionText; private set => this.RaiseAndSetIfChanged(ref _decisionText, value); }

    private bool _hasSignals;
    public bool HasSignals { get => _hasSignals; private set => this.RaiseAndSetIfChanged(ref _hasSignals, value); }

    private bool _hasChips;
    public bool HasChips { get => _hasChips; private set => this.RaiseAndSetIfChanged(ref _hasChips, value); }

    private bool _hasDestinations;
    public bool HasDestinations { get => _hasDestinations; private set => this.RaiseAndSetIfChanged(ref _hasDestinations, value); }

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

    /// <summary>Opens at once and loads behind its busy ring (IX-4); a failure stays in the dialog, which Esc closes.</summary>
    public override Task ActivateAsync(RiskIndexObjectDetailDialogParameter parameter, CancellationToken cancellationToken = default)
    {
        _ = LoadAsync(parameter, cancellationToken);
        return Task.CompletedTask;
    }

    private Task LoadAsync(RiskIndexObjectDetailDialogParameter parameter, CancellationToken cancellationToken) =>
        WithBusyAsync(async () =>
        {
            try
            {
                var detail = await RiskIndexService.GetObjectAsync(parameter.Kind, parameter.Reference, parameter.Node,
                    parameter.Date, cancellationToken);
                Apply(detail);
            }
            catch (Exception ex)
            {
                Logger.Error(ex, "Error loading the risk index object detail");
                ErrorMessage = ex is InvalidHttpRequestException { ErrorCode: { Length: > 0 } code } refusal
                    ? RiskApiErrorText.Translate(code, refusal.Message, Text, StrLoadError)
                    : StrLoadError;
            }
        });

    private void Apply(RiskIndexObjectDetailDto detail)
    {
        var culture = CultureInfo.CurrentCulture;
        var header = ObjectDetailFormatter.Header(detail, Text, culture);
        Title = header.Title;
        Snapshot = header.Snapshot;
        KindAndClass = header.KindAndClass;
        Nodes = header.Nodes;
        Criticality = header.Criticality;
        CriticalityOrigin = header.CriticalityOrigin;
        InternetFacing = header.InternetFacing;
        VendorReference = header.VendorReference;
        RedactionNote = header.RedactionNote;

        Replace(CategoryRows, ObjectDetailFormatter.Categories(detail, Text, culture));
        Replace(FactorRows, ObjectDetailFormatter.Factors(detail, Text, culture));
        Replace(Signals, ObjectDetailFormatter.Signals(detail, Text, culture));
        HasSignals = Signals.Count > 0;

        var scenario = ObjectDetailFormatter.Scenario(detail, Text, culture);
        IsScenario = scenario is not null;
        Replace(ScenarioLines, scenario ?? []);
        DecisionText = ObjectDetailFormatter.Decision(detail, Text, culture);

        Replace(Chips, ObjectDetailFormatter.Chips(detail, Text));
        HasChips = Chips.Count > 0;

        var user = AuthenticationService.AuthenticatedUserInfo;
        Replace(Destinations, ObjectDetailFormatter.Destinations(detail, view => ModuleAccess.CanOpen(view, user), Text, culture)
            .Select(d => new RiskDetailDestinationViewModel(d, OpenDestination)));
        HasDestinations = Destinations.Count > 0;
        HasDetail = true;
    }

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}

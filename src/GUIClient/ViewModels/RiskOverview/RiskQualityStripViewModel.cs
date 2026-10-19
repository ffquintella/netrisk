using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
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

/// <summary>A data-quality item of the strip, or a row of its detail, with the "›" that opens its list (§5.12).</summary>
public sealed class RiskQualityItemViewModel(QualityItem item, string openHint, ReactiveCommand<QualityItem, RxVoid> open)
{
    public QualityItem Item { get; } = item;
    public string Label => Item.Label;
    public string Value => Item.Value;
    public bool HasValue => Item.Value.Length > 0;
    public bool IsWarning => Item.IsWarning;
    public bool IsUnavailable => Item.IsUnavailable;
    public string? Hint => Item.Hint;
    public string Threshold => Item.Threshold;
    public bool CanOpen => Item.CanOpen;

    /// <summary>"Abrir a lista de objetos filtrada", or "Abrir o nó Não atribuído".</summary>
    public string OpenHint { get; } = openHint;

    public ReactiveCommand<QualityItem, RxVoid> Open { get; } = open;
}

/// <summary>
/// The always-visible data-quality strip (S40 §5.12.1; T256) and the reader of its detail (§5.12.2, <see cref="Panel"/>): one
/// <c>GET /RiskIndex/Quality</c> per page, queued on the page's gate. It never collapses (S39 §8.6). A "›" opens the object
/// list filtered, or the Unassigned node; a failed read shows why with "Tentar novamente" (§5.13).
/// </summary>
public class RiskQualityStripViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["RiskQualityStripTitle"];
    public string StrHelp { get; } = Localizer["RiskQualityStripHelp"];
    public string StrPartial { get; } = Localizer["RiskQualityStripPartial"];
    public string StrMyScope { get; } = Localizer["RiskQualityStripMyScope"];
    public string StrRetry { get; } = Localizer["RiskOverviewRetry"];
    private string StrOpenList { get; } = Localizer["RiskQualityStripOpenHint"];
    private string StrOpenNode { get; } = Localizer["RiskQualityStripOpenNodeHint"];
    private string StrLoadError { get; } = Localizer["RiskOverviewLoadError"];

    #endregion

    private static TextLookup Text => RiskHeadlineViewModel.Text;

    private IRiskIndexService RiskIndexService { get; } = GetService<IRiskIndexService>();

    private readonly Func<string, Task> _openNode;
    private readonly Func<string, string?, Task> _openObjects;
    private string? _node;
    private DateOnly _date;
    private bool _isOrganization;
    private double? _unassignedThreshold;
    private SemaphoreSlim _gate = new(1, 1);
    private CancellationToken _token;
    private int _generation;

    /// <param name="openNode">Opens a node of the drill-down (the Unassigned item's "›").</param>
    /// <param name="openObjects">Opens the page's object list with a <c>filters</c> preset and the label its chip reads.</param>
    public RiskQualityStripViewModel(Func<string, Task> openNode, Func<string, string?, Task> openObjects)
    {
        _openNode = openNode;
        _openObjects = openObjects;
        BtRetryClicked = ReactiveCommand.CreateFromTask(ReadAsync);
        OpenItem = ReactiveCommand.CreateFromTask<QualityItem>(OpenAsync);
        Panel = new RiskQualityPanelViewModel(ReadAsync);
    }

    public ReactiveCommand<RxVoid, RxVoid> BtRetryClicked { get; }

    /// <summary>An item's "›": read-only navigation — the node or the filtered list, never an action on a record.</summary>
    public ReactiveCommand<QualityItem, RxVoid> OpenItem { get; }

    /// <summary>The collapsible detail (§5.12.2), fed by the same read.</summary>
    public RiskQualityPanelViewModel Panel { get; }

    #region PROPERTIES

    public ObservableCollection<RiskQualityItemViewModel> Items { get; } = [];

    private bool _isPartial;
    /// <summary>Partial visibility (§5.6.5): the values count only what the user can see.</summary>
    public bool IsPartial { get => _isPartial; private set => this.RaiseAndSetIfChanged(ref _isPartial, value); }

    private bool _isMyScope;
    /// <summary>"Meu escopo" has no node to measure: the strip stays, and says to open an entity.</summary>
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

    /// <summary>No page (§5.13 states): nothing measured, nothing kept.</summary>
    public void Clear()
    {
        _generation++;
        _node = null;
        Items.Clear();
        IsPartial = IsMyScope = false;
        ErrorMessage = null;
        IsBusy = false;
        Panel.Clear();
    }

    /// <summary>"Meu escopo" (§5.1): the strip says the quality is shown per entity.</summary>
    public void ShowMyScope()
    {
        Clear();
        IsMyScope = true;
    }

    /// <summary>A new page: the node's quality on the snapshot shown, through the page's gate.</summary>
    public Task LoadAsync(RiskIndexOverviewDto overview, double? unassignedThreshold, SemaphoreSlim gate, CancellationToken token)
    {
        Clear();
        _node = overview.Node.Key;
        _date = overview.SnapshotDate;
        _isOrganization = RiskIndexNodeKey.TryParse(overview.Node.Key, out var key) && key.IsOrganization && !key.HasFilters;
        _unassignedThreshold = unassignedThreshold;
        _gate = gate;
        _token = token;
        IsPartial = overview.Provenance == RiskIndexProvenance.PartialRestricted;
        Panel.Activate();
        return ReadAsync();
    }

    private async Task ReadAsync()
    {
        if (_node is not { } node) return;
        var generation = ++_generation;
        var token = _token;
        IsBusy = Panel.IsBusy = true;
        ErrorMessage = null;
        Panel.ErrorMessage = null;
        try
        {
            await _gate.WaitAsync(token);
            RiskIndexQualityDto quality;
            try
            {
                quality = await RiskIndexService.GetQualityAsync(node, _date, token);
            }
            finally
            {
                _gate.Release();
            }

            if (generation != _generation || token.IsCancellationRequested) return;
            var culture = CultureInfo.CurrentCulture;
            Replace(Items, QualityStripFormatter.Strip(quality, _isOrganization, Text, culture));
            // The residual-above-inherent list is the register's (§11.3): its "›" only where the server would answer it.
            var mayReadRegister = ModuleAccess.CanOpen(AvaliableViews.Risk, AuthenticationService.AuthenticatedUserInfo);
            Panel.Apply(QualityStripFormatter.Panel(quality, _isOrganization, _unassignedThreshold, Text, culture, mayReadRegister)
                .Select(View).ToList());
        }
        catch (Exception ex) when (generation == _generation && !token.IsCancellationRequested)
        {
            // The items already shown for this node stay (§5.13: previous data preserved); the error says why.
            Logger.Error(ex, "Error loading the risk index data quality");
            ErrorMessage = Panel.ErrorMessage = ex is InvalidHttpRequestException { ErrorCode: { Length: > 0 } code } refusal
                ? RiskApiErrorText.Translate(code, refusal.Message, Text, StrLoadError)
                : StrLoadError;
        }
        catch (Exception ex)
        {
            Logger.Debug("A superseded read of the data quality ended: {Message}", ex.Message);
        }
        finally
        {
            if (generation == _generation) IsBusy = Panel.IsBusy = false;
        }
    }

    private RiskQualityItemViewModel View(QualityItem item) =>
        new(item, item.NodeKey is not null ? StrOpenNode : StrOpenList, OpenItem);

    private void Replace(ObservableCollection<RiskQualityItemViewModel> target, IEnumerable<QualityItem> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(View(item));
    }

    private Task OpenAsync(QualityItem? item)
    {
        if (item is null || !item.CanOpen) return Task.CompletedTask;
        return item.NodeKey is { } node ? _openNode(node) : _openObjects(item.ObjectFilter!, item.Label);
    }
}

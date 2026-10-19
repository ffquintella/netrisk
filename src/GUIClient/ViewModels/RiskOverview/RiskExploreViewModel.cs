using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Tools.RiskIndex;
using Model.RiskIndex;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>A choice of the dimension selector.</summary>
public sealed record DimensionOption(RiskIndexDimension Dimension, string Label);

/// <summary>A chip of the Explore panel: the term's label, its kind as the hint and the "×" that removes it (§5.6.2).</summary>
public sealed record FilterChip(DrillFilter Filter, string Label, string Hint, ReactiveCommand<DrillFilterKind, RxVoid> BtRemoveFilterClicked)
{
    public DrillFilterKind Kind => Filter.Kind;
}

/// <summary>One value "+ Filtro" offers: its label with the visible objects that carry it.</summary>
public sealed record FilterMenuValue(DrillFilter Filter, string Label, ReactiveCommand<DrillFilter, RxVoid> BtAddFilterClicked);

/// <summary>The values of one kind in the "+ Filtro" flyout.</summary>
public sealed record FilterMenuGroup(string Title, IReadOnlyList<FilterMenuValue> Values);

/// <summary>
/// The always-visible Explore panel (S40 §5.6.1–§5.6.2, §10.9; T254): the dimension selector — the only one on the page —,
/// the chips and "+ Filtro", the tree of the current node's children and, beside it, the node's own monthly summary. It
/// shows the page's <see cref="DrillState"/> and reports every action to the module, which owns the state and decides what
/// to read again; nothing here keeps a node, a dimension or a filter of its own. Reads go through the page's request gate
/// (the server admits two derived computations per user, §8.1) and the cache of the last visited nodes (§10.9).
/// </summary>
public class RiskExploreViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["RiskExploreTitle"];
    public string StrHelp { get; } = Localizer["RiskExploreHelp"];
    public string StrDimension { get; } = Localizer["RiskExploreDimension"];
    public string StrFilters { get; } = Localizer["RiskExploreFilters"];
    public string StrAddFilter { get; } = Localizer["RiskExploreAddFilter"];
    public string StrAddFilterHint { get; } = Localizer["RiskExploreAddFilterHint"];
    public string StrNoFilterValues { get; } = Localizer["RiskExploreNoFilterValues"];
    public string StrRemoveFilterHint { get; } = Localizer["RiskExploreRemoveFilterHint"];
    public string StrMonthlyScopeHint { get; } = Localizer["RiskExploreMonthlyScopeHint"];
    private string StrFilterRemovedFmt { get; } = Localizer["RiskExploreFilterRemovedFMT"];
    private string StrMonthlyTitleFmt { get; } = Localizer["RiskExploreMonthlyTitleFMT"];
    private string StrFilterValueFmt { get; } = Localizer["RiskExploreFilterValueFMT"];

    #endregion

    private static TextLookup Text => RiskHeadlineViewModel.Text;

    private IRiskIndexService RiskIndexService { get; } = GetService<IRiskIndexService>();

    private readonly Action<RiskIndexDimension> _chooseDimension;
    private readonly RecentCache<(string Parent, DateOnly Date, RiskIndexDimension Dimension, string? Filters), RiskIndexNodeChildrenDto> _children = new();
    private readonly RecentCache<(string Node, DateOnly Date, int Months), RiskIndexMonthlySummaryDto> _monthly = new();
    private readonly RecentCache<(string Node, DateOnly Date), RiskIndexDimensionsDto> _dimensions = new();

    private DrillState? _state;
    private DateOnly _date;
    private SemaphoreSlim _gate = new(1, 1);
    private CancellationToken _token;
    private bool _applying;

    public RiskExploreViewModel(Action<RiskIndexDimension> chooseDimension, Action<DrillFilter> addFilter,
        Action<DrillFilterKind> removeFilter, Action<string> openNode, Action goUp)
    {
        _chooseDimension = chooseDimension;
        BtAddFilterClicked = ReactiveCommand.Create(addFilter);
        BtRemoveFilterClicked = ReactiveCommand.Create(removeFilter);
        Tree = new RiskNodeTreeViewModel(openNode, goUp);
        Monthly = new RiskMonthlySummaryViewModel();
    }

    #region COMMANDS

    public ReactiveCommand<DrillFilter, RxVoid> BtAddFilterClicked { get; }
    public ReactiveCommand<DrillFilterKind, RxVoid> BtRemoveFilterClicked { get; }

    #endregion

    #region PROPERTIES

    public RiskNodeTreeViewModel Tree { get; }

    /// <summary>The current node's monthly summary with every applicable category (§5.6.1).</summary>
    public RiskMonthlySummaryViewModel Monthly { get; }

    private bool _isActive;
    public bool IsActive { get => _isActive; private set => this.RaiseAndSetIfChanged(ref _isActive, value); }

    public ObservableCollection<DimensionOption> DimensionOptions { get; } = [];

    private DimensionOption? _selectedDimension;
    /// <summary>The selector writes null when its items are refilled; that write is ignored.</summary>
    public DimensionOption? SelectedDimension
    {
        get => _selectedDimension;
        set
        {
            if (value is null || value == _selectedDimension) return;
            this.RaiseAndSetIfChanged(ref _selectedDimension, value);
            if (!_applying) _chooseDimension(value.Dimension);
        }
    }

    private bool _isDimensionEnabled;
    public bool IsDimensionEnabled { get => _isDimensionEnabled; private set => this.RaiseAndSetIfChanged(ref _isDimensionEnabled, value); }

    public ObservableCollection<FilterChip> Chips { get; } = [];

    public ObservableCollection<FilterMenuGroup> FilterMenu { get; } = [];

    private bool _isAddFilterEnabled;
    public bool IsAddFilterEnabled { get => _isAddFilterEnabled; private set => this.RaiseAndSetIfChanged(ref _isAddFilterEnabled, value); }

    private bool _hasFilterValues;
    public bool HasFilterValues { get => _hasFilterValues; private set => this.RaiseAndSetIfChanged(ref _hasFilterValues, value); }

    private string _monthlyTitle = "";
    public string MonthlyTitle { get => _monthlyTitle; private set => this.RaiseAndSetIfChanged(ref _monthlyTitle, value); }

    private bool _showsMonthly;
    /// <summary>"Meu escopo" has no node of its own, so no summary (§5.1).</summary>
    public bool ShowsMonthly { get => _showsMonthly; private set => this.RaiseAndSetIfChanged(ref _showsMonthly, value); }

    /// <summary>The values present in the node, once read — the object list's class and source choices reuse them.</summary>
    public event Action<RiskIndexDimensionsDto?>? DimensionsLoaded;

    #endregion

    #region LOADING

    /// <summary>"Atualizar" re-reads the snapshot: the cache of visited nodes is dropped (§10.9).</summary>
    public void ClearCache()
    {
        _children.Clear();
        _monthly.Clear();
        _dimensions.Clear();
    }

    public void Clear()
    {
        IsActive = false;
        _state = null;
        Chips.Clear();
        FilterMenu.Clear();
        Tree.SetSource(null, null, [], new NodeLabels("", "", ""), CancellationToken.None);
        Monthly.SetSource(null, CancellationToken.None);
    }

    /// <summary>
    /// A new page (a node or a day): the selector, the chips, then — one after the other, through the page's gate — the
    /// tree, the node's summary and the values "+ Filtro" offers.
    /// </summary>
    public async Task LoadAsync(DrillState state, DateOnly date, RiskIndexOverviewDto overview, NodeLabels labels,
        SemaphoreSlim gate, CancellationToken token)
    {
        _state = state;
        _date = date;
        _gate = gate;
        _token = token;
        _lastDimensions = null;
        FilterMenu.Clear();
        HasFilterValues = false;
        IsActive = true;
        ApplyState(state);

        var node = state.NodeText;
        Tree.SetSource(ReadChildrenAsync, node, overview.Bands, labels, token);
        ShowsMonthly = !state.Node.IsScopeRoot;
        MonthlyTitle = string.Format(CultureInfo.CurrentCulture, StrMonthlyTitleFmt, string.Join(" · ",
            DrillState.FilterTerms(state.Node).Select(f => NodeKeyDisplay.TermLabel(f, Text, CultureInfo.CurrentCulture))
                .Prepend(NodeKeyDisplay.EntityLabel(overview.Node, labels))));
        Monthly.SetSource(ShowsMonthly ? (months, t) => ReadMonthlyAsync(node, months, t) : null, token);

        await Tree.LoadAsync();
        if (token.IsCancellationRequested) return;
        if (ShowsMonthly) await Monthly.LoadAsync();
        if (token.IsCancellationRequested) return;
        await LoadFilterValuesAsync();
    }

    /// <summary>The dimension or the chips changed (§5.6.2): the selector and chips follow, and only the tree is read again.</summary>
    public Task ApplyTreeAsync(DrillState state)
    {
        if (_state is null) return Task.CompletedTask;
        _state = state;
        ApplyState(state);
        _ = BuildFilterMenu();
        return Tree.LoadAsync();
    }

    /// <summary>The toast of a dimension change that removed a chip of its own kind (§5.6.2).</summary>
    public string FilterRemovedText(DrillFilter removed) =>
        string.Format(CultureInfo.CurrentCulture, StrFilterRemovedFmt, NodeKeyDisplay.TermLabel(removed, Text, CultureInfo.CurrentCulture));

    private void ApplyState(DrillState state)
    {
        _applying = true;
        try
        {
            var available = state.AvailableDimensions;
            if (!DimensionOptions.Select(o => o.Dimension).SequenceEqual(available))
            {
                DimensionOptions.Clear();
                foreach (var dimension in available)
                    DimensionOptions.Add(new DimensionOption(dimension, NodeKeyDisplay.DimensionLabel(dimension, Text)));
            }

            _selectedDimension = DimensionOptions.FirstOrDefault(o => o.Dimension == state.Dimension);
            this.RaisePropertyChanged(nameof(SelectedDimension));
            IsDimensionEnabled = DimensionOptions.Count > 1;

            Chips.Clear();
            foreach (var chip in state.Chips)
                Chips.Add(new FilterChip(chip, NodeKeyDisplay.TermLabel(chip, Text, CultureInfo.CurrentCulture),
                    NodeKeyDisplay.FilterKindLabel(chip.Kind, Text), BtRemoveFilterClicked));
        }
        finally
        {
            _applying = false;
        }
    }

    private async Task LoadFilterValuesAsync()
    {
        if (_state is not { } state || state.Node.IsScopeRoot)
        {
            FilterMenu.Clear();
            IsAddFilterEnabled = false;
            HasFilterValues = false;
            DimensionsLoaded?.Invoke(null);
            return;
        }

        var dimensions = await ReadDimensionsAsync(state.NodeText);
        if (_token.IsCancellationRequested) return;
        _lastDimensions = dimensions;
        DimensionsLoaded?.Invoke(dimensions);
        BuildFilterMenu();
    }

    /// <summary>The values present in the node shown; null until read, or when the read failed.</summary>
    private RiskIndexDimensionsDto? _lastDimensions;

    /// <summary>
    /// "+ Filtro" (§5.6.2): for each kind other than the dimension's and absent from the node, the values the node holds
    /// with their visible objects. A chip already of that value is not offered again.
    /// </summary>
    private bool BuildFilterMenu()
    {
        FilterMenu.Clear();
        if (_state is not { } state || _lastDimensions is not { } present)
        {
            IsAddFilterEnabled = _state is { } s && s.AddableFilterKinds.Count > 0;
            HasFilterValues = false;
            return false;
        }

        var culture = CultureInfo.CurrentCulture;
        foreach (var kind in state.AddableFilterKinds)
        {
            var source = kind switch
            {
                DrillFilterKind.AssetClass => present.AssetClasses,
                DrillFilterKind.Source => present.Sources,
                DrillFilterKind.Criticality => present.Criticalities,
                _ => present.Environments
            };
            var values = new List<FilterMenuValue>();
            foreach (var value in source)
            {
                if (!DrillFilter.TryParse(value.Term, out var filter) || filter.Kind != kind || state.Chips.Contains(filter)) continue;
                values.Add(new FilterMenuValue(filter, string.Format(culture, StrFilterValueFmt,
                    NodeKeyDisplay.TermLabel(filter, Text, culture), value.VisibleObjects.ToString("N0", culture)), BtAddFilterClicked));
            }

            if (values.Count > 0) FilterMenu.Add(new FilterMenuGroup(NodeKeyDisplay.FilterKindLabel(kind, Text), values));
        }

        IsAddFilterEnabled = state.AddableFilterKinds.Count > 0;
        HasFilterValues = FilterMenu.Count > 0;
        return HasFilterValues;
    }

    private async Task<T> OneAtATime<T>(Func<CancellationToken, Task<T>> read, CancellationToken token)
    {
        await _gate.WaitAsync(token);
        try
        {
            return await read(token);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The children of a key along the state's dimension with its chips, from the cache when that page was seen.</summary>
    private async Task<RiskIndexNodeChildrenDto> ReadChildrenAsync(string parent, CancellationToken token)
    {
        var state = _state ?? DrillState.Start(parent);
        var key = (parent, _date, state.Dimension, state.FiltersText);
        if (_children.TryGet(key, out var cached)) return cached;
        var children = await OneAtATime(t => RiskIndexService.GetNodesAsync(parent, state.Dimension, state.FiltersText, _date, t), token);
        _children.Put(key, children);
        return children;
    }

    private async Task<RiskIndexMonthlySummaryDto> ReadMonthlyAsync(string node, int months, CancellationToken token)
    {
        var key = (node, _date, months);
        if (_monthly.TryGet(key, out var cached)) return cached;
        var summary = await OneAtATime(t => RiskIndexService.GetMonthlySummaryAsync(node, null, months, _date, t), token);
        _monthly.Put(key, summary);
        return summary;
    }

    /// <summary>The values present in the node; a failure only empties "+ Filtro" — the tree and the summary stand.</summary>
    private async Task<RiskIndexDimensionsDto?> ReadDimensionsAsync(string node)
    {
        var token = _token;
        var key = (node, _date);
        if (_dimensions.TryGet(key, out var cached)) return cached;
        try
        {
            var dimensions = await OneAtATime(t => RiskIndexService.GetDimensionsAsync(node, _date, t), token);
            _dimensions.Put(key, dimensions);
            return dimensions;
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            Logger.Warning("Could not read the values of {Node} for the Explore filters: {Message}", node, ex.Message);
            return null;
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            return null;
        }
    }

    #endregion

    public override void Dispose()
    {
        Tree.Dispose();
        Monthly.Dispose();
        base.Dispose();
    }
}

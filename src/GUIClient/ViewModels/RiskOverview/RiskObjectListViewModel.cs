using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using Model.Exceptions;
using Model.RiskIndex;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>One row of the object list (§5.9.1) and the "›" that opens its detail.</summary>
public sealed class RiskObjectRowViewModel(RiskIndexObjectRowDto row, ObjectRowText text,
    ReactiveCommand<RiskObjectRowViewModel, RxVoid> open, bool canOpen)
{
    public RiskIndexObjectRowDto Row { get; } = row;
    public string Label => text.Label;
    public string? LabelHint => text.LabelHint;
    public bool IsRedacted => text.LabelHint is not null;
    public string Kind => text.Kind;
    public string AssetClass => text.AssetClass;
    public string Category => text.Category;
    public string R => text.R;
    public string Criticality => text.Criticality;
    public string State => text.State;
    public string Reading => text.Reading;
    public string Points => text.Points;
    public string Shared => text.Shared;

    /// <summary>The object detail (§5.9.2) is T255's dialog; the "›" shows only once the module wires it.</summary>
    public bool CanOpen { get; } = canOpen;

    public ReactiveCommand<RiskObjectRowViewModel, RxVoid> Open { get; } = open;
}

/// <summary>A choice of one of the list's filters; <see cref="Value"/> null is "Todas".</summary>
public sealed record ListOption(string? Value, string Label)
{
    public bool IsAll => Value is null;
}

/// <summary>A page size choice.</summary>
public sealed record PageSizeOption(int Size, string Label);

/// <summary>
/// The object list (S40 §5.9.1, §10.9; T254): one line per (object, category) of the page's node, paged and sorted by the
/// server (50 a page by default, up to its 500) over a virtualized <c>DataGrid</c>. A header asks the server for the sort
/// and reads the first page again; there is no sort by sensitivity or by Δ. A category detail pre-selects the category, a
/// factor line lists node ∩ tab filtered by the factor, and a row's "›" asks <see cref="OpenDetail"/> (T255's dialog).
/// </summary>
public class RiskObjectListViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["RiskObjectsTitle"];
    public string StrHelp { get; } = Localizer["RiskObjectsHelp"];
    public string StrColObject { get; } = Localizer["RiskObjectsColObject"];
    public string StrColKind { get; } = Localizer["RiskObjectsColKind"];
    public string StrColClass { get; } = Localizer["RiskObjectsColClass"];
    public string StrColCategory { get; } = Localizer["RiskObjectsColCategory"];
    public string StrColR { get; } = Localizer["RiskObjectsColR"];
    public string StrColCriticality { get; } = Localizer["RiskObjectsColCriticality"];
    public string StrColState { get; } = Localizer["RiskObjectsColState"];
    public string StrColReading { get; } = Localizer["RiskObjectsColReading"];
    public string StrColPoints { get; } = Localizer["RiskObjectsColPoints"];
    public string StrColShared { get; } = Localizer["RiskObjectsColShared"];
    public string StrCategory { get; } = Localizer["RiskObjectsFilterCategory"];
    public string StrState { get; } = Localizer["RiskObjectsFilterState"];
    public string StrClass { get; } = Localizer["RiskObjectsFilterClass"];
    public string StrSource { get; } = Localizer["RiskObjectsFilterSource"];
    public string StrCriticality { get; } = Localizer["RiskObjectsFilterCriticality"];
    public string StrPageSize { get; } = Localizer["RiskObjectsPageSize"];
    public string StrSearchWatermark { get; } = Localizer["RiskObjectsSearchWatermark"];
    public string StrSortNote { get; } = Localizer["RiskObjectsSortNote"];
    public string StrEmpty { get; } = Localizer["RiskObjectsEmpty"];
    public string StrPrevious { get; } = Localizer["RiskObjectsPreviousPage"];
    public string StrNext { get; } = Localizer["RiskObjectsNextPage"];
    public string StrOpenDetailHint { get; } = Localizer["RiskObjectsOpenDetailHint"];
    public string StrClearPresetHint { get; } = Localizer["RiskObjectsClearPresetHint"];
    public string StrRetry { get; } = Localizer["RiskOverviewRetry"];
    private string StrAll { get; } = Localizer["RiskObjectsAll"];
    private string StrLoadError { get; } = Localizer["RiskOverviewLoadError"];
    private string StrTabScopeFmt { get; } = Localizer["RiskObjectsTabScopeFMT"];

    #endregion

    private static TextLookup Text => RiskHeadlineViewModel.Text;

    private IRiskIndexService RiskIndexService { get; } = GetService<IRiskIndexService>();

    private ObjectListQuery _query = new();
    private RiskIndexNodeKey? _node;
    private DateOnly _date;
    private SemaphoreSlim _gate = new(1, 1);
    private CancellationToken _token;
    private bool _applying;
    private int _generation;

    public RiskObjectListViewModel()
    {
        var culture = CultureInfo.CurrentCulture;
        CategoryOptions = ChartPaletteMap.SeriesOrder
            .Select(c => new ListOption(c.ToString(), HeadlineFormatter.CategoryCode(c) + " — " +
                                                      RiskText.Get(Text, "RiskCategory_" + HeadlineFormatter.CategoryCode(c) + "_Name")))
            .Prepend(new ListOption(null, StrAll)).ToList();
        StateOptions = Enum.GetValues<RiskIndexObjectState>()
            .Select(s => new ListOption(s.ToString(), RiskText.Get(Text, "RiskObjectState_" + s)))
            .Prepend(new ListOption(null, StrAll)).ToList();
        CriticalityOptions = Enumerable.Range(1, 5)
            .Select(k => new ListOption(k.ToString(CultureInfo.InvariantCulture), k.ToString(culture)))
            .Prepend(new ListOption(null, StrAll)).ToList();
        PageSizeOptions = ObjectListQuery.PageSizes.Select(s => new PageSizeOption(s, s.ToString(culture))).ToList();
        ClassOptions.Add(new ListOption(null, StrAll));
        SourceOptions.Add(new ListOption(null, StrAll));
        _selectedCategory = CategoryOptions[0];
        _selectedState = StateOptions[0];
        _selectedCriticality = CriticalityOptions[0];
        _selectedClass = ClassOptions[0];
        _selectedSource = SourceOptions[0];
        _selectedPageSize = PageSizeOptions[0];

        BtPreviousPageClicked = ReactiveCommand.CreateFromTask(() => ReadAsync(_query.Previous()));
        BtNextPageClicked = ReactiveCommand.CreateFromTask(() => ReadAsync(_query.Next()));
        BtClearPresetClicked = ReactiveCommand.CreateFromTask(() => ReadAsync(_query.WithoutPreset()));
        BtRetryClicked = ReactiveCommand.CreateFromTask(() => ReadAsync(_query));
        BtFocusSearchClicked = ReactiveCommand.Create(() => { SearchFocusSignal++; });
        BtObjectDetailClicked = ReactiveCommand.CreateFromTask<RiskObjectRowViewModel>(OpenDetailAsync);

        // The name search is the server's (its label filter matches only labels the caller may see, §8.2); typing is
        // gathered for a moment so each keystroke is not a request.
        this.WhenAnyValue(vm => vm.SearchText)
            .Skip(1)
            .Throttle(TimeSpan.FromMilliseconds(400))
            .Subscribe(text => Dispatcher.UIThread.Post(() =>
                _ = ReadAsync(_query with { Label = ObjectListQuery.CleanLabel(text), Page = 1 })));
    }

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtPreviousPageClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtNextPageClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtClearPresetClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRetryClicked { get; }

    /// <summary>Ctrl+F (IX-8): focuses the name search.</summary>
    public ReactiveCommand<RxVoid, RxVoid> BtFocusSearchClicked { get; }

    /// <summary>A row's "›" or double click (§5.9.1): asks for the object's detail.</summary>
    public ReactiveCommand<RiskObjectRowViewModel, RxVoid> BtObjectDetailClicked { get; }

    #endregion

    #region PROPERTIES

    private Func<RiskIndexObjectRowDto, Task>? _openDetail;

    /// <summary>The object detail's hook (§5.9.2, T255). Until the module sets it, rows show no "›" and a double click is inert.</summary>
    public Func<RiskIndexObjectRowDto, Task>? OpenDetail
    {
        get => _openDetail;
        set
        {
            _openDetail = value;
            this.RaisePropertyChanged(nameof(CanOpenDetail));
        }
    }

    public bool CanOpenDetail => OpenDetail is not null;

    private bool _isActive;
    public bool IsActive { get => _isActive; private set => this.RaiseAndSetIfChanged(ref _isActive, value); }

    private bool _isExpanded = true;
    /// <summary>The "Objetos" expander, opened when a category or a factor sends the user here.</summary>
    public bool IsExpanded { get => _isExpanded; set => this.RaiseAndSetIfChanged(ref _isExpanded, value); }

    public ObservableCollection<RiskObjectRowViewModel> Rows { get; } = [];

    private RiskObjectRowViewModel? _selectedRow;
    public RiskObjectRowViewModel? SelectedRow { get => _selectedRow; set => this.RaiseAndSetIfChanged(ref _selectedRow, value); }

    public IReadOnlyList<ListOption> CategoryOptions { get; }
    public IReadOnlyList<ListOption> StateOptions { get; }
    public IReadOnlyList<ListOption> CriticalityOptions { get; }
    public IReadOnlyList<PageSizeOption> PageSizeOptions { get; }

    /// <summary>The classes and sources present in the node, refilled from the Explore panel's read (§8.2 Dimensions).</summary>
    public ObservableCollection<ListOption> ClassOptions { get; } = [];
    public ObservableCollection<ListOption> SourceOptions { get; } = [];

    private ListOption _selectedCategory;
    public ListOption SelectedCategory
    {
        get => _selectedCategory;
        set
        {
            if (value is null || value == _selectedCategory) return;
            this.RaiseAndSetIfChanged(ref _selectedCategory, value);
            if (!_applying)
                _ = ReadAsync(_query with { Category = Enum.TryParse<RiskIndexCategory>(value.Value, out var c) ? c : null, Page = 1 });
        }
    }

    private ListOption _selectedState;
    public ListOption SelectedState
    {
        get => _selectedState;
        set
        {
            if (value is null || value == _selectedState) return;
            this.RaiseAndSetIfChanged(ref _selectedState, value);
            if (!_applying)
                _ = ReadAsync(_query with { State = Enum.TryParse<RiskIndexObjectState>(value.Value, out var s) ? s : null, Page = 1 });
        }
    }

    private ListOption _selectedClass;
    public ListOption SelectedClass
    {
        get => _selectedClass;
        set
        {
            if (value is null || value == _selectedClass) return;
            this.RaiseAndSetIfChanged(ref _selectedClass, value);
            if (!_applying)
                _ = ReadAsync(_query with { AssetClass = Enum.TryParse<RiskIndexAssetClass>(value.Value, out var a) ? a : null, Page = 1 });
        }
    }

    private ListOption _selectedSource;
    public ListOption SelectedSource
    {
        get => _selectedSource;
        set
        {
            if (value is null || value == _selectedSource) return;
            this.RaiseAndSetIfChanged(ref _selectedSource, value);
            if (!_applying) _ = ReadAsync(_query with { Source = value.Value, Page = 1 });
        }
    }

    private ListOption _selectedCriticality;
    public ListOption SelectedCriticality
    {
        get => _selectedCriticality;
        set
        {
            if (value is null || value == _selectedCriticality) return;
            this.RaiseAndSetIfChanged(ref _selectedCriticality, value);
            if (!_applying)
                _ = ReadAsync(_query with
                {
                    Criticality = int.TryParse(value.Value, NumberStyles.None, CultureInfo.InvariantCulture, out var k) ? k : null,
                    Page = 1
                });
        }
    }

    private PageSizeOption _selectedPageSize;
    public PageSizeOption SelectedPageSize
    {
        get => _selectedPageSize;
        set
        {
            if (value is null || value == _selectedPageSize) return;
            this.RaiseAndSetIfChanged(ref _selectedPageSize, value);
            if (!_applying) _ = ReadAsync(_query.WithPageSize(value.Size));
        }
    }

    private string? _searchText;
    public string? SearchText { get => _searchText; set => this.RaiseAndSetIfChanged(ref _searchText, value); }

    private int _searchFocusSignal;
    public int SearchFocusSignal { get => _searchFocusSignal; set => this.RaiseAndSetIfChanged(ref _searchFocusSignal, value); }

    private string? _presetText;
    /// <summary>The chip of a factor line's filter and of its tab, with one "×" (§5.5.1).</summary>
    public string? PresetText
    {
        get => _presetText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _presetText, value);
            this.RaisePropertyChanged(nameof(HasPreset));
        }
    }

    public bool HasPreset => !string.IsNullOrEmpty(PresetText);

    private string _rangeText = "";
    public string RangeText { get => _rangeText; private set => this.RaiseAndSetIfChanged(ref _rangeText, value); }

    private string _sortText = "";
    public string SortText { get => _sortText; private set => this.RaiseAndSetIfChanged(ref _sortText, value); }

    private bool _isPreviousPageEnabled;
    public bool IsPreviousPageEnabled { get => _isPreviousPageEnabled; private set => this.RaiseAndSetIfChanged(ref _isPreviousPageEnabled, value); }

    private bool _isNextPageEnabled;
    public bool IsNextPageEnabled { get => _isNextPageEnabled; private set => this.RaiseAndSetIfChanged(ref _isNextPageEnabled, value); }

    private bool _isEmpty;
    public bool IsEmpty { get => _isEmpty; private set => this.RaiseAndSetIfChanged(ref _isEmpty, value); }

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

    #region LOADING

    public void Clear()
    {
        _generation++;
        _node = null;
        IsActive = false;
        Rows.Clear();
        ErrorMessage = null;
        IsEmpty = false;
        RangeText = "";
    }

    /// <summary>
    /// A new page (node or day): the list keeps its category, state, criticality and name filters and reads the new node's
    /// first page through the page's gate. A factor's preset belonged to the page it came from, and the class and source
    /// choices are the node's own (§8.2 Dimensions), so they leave with it.
    /// </summary>
    public Task LoadAsync(RiskIndexNodeKey node, DateOnly date, SemaphoreSlim gate, CancellationToken token)
    {
        _node = node;
        _date = date;
        _gate = gate;
        _token = token;
        IsActive = true;
        SetDimensions(null);
        return ReadAsync(_query.WithoutPreset() with { AssetClass = null, Source = null });
    }

    /// <summary>The category detail (§5.4): the list of that category of the node, nothing else filtered.</summary>
    public Task ShowCategoryAsync(RiskIndexCategory category)
    {
        IsExpanded = true;
        return ReadAsync(_query.ForCategory(category));
    }

    /// <summary>A factor line's "›" (§5.5.1): the node ∩ the line's tab filtered by the line's <c>ObjectFilter</c>.</summary>
    public Task ShowPresetAsync(string objectFilter, string? label, RiskIndexTabGroup? tab)
    {
        IsExpanded = true;
        return ReadAsync(_query.ForPreset(objectFilter, label, tab));
    }

    /// <summary>A column header (§5.9.1): the server sorts and the first page is read again. Called by the grid's code-behind.</summary>
    public void SortBy(string? column)
    {
        var sorted = _query.SortBy(column);
        if (!ReferenceEquals(sorted, _query)) _ = ReadAsync(sorted);
    }

    /// <summary>The classes and sources the node holds (§8.2 Dimensions), as choices; null keeps "Todas" only.</summary>
    public void SetDimensions(RiskIndexDimensionsDto? dimensions)
    {
        var culture = CultureInfo.CurrentCulture;
        var classes = new List<ListOption>();
        var sources = new List<ListOption>();
        foreach (var value in dimensions?.AssetClasses ?? [])
            if (DrillFilter.TryParse(value.Term, out var f) && RiskIndexNodeKey.ClassTerms.TryGetValue(f.Value, out var c))
                classes.Add(new ListOption(c.ToString(), NodeKeyDisplay.TermLabel(f, Text, culture)));
        foreach (var value in dimensions?.Sources ?? [])
            if (DrillFilter.TryParse(value.Term, out var f))
                sources.Add(new ListOption(f.Value, NodeKeyDisplay.TermLabel(f, Text, culture)));

        _applying = true;
        try
        {
            SelectedClass = Refill(ClassOptions, classes, _query.AssetClass?.ToString());
            SelectedSource = Refill(SourceOptions, sources, _query.Source);
        }
        finally
        {
            _applying = false;
        }
    }

    /// <summary>Refills a choice list behind its "Todas" and returns the option to select (the query's, or "Todas").</summary>
    private static ListOption Refill(ObservableCollection<ListOption> options, IEnumerable<ListOption> values, string? selected)
    {
        var all = options[0];
        options.Clear();
        options.Add(all);
        foreach (var value in values) options.Add(value);
        return options.FirstOrDefault(o => o.Value == selected) ?? all;
    }

    private async Task ReadAsync(ObjectListQuery query)
    {
        _query = query;
        if (_node is not { } node) return;
        var generation = ++_generation;
        var token = _token;
        ShowQuery(query);
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            await _gate.WaitAsync(token);
            (IReadOnlyList<RiskIndexObjectRowDto> Rows, int Total) page;
            try
            {
                page = await RiskIndexService.GetObjectsAsync(query.NodeKey(node), _date, query.FiltersText, query.SortsText, query.Page,
                    query.PageSize, token);
            }
            finally
            {
                _gate.Release();
            }

            if (generation != _generation || token.IsCancellationRequested) return;
            var culture = CultureInfo.CurrentCulture;
            Rows.Clear();
            foreach (var row in page.Rows)
                Rows.Add(new RiskObjectRowViewModel(row, ObjectRowFormatter.Format(row, Text, culture), BtObjectDetailClicked,
                    CanOpenDetail));
            var range = query.Range(page.Total, page.Rows.Count);
            RangeText = ObjectListQuery.RangeText(range, Text, culture);
            IsPreviousPageEnabled = range.HasPrevious;
            IsNextPageEnabled = range.HasNext;
            IsEmpty = Rows.Count == 0;
        }
        catch (Exception ex) when (generation == _generation && !token.IsCancellationRequested)
        {
            Logger.Error(ex, "Error loading the risk index objects");
            Rows.Clear();
            IsEmpty = false;
            IsPreviousPageEnabled = query.Page > 1;
            IsNextPageEnabled = false;
            ErrorMessage = ex is InvalidHttpRequestException { ErrorCode: { Length: > 0 } code } refusal
                ? RiskApiErrorText.Translate(code, refusal.Message, Text, StrLoadError)
                : StrLoadError;
        }
        catch (Exception ex)
        {
            // Superseded by a newer read or a new page: nothing to show, but the failure is still recorded.
            Logger.Debug("A superseded read of the risk index objects ended: {Message}", ex.Message);
        }
        finally
        {
            if (generation == _generation) IsBusy = false;
        }
    }

    /// <summary>Puts the filter choices, the preset chip and the sort line in step with the query being read.</summary>
    private void ShowQuery(ObjectListQuery query)
    {
        _applying = true;
        try
        {
            SelectedCategory = CategoryOptions.FirstOrDefault(o => o.Value == query.Category?.ToString()) ?? CategoryOptions[0];
            SelectedState = StateOptions.FirstOrDefault(o => o.Value == query.State?.ToString()) ?? StateOptions[0];
            SelectedClass = ClassOptions.FirstOrDefault(o => o.Value == query.AssetClass?.ToString()) ?? ClassOptions[0];
            SelectedSource = SourceOptions.FirstOrDefault(o => o.Value == query.Source) ?? SourceOptions[0];
            SelectedCriticality = CriticalityOptions.FirstOrDefault(o =>
                o.Value == query.Criticality?.ToString(CultureInfo.InvariantCulture)) ?? CriticalityOptions[0];
            SelectedPageSize = PageSizeOptions.FirstOrDefault(o => o.Size == query.PageSize) ?? PageSizeOptions[0];
        }
        finally
        {
            _applying = false;
        }

        var preset = new List<string>();
        if (query.TabScope is { } tab)
            preset.Add(string.Format(CultureInfo.CurrentCulture, StrTabScopeFmt, RiskText.Get(Text, AssetTabFormatter.NameKey(tab))));
        if (query.Preset is { } filter) preset.Add(query.PresetLabel ?? filter);
        PresetText = preset.Count == 0 ? null : string.Join(" · ", preset);
        SortText = ObjectListQuery.SortText(query, Text, CultureInfo.CurrentCulture);
    }

    private async Task OpenDetailAsync(RiskObjectRowViewModel? row)
    {
        if (row is null || OpenDetail is not { } open) return;
        await open(row.Row);
    }

    /// <summary>A double click on a row: the same as its "›".</summary>
    public void OpenSelected()
    {
        if (SelectedRow is { } row) _ = OpenDetailAsync(row);
    }

    #endregion
}

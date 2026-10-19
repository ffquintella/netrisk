using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GUIClient.Tools.RiskIndex;
using Material.Icons;
using Model.Exceptions;
using Model.RiskIndex;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>
/// One line of the Explore tree (S40 §5.6.1): a node with its cells, the "não comparáveis" note opening a comparability
/// group, or a state message ("Carregando…", none, an error) inside an expanded node. A node's children load the first time
/// it is expanded (§10.9): until then it holds one "Carregando…" line, so the tree draws the expansion at once.
/// </summary>
public sealed class RiskNodeTreeItem : ReactiveObject
{
    private readonly Func<RiskNodeTreeItem, Task>? _loadChildren;
    private bool _childrenRequested;

    public RiskNodeTreeItem(NodeTreeEntry entry, ReactiveCommand<string, RxVoid> open, string loadingText,
        Func<RiskNodeTreeItem, Task>? loadChildren)
    {
        Entry = entry;
        Open = open;
        _loadChildren = loadChildren;
        if (entry.CanExpand && loadChildren is not null)
            Children.Add(new RiskNodeTreeItem(NodeTreeRows.Message(loadingText), open, loadingText, null));
    }

    public NodeTreeEntry Entry { get; }

    public string Key => Entry.Key;
    public string Label => Entry.Label;
    public string TypeText => Entry.TypeText;
    public string IcrText => Entry.IcrText;
    public string DeltaText => Entry.DeltaText;
    public string CoverageText => Entry.CoverageText;
    public string ObjectsText => Entry.ObjectsText;
    public string SharedText => Entry.SharedText;
    public string StateText => Entry.StateText;
    public bool StateWarning => Entry.StateWarning;

    public bool IsNode => Entry.IsNode;
    public bool IsSeparator => Entry.Kind == NodeTreeEntryKind.Separator;
    public bool IsMessage => Entry.Kind == NodeTreeEntryKind.Message;

    public bool HasBand => Entry.BandLabel is not null;

    /// <summary>A node without a band (indicative, no data) shows "—" in the band column, never a pill.</summary>
    public bool ShowsNoBand => IsNode && !HasBand;
    public string BandLabel => Entry.BandLabel ?? "—";
    public MaterialIconKind BandGlyph => Entry.BandGlyph ?? MaterialIconKind.HelpCircleOutline;
    public bool IsBandStep1 => Entry.BandClass == "b1";
    public bool IsBandStep2 => Entry.BandClass == "b2";
    public bool IsBandStep3 => Entry.BandClass == "b3";
    public bool IsBandStep4 => Entry.BandClass == "b4";
    public bool IsBandStep5 => Entry.BandClass == "b5";

    /// <summary>Whether the row shows an expander: an entity child with children of its own, along an entity dimension.</summary>
    public bool CanExpand => Entry.CanExpand && _loadChildren is not null;

    /// <summary>The detail "›" (§5.6.1): opens the node for the whole module. Notes and messages open nothing.</summary>
    public ReactiveCommand<string, RxVoid> Open { get; }

    public ObservableCollection<RiskNodeTreeItem> Children { get; } = [];

    /// <summary>The tree's child selector: the first call starts the read of the children (lazy load on expand, §10.9).</summary>
    public IEnumerable<RiskNodeTreeItem> ExpandChildren()
    {
        if (!_childrenRequested && CanExpand)
        {
            _childrenRequested = true;
            _ = _loadChildren!(this);
        }

        return Children;
    }

    /// <summary>Replaces the children (or the message standing in for them) once read.</summary>
    public void SetChildren(IEnumerable<RiskNodeTreeItem> children)
    {
        Children.Clear();
        foreach (var child in children) Children.Add(child);
    }
}

/// <summary>
/// The Explore tree (S40 §5.6.1, §5.6.4, §10.8, §10.9; T254): the children of the current node along the dimension, with
/// the chips applied, grouped by comparability with a note between groups; each entity child expands on demand; Enter,
/// double click or "›" opens a node for the whole module; Alt+← goes up one breadcrumb level; Ctrl+F filters by name
/// (IX-8). The columns are built in the view's code-behind, like <c>VulnerabilitiesView</c>'s. It holds no node, dimension
/// or filter of its own: its owner hands it the read of the children (§5.6.2).
/// </summary>
public class RiskNodeTreeViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrColNode { get; } = Localizer["RiskExploreColNode"];
    public string StrColType { get; } = Localizer["RiskExploreColType"];
    public string StrColIcr { get; } = Localizer["RiskExploreColIcr"];
    public string StrColBand { get; } = Localizer["RiskExploreColBand"];
    public string StrColDelta { get; } = Localizer["RiskExploreColDelta"];
    public string StrColCoverage { get; } = Localizer["RiskExploreColCoverage"];
    public string StrColObjects { get; } = Localizer["RiskExploreColObjects"];
    public string StrColShared { get; } = Localizer["RiskExploreColShared"];
    public string StrColState { get; } = Localizer["RiskExploreColState"];
    public string StrOpenNodeHint { get; } = Localizer["RiskExploreOpenNodeHint"];
    public string StrSearchWatermark { get; } = Localizer["RiskExploreSearchWatermark"];
    public string StrSortNote { get; } = Localizer["RiskExploreSortNote"];
    public string StrKeyboardHint { get; } = Localizer["RiskExploreKeyboardHint"];
    public string StrRetry { get; } = Localizer["RiskOverviewRetry"];
    public string StrNoChildren { get; } = Localizer["RiskExploreNoChildren"];
    private string StrLoading { get; } = Localizer["RiskOverviewLoading"];
    private string StrLoadError { get; } = Localizer["RiskOverviewLoadError"];

    #endregion

    private static TextLookup Text => RiskHeadlineViewModel.Text;

    private Func<string, CancellationToken, Task<RiskIndexNodeChildrenDto>>? _read;
    private string? _parent;
    private IReadOnlyList<RiskIndexBandDto> _bands = [];
    private NodeLabels _labels = new("", "", "");
    private CancellationToken _token;
    private IReadOnlyList<NodeTreeEntry> _entries = [];
    private readonly Action<string> _openNode;

    public RiskNodeTreeViewModel(Action<string> openNode, Action goUp)
    {
        _openNode = openNode;
        BtOpenNodeClicked = ReactiveCommand.Create<string>(key => { if (!string.IsNullOrWhiteSpace(key)) openNode(key); });
        BtUpClicked = ReactiveCommand.Create(goUp);
        BtFocusSearchClicked = ReactiveCommand.Create(() => { SearchFocusSignal++; });
        BtRetryClicked = ReactiveCommand.CreateFromTask(LoadAsync);
    }

    #region COMMANDS

    /// <summary>Enter, double click or "›" on a node: the module shows that node (§5.6.1).</summary>
    public ReactiveCommand<string, RxVoid> BtOpenNodeClicked { get; }

    /// <summary>Alt+←: one breadcrumb level up.</summary>
    public ReactiveCommand<RxVoid, RxVoid> BtUpClicked { get; }

    /// <summary>Ctrl+F (IX-8): focuses the name filter.</summary>
    public ReactiveCommand<RxVoid, RxVoid> BtFocusSearchClicked { get; }

    public ReactiveCommand<RxVoid, RxVoid> BtRetryClicked { get; }

    #endregion

    #region PROPERTIES

    /// <summary>The top-level lines. Replaced in place, so the tree's source keeps observing the same collection.</summary>
    public ObservableCollection<RiskNodeTreeItem> Roots { get; } = [];

    private RiskNodeTreeItem? _selectedNode;
    public RiskNodeTreeItem? SelectedNode { get => _selectedNode; set => this.RaiseAndSetIfChanged(ref _selectedNode, value); }

    private string? _searchText;
    /// <summary>Ctrl+F's name filter over the top-level nodes (IX-8).</summary>
    public string? SearchText
    {
        get => _searchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchText, value);
            ShowRoots();
        }
    }

    private int _searchFocusSignal;
    public int SearchFocusSignal { get => _searchFocusSignal; set => this.RaiseAndSetIfChanged(ref _searchFocusSignal, value); }

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

    private bool _isEmpty;
    /// <summary>The node has no child with a visible object along this dimension.</summary>
    public bool IsEmpty { get => _isEmpty; private set => this.RaiseAndSetIfChanged(ref _isEmpty, value); }

    private string? _outsideNote;
    /// <summary>The Environment dimension's note: objects without an environment are outside the cut (§5.6.1).</summary>
    public string? OutsideNote
    {
        get => _outsideNote;
        private set
        {
            this.RaiseAndSetIfChanged(ref _outsideNote, value);
            this.RaisePropertyChanged(nameof(HasOutsideNote));
        }
    }

    public bool HasOutsideNote => !string.IsNullOrEmpty(OutsideNote);

    #endregion

    /// <summary>Opens the selected node (Enter, double click).</summary>
    public void OpenSelected()
    {
        if (SelectedNode is { IsNode: true } node) _openNode(node.Key);
    }

    /// <summary>What to read — the children of a node key — for which parent, and the bands and labels to show them with.</summary>
    public void SetSource(Func<string, CancellationToken, Task<RiskIndexNodeChildrenDto>>? read, string? parent,
        IReadOnlyList<RiskIndexBandDto> bands, NodeLabels labels, CancellationToken token)
    {
        _read = read;
        _parent = parent;
        _bands = bands;
        _labels = labels;
        _token = token;
        if (read is null) Clear();
    }

    public void Clear()
    {
        _entries = [];
        Roots.Clear();
        SelectedNode = null;
        ErrorMessage = null;
        IsEmpty = false;
        IsBusy = false;
        OutsideNote = null;
    }

    public async Task LoadAsync()
    {
        if (_read is not { } read || _parent is not { } parent) return;
        var token = _token;
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var children = await read(parent, token);
            if (token.IsCancellationRequested) return;
            _entries = NodeTreeRows.Build(children, _bands, _labels, Text, CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
            OutsideNote = NodeTreeRows.OutsideNote(children, Text, CultureInfo.CurrentCulture);
            IsEmpty = !_entries.Any(e => e.IsNode);
            ShowRoots();
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            Logger.Error(ex, "Error loading the Explore tree");
            _entries = [];
            Roots.Clear();
            IsEmpty = false;
            ErrorMessage = Explain(ex);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Superseded by another load.
        }
        finally
        {
            if (!token.IsCancellationRequested) IsBusy = false;
        }
    }

    private void ShowRoots()
    {
        var selected = SelectedNode?.Key;
        Roots.Clear();
        foreach (var entry in NodeTreeRows.Filter(_entries, SearchText)) Roots.Add(Item(entry));
        SelectedNode = Roots.FirstOrDefault(r => r.IsNode && r.Key == selected);
    }

    private RiskNodeTreeItem Item(NodeTreeEntry entry) => new(entry, BtOpenNodeClicked, StrLoading, LoadChildrenAsync);

    /// <summary>An expanded node's children, through the same read; a failure or an empty answer becomes a message line.</summary>
    private async Task LoadChildrenAsync(RiskNodeTreeItem item)
    {
        if (_read is not { } read) return;
        var token = _token;
        try
        {
            var children = await read(item.Key, token);
            if (token.IsCancellationRequested) return;
            var entries = NodeTreeRows.Build(children, _bands, _labels, Text, CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture);
            item.SetChildren(entries.Count == 0 ? [Item(NodeTreeRows.Message(StrNoChildren))] : entries.Select(Item));
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            Logger.Error(ex, "Error loading the children of {Node}", item.Key);
            item.SetChildren([Item(NodeTreeRows.Message(Explain(ex)))]);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Superseded.
        }
    }

    private string Explain(Exception ex) => ex is InvalidHttpRequestException { ErrorCode: { Length: > 0 } code } refusal
        ? RiskApiErrorText.Translate(code, refusal.Message, Text, StrLoadError)
        : StrLoadError;
}

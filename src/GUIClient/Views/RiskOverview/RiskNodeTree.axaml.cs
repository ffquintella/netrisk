using System;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Input;
using Avalonia.Interactivity;
using GUIClient.Tools.RiskIndex;
using GUIClient.ViewModels.RiskOverview;

namespace GUIClient.Views.RiskOverview;

/// <summary>
/// The Explore tree (S40 §5.6.1, §10.1, §10.8; T254). The TreeDataGrid's columns are built here, like
/// <c>VulnerabilitiesView</c>'s, because a hierarchical source and its expander column are code, not XAML. Sorting never
/// crosses a comparability group (<see cref="NodeTreeRows.Compare"/>), and Δ7d, band, type and state do not sort at all.
/// </summary>
public partial class RiskNodeTree : UserControl
{
    private RiskNodeTreeViewModel? _viewModel;
    private HierarchicalTreeDataGridSource<RiskNodeTreeItem>? _source;

    public RiskNodeTree()
    {
        InitializeComponent();

        // Enter and Alt+← are read on the way down, before the grid's own tunnel handler, which would take Alt+← for ←
        // (collapse, or go to the parent row) and swallow it.
        AddHandler(KeyDownEvent, OnPreviewKeyDown, RoutingStrategies.Tunnel);
        NodeTreeGrid.DoubleTapped += (_, _) => _viewModel?.OpenSelected();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        _viewModel = DataContext as RiskNodeTreeViewModel;
        BuildSource();
    }

    private void OnPreviewKeyDown(object? sender, KeyEventArgs e)
    {
        if (_viewModel is null || e.Source is TextBox) return;

        if (e.Key == Key.Left && e.KeyModifiers == KeyModifiers.Alt)
        {
            _viewModel.BtUpClicked.Execute().Subscribe();
            e.Handled = true;
        }
        else if (e.Key == Key.Enter && e.KeyModifiers == KeyModifiers.None && _viewModel.SelectedNode is { IsNode: true })
        {
            _viewModel.OpenSelected();
            e.Handled = true;
        }
    }

    /// <summary>A column that sorts by <paramref name="column"/> inside each comparability group (§5.6.4).</summary>
    internal static TextColumnOptions<RiskNodeTreeItem> Sorted(NodeTreeSortColumn column) => new()
    {
        CanUserSortColumn = true,
        CompareAscending = (a, b) => NodeTreeRows.Compare(a?.Entry, b?.Entry, column, false),
        CompareDescending = (a, b) => NodeTreeRows.Compare(a?.Entry, b?.Entry, column, true)
    };

    /// <summary>Δ7d, band, type and state: never sortable (§5.6.1 "Δ não é ordenável").</summary>
    internal static TextColumnOptions<RiskNodeTreeItem> Unsorted() => new() { CanUserSortColumn = false };

    private void BuildSource()
    {
        if (_source?.RowSelection is { } previous) previous.SelectionChanged -= OnSelectionChanged;
        _source = null;
        NodeTreeGrid.Source = null;
        if (_viewModel is not { } vm) return;

        var nameCell = new TemplateColumn<RiskNodeTreeItem>(vm.StrColNode, "RiskNodeNameCell", width: new GridLength(2, GridUnitType.Star),
            options: new TemplateColumnOptions<RiskNodeTreeItem>
            {
                CanUserSortColumn = true,
                CompareAscending = (a, b) => NodeTreeRows.Compare(a?.Entry, b?.Entry, NodeTreeSortColumn.Node, false),
                CompareDescending = (a, b) => NodeTreeRows.Compare(a?.Entry, b?.Entry, NodeTreeSortColumn.Node, true)
            });

        var source = new HierarchicalTreeDataGridSource<RiskNodeTreeItem>(vm.Roots)
        {
            Columns =
            {
                new HierarchicalExpanderColumn<RiskNodeTreeItem>(nameCell, x => x.ExpandChildren(), x => x.CanExpand),
                new TextColumn<RiskNodeTreeItem, string>(vm.StrColType, x => x.TypeText, options: Unsorted()),
                new TextColumn<RiskNodeTreeItem, string>(vm.StrColIcr, x => x.IcrText, options: Sorted(NodeTreeSortColumn.Icr)),
                new TemplateColumn<RiskNodeTreeItem>(vm.StrColBand, "RiskNodeBandCell",
                    options: new TemplateColumnOptions<RiskNodeTreeItem> { CanUserSortColumn = false }),
                new TextColumn<RiskNodeTreeItem, string>(vm.StrColDelta, x => x.DeltaText, options: Unsorted()),
                new TextColumn<RiskNodeTreeItem, string>(vm.StrColCoverage, x => x.CoverageText,
                    options: Sorted(NodeTreeSortColumn.Coverage)),
                new TextColumn<RiskNodeTreeItem, string>(vm.StrColObjects, x => x.ObjectsText,
                    options: Sorted(NodeTreeSortColumn.Objects)),
                new TextColumn<RiskNodeTreeItem, string>(vm.StrColShared, x => x.SharedText,
                    options: Sorted(NodeTreeSortColumn.Shared)),
                new TemplateColumn<RiskNodeTreeItem>(vm.StrColState, "RiskNodeStateCell",
                    options: new TemplateColumnOptions<RiskNodeTreeItem> { CanUserSortColumn = false }),
                new TemplateColumn<RiskNodeTreeItem>(null, "RiskNodeOpenCell",
                    options: new TemplateColumnOptions<RiskNodeTreeItem> { CanUserSortColumn = false })
            }
        };

        if (source.RowSelection is { } selection)
        {
            selection.SingleSelect = true;
            selection.SelectionChanged += OnSelectionChanged;
        }

        _source = source;
        NodeTreeGrid.Source = source;
    }

    private void OnSelectionChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null) _viewModel.SelectedNode = _source?.RowSelection?.SelectedItem;
    }
}

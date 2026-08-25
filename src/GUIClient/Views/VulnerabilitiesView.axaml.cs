using System;
using System.ComponentModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Controls.Models.TreeDataGrid;
using Avalonia.Controls.Selection;
using Avalonia.Controls.Templates;
using Avalonia.Data.Converters;
using Avalonia.Markup.Xaml;
using DAL.Entities;
using DAL.Enums;
using GUIClient.ViewModels;

namespace GUIClient.Views;

public partial class VulnerabilitiesView : UserControl
{
    private VulnerabilitiesViewModel? _viewModel;
    private FlatTreeDataGridSource<Vulnerability>? _source;
    private bool _syncingSelection;

    public VulnerabilitiesView()
    {
        InitializeComponent();

        // Owned here rather than bound from MainWindow, matching every other content view.
        // The order matters: OnDataContextChanged builds the TreeDataGrid source, so the named
        // grid has to exist before the DataContext lands — hence after InitializeComponent.
        DataContext = new VulnerabilitiesViewModel();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as VulnerabilitiesViewModel;

        if (_viewModel is null)
            return;

        _viewModel.PropertyChanged += OnViewModelPropertyChanged;
        BuildSource();
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            // The collection is reassigned on paging/reload, so the source must be rebuilt.
            case nameof(VulnerabilitiesViewModel.Vulnerabilities):
                BuildSource();
                break;
            case nameof(VulnerabilitiesViewModel.SelectedVulnerability):
                SyncSelectionToGrid();
                break;
        }
    }

    /// <summary>Applies one of the view's registered <see cref="IValueConverter"/> resources, mirroring the old DataGrid column bindings.</summary>
    private string? ConvertWith(string converterKey, object? value, object? parameter = null)
    {
        if (this.TryFindResource(converterKey, out var resource) && resource is IValueConverter converter)
            return converter.Convert(value, typeof(string), parameter, CultureInfo.CurrentCulture)?.ToString();

        return value?.ToString();
    }

    private void BuildSource()
    {
        if (_viewModel is null)
            return;

        var statusTemplate = this.TryFindResource("VulnStatusCellTemplate", out var resource)
            ? resource as IDataTemplate
            : null;

        var source = new FlatTreeDataGridSource<Vulnerability>(_viewModel.Vulnerabilities);

        source.Columns.Add(new TextColumn<Vulnerability, int>("ID", x => x.Id));

        if (statusTemplate is not null)
            source.Columns.Add(new TemplateColumn<Vulnerability>(_viewModel.StrStatus, statusTemplate));

        source.Columns.Add(new TextColumn<Vulnerability, string?>(_viewModel.StrTitle, x => x.Title));
        source.Columns.Add(new TextColumn<Vulnerability, string?>(_viewModel.StrScore, x => x.Score == null ? null : x.Score.Value.ToString("F2")));
        source.Columns.Add(new TextColumn<Vulnerability, string?>(_viewModel.StrImpact, x => ConvertWith("StringIdToImpactConverter", x.Severity, "keepId")));
        source.Columns.Add(new TextColumn<Vulnerability, DateTime>(_viewModel.StrFirstDetection, x => x.FirstDetection));
        source.Columns.Add(new TextColumn<Vulnerability, DateTime>(_viewModel.StrLastDetection, x => x.LastDetection));
        source.Columns.Add(new TextColumn<Vulnerability, int>(_viewModel.StrDetectionCount, x => x.DetectionCount));
        source.Columns.Add(new TextColumn<Vulnerability, string?>(_viewModel.StrFixTeam, x => ConvertWith("TeamIdToTeamNameConverter", x.FixTeamId, "keepId")));
        source.Columns.Add(new TextColumn<Vulnerability, string?>(_viewModel.StrAnalyst, x => ConvertWith("AnalystIdToAnalystNameConverter", x.AnalystId, "keepId")));
        source.Columns.Add(new TextColumn<Vulnerability, string?>(_viewModel.StrHost, x => ConvertWith("HostIdToNameConverter", x.HostId, "keepId")));
        source.Columns.Add(new TextColumn<Vulnerability, string?>(_viewModel.StrApplication, x => ConvertWith("EntityIdToNameConverter", x.EntityId, "keepId")));
        source.Columns.Add(new TextColumn<Vulnerability, string?>(_viewModel.StrTechnology, x => x.Technology));
        source.Columns.Add(new TextColumn<Vulnerability, string?>(_viewModel.StrSource, x => x.ImportSource));

        // Track 3 milestone 3.4.2 — the SLA columns. Sortable like every other column here, and
        // showing the deadline beside the days-overdue count is what makes an aging report readable
        // without leaving the grid.
        // Null-propagation is not allowed in a TreeDataGrid column expression (it becomes an
        // expression tree), so the null case is written out.
        source.Columns.Add(new TextColumn<Vulnerability, string?>(_viewModel.StrSlaDueDate,
            x => x.SlaDueDate == null ? null : x.SlaDueDate.Value.ToString("yyyy-MM-dd")));
        source.Columns.Add(new TextColumn<Vulnerability, int?>(_viewModel.StrDaysOverdue,
            x => x.DaysOverdue(DateTime.UtcNow)));
        // The enum is handed over raw rather than as x.LifecycleStatus.ToString(): TreeDataGrid
        // walks the getter as an expression tree, and its VisitMethodCall admits any call whose
        // *return* type is a reference type while building a Func<TModel, object> over the call's
        // *instance* — so a ToString() on a value type throws "Expression of type
        // 'DAL.Enums.FindingStatus' cannot be used for return type 'System.Object'". TextCell
        // renders via value?.ToString() anyway, so the displayed text is unchanged.
        source.Columns.Add(new TextColumn<Vulnerability, FindingStatus>(_viewModel.StrFindingLifecycle,
            x => x.LifecycleStatus));

        if (_source?.RowSelection is not null)
            _source.RowSelection.SelectionChanged -= OnRowSelectionChanged;

        _source = source;
        if (_source.RowSelection is not null)
        {
            _source.RowSelection.SingleSelect = true;
            _source.RowSelection.SelectionChanged += OnRowSelectionChanged;
        }

        VulnerabilitiesTreeGrid.Source = _source;

        SyncSelectionToGrid();
    }

    private void OnRowSelectionChanged(object? sender, TreeSelectionModelSelectionChangedEventArgs<Vulnerability> e)
    {
        if (_viewModel is null || _syncingSelection)
            return;

        _syncingSelection = true;
        _viewModel.SelectedVulnerability = _source?.RowSelection?.SelectedItem;
        _syncingSelection = false;
    }

    private void SyncSelectionToGrid()
    {
        if (_viewModel is null || _source?.RowSelection is null || _syncingSelection)
            return;

        _syncingSelection = true;

        var selected = _viewModel.SelectedVulnerability;
        if (selected is null)
        {
            _source.RowSelection.Clear();
        }
        else
        {
            var index = _viewModel.Vulnerabilities.IndexOf(selected);
            _source.RowSelection.SelectedIndex = index >= 0 ? new IndexPath(index) : default;
        }

        _syncingSelection = false;
    }
}

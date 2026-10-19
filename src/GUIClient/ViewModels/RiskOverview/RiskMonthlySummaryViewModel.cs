using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using LiveChartsCore;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Model.Exceptions;
using Model.RiskIndex;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>
/// A monthly category summary (S40 §5.5.1, §10.4, §10.8): grouped columns labelled "EXP 74" in the fixed order, or the same
/// values as a table, over 6 or 12 months. The Explore panel shows the current node's (§5.6.1: "mesmo gráfico, mesmos
/// rótulos e mesma alternativa em tabela da §5.5.1"); its owner hands it the read, which carries the node, the day, the
/// page's request gate and the cancellation of the page load.
/// </summary>
public class RiskMonthlySummaryViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrMonthlyHelp { get; } = Localizer["RiskMonthlyHelp"];
    public string StrMonthlyEmpty { get; } = Localizer["RiskMonthlyEmpty"];
    public string StrColMonth { get; } = Localizer["RiskMonthlyColMonth"];
    public string StrColNote { get; } = Localizer["RiskMonthlyColNote"];
    public string StrRetry { get; } = Localizer["RiskOverviewRetry"];
    private string StrViewAsTable { get; } = Localizer["RiskOverviewViewAsTable"];
    private string StrViewAsChart { get; } = Localizer["RiskOverviewViewAsChart"];
    private string StrLoadError { get; } = Localizer["RiskOverviewLoadError"];

    public string StrToggleView => IsMonthlyTable ? StrViewAsChart : StrViewAsTable;

    // The category codes are S39's identifiers, shown untranslated everywhere in the module (HeadlineFormatter.CategoryCode).
    public string StrColReg { get; } = HeadlineFormatter.CategoryCode(RiskIndexCategory.Reg);
    public string StrColExp { get; } = HeadlineFormatter.CategoryCode(RiskIndexCategory.Exp);
    public string StrColAme { get; } = HeadlineFormatter.CategoryCode(RiskIndexCategory.Ame);
    public string StrColCtl { get; } = HeadlineFormatter.CategoryCode(RiskIndexCategory.Ctl);
    public string StrColGov { get; } = HeadlineFormatter.CategoryCode(RiskIndexCategory.Gov);
    public string StrColTer { get; } = HeadlineFormatter.CategoryCode(RiskIndexCategory.Ter);

    #endregion

    private static TextLookup Text => RiskHeadlineViewModel.Text;

    private Func<int, CancellationToken, Task<RiskIndexMonthlySummaryDto>>? _read;
    private CancellationToken _token;
    private RiskIndexMonthlySummaryDto? _summary;

    public RiskMonthlySummaryViewModel()
    {
        MonthsOptions = MonthlySummaryFormatter.MonthOptions
            .Select(m => new MonthsOption(m, string.Format(CultureInfo.CurrentCulture, Localizer["RiskMonthlyMonthsFMT"], m))).ToList();
        _selectedMonths = MonthsOptions[0];
        BtToggleMonthlyTableClicked = ReactiveCommand.Create(() => { IsMonthlyTable = !IsMonthlyTable; });
        BtRetryClicked = ReactiveCommand.CreateFromTask(LoadAsync);
        ChartPalette.Current.Changed += OnThemeChanged;
    }

    public ReactiveCommand<RxVoid, RxVoid> BtToggleMonthlyTableClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRetryClicked { get; }

    #region PROPERTIES

    public IReadOnlyList<MonthsOption> MonthsOptions { get; }

    private MonthsOption _selectedMonths;
    public MonthsOption SelectedMonths
    {
        get => _selectedMonths;
        set
        {
            if (value is null || value == _selectedMonths) return;
            this.RaiseAndSetIfChanged(ref _selectedMonths, value);
            _ = LoadAsync();
        }
    }

    private bool _isMonthlyTable;
    public bool IsMonthlyTable
    {
        get => _isMonthlyTable;
        set
        {
            this.RaiseAndSetIfChanged(ref _isMonthlyTable, value);
            this.RaisePropertyChanged(nameof(StrToggleView));
            this.RaisePropertyChanged(nameof(IsMonthlyChart));
            this.RaisePropertyChanged(nameof(IsMonthlyGrid));
        }
    }

    public bool IsMonthlyChart => !IsMonthlyTable && HasMonthly;
    public bool IsMonthlyGrid => IsMonthlyTable && HasMonthly;

    private bool _hasMonthly;
    public bool HasMonthly
    {
        get => _hasMonthly;
        private set
        {
            this.RaiseAndSetIfChanged(ref _hasMonthly, value);
            this.RaisePropertyChanged(nameof(IsMonthlyChart));
            this.RaisePropertyChanged(nameof(IsMonthlyGrid));
        }
    }

    private bool _isMonthlyEmpty;
    public bool IsMonthlyEmpty { get => _isMonthlyEmpty; private set => this.RaiseAndSetIfChanged(ref _isMonthlyEmpty, value); }

    private bool _isMonthlyBusy;
    public bool IsMonthlyBusy { get => _isMonthlyBusy; private set => this.RaiseAndSetIfChanged(ref _isMonthlyBusy, value); }

    private string? _monthlyError;
    public string? MonthlyError
    {
        get => _monthlyError;
        private set
        {
            this.RaiseAndSetIfChanged(ref _monthlyError, value);
            this.RaisePropertyChanged(nameof(HasMonthlyError));
        }
    }

    public bool HasMonthlyError => !string.IsNullOrEmpty(MonthlyError);

    private ISeries[] _monthlySeries = [];
    public ISeries[] MonthlySeries { get => _monthlySeries; private set => this.RaiseAndSetIfChanged(ref _monthlySeries, value); }

    private Axis[] _monthlyXAxes = [new Axis()];
    public Axis[] MonthlyXAxes { get => _monthlyXAxes; private set => this.RaiseAndSetIfChanged(ref _monthlyXAxes, value); }

    private Axis[] _monthlyYAxes = [new Axis { MinLimit = 0, MaxLimit = 100 }];
    public Axis[] MonthlyYAxes { get => _monthlyYAxes; private set => this.RaiseAndSetIfChanged(ref _monthlyYAxes, value); }

    public ObservableCollection<MonthlyTableRow> MonthlyRows { get; } = [];

    private string _monthlyLegend = "";
    public string MonthlyLegend { get => _monthlyLegend; private set => this.RaiseAndSetIfChanged(ref _monthlyLegend, value); }

    private IReadOnlySet<RiskIndexCategory> _columns = new HashSet<RiskIndexCategory>();
    public bool ShowsReg => _columns.Contains(RiskIndexCategory.Reg);
    public bool ShowsExp => _columns.Contains(RiskIndexCategory.Exp);
    public bool ShowsAme => _columns.Contains(RiskIndexCategory.Ame);
    public bool ShowsCtl => _columns.Contains(RiskIndexCategory.Ctl);
    public bool ShowsGov => _columns.Contains(RiskIndexCategory.Gov);
    public bool ShowsTer => _columns.Contains(RiskIndexCategory.Ter);

    #endregion

    #region LOADING

    /// <summary>What to read (months in, summary out) and the token that drops it when the page moves on; null clears it.</summary>
    public void SetSource(Func<int, CancellationToken, Task<RiskIndexMonthlySummaryDto>>? read, CancellationToken token)
    {
        _read = read;
        _token = token;
        if (read is null) Clear();
    }

    public void Clear()
    {
        _summary = null;
        MonthlySeries = [];
        MonthlyRows.Clear();
        MonthlyLegend = "";
        HasMonthly = false;
        IsMonthlyEmpty = false;
        IsMonthlyBusy = false;
        MonthlyError = null;
    }

    public async Task LoadAsync()
    {
        if (_read is not { } read) return;
        var token = _token;
        IsMonthlyBusy = true;
        MonthlyError = null;
        try
        {
            var summary = await read(SelectedMonths.Months, token);
            if (token.IsCancellationRequested) return;
            _summary = summary;
            Draw(summary);
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            Logger.Error(ex, "Error loading a monthly category summary");
            Clear();
            MonthlyError = ex is InvalidHttpRequestException { ErrorCode: { Length: > 0 } code } refusal
                ? RiskApiErrorText.Translate(code, refusal.Message, Text, StrLoadError)
                : StrLoadError;
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Superseded by another page load.
        }
        finally
        {
            if (!token.IsCancellationRequested) IsMonthlyBusy = false;
        }
    }

    #endregion

    #region DRAWING

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_summary is { } summary) Draw(summary);
    }

    /// <summary>One <c>ColumnSeries&lt;double?&gt;</c> per category in the fixed order, each bar labelled "EXP 74" (§10.4).</summary>
    private void Draw(RiskIndexMonthlySummaryDto summary)
    {
        var culture = CultureInfo.CurrentCulture;
        var palette = ChartPalette.Current;
        MonthlySeries = MonthlySummaryFormatter.Series(summary).Select(s =>
        {
            var category = s.Category;
            return (ISeries)new ColumnSeries<double?>
            {
                Name = s.Code, Values = s.Values.ToList(), Fill = new SolidColorPaint(palette.Series(category)),
                DataLabelsPaint = new SolidColorPaint(palette.Axis), DataLabelsSize = 10,
                DataLabelsPosition = DataLabelsPosition.Top,
                DataLabelsFormatter = p => MonthlySummaryFormatter.DataLabel(category, p.Model, culture),
                YToolTipLabelFormatter = p => MonthlySummaryFormatter.DataLabel(category, p.Model, culture)
            };
        }).ToArray();
        MonthlyXAxes = [new Axis
        {
            Labels = MonthlySummaryFormatter.MonthLabels(summary, culture).ToList(), LabelsPaint = new SolidColorPaint(palette.Axis),
            MinStep = 1
        }];
        MonthlyYAxes = [new Axis
        {
            MinLimit = 0, MaxLimit = 100, MinStep = 25, LabelsPaint = new SolidColorPaint(palette.Axis),
            SeparatorsPaint = new SolidColorPaint(palette.Grid, 1)
        }];

        MonthlyRows.Clear();
        foreach (var row in MonthlySummaryFormatter.TableRows(summary, Text, culture)) MonthlyRows.Add(row);
        _columns = summary.SeriesOrder.ToHashSet();
        foreach (var name in new[] { nameof(ShowsReg), nameof(ShowsExp), nameof(ShowsAme), nameof(ShowsCtl), nameof(ShowsGov), nameof(ShowsTer) })
            this.RaisePropertyChanged(name);
        MonthlyLegend = MonthlySummaryFormatter.Legend(summary, Text);
        HasMonthly = MonthlySummaryFormatter.HasValues(summary);
        IsMonthlyEmpty = !HasMonthly;
    }

    #endregion

    public override void Dispose()
    {
        ChartPalette.Current.Changed -= OnThemeChanged;
        base.Dispose();
    }
}

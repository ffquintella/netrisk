using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using LiveChartsCore;
using LiveChartsCore.Defaults;
using LiveChartsCore.Kernel;
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Drawing.Geometries;
using LiveChartsCore.SkiaSharpView.Painting;
using LiveChartsCore.SkiaSharpView.Painting.Effects;
using Model.Exceptions;
using Model.RiskIndex;
using ReactiveUI;
using SkiaSharp;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>
/// The trend (S40 §5.3, §10.4, §10.8; T252): the node's daily ICR* (or a category's C_k) over 30, 90 or 365 days on the
/// band areas of each profile stretch, with the EWMA, floor shading, discontinuity markers, vendor annotations and the
/// shapes of gaps and indicative days, and "Ver como tabela" with the same information as the hints. It formats what the
/// server sent (<see cref="TrendFormatter"/>); colours come from the token palette, never a literal.
/// </summary>
public class RiskTrendViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrTitle { get; } = Localizer["RiskTrendTitle"];
    public string StrHelp { get; } = Localizer["RiskTrendHelp"];
    public string StrWindow { get; } = Localizer["RiskTrendWindow"];
    public string StrSeries { get; } = Localizer["RiskTrendSeries"];
    public string StrPartialNoTrend { get; } = Localizer["RiskTrendPartialUnavailable"];
    public string StrNoPoints { get; } = Localizer["RiskTrendNoPoints"];
    public string StrRetry { get; } = Localizer["RiskOverviewRetry"];
    public string StrLegendLine { get; } = Localizer["RiskTrendLegendLine"];
    public string StrLegendEwma { get; } = Localizer["RiskTrendLegendEwma"];
    public string StrLegendMarker { get; } = Localizer["RiskTrendLegendMarker"];
    public string StrLegendAnnotation { get; } = Localizer["RiskTrendLegendAnnotation"];
    public string StrLegendGap { get; } = Localizer["RiskTrendLegendGap"];
    public string StrLegendIndicative { get; } = Localizer["RiskTrendLegendIndicative"];
    public string StrColDate { get; } = Localizer["RiskTrendColDate"];
    public string StrColUnfloored { get; } = Localizer["RiskTrendColUnfloored"];
    public string StrColFloored { get; } = Localizer["RiskTrendColFloored"];
    public string StrColEwma { get; } = Localizer["RiskTrendColEwma"];
    public string StrColDelta { get; } = Localizer["RiskTrendColDelta"];
    public string StrColState { get; } = Localizer["RiskTrendColState"];
    public string StrColInterval { get; } = Localizer["RiskTrendColInterval"];
    public string StrColMarker { get; } = Localizer["RiskTrendColMarker"];
    public string StrColGap { get; } = Localizer["RiskTrendColGap"];
    private string StrViewAsTable { get; } = Localizer["RiskOverviewViewAsTable"];
    private string StrViewAsChart { get; } = Localizer["RiskOverviewViewAsChart"];
    private string StrLoadError { get; } = Localizer["RiskOverviewLoadError"];

    /// <summary>The toggle's label alternates (§5.3): "Ver como tabela" ↔ "Ver como gráfico".</summary>
    public string StrToggleView => IsTableView ? StrViewAsChart : StrViewAsTable;

    #endregion

    private static TextLookup Text => RiskHeadlineViewModel.Text;

    private IRiskIndexService RiskIndexService { get; } = GetService<IRiskIndexService>();
    private readonly Action<DateOnly> _selectDay;

    private string? _node;
    private DateOnly? _date;
    private RiskIndexProvenance _provenance;
    private RiskIndexTrendDto? _trend;
    private Dictionary<DateOnly, string> _tooltips = new();
    private CancellationTokenSource? _loading;
    private bool _applyingOptions;

    /// <param name="selectDay">Shows the dashboard as it was on a clicked day (the module's date picker rule).</param>
    public RiskTrendViewModel(Action<DateOnly> selectDay)
    {
        _selectDay = selectDay;
        BtToggleTableClicked = ReactiveCommand.Create(() => { IsTableView = !IsTableView; });
        BtTrendPointClicked = ReactiveCommand.Create<object?>(PointClicked);
        BtRetryClicked = ReactiveCommand.CreateFromTask(ReloadAsync);
        ChartPalette.Current.Changed += OnThemeChanged;
    }

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtToggleTableClicked { get; }

    /// <summary>The chart's <c>DataPointerDownCommand</c>: a click on a day selects that snapshot (§10.4).</summary>
    public ReactiveCommand<object?, RxVoid> BtTrendPointClicked { get; }

    public ReactiveCommand<RxVoid, RxVoid> BtRetryClicked { get; }

    #endregion

    #region PROPERTIES

    public ObservableCollection<TrendWindowOption> Windows { get; } = [];
    public ObservableCollection<TrendSeriesOption> SeriesOptions { get; } = [];
    public ObservableCollection<TrendTableRow> Rows { get; } = [];

    private TrendWindowOption? _selectedWindow;
    public TrendWindowOption? SelectedWindow
    {
        get => _selectedWindow;
        set
        {
            if (value is { IsEnabled: false }) return; // a disabled window keeps the current one (§5.3)
            this.RaiseAndSetIfChanged(ref _selectedWindow, value);
            if (!_applyingOptions && value is not null) _ = ReloadAsync();
        }
    }

    private TrendSeriesOption? _selectedSeries;
    public TrendSeriesOption? SelectedSeries
    {
        get => _selectedSeries;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedSeries, value);
            if (!_applyingOptions && value is not null) _ = ReloadAsync();
        }
    }

    private bool _isTableView;
    public bool IsTableView
    {
        get => _isTableView;
        set
        {
            this.RaiseAndSetIfChanged(ref _isTableView, value);
            this.RaisePropertyChanged(nameof(StrToggleView));
            this.RaisePropertyChanged(nameof(IsChartView));
        }
    }

    public bool IsChartView => !IsTableView && HasPoints;

    private bool _isActive;
    /// <summary>A node with a trend was given (not "Meu escopo", not a §5.13 state).</summary>
    public bool IsActive { get => _isActive; private set => this.RaiseAndSetIfChanged(ref _isActive, value); }

    private bool _isPartial;
    /// <summary>A partial view has no trend (§5.0): the panel says so instead of drawing.</summary>
    public bool IsPartial { get => _isPartial; private set => this.RaiseAndSetIfChanged(ref _isPartial, value); }

    private bool _hasPoints;
    public bool HasPoints
    {
        get => _hasPoints;
        private set
        {
            this.RaiseAndSetIfChanged(ref _hasPoints, value);
            this.RaisePropertyChanged(nameof(IsChartView));
            this.RaisePropertyChanged(nameof(IsEmpty));
        }
    }

    public bool IsEmpty => IsActive && !IsPartial && !IsBusy && !HasError && !HasPoints;

    private string? _errorMessage;
    public string? ErrorMessage
    {
        get => _errorMessage;
        private set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
            this.RaisePropertyChanged(nameof(IsEmpty));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    private ISeries[] _series = [];
    public ISeries[] Series { get => _series; private set => this.RaiseAndSetIfChanged(ref _series, value); }

    private RectangularSection[] _sections = [];
    public RectangularSection[] Sections { get => _sections; private set => this.RaiseAndSetIfChanged(ref _sections, value); }

    private Axis[] _xAxes = [new DateTimeAxis(TimeSpan.FromDays(1), d => d.ToString("dd/MM", CultureInfo.CurrentCulture))];
    public Axis[] XAxes { get => _xAxes; private set => this.RaiseAndSetIfChanged(ref _xAxes, value); }

    private Axis[] _yAxes = [new Axis { MinLimit = 0, MaxLimit = 100 }];
    public Axis[] YAxes { get => _yAxes; private set => this.RaiseAndSetIfChanged(ref _yAxes, value); }

    #endregion

    #region LOADING

    /// <summary>No trend to show: "Meu escopo", a §5.13 state, or before the first overview.</summary>
    public void Clear()
    {
        _loading?.Cancel();
        _trend = null;
        _node = null;
        IsActive = false;
        IsPartial = false;
        ErrorMessage = null;
        Rows.Clear();
        Series = [];
        Sections = [];
        HasPoints = false;
    }

    /// <summary>
    /// The node shown changed: rebuild the window and series choices for it (365 days only for a pre-computed node, §5.3)
    /// and read its trend. A partial view reads nothing.
    /// </summary>
    public Task LoadAsync(string node, DateOnly snapshotDate, RiskIndexOverviewDto overview)
    {
        _node = node;
        _date = snapshotDate;
        _provenance = overview.Provenance;
        IsActive = true;
        IsPartial = !TrendFormatter.HasTrend(overview.Provenance);

        _applyingOptions = true;
        try
        {
            var window = TrendFormatter.EffectiveWindow(SelectedWindow?.Days ?? TrendFormatter.DefaultWindow, overview.Provenance);
            Replace(Windows, TrendFormatter.WindowOptions(overview.Provenance, Text, CultureInfo.CurrentCulture));
            SelectedWindow = Windows.First(w => w.Days == window);

            var series = SelectedSeries?.Series ?? RiskIndexCategory.Icr;
            Replace(SeriesOptions, TrendFormatter.SeriesOptions(overview.Categories, Text));
            SelectedSeries = SeriesOptions.FirstOrDefault(s => s.Series == series) ?? SeriesOptions[0];
        }
        finally
        {
            _applyingOptions = false;
        }

        if (!IsPartial) return ReloadAsync();

        _loading?.Cancel();
        Rows.Clear();
        Series = [];
        Sections = [];
        HasPoints = false;
        ErrorMessage = null;
        return Task.CompletedTask;
    }

    /// <summary>The category's "›" (§5.4): the same node with the trend in that category's series.</summary>
    public void ShowSeries(RiskIndexCategory category)
    {
        if (SeriesOptions.FirstOrDefault(s => s.Series == category) is { } option) SelectedSeries = option;
    }

    private async Task ReloadAsync()
    {
        if (_node is null || IsPartial || SelectedWindow is null || SelectedSeries is null) return;

        _loading?.Cancel();
        var loading = _loading = new CancellationTokenSource();
        IsBusy = true;
        ErrorMessage = null;
        try
        {
            var trend = await RiskIndexService.GetTrendAsync(_node, SelectedWindow.Days, SelectedSeries.Series, _date, loading.Token);
            if (loading.IsCancellationRequested) return;
            _trend = trend;
            Draw(trend);
        }
        catch (Exception ex) when (!loading.IsCancellationRequested)
        {
            Logger.Error(ex, "Error loading the risk index trend");
            _trend = null;
            HasPoints = false;
            Rows.Clear();
            ErrorMessage = ex is InvalidHttpRequestException { ErrorCode: { Length: > 0 } code } refusal
                ? RiskApiErrorText.Translate(code, refusal.Message, Text, StrLoadError)
                : StrLoadError;
        }
        catch (Exception) when (loading.IsCancellationRequested)
        {
            // Superseded by a newer node, window or series.
        }
        finally
        {
            if (ReferenceEquals(_loading, loading))
            {
                IsBusy = false;
                this.RaisePropertyChanged(nameof(IsEmpty));
            }
        }
    }

    #endregion

    #region DRAWING

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_trend is { } trend) Draw(trend);
    }

    /// <summary>Builds the chart and the table from one trend; LiveCharts types stay here, the rules in TrendFormatter.</summary>
    private void Draw(RiskIndexTrendDto trend)
    {
        var culture = CultureInfo.CurrentCulture;
        var palette = ChartPalette.Current;
        var markers = TrendFormatter.Markers(trend, Text, culture);
        var markersByDay = markers.GroupBy(m => m.Date).ToDictionary(g => g.Key, g => (IReadOnlyList<TrendMark>)g.ToList());
        var annotations = TrendFormatter.Annotations(trend, Text, culture);
        _tooltips = trend.Points.ToDictionary(p => p.Date,
            p => TrendFormatter.PointTooltip(p, markersByDay.GetValueOrDefault(p.Date) ?? [], Text, culture));
        foreach (var annotation in annotations)
            _tooltips[annotation.Date] = _tooltips.TryGetValue(annotation.Date, out var tip) ? tip + Environment.NewLine + annotation.Text
                : annotation.Text;

        var lineColor = trend.Series == RiskIndexCategory.Icr ? palette.Axis : palette.Series(trend.Series);
        var series = new List<ISeries>
        {
            new LineSeries<DateTimePoint>
            {
                Name = StrLegendLine, Values = trend.Points.Select(p => Point(p.Date, TrendFormatter.LineValue(p))).ToList(),
                Stroke = new SolidColorPaint(lineColor, 3), Fill = null, GeometrySize = 0, LineSmoothness = 0,
                YToolTipLabelFormatter = p => Tip(p.Model)
            },
            new LineSeries<DateTimePoint>
            {
                Name = StrLegendEwma, Values = trend.Points.Select(p => Point(p.Date, p.Ewma)).ToList(),
                Stroke = new SolidColorPaint(palette.Ewma, 2) { PathEffect = new DashEffect([6, 4]) }, Fill = null, GeometrySize = 0,
                LineSmoothness = 0, IsHoverable = false
            }
        };

        // The rebaseline bridge (§5.3): "v{n} recalculado" as a dotted line over the 90 days before the change.
        foreach (var overlay in trend.Overlays)
            series.Add(new LineSeries<DateTimePoint>
            {
                Name = string.Format(culture, Localizer["RiskTrendOverlayFMT"], overlay.ToProfileVersion) +
                       (overlay.IsPartial ? " " + Localizer["RiskTrendOverlayPartial"] : ""),
                Values = overlay.Points.Select(o => Point(o.Date, o.Value)).ToList(),
                Stroke = new SolidColorPaint(palette.Marker, 2) { PathEffect = new DashEffect([2, 3]) }, Fill = null, GeometrySize = 0,
                LineSmoothness = 0, IsHoverable = false
            });

        // The interval whiskers: one vertical segment per day, broken by a null between days.
        var whiskers = TrendFormatter.Whiskers(trend);
        if (whiskers.Count > 0)
            series.Add(new LineSeries<DateTimePoint>
            {
                Name = StrColInterval, IsVisibleAtLegend = false, IsHoverable = false, GeometrySize = 0, Fill = null, LineSmoothness = 0,
                Stroke = new SolidColorPaint(palette.Axis.WithAlpha((byte)(255 * ChartPaletteMap.IntervalOpacity)), 2),
                Values = whiskers.SelectMany(w => new[] { Point(w.Date, w.Low), Point(w.Date, w.High), Point(w.Date, null) }).ToList()
            });

        // Shape, not colour, tells the days apart (§10.8): hollow circle = gap, hollow square = indicative, triangle =
        // marker, hollow diamond = vendor annotation at the foot.
        var gaps = TrendFormatter.Gaps(trend);
        if (gaps.Count > 0)
            series.Add(new ScatterSeries<DateTimePoint, CircleGeometry>
            {
                Name = StrLegendGap, Values = gaps.Select(g => Point(g.Date, g.Value)).ToList(), GeometrySize = 10, Fill = null,
                Stroke = new SolidColorPaint(lineColor, 2), YToolTipLabelFormatter = p => Tip(p.Model)
            });
        var indicative = TrendFormatter.Indicative(trend);
        if (indicative.Count > 0)
            series.Add(new ScatterSeries<DateTimePoint, RectangleGeometry>
            {
                Name = StrLegendIndicative, Values = indicative.Select(g => Point(g.Date, g.Value)).ToList(), GeometrySize = 10,
                Fill = null, Stroke = new SolidColorPaint(lineColor, 2), YToolTipLabelFormatter = p => Tip(p.Model)
            });
        if (markers.Count > 0)
            series.Add(new ScatterSeries<DateTimePoint, VariableSVGPathGeometry>
            {
                Name = StrLegendMarker, GeometrySvg = "M 0 0 L 10 0 L 5 9 Z", GeometrySize = 12,
                Values = markers.Select(m => Point(m.Date, 100)).ToList(), Fill = new SolidColorPaint(palette.Marker), Stroke = null,
                YToolTipLabelFormatter = p => Tip(p.Model)
            });
        if (annotations.Count > 0)
            series.Add(new ScatterSeries<DateTimePoint, DiamondGeometry>
            {
                Name = StrLegendAnnotation, Values = annotations.Select(a => Point(a.Date, 2)).ToList(), GeometrySize = 11, Fill = null,
                Stroke = new SolidColorPaint(palette.Marker, 2), YToolTipLabelFormatter = p => Tip(p.Model)
            });

        Series = series.ToArray();
        Sections = BuildSections(trend, markers, palette, culture);
        XAxes = [new DateTimeAxis(TimeSpan.FromDays(1), d => d.ToString("dd/MM", culture))
        {
            LabelsPaint = new SolidColorPaint(palette.Axis), SeparatorsPaint = new SolidColorPaint(palette.Grid, 1)
        }];
        YAxes = [new Axis
        {
            MinLimit = 0, MaxLimit = 100, MinStep = 10, LabelsPaint = new SolidColorPaint(palette.Axis),
            SeparatorsPaint = new SolidColorPaint(palette.Grid, 1)
        }];

        Replace(Rows, TrendFormatter.TableRows(trend, Text, culture));
        HasPoints = trend.Points.Count > 0;
    }

    private static RectangularSection[] BuildSections(RiskIndexTrendDto trend, IReadOnlyList<TrendMark> markers, ChartPalette palette,
        CultureInfo culture)
    {
        var sections = new List<RectangularSection>();
        foreach (var area in TrendFormatter.BandAreas(trend, Text, culture, CultureInfo.CurrentUICulture))
            sections.Add(new RectangularSection
            {
                Xi = Ticks(area.From), Xj = Ticks(area.ToExclusive), Yi = area.Lower, Yj = area.Upper,
                Fill = new SolidColorPaint(palette.BandArea(area.Position, area.Count)),
                Label = area.Label, LabelSize = 11, LabelPaint = new SolidColorPaint(palette.Axis), ZIndex = -2
            });

        foreach (var (from, to) in TrendFormatter.FloorRanges(trend))
            sections.Add(new RectangularSection
            {
                Xi = Ticks(from), Xj = Ticks(to), Fill = new SolidColorPaint(palette.FloorShade),
                Label = Localizer["RiskTrendFloorLabel"], LabelSize = 10, LabelPaint = new SolidColorPaint(palette.Axis), ZIndex = -1
            });

        foreach (var marker in markers)
            sections.Add(new RectangularSection
            {
                Xi = Ticks(marker.Date), Xj = Ticks(marker.Date),
                Stroke = new SolidColorPaint(palette.Marker, 2) { PathEffect = new DashEffect([4, 4]) }
            });

        return sections.ToArray();
    }

    private static double Ticks(DateOnly date) => date.ToDateTime(TimeOnly.MinValue).Ticks;

    private static DateTimePoint Point(DateOnly date, double? value) => new(date.ToDateTime(TimeOnly.MinValue), value);

    private string Tip(DateTimePoint? point) =>
        point is not null && _tooltips.TryGetValue(DateOnly.FromDateTime(point.DateTime), out var tip) ? tip : "";

    /// <summary>A click selects the day; a day without a snapshot is refused by the module (its date-picker rule).</summary>
    private void PointClicked(object? argument)
    {
        // The chart hands the points under the pointer; some builds wrap them in PointerCommandArgs.
        var points = argument switch
        {
            IEnumerable<ChartPoint> direct => direct,
            LiveChartsCore.Kernel.Events.PointerCommandArgs { OriginalEventArgs: IEnumerable<ChartPoint> wrapped } => wrapped,
            _ => null
        };
        if (points is null) return;
        var model = points.Select(p => p.Context.DataSource).OfType<DateTimePoint>().FirstOrDefault();
        if (model is null) return;
        _selectDay(DateOnly.FromDateTime(model.DateTime));
    }

    #endregion

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }

    public override void Dispose()
    {
        ChartPalette.Current.Changed -= OnThemeChanged;
        _loading?.Cancel();
        base.Dispose();
    }
}

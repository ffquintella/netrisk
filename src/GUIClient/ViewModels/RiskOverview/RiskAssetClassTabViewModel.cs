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
using LiveChartsCore.Measure;
using LiveChartsCore.SkiaSharpView;
using LiveChartsCore.SkiaSharpView.Painting;
using Material.Icons;
using Model.Exceptions;
using Model.RiskIndex;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>
/// A risk factor line (§5.5.1): volume, what it is, and the points it carries. Its "›" lists the objects behind it — the
/// object list filtered by <see cref="ObjectFilter"/> (T254).
/// </summary>
public sealed record RiskFactorLine(string CountText, string Label, string PointsText, string ObjectFilter,
    ReactiveCommand<string, RxVoid> Open)
{
    public bool CanOpen => ObjectListQuery.IsValidPreset(ObjectFilter);
}

/// <summary>A segment of the visibility bar with its width, glyph and the class that colours it.</summary>
public sealed record VisibilitySegmentView(VisibilitySegment Segment, double Width)
{
    public string Label => Segment.Label;
    public MaterialIconKind Glyph => Segment.Glyph;
    public bool IsAssessed => Segment.Kind == "assessed";
    public bool IsStale => Segment.Kind == "stale";
    public bool IsPresumed => Segment.Kind == "presumed";
    public bool IsNotAssessed => Segment.Kind == "notAssessed";
    public bool IsCurrent => Segment.Kind == "current";
    public bool IsExpired => Segment.Kind == "expired";
    public bool IsNoReading => Segment.Kind == "noReading";

    /// <summary>The segments of one bar, each as wide as its share of <paramref name="width"/>.</summary>
    public static IReadOnlyList<VisibilitySegmentView> Of(IReadOnlyList<VisibilitySegment> segments, double width)
    {
        var widths = MeterGeometry.Segments(segments.Select(s => s.Count).ToList(), width);
        return segments.Select((s, i) => new VisibilitySegmentView(s, widths[i])).ToList();
    }
}

/// <summary>
/// One source's visibility row (§5.5.1; T256): its name and either its reading bar with the counts in the legend, or why it
/// is not enabled. Colour is never the only information: every segment has its glyph and count (§10.7).
/// </summary>
public sealed record SourceVisibilityRowView(SourceVisibilityRow Row, IReadOnlyList<VisibilitySegmentView> Segments)
{
    public string Label => Row.Label;
    public bool IsEnabled => Row.IsEnabled;
    public bool IsDisabled => !Row.IsEnabled;
    public string? DisabledText => Row.DisabledText;
    public string? LastReadingText => Row.LastReadingText;
    public bool HasLastReading => !string.IsNullOrEmpty(Row.LastReadingText);

    /// <summary>A source that read nothing in the tab: the legend still says "Sem leitura N", the bar stays empty.</summary>
    public bool HasBar => Segments.Any(s => s.Width > 0);
}

/// <summary>One band's count of the R distribution, as text and glyph on the band's pill.</summary>
public sealed record DistributionItem(string Text, MaterialIconKind Glyph, string BandClass)
{
    public bool IsBandStep1 => BandClass == "b1";
    public bool IsBandStep2 => BandClass == "b2";
    public bool IsBandStep3 => BandClass == "b3";
    public bool IsBandStep4 => BandClass == "b4";
    public bool IsBandStep5 => BandClass == "b5";
}

/// <summary>A choice of the summary's span: 6 or 12 months.</summary>
public sealed record MonthsOption(int Months, string Label);

/// <summary>
/// One asset-class tab (S40 §5.5–§5.5.1; T253): its header level (node ∩ class), and its content — the monthly category
/// summary (grouped columns labelled "EXP 74", or the table), the risk factors (volume and points) and the attack-surface
/// visibility of the inventory — per source (T256) and overall. Each block loads and fails on its own (§5.13).
/// </summary>
public class RiskAssetClassTabViewModel : ViewModelBase
{
    /// <summary>Width of the visibility bar, in device-independent pixels.</summary>
    public const double VisibilityWidth = 420;

    #region LANGUAGE

    public string StrMonthlyTitle { get; } = Localizer["RiskMonthlyTitle"];
    public string StrMonthlyHelp { get; } = Localizer["RiskMonthlyHelp"];
    public string StrFactorsTitle { get; } = Localizer["RiskFactorsTitle"];
    public string StrFactorsHelp { get; } = Localizer["RiskFactorsHelp"];
    public string StrFactorsVolumeNote { get; } = Localizer["RiskFactorsVolumeNote"];
    public string StrFactorsNone { get; } = Localizer["RiskFactorsNone"];
    public string StrFactorsPoints { get; } = Localizer["RiskFactorsPoints"];
    public string StrVisibilityTitle { get; } = Localizer["RiskVisibilityTitle"];
    public string StrVisibilityHelp { get; } = Localizer["RiskVisibilityHelp"];
    public string StrDistributionTitle { get; } = Localizer["RiskVisibilityDistribution"];
    public string StrSourcesTitle { get; } = Localizer["RiskVisibilitySourcesTitle"];
    public string StrOverallTitle { get; } = Localizer["RiskVisibilityOverall"];
    public string StrNoSources { get; } = Localizer["RiskVisibilityNoSources"];
    public string StrMonthlyEmpty { get; } = Localizer["RiskMonthlyEmpty"];
    public string StrColMonth { get; } = Localizer["RiskMonthlyColMonth"];
    public string StrColNote { get; } = Localizer["RiskMonthlyColNote"];
    public string StrRetry { get; } = Localizer["RiskOverviewRetry"];
    public string StrMonths { get; } = Localizer["RiskMonthlyMonths"];
    private string StrViewAsTable { get; } = Localizer["RiskOverviewViewAsTable"];
    private string StrViewAsChart { get; } = Localizer["RiskOverviewViewAsChart"];
    private string StrLoadError { get; } = Localizer["RiskOverviewLoadError"];

    public string StrToggleView => IsMonthlyTable ? StrViewAsChart : StrViewAsTable;

    #endregion

    private static TextLookup Text => RiskHeadlineViewModel.Text;

    private IRiskIndexService RiskIndexService { get; } = GetService<IRiskIndexService>();

    private string? _node;
    private DateOnly? _date;
    private IReadOnlyList<RiskIndexBandDto> _bands = [];
    private RiskIndexMonthlySummaryDto? _summary;
    private bool _contentLoaded;
    private CancellationToken _token;
    private SemaphoreSlim _gate = new(1, 1);

    public RiskAssetClassTabViewModel(RiskIndexTabGroup tab, string? disabledReason,
        Action<RiskIndexTabGroup, string, string?>? openFactorObjects = null)
    {
        BtFactorObjectsClicked = ReactiveCommand.Create<string>(filter =>
            openFactorObjects?.Invoke(tab, filter, Factors.FirstOrDefault(f => f.ObjectFilter == filter)?.Label));
        Tab = tab;
        Icon = AssetTabFormatter.Icon(tab);
        Name = Localizer[AssetTabFormatter.NameKey(tab)];
        CategoriesHint = AssetTabFormatter.CategoriesHint(tab, Text, CultureInfo.CurrentCulture);
        DisabledReason = disabledReason;
        HeaderTip = disabledReason ?? CategoriesHint;
        MonthsOptions = MonthlySummaryFormatter.MonthOptions
            .Select(m => new MonthsOption(m, string.Format(CultureInfo.CurrentCulture, Localizer["RiskMonthlyMonthsFMT"], m))).ToList();
        _selectedMonths = MonthsOptions[0];

        BtToggleMonthlyTableClicked = ReactiveCommand.Create(() => { IsMonthlyTable = !IsMonthlyTable; });
        BtRetryClicked = ReactiveCommand.CreateFromTask(() => LoadContentAsync(force: true));
        ChartPalette.Current.Changed += OnThemeChanged;
    }

    #region PROPERTIES

    public RiskIndexTabGroup Tab { get; }
    public MaterialIconKind Icon { get; }
    public string Name { get; }
    public string CategoriesHint { get; }
    public string? DisabledReason { get; }
    public bool IsEnabled => DisabledReason is null;
    public string HeaderTip { get; }

    private string _levelText = "";
    public string LevelText { get => _levelText; private set => this.RaiseAndSetIfChanged(ref _levelText, value); }

    private MaterialIconKind _levelGlyph = MaterialIconKind.HelpCircleOutline;
    public MaterialIconKind LevelGlyph { get => _levelGlyph; private set => this.RaiseAndSetIfChanged(ref _levelGlyph, value); }

    private string? _levelClass;
    public string? LevelClass
    {
        get => _levelClass;
        private set
        {
            this.RaiseAndSetIfChanged(ref _levelClass, value);
            this.RaisePropertyChanged(nameof(IsLevelStep1));
            this.RaisePropertyChanged(nameof(IsLevelStep2));
            this.RaisePropertyChanged(nameof(IsLevelStep3));
            this.RaisePropertyChanged(nameof(IsLevelStep4));
            this.RaisePropertyChanged(nameof(IsLevelStep5));
            this.RaisePropertyChanged(nameof(IsLevelUnclassified));
        }
    }

    public bool IsLevelStep1 => LevelClass == "b1";
    public bool IsLevelStep2 => LevelClass == "b2";
    public bool IsLevelStep3 => LevelClass == "b3";
    public bool IsLevelStep4 => LevelClass == "b4";
    public bool IsLevelStep5 => LevelClass == "b5";
    public bool IsLevelUnclassified => LevelClass is null;

    private bool _hasLevel;
    public bool HasLevel { get => _hasLevel; private set => this.RaiseAndSetIfChanged(ref _hasLevel, value); }

    private string? _unavailableText;
    /// <summary>The state card of an unavailable tab (§5.5): why, and which task enables it. The tab stays selectable.</summary>
    public string? UnavailableText
    {
        get => _unavailableText;
        private set
        {
            this.RaiseAndSetIfChanged(ref _unavailableText, value);
            this.RaisePropertyChanged(nameof(IsUnavailable));
            this.RaisePropertyChanged(nameof(HasContent));
        }
    }

    public bool IsUnavailable => !string.IsNullOrEmpty(UnavailableText);
    public bool HasContent => !IsUnavailable;

    // --- monthly summary ---

    public IReadOnlyList<MonthsOption> MonthsOptions { get; }

    private MonthsOption _selectedMonths;
    public MonthsOption SelectedMonths
    {
        get => _selectedMonths;
        set
        {
            if (value is null || value == _selectedMonths) return;
            this.RaiseAndSetIfChanged(ref _selectedMonths, value);
            _ = LoadMonthlyAsync();
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
        }
    }

    public bool IsMonthlyChart => !IsMonthlyTable && HasMonthly;

    private bool _hasMonthly;
    public bool HasMonthly
    {
        get => _hasMonthly;
        private set
        {
            this.RaiseAndSetIfChanged(ref _hasMonthly, value);
            this.RaisePropertyChanged(nameof(IsMonthlyChart));
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
    // The category codes are S39's identifiers, shown untranslated everywhere in the module (HeadlineFormatter.CategoryCode).
    public string StrColReg { get; } = HeadlineFormatter.CategoryCode(RiskIndexCategory.Reg);
    public string StrColExp { get; } = HeadlineFormatter.CategoryCode(RiskIndexCategory.Exp);
    public string StrColAme { get; } = HeadlineFormatter.CategoryCode(RiskIndexCategory.Ame);
    public string StrColCtl { get; } = HeadlineFormatter.CategoryCode(RiskIndexCategory.Ctl);
    public string StrColGov { get; } = HeadlineFormatter.CategoryCode(RiskIndexCategory.Gov);
    public string StrColTer { get; } = HeadlineFormatter.CategoryCode(RiskIndexCategory.Ter);

    // --- risk factors ---

    public ObservableCollection<RiskFactorLine> Factors { get; } = [];

    private bool _isFactorsBusy;
    public bool IsFactorsBusy { get => _isFactorsBusy; private set => this.RaiseAndSetIfChanged(ref _isFactorsBusy, value); }

    private string? _factorsError;
    public string? FactorsError
    {
        get => _factorsError;
        private set
        {
            this.RaiseAndSetIfChanged(ref _factorsError, value);
            this.RaisePropertyChanged(nameof(HasFactorsError));
        }
    }

    public bool HasFactorsError => !string.IsNullOrEmpty(FactorsError);

    private bool _hasNoFactors;
    public bool HasNoFactors { get => _hasNoFactors; private set => this.RaiseAndSetIfChanged(ref _hasNoFactors, value); }

    // --- attack-surface visibility: one row per source (T256), then the general bar ---

    private string _inventoryText = "";
    public string InventoryText { get => _inventoryText; private set => this.RaiseAndSetIfChanged(ref _inventoryText, value); }

    public ObservableCollection<SourceVisibilityRowView> SourceRows { get; } = [];

    private bool _hasNoSources;
    /// <summary>The tab reads no source at all (§5.5.1): said, rather than an empty block.</summary>
    public bool HasNoSources { get => _hasNoSources; private set => this.RaiseAndSetIfChanged(ref _hasNoSources, value); }

    public ObservableCollection<VisibilitySegmentView> Segments { get; } = [];
    public ObservableCollection<DistributionItem> Distribution { get; } = [];

    private bool _isVisibilityBusy;
    public bool IsVisibilityBusy { get => _isVisibilityBusy; private set => this.RaiseAndSetIfChanged(ref _isVisibilityBusy, value); }

    private string? _visibilityError;
    public string? VisibilityError
    {
        get => _visibilityError;
        private set
        {
            this.RaiseAndSetIfChanged(ref _visibilityError, value);
            this.RaisePropertyChanged(nameof(HasVisibilityError));
        }
    }

    public bool HasVisibilityError => !string.IsNullOrEmpty(VisibilityError);

    private bool _hasVisibility;
    public bool HasVisibility { get => _hasVisibility; private set => this.RaiseAndSetIfChanged(ref _hasVisibility, value); }

    #endregion

    #region COMMANDS

    public ReactiveCommand<RxVoid, RxVoid> BtToggleMonthlyTableClicked { get; }
    public ReactiveCommand<RxVoid, RxVoid> BtRetryClicked { get; }

    /// <summary>A factor line's "›" (§5.5.1): the object list of node ∩ this tab, filtered by the factor (T254).</summary>
    public ReactiveCommand<string, RxVoid> BtFactorObjectsClicked { get; }

    #endregion

    #region LOADING

    /// <summary>
    /// The node, day and bands this tab reads; the token that drops its requests when the module moves on; and the gate
    /// every tab of the strip shares, so their reads — each possibly a derived computation — run one at a time (§8.1).
    /// </summary>
    public void SetContext(string node, DateOnly date, IReadOnlyList<RiskIndexBandDto> bands, CancellationToken token,
        SemaphoreSlim gate)
    {
        _node = node;
        _date = date;
        _bands = bands;
        _token = token;
        _gate = gate;
        _contentLoaded = false;
    }

    private async Task<T> OneAtATime<T>(Func<CancellationToken, Task<T>> read)
    {
        var token = _token;
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

    private string IntersectionKey => AssetTabFormatter.IntersectionKey(_node, Tab);

    /// <summary>The header's level: the overview of node ∩ class (§5.5). A tab with nothing in it is "Indisponível".</summary>
    public async Task LoadLevelAsync()
    {
        if (_node is null || !IsEnabled) return;
        try
        {
            var overview = await OneAtATime(t => RiskIndexService.GetOverviewAsync(IntersectionKey, _date, t));
            if (_token.IsCancellationRequested) return;
            ApplyLevel(AssetTabFormatter.Level(overview, Text, CultureInfo.CurrentUICulture));
        }
        catch (Exception ex) when (!_token.IsCancellationRequested)
        {
            Logger.Warning("Could not read the level of the {Tab} tab: {Message}", Tab, ex.Message);
            ApplyLevel(new AssetTabLevel("—", MaterialIconKind.HelpCircleOutline, null, false));
        }
        catch (Exception) when (_token.IsCancellationRequested)
        {
            // Superseded.
        }
    }

    private void ApplyLevel(AssetTabLevel level)
    {
        LevelText = level.Text;
        LevelGlyph = level.Glyph;
        LevelClass = level.BandClass;
        HasLevel = true;
        if (level.IsUnavailable)
            UnavailableText = AssetTabFormatter.KnownUnavailableKey(Tab) is { } key
                ? Localizer[key]
                : Localizer["RiskTab_NoObjects"];
    }

    /// <summary>The content, block after block: the derived budget allows two computations per user (§8.1).</summary>
    public async Task LoadContentAsync(bool force = false)
    {
        if (_node is null || !IsEnabled || (_contentLoaded && !force)) return;
        _contentLoaded = true;
        await LoadMonthlyAsync();
        await LoadFactorsAsync();
        await LoadVisibilityAsync();
    }

    private async Task LoadMonthlyAsync()
    {
        if (_node is null) return;
        var token = _token;
        IsMonthlyBusy = true;
        MonthlyError = null;
        try
        {
            var summary = await OneAtATime(t => RiskIndexService.GetMonthlySummaryAsync(_node, Tab, SelectedMonths.Months, _date, t));
            if (token.IsCancellationRequested) return;
            _summary = summary;
            DrawMonthly(summary);
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            Logger.Error(ex, "Error loading the monthly summary of the {Tab} tab", Tab);
            _summary = null;
            HasMonthly = false;
            IsMonthlyEmpty = false;
            MonthlyError = Explain(ex);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Superseded.
        }
        finally
        {
            IsMonthlyBusy = false;
        }
    }

    private async Task LoadFactorsAsync()
    {
        if (_node is null) return;
        var token = _token;
        IsFactorsBusy = true;
        FactorsError = null;
        try
        {
            var lines = await OneAtATime(t => RiskIndexService.GetRiskFactorsAsync(_node, Tab, _date, t));
            if (token.IsCancellationRequested) return;
            var culture = CultureInfo.CurrentCulture;
            Factors.Clear();
            foreach (var line in lines)
                Factors.Add(new RiskFactorLine(line.Count.ToString("N0", culture), Text(line.LabelKey) ?? line.FactorKey,
                    line.Points.ToString(culture), line.ObjectFilter, BtFactorObjectsClicked));
            HasNoFactors = Factors.Count == 0;
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            Logger.Error(ex, "Error loading the risk factors of the {Tab} tab", Tab);
            Factors.Clear();
            HasNoFactors = false;
            FactorsError = Explain(ex);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Superseded.
        }
        finally
        {
            IsFactorsBusy = false;
        }
    }

    private async Task LoadVisibilityAsync()
    {
        if (_node is null) return;
        var token = _token;
        IsVisibilityBusy = true;
        VisibilityError = null;
        try
        {
            var inventory = await OneAtATime(t => RiskIndexService.GetInventoryAsync(_node, Tab, _date, t));
            if (token.IsCancellationRequested) return;
            var culture = CultureInfo.CurrentCulture;
            InventoryText = AssetTabFormatter.InventoryText(inventory, Text, culture);
            SourceRows.Clear();
            foreach (var row in AssetTabFormatter.Sources(inventory, Text, culture))
                SourceRows.Add(new SourceVisibilityRowView(row, VisibilitySegmentView.Of(row.Segments, VisibilityWidth)));
            HasNoSources = SourceRows.Count == 0;
            Segments.Clear();
            foreach (var segment in VisibilitySegmentView.Of(AssetTabFormatter.Overall(inventory.Overall, Text, culture), VisibilityWidth))
                Segments.Add(segment);
            Distribution.Clear();
            foreach (var (text, glyph, bandClass) in AssetTabFormatter.Distribution(inventory, _bands, culture, CultureInfo.CurrentUICulture))
                Distribution.Add(new DistributionItem(text, glyph, bandClass));
            HasVisibility = true;
        }
        catch (Exception ex) when (!token.IsCancellationRequested)
        {
            Logger.Error(ex, "Error loading the visibility of the {Tab} tab", Tab);
            HasVisibility = false;
            HasNoSources = false;
            VisibilityError = Explain(ex);
        }
        catch (Exception) when (token.IsCancellationRequested)
        {
            // Superseded.
        }
        finally
        {
            IsVisibilityBusy = false;
        }
    }

    private string Explain(Exception ex) => ex is InvalidHttpRequestException { ErrorCode: { Length: > 0 } code } refusal
        ? RiskApiErrorText.Translate(code, refusal.Message, Text, StrLoadError)
        : StrLoadError;

    #endregion

    #region DRAWING

    private void OnThemeChanged(object? sender, EventArgs e)
    {
        if (_summary is { } summary) DrawMonthly(summary);
    }

    /// <summary>
    /// Grouped columns in the fixed order (§10.4): one <c>ColumnSeries&lt;double?&gt;</c> per category, each bar labelled
    /// "EXP 74" so the series reads without colour; null is no bar here and "—" in the table.
    /// </summary>
    private void DrawMonthly(RiskIndexMonthlySummaryDto summary)
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

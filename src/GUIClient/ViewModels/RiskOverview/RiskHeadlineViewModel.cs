using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Threading.Tasks;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using Material.Icons;
using Model.RiskIndex;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>
/// The headline card (S40 §5.2–§5.2.3, §10.8; T251): the integer value, the band as text and glyph, the 7-day line, quality
/// and coverage, the interval, the headline's branch, the unfloored value (full mode), seals and chips. It formats what the
/// overview carries through <see cref="HeadlineFormatter"/>; the module view-model loads it and owns busy and retry.
/// </summary>
public class RiskHeadlineViewModel : ViewModelBase
{
    #region LANGUAGE

    public string StrHeadlineTitle { get; } = Localizer["RiskOverviewHeadlineTitle"];
    public string StrHeadlineHint { get; } = Localizer["RiskOverviewHeadlineHint"];
    public string StrScale { get; } = Localizer["RiskOverviewHeadlineScale"];
    public string StrQuality { get; } = Localizer["RiskOverviewQualityLabel"];
    public string StrSeals { get; } = Localizer["RiskOverviewSeals"];
    public string StrChips { get; } = Localizer["RiskOverviewChips"];
    public string StrNoSeals { get; } = Localizer["RiskOverviewNoSeals"];
    public string StrNoChips { get; } = Localizer["RiskOverviewNoChips"];
    public string StrNoData { get; } = Localizer["RiskState_NoData"];
    public string StrRetry { get; } = Localizer["RiskOverviewRetry"];
    public string StrPartialBanner { get; } = Localizer["RiskState_PartialBanner"];

    #endregion

    /// <summary>Resolves a resource key to its text, or null when no resource declares it (the formatter then falls back).</summary>
    internal static string? Text(string key)
    {
        var localized = Localizer[key];
        return localized.ResourceNotFound ? null : localized.Value;
    }

    public RiskHeadlineViewModel(Action retry, Func<HeadlineLink, Task>? openLink = null)
    {
        BtRetryClicked = ReactiveCommand.Create(retry);
        OpenLink = ReactiveCommand.CreateFromTask<HeadlineLink>(link =>
            link is { CanOpen: true } && openLink is not null ? openLink(link) : Task.CompletedTask);
    }

    public ReactiveCommand<RxVoid, RxVoid> BtRetryClicked { get; }

    /// <summary>A seal's or chip's "›" (§5.2.2): the object list filtered by its <c>ObjectFilter</c> (T255).</summary>
    public ReactiveCommand<HeadlineLink, RxVoid> OpenLink { get; }

    #region PROPERTIES

    private bool _hasContent;
    /// <summary>A node with a headline was read (false on "Meu escopo", before the first load and in the §5.13 states).</summary>
    public bool HasContent { get => _hasContent; private set => this.RaiseAndSetIfChanged(ref _hasContent, value); }

    private bool _showsValue;
    /// <summary>A point value (with or without interval): the big number and its band.</summary>
    public bool ShowsValue { get => _showsValue; private set => this.RaiseAndSetIfChanged(ref _showsValue, value); }

    private bool _isIndicative;
    public bool IsIndicative { get => _isIndicative; private set => this.RaiseAndSetIfChanged(ref _isIndicative, value); }

    private bool _isNoData;
    public bool IsNoData { get => _isNoData; private set => this.RaiseAndSetIfChanged(ref _isNoData, value); }

    private string _valueText = string.Empty;
    public string ValueText { get => _valueText; private set => this.RaiseAndSetIfChanged(ref _valueText, value); }

    private bool _showsBand;
    public bool ShowsBand { get => _showsBand; private set => this.RaiseAndSetIfChanged(ref _showsBand, value); }

    private string _bandLabel = string.Empty;
    public string BandLabel { get => _bandLabel; private set => this.RaiseAndSetIfChanged(ref _bandLabel, value); }

    private MaterialIconKind _bandGlyph = MaterialIconKind.HelpCircleOutline;
    public MaterialIconKind BandGlyph { get => _bandGlyph; private set => this.RaiseAndSetIfChanged(ref _bandGlyph, value); }

    private string? _bandClass;
    /// <summary>The pill's class, <c>b{step}</c> from <see cref="ChartPaletteMap.BandStep"/>; the view binds one flag per step.</summary>
    public string? BandClass
    {
        get => _bandClass;
        private set
        {
            this.RaiseAndSetIfChanged(ref _bandClass, value);
            this.RaisePropertyChanged(nameof(IsBandStep1));
            this.RaisePropertyChanged(nameof(IsBandStep2));
            this.RaisePropertyChanged(nameof(IsBandStep3));
            this.RaisePropertyChanged(nameof(IsBandStep4));
            this.RaisePropertyChanged(nameof(IsBandStep5));
        }
    }

    public bool IsBandStep1 => BandClass == "b1";
    public bool IsBandStep2 => BandClass == "b2";
    public bool IsBandStep3 => BandClass == "b3";
    public bool IsBandStep4 => BandClass == "b4";
    public bool IsBandStep5 => BandClass == "b5";

    private MaterialIconKind _stateGlyph = MaterialIconKind.HelpCircleOutline;
    /// <summary>The glyph of an indicative or empty card (§10.7).</summary>
    public MaterialIconKind StateGlyph { get => _stateGlyph; private set => this.RaiseAndSetIfChanged(ref _stateGlyph, value); }

    private string? _indicativeText;
    public string? IndicativeText { get => _indicativeText; private set => this.RaiseAndSetIfChanged(ref _indicativeText, value); }

    private string? _indicativeReason;
    public string? IndicativeReason { get => _indicativeReason; private set => this.RaiseAndSetIfChanged(ref _indicativeReason, value); }

    private bool _hasDelta;
    public bool HasDelta { get => _hasDelta; private set => this.RaiseAndSetIfChanged(ref _hasDelta, value); }

    private string _deltaText = string.Empty;
    public string DeltaText { get => _deltaText; private set => this.RaiseAndSetIfChanged(ref _deltaText, value); }

    private bool _hasDeltaGlyph;
    public bool HasDeltaGlyph { get => _hasDeltaGlyph; private set => this.RaiseAndSetIfChanged(ref _hasDeltaGlyph, value); }

    private MaterialIconKind _deltaGlyph = MaterialIconKind.ArrowRightThin;
    public MaterialIconKind DeltaGlyph { get => _deltaGlyph; private set => this.RaiseAndSetIfChanged(ref _deltaGlyph, value); }

    private string _deltaClass = "neutral";
    public string DeltaClass
    {
        get => _deltaClass;
        private set
        {
            this.RaiseAndSetIfChanged(ref _deltaClass, value);
            this.RaisePropertyChanged(nameof(IsDeltaWorse));
            this.RaisePropertyChanged(nameof(IsDeltaBetter));
            this.RaisePropertyChanged(nameof(IsDeltaNeutral));
        }
    }

    public bool IsDeltaWorse => DeltaClass == "worse";
    public bool IsDeltaBetter => DeltaClass == "better";
    public bool IsDeltaNeutral => DeltaClass == "neutral";

    private string _qualityBandText = string.Empty;
    public string QualityBandText { get => _qualityBandText; private set => this.RaiseAndSetIfChanged(ref _qualityBandText, value); }

    private string _qualityValueText = string.Empty;
    public string QualityValueText { get => _qualityValueText; private set => this.RaiseAndSetIfChanged(ref _qualityValueText, value); }

    private string _coverageText = string.Empty;
    public string CoverageText { get => _coverageText; private set => this.RaiseAndSetIfChanged(ref _coverageText, value); }

    private string _freshnessText = string.Empty;
    public string FreshnessText { get => _freshnessText; private set => this.RaiseAndSetIfChanged(ref _freshnessText, value); }

    private bool _hasInterval;
    public bool HasInterval { get => _hasInterval; private set => this.RaiseAndSetIfChanged(ref _hasInterval, value); }

    private string _intervalText = string.Empty;
    public string IntervalText { get => _intervalText; private set => this.RaiseAndSetIfChanged(ref _intervalText, value); }

    private string? _intervalHint;
    public string? IntervalHint { get => _intervalHint; private set => this.RaiseAndSetIfChanged(ref _intervalHint, value); }

    private string? _branchText;
    public string? BranchText { get => _branchText; private set => this.RaiseAndSetIfChanged(ref _branchText, value); }

    private string? _unflooredText;
    public string? UnflooredText { get => _unflooredText; private set => this.RaiseAndSetIfChanged(ref _unflooredText, value); }

    public ObservableCollection<HeadlineLink> Seals { get; } = [];

    public ObservableCollection<HeadlineLink> Chips { get; } = [];

    private bool _hasSeals;
    public bool HasSeals { get => _hasSeals; private set => this.RaiseAndSetIfChanged(ref _hasSeals, value); }

    private bool _hasChips;
    public bool HasChips { get => _hasChips; private set => this.RaiseAndSetIfChanged(ref _hasChips, value); }

    private bool _isPartial;
    /// <summary>The partial banner above the card (§5.2.3, §5.6.5).</summary>
    public bool IsPartial { get => _isPartial; private set => this.RaiseAndSetIfChanged(ref _isPartial, value); }

    private string? _partialHint;
    public string? PartialHint { get => _partialHint; private set => this.RaiseAndSetIfChanged(ref _partialHint, value); }

    private string _accessibleName = string.Empty;
    /// <summary>The card's <c>AutomationProperties.Name</c> (§10.8).</summary>
    public string AccessibleName { get => _accessibleName; private set => this.RaiseAndSetIfChanged(ref _accessibleName, value); }

    private string? _errorMessage;
    /// <summary>The panel's error (§5.13): shown with "Tentar novamente", the previous values kept.</summary>
    public string? ErrorMessage
    {
        get => _errorMessage;
        set
        {
            this.RaiseAndSetIfChanged(ref _errorMessage, value);
            this.RaisePropertyChanged(nameof(HasError));
        }
    }

    public bool HasError => !string.IsNullOrEmpty(ErrorMessage);

    #endregion

    /// <summary>Clears the card: "Meu escopo" has no headline and the §5.13 states show no value.</summary>
    public void Clear()
    {
        HasContent = false;
        ShowsValue = IsIndicative = IsNoData = ShowsBand = HasDelta = HasInterval = IsPartial = false;
        Seals.Clear();
        Chips.Clear();
        HasSeals = HasChips = false;
        ErrorMessage = null;
    }

    /// <summary>
    /// Shows one overview's headline. <paramref name="arrowThreshold"/> and <paramref name="coverageIndicative"/> come from the
    /// snapshot's profile when it could be read; without them the texts say "below the threshold" instead of naming it.
    /// </summary>
    public void Apply(RiskIndexOverviewDto overview, int? arrowThreshold, double? coverageIndicative)
    {
        var culture = CultureInfo.CurrentCulture;
        var ui = CultureInfo.CurrentUICulture;
        ErrorMessage = null;

        if (overview.Headline is not { } headline)
        {
            Clear();
            return;
        }

        HasContent = true;
        IsNoData = headline.DisplayState == RiskIndexDisplayState.NoData;
        IsIndicative = headline.DisplayState == RiskIndexDisplayState.Indicative;
        ShowsValue = !IsNoData && !IsIndicative && headline.Displayed is not null;
        ValueText = headline.Displayed?.ToString(culture) ?? string.Empty;

        ShowsBand = HeadlineFormatter.ShowsBand(headline, overview.Bands);
        BandClass = HeadlineFormatter.BandClass(headline, overview.Bands);
        BandLabel = ShowsBand ? HeadlineFormatter.BandLabel(HeadlineFormatter.BandAt(overview.Bands, headline.BandIndex)!, ui) : "";
        BandGlyph = BandGlyphFor(headline, overview.Bands.Count) ?? MaterialIconKind.HelpCircleOutline;
        StateGlyph = IsNoData ? Tools.RiskIndex.BandGlyph.NoData : Tools.RiskIndex.BandGlyph.Indicative;

        var indicative = HeadlineFormatter.Indicative(headline, overview.Quality.Coverage, coverageIndicative, Text, culture);
        IndicativeText = indicative?.Text;
        IndicativeReason = indicative?.Reason;

        var delta = HeadlineFormatter.Delta(headline, overview.Provenance, arrowThreshold, Text, culture);
        HasDelta = delta is not null;
        DeltaText = delta?.Text ?? string.Empty;
        HasDeltaGlyph = delta?.Glyph is not null;
        DeltaGlyph = delta?.Glyph ?? MaterialIconKind.ArrowRightThin;
        DeltaClass = delta?.CssClass ?? "neutral";

        var quality = HeadlineFormatter.Quality(overview.Quality, Text, culture);
        QualityBandText = quality.BandText;
        QualityValueText = quality.ValueText;
        CoverageText = quality.CoverageText;
        FreshnessText = quality.FreshnessText;

        var interval = HeadlineFormatter.Interval(headline, Text, culture);
        HasInterval = interval is not null;
        IntervalText = interval?.Text ?? string.Empty;
        IntervalHint = interval?.Hint;

        BranchText = HeadlineFormatter.Branch(headline, Text, culture);
        UnflooredText = HeadlineFormatter.Unfloored(headline, overview.Mode, overview.Bands, Text, culture, ui);

        Replace(Seals, HeadlineLinks.Seals(overview.Seals, Text, culture));
        Replace(Chips, HeadlineLinks.Chips(overview.Chips, overview.Categories, Text, culture));
        HasSeals = Seals.Count > 0;
        HasChips = Chips.Count > 0;

        IsPartial = overview.Provenance == RiskIndexProvenance.PartialRestricted;
        PartialHint = IsPartial ? Text(RiskOverviewTopBar.PartialCauseKey(overview.Chips)) : null;

        AccessibleName = HeadlineFormatter.AccessibleName(headline, overview.Bands, overview.Quality.Coverage,
            overview.Provenance, Text, culture, ui);
    }

    private static MaterialIconKind? BandGlyphFor(RiskIndexHeadlineDto headline, int bandCount) =>
        Tools.RiskIndex.BandGlyph.ForHeadline(headline.DisplayState, headline.BandIndex, bandCount);

    private static void Replace<T>(ObservableCollection<T> target, IEnumerable<T> items)
    {
        target.Clear();
        foreach (var item in items) target.Add(item);
    }
}

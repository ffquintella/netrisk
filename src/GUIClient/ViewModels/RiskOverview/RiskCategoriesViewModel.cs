using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using Material.Icons;
using Model.RiskIndex;
using ReactiveUI;
using RxVoid = ReactiveUI.Primitives.RxVoid;

namespace GUIClient.ViewModels.RiskOverview;

/// <summary>One line of the contributing categories, with the meter's fill width and the band pill's step flags.</summary>
public sealed class RiskCategoryRowViewModel(CategoryRow row, double meterWidth, ReactiveCommand<RiskIndexCategory, RxVoid> open)
{
    public RiskIndexCategory Category => row.Category;
    public string Code => row.Code;
    public string Name => row.Name;
    public string ValueText => row.ValueText;
    public bool IsAvailable => row.IsAvailable;
    public bool IsDimmed => row.IsNotApplicable;
    public double MeterWidth { get; } = meterWidth;
    public double MeterFill { get; } = MeterGeometry.Fill(row.Fraction, meterWidth);
    public bool HasBand => row.BandLabel is not null;
    public string BandLabel => row.BandLabel ?? "";
    public MaterialIconKind BandGlyph => row.BandGlyph ?? MaterialIconKind.HelpCircleOutline;
    public bool IsBandStep1 => row.BandClass == "b1";
    public bool IsBandStep2 => row.BandClass == "b2";
    public bool IsBandStep3 => row.BandClass == "b3";
    public bool IsBandStep4 => row.BandClass == "b4";
    public bool IsBandStep5 => row.BandClass == "b5";
    public string WeightText => row.WeightText;
    public string PointsText => row.PointsText;
    public string HeldByText => row.HeldByText;
    public string? HeldByHint => row.HeldByHint;
    public string CoverageText => row.CoverageText;
    public string? CoverageHint => row.CoverageHint;
    public bool HasState => row.StateText is not null;
    public string StateText => row.StateText ?? "";
    public MaterialIconKind StateGlyph => row.StateGlyph ?? MaterialIconKind.CloudOffOutline;
    public IReadOnlyList<string> Seals => row.Seals;
    public bool HasSeals => row.Seals.Count > 0;

    /// <summary>"›" (§5.4): the same node, the trend in this category's series.</summary>
    public ReactiveCommand<RiskIndexCategory, RxVoid> Open { get; } = open;

    /// <summary>A category without a value has no series to open.</summary>
    public bool CanOpen => row.IsAvailable;
}

/// <summary>
/// "Categorias contribuintes" (S40 §5.4; T253): REG, EXP, AME, CTL, GOV (TER while weighted) with value, band, weight,
/// Euler points, the object holding the floor and coverage — from the overview the module already read, so the panel adds
/// no request. The meter is a <c>Border.riskMeter</c> with a <c>Border.riskMeterFill</c> (§10.4: not LiveCharts).
/// </summary>
public class RiskCategoriesViewModel : ViewModelBase
{
    /// <summary>Width of the 0–100 meter, in device-independent pixels.</summary>
    public const double MeterWidth = 140;

    #region LANGUAGE

    public string StrTitle { get; } = Localizer["RiskCategoriesTitle"];
    public string StrHelp { get; } = Localizer["RiskCategoriesHelp"];
    public string StrColCategory { get; } = Localizer["RiskCategoriesColCategory"];
    public string StrColValue { get; } = Localizer["RiskCategoriesColValue"];
    public string StrColWeight { get; } = Localizer["RiskCategoriesColWeight"];
    public string StrColPoints { get; } = Localizer["RiskCategoriesColPoints"];
    public string StrColHeldBy { get; } = Localizer["RiskCategoriesColHeldBy"];
    public string StrColCoverage { get; } = Localizer["RiskCategoriesColCoverage"];
    public string StrRoundingNote { get; } = Localizer["RiskCategoriesRoundingNote"];
    public string StrTerNote { get; } = Localizer["RiskCategoriesTerNote"];
    public string StrOpenHint { get; } = Localizer["RiskCategoriesOpenHint"];

    #endregion

    public RiskCategoriesViewModel(Action<RiskIndexCategory> showInTrend)
    {
        BtCategoryDetailClicked = ReactiveCommand.Create<RiskIndexCategory>(showInTrend);
    }

    public ReactiveCommand<RiskIndexCategory, RxVoid> BtCategoryDetailClicked { get; }

    public ObservableCollection<RiskCategoryRowViewModel> Rows { get; } = [];

    private bool _hasRows;
    public bool HasRows { get => _hasRows; private set => this.RaiseAndSetIfChanged(ref _hasRows, value); }

    private bool _showsTerNote;
    public bool ShowsTerNote { get => _showsTerNote; private set => this.RaiseAndSetIfChanged(ref _showsTerNote, value); }

    public void Apply(RiskIndexOverviewDto overview)
    {
        Rows.Clear();
        foreach (var row in CategoryRowFormatter.Rows(overview.Categories, overview.Bands, RiskHeadlineViewModel.Text,
                     CultureInfo.CurrentCulture, CultureInfo.CurrentUICulture))
            Rows.Add(new RiskCategoryRowViewModel(row, MeterWidth, BtCategoryDetailClicked));
        HasRows = Rows.Count > 0;
        ShowsTerNote = HasRows && CategoryRowFormatter.ShowsTerNote(overview.Categories);
    }

    public void Clear()
    {
        Rows.Clear();
        HasRows = false;
        ShowsTerNote = false;
    }
}

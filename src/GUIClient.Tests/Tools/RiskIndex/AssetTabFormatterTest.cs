using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Material.Icons;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// T253 (S40 §5.5–§5.5.1, §10.7): the asset-class tabs — order and icons, which are enabled for a node already cut by a
/// class, the node ∩ tab key, the header level (band, indicative or unavailable), and the attack-surface summary with a
/// glyph and count for every segment; T256's visibility per source. Also that every resource key the panels compute exists
/// in all three files.
/// </summary>
[TestSubject(typeof(AssetTabFormatter))]
public class AssetTabFormatterTest
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    private static readonly IReadOnlyList<RiskIndexBandDto> Bands =
    [
        new("b0", "Baixo", "Low", 0, 1, 0), new("b1", "Médio", "Medium", 1, 3, 31), new("b2", "Alto", "High", 2, 5, 70)
    ];

    [Fact]
    public void TestTheFiveTabsInOrderWithTheirCategories()
    {
        Assert.Equal(new[] { RiskIndexTabGroup.Devices, RiskIndexTabGroup.InternetFacing, RiskIndexTabGroup.Identities,
            RiskIndexTabGroup.Applications, RiskIndexTabGroup.Clouds }, AssetTabFormatter.Tabs);
        Assert.Equal("Categorias: EXP, CTL, GOV", AssetTabFormatter.CategoriesHint(RiskIndexTabGroup.Devices, RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal("Categorias: EXP", AssetTabFormatter.CategoriesHint(RiskIndexTabGroup.Clouds, RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal(AssetTabFormatter.Tabs.Count, AssetTabFormatter.Tabs.Select(AssetTabFormatter.Icon).Distinct().Count());
    }

    [Theory]
    [InlineData(null, RiskIndexTabGroup.Clouds, "c:clouds")]
    [InlineData("org", RiskIndexTabGroup.Devices, "c:devices")]
    [InlineData("e:7", RiskIndexTabGroup.Devices, "e:7;c:devices")]
    [InlineData("e:7;k:5", RiskIndexTabGroup.Applications, "e:7;c:applications;k:5")]
    [InlineData("e:7;c:server", RiskIndexTabGroup.Devices, "e:7;c:server")]
    public void TestTheTabReadsTheNodeIntersectedWithItsClass(string? node, RiskIndexTabGroup tab, string expected) =>
        Assert.Equal(expected, AssetTabFormatter.IntersectionKey(node, tab));

    /// <summary>§5.5: a node already cut by a class enables only that class's tab, and says why for the others.</summary>
    [Fact]
    public void TestANodeCutByAClassEnablesOnlyItsTab()
    {
        Assert.Null(AssetTabFormatter.DisabledReason("e:7", RiskIndexTabGroup.Clouds, RiskIndexTestText.Portuguese, PtBr));
        Assert.Null(AssetTabFormatter.DisabledReason("e:7;c:server", RiskIndexTabGroup.Devices, RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal("O nó atual já é recortado por server",
            AssetTabFormatter.DisabledReason("e:7;c:server", RiskIndexTabGroup.Clouds, RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal("O nó atual já é recortado por Aplicações",
            AssetTabFormatter.DisabledReason("c:applications", RiskIndexTabGroup.Devices, RiskIndexTestText.Portuguese, PtBr));
    }

    private static RiskIndexOverviewDto Overview(int objects, RiskIndexDisplayState state, int? band) => new(
        new RiskIndexNodeRef("c:devices", "Organization ∩ Devices", "organization∩"), [], new DateOnly(2026, 10, 5),
        DateTime.UtcNow, RiskIndexProvenance.OfficialDerived,
        new RiskIndexProfileSummaryDto(1, 1, "P", null, RiskIndexProfileStatus.Active, null, null, "a", null, false, "CRTI"),
        RiskIndexMode.Interim,
        new RiskIndexHeadlineDto(state == RiskIndexDisplayState.NoData ? null : 64, 63.6, 63.6, band is { } b ? "b" + b : null, band, state,
            null, null, false, null, [], true, null),
        [], [], [], new RiskIndexQualitySummaryDto(0.9, 1, 0.9, "high", []), objects, Bands, []);

    [Fact]
    public void TestTheHeaderLevelIsTheBandIndicativeOrUnavailable()
    {
        var medium = AssetTabFormatter.Level(Overview(10, RiskIndexDisplayState.Point, 1), RiskIndexTestText.Portuguese, PtBr);
        var indicative = AssetTabFormatter.Level(Overview(10, RiskIndexDisplayState.Indicative, null), RiskIndexTestText.Portuguese, PtBr);
        var empty = AssetTabFormatter.Level(Overview(0, RiskIndexDisplayState.Point, 1), RiskIndexTestText.Portuguese, PtBr);
        var none = AssetTabFormatter.Level(null, RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(new AssetTabLevel("Médio", MaterialIconKind.AlertOutline, "b3", false), medium);
        Assert.Equal(new AssetTabLevel("Indicativo", MaterialIconKind.HelpCircleOutline, null, false), indicative);
        Assert.Equal(new AssetTabLevel("Indisponível", MaterialIconKind.CloudOffOutline, null, true), empty);
        Assert.True(none.IsUnavailable);
        Assert.Equal("RiskTab_IdentitiesUnavailable", AssetTabFormatter.KnownUnavailableKey(RiskIndexTabGroup.Identities));
        Assert.Null(AssetTabFormatter.KnownUnavailableKey(RiskIndexTabGroup.Devices));
    }

    [Fact]
    public void TestTheAttackSurfaceSummaryCountsEveryStateWithAGlyph()
    {
        var inventory = new RiskIndexInventoryDto(RiskIndexTabGroup.Devices, 16831, "exp_objects", [],
            new RiskIndexStateCountsDto(9120, 3004, 4707, 0), new Dictionary<string, int> { ["b0"] = 1226, ["b1"] = 7858, ["b2"] = 36 });

        var segments = AssetTabFormatter.Overall(inventory.Overall, RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal("Inventário de EXP: 16.831 objetos", AssetTabFormatter.InventoryText(inventory, RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal(new[] { "Avaliado 9.120", "Obsoleto 3.004", "Presumido 4.707" }, segments.Select(s => s.Label));
        Assert.Equal(new[] { MaterialIconKind.CheckCircle, MaterialIconKind.ClockAlertOutline, MaterialIconKind.HelpCircleOutline },
            segments.Select(s => s.Glyph));
        Assert.Equal(4, AssetTabFormatter.Overall(new RiskIndexStateCountsDto(1, 0, 0, 2), RiskIndexTestText.Portuguese, PtBr).Count);

        var distribution = AssetTabFormatter.Distribution(inventory, Bands, PtBr, PtBr);
        Assert.Equal(new[] { "Baixo 1.226", "Médio 7.858", "Alto 36" }, distribution.Select(d => d.Text));
        Assert.Equal(new[] { "b1", "b3", "b5" }, distribution.Select(d => d.BandClass));
    }

    // ---------- visibility per source (T256) -------------------------------------------------------------------------

    private static RiskIndexInventoryDto Inventory(params RiskIndexSourceVisibilityDto[] sources) =>
        new(RiskIndexTabGroup.Devices, 100, "exp_objects", sources, new RiskIndexStateCountsDto(60, 20, 20, 0),
            new Dictionary<string, int>());

    /// <summary>§5.5.1, §10.7: an enabled source lists its four reading states, zero included, each with its own glyph.</summary>
    [Fact]
    public void TestAnEnabledSourceCountsEveryReadingStateWithItsGlyph()
    {
        var read = new DateTime(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);
        var row = Assert.Single(AssetTabFormatter.Sources(Inventory(
            new RiskIndexSourceVisibilityDto(RiskIndexSources.V1Device, "H", true, 7241, 2931, 0, 6247, read, null)),
            RiskIndexTestText.Portuguese, PtBr));

        Assert.True(row.IsEnabled);
        Assert.Equal("Vision One — dispositivos", row.Label);
        Assert.Equal(["current", "stale", "expired", "noReading"], row.Segments.Select(s => s.Kind));
        Assert.Equal(["Atual 7.241", "Obsoleta 2.931", "Expirada 0", "Sem leitura 6.247"], row.Segments.Select(s => s.Label));
        Assert.Equal([MaterialIconKind.CheckCircle, MaterialIconKind.ClockAlertOutline, MaterialIconKind.TimerSandComplete,
            MaterialIconKind.CircleOffOutline], row.Segments.Select(s => s.Glyph));
        Assert.Equal("última leitura " + read.ToLocalTime().ToString("d", PtBr), row.LastReadingText);
    }

    [Fact]
    public void TestADisabledSourceSaysWhyAndHasNoBar()
    {
        var rows = AssetTabFormatter.Sources(Inventory(
            new RiskIndexSourceVisibilityDto(RiskIndexSources.TenableVm, "N", false, 0, 0, 0, 100, null, "source_not_enabled"),
            new RiskIndexSourceVisibilityDto("XYZ-1", "N", false, 0, 0, 0, 100, null, "reason_the_client_does_not_know")),
            RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(("Tenable VM", false, "Fonte não habilitada no perfil vigente"), (rows[0].Label, rows[0].IsEnabled, rows[0].DisabledText));
        Assert.Empty(rows[0].Segments);
        Assert.Null(rows[0].LastReadingText);
        // An unknown source reads as its id and an unknown reason as the generic sentence, never as a key name.
        Assert.Equal(("XYZ-1", "Fonte indisponível"), (rows[1].Label, rows[1].DisabledText));
    }

    [Fact]
    public void TestATabWithoutSourcesHasNoRows() =>
        Assert.Empty(AssetTabFormatter.Sources(Inventory(), RiskIndexTestText.Portuguese, PtBr));

    [Fact]
    public void TestTheVisibilityKeysAndSourceNamesAreInTheThreeResourceFiles()
    {
        var keys = AssetTabFormatter.SourceKeys().Concat(NodeKeyDisplay.NamedSources.Select(s => "RiskSource_" + s))
            .Concat(["RiskVisibilitySourcesTitle", "RiskVisibilityOverall", "RiskVisibilityNoSources"]).ToList();

        foreach (var file in RiskIndexTestText.ResourceFiles)
        {
            var texts = RiskIndexTestText.Load(file);
            var missing = keys.Where(k => !texts.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{file} lacks: {string.Join(", ", missing)}");
        }
    }

    /// <summary>
    /// §10.5: every key the trend, categories and tabs compute — category names, markers, annotations, tabs, display states,
    /// visibility bases and each factor line the server can send — resolves in all three resource files, so none renders
    /// as its key name.
    /// </summary>
    [Fact]
    public void TestEveryComputedKeyExistsInTheThreeResourceFiles()
    {
        var keys = new List<string>();
        keys.AddRange(ChartPaletteMap.SeriesOrder.Select(c => "RiskCategory_" + HeadlineFormatter.CategoryCode(c) + "_Name"));
        keys.AddRange(Enum.GetNames<RiskIndexMarkerKind>().Select(k => "RiskMarker_" + k));
        keys.AddRange(new[] { "VendorModel", "VendorRecalibration", "VendorBreachPenalty", "Note" }.Select(k => "RiskAnnotation_" + k));
        keys.AddRange(AssetTabFormatter.Tabs.Select(AssetTabFormatter.NameKey));
        keys.AddRange(Enum.GetNames<RiskIndexDisplayState>().Select(s => "RiskTrendState_" + s));
        keys.Add("RiskTrendState_Gap");
        keys.Add("RiskVisibility_Basis_exp_objectsFMT");
        keys.AddRange(new[] { "source_disabled", "no_incident_intake", "requires_m39", "unavailable" }.Select(r => "RiskCategoryReason_" + r));
        keys.AddRange(new[]
        {
            "EXP.D", "EXP.V", "EXP.V.exploited", "EXP.X", "EXP.I", "EXP.N", "CTL.A", "CTL.P", "CTL.C", "GOV.A", "GOV.C", "GOV.T",
            "GOV.R", "GOV.G", "AME.I", "REG.R", "TER", "presumed"
        }.Select(f => "RiskFactor_" + f.Replace('.', '_')));
        keys.AddRange(RiskApiCodes.ReadPath.Select(RiskApiErrorText.ResourceKey));

        foreach (var file in RiskIndexTestText.ResourceFiles)
        {
            var texts = RiskIndexTestText.Load(file);
            var missing = keys.Where(k => !texts.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{file} lacks: {string.Join(", ", missing)}");
        }
    }
}

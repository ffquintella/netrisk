using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// T254 (S40 §5.6.2, §12.4 <c>DrillStateTest</c>): the precedence table of the single drill-down state — the dimension
/// removes a chip of its own kind, the tab never changes the node or the tree, opening a child takes in the chips, the
/// breadcrumb clears them — and what each action reads again; plus the node display (<see cref="NodeKeyDisplay"/>) over the
/// same <see cref="RiskIndexNodeKey"/> parser as the server.
/// </summary>
[TestSubject(typeof(DrillState))]
public class DrillStateTest
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly NodeLabels Labels = new("Organização", "Meu escopo", "Não atribuído");

    private static DrillFilter F(string term)
    {
        Assert.True(DrillFilter.TryParse(term, out var filter), term);
        return filter;
    }

    [Fact]
    public void TestAStateStartsOnTheNodeWithTheUnitDimensionAndNoChips()
    {
        var state = DrillState.Start("e:7");

        Assert.Equal("e:7", state.NodeText);
        Assert.Equal(RiskIndexDimension.Unit, state.Dimension);
        Assert.Empty(state.Chips);
        Assert.Null(state.FiltersText);
        Assert.Null(state.ClassTab);
        Assert.True(DrillState.Start(null).Node.IsOrganization);
        Assert.True(DrillState.Start("not a key").Node.IsOrganization);
        Assert.Equal("e:7;c:server;k:5", DrillState.Start("k:5;e:7;c:server").NodeText);
    }

    [Theory]
    [InlineData("c:server", DrillFilterKind.AssetClass, "server")]
    [InlineData("c:devices", DrillFilterKind.AssetClass, "devices")]
    [InlineData("s:V1-DEV", DrillFilterKind.Source, "V1-DEV")]
    [InlineData("k:5", DrillFilterKind.Criticality, "5")]
    [InlineData("a:prod", DrillFilterKind.Environment, "prod")]
    public void TestAFilterTermRoundTrips(string term, DrillFilterKind kind, string value)
    {
        var filter = F(term);
        Assert.Equal((kind, value, term), (filter.Kind, filter.Value, filter.Term));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("e:7")]
    [InlineData("u")]
    [InlineData("org")]
    [InlineData("scope")]
    [InlineData("c:server;k:5")]
    [InlineData("k:9")]
    [InlineData("c:spaceship")]
    public void TestAnythingButOneFilterTermIsNotAFilter(string? term) => Assert.False(DrillFilter.TryParse(term, out _));

    /// <summary>§5.6.2 row 1: the dimension changes the children listed; a chip of its kind leaves, and the toast names it.</summary>
    [Fact]
    public void TestChoosingTheDimensionRemovesAChipOfTheSameKind()
    {
        var state = DrillState.Start("e:7").AddFilter(F("c:devices")).AddFilter(F("s:V1-DEV"));

        var change = state.WithDimension(RiskIndexDimension.AssetClass);

        Assert.Equal(RiskIndexDimension.AssetClass, change.State.Dimension);
        Assert.Equal(F("c:devices"), change.RemovedFilter);
        Assert.Equal(["s:V1-DEV"], change.State.Chips.Select(c => c.Term));
        Assert.Equal("e:7", change.State.NodeText);
        Assert.Equal(DrillReload.Tree, DrillState.Reload(state, change.State));

        var entity = state.WithDimension(RiskIndexDimension.Process);
        Assert.Null(entity.RemovedFilter);
        Assert.Equal(2, entity.State.Chips.Count);
    }

    [Fact]
    public void TestADimensionTheNodeRulesOutChangesNothing()
    {
        var cut = DrillState.Start("e:7;c:server");
        Assert.DoesNotContain(RiskIndexDimension.AssetClass, cut.AvailableDimensions);
        Assert.Same(cut, cut.WithDimension(RiskIndexDimension.AssetClass).State);

        var unassigned = DrillState.Start("u");
        Assert.DoesNotContain(RiskIndexDimension.Unit, unassigned.AvailableDimensions);
        Assert.Equal(RiskIndexDimension.AssetClass, unassigned.Dimension);
        Assert.Equal([RiskIndexDimension.AssetClass, RiskIndexDimension.Source, RiskIndexDimension.Criticality,
            RiskIndexDimension.Environment], unassigned.AvailableDimensions);

        var scope = DrillState.Start("scope");
        Assert.Equal([RiskIndexDimension.Unit], scope.AvailableDimensions);
        Assert.Empty(scope.AddableFilterKinds);
        Assert.Same(scope, scope.AddFilter(F("k:5")));
    }

    /// <summary>§5.6.2 row 2: "+ Filtro" offers the kinds other than the dimension's and absent from the node.</summary>
    [Fact]
    public void TestAFilterIsAddedOnlyForAKindOfferedAndReplacesItsKind()
    {
        var state = DrillState.Start("e:7;k:5").WithDimension(RiskIndexDimension.Source).State;

        Assert.Equal([DrillFilterKind.AssetClass, DrillFilterKind.Environment], state.AddableFilterKinds);
        Assert.Same(state, state.AddFilter(F("s:V1-DEV")));
        Assert.Same(state, state.AddFilter(F("k:3")));

        var once = state.AddFilter(F("c:devices"));
        var replaced = once.AddFilter(F("c:server")).AddFilter(F("a:prod"));
        Assert.Equal("c:server;a:prod", replaced.FiltersText);
        Assert.Equal("e:7;k:5", replaced.NodeText);
        Assert.Equal(DrillReload.Tree, DrillState.Reload(state, once));

        var removed = replaced.RemoveFilter(DrillFilterKind.AssetClass);
        Assert.Equal("a:prod", removed.FiltersText);
        Assert.Same(removed, removed.RemoveFilter(DrillFilterKind.Source));
    }

    /// <summary>§5.6.2 row 3: a tab changes only the tab — never the node, never the tree.</summary>
    [Fact]
    public void TestSelectingATabChangesNeitherTheNodeNorTheTree()
    {
        var state = DrillState.Start("e:7").AddFilter(F("k:5")).WithDimension(RiskIndexDimension.Process).State;

        var tabbed = state.SelectTab(RiskIndexTabGroup.Applications);

        Assert.Equal(RiskIndexTabGroup.Applications, tabbed.ClassTab);
        Assert.Equal((state.NodeText, state.Dimension, state.FiltersText), (tabbed.NodeText, tabbed.Dimension, tabbed.FiltersText));
        Assert.Equal(DrillReload.Tab, DrillState.Reload(state, tabbed));
        Assert.Same(tabbed, tabbed.SelectTab(RiskIndexTabGroup.Applications));
    }

    /// <summary>§5.6.2 row 4: opening a child — whose key already carries the chips — clears them, keeps dimension and tab.</summary>
    [Fact]
    public void TestOpeningAChildTakesInTheChipsAndKeepsDimensionAndTab()
    {
        var state = DrillState.Start("e:7").WithDimension(RiskIndexDimension.Process).State.AddFilter(F("k:5"))
            .SelectTab(RiskIndexTabGroup.Devices);

        var child = state.OpenChild("e:20;k:5");

        Assert.Equal("e:20;k:5", child.NodeText);
        Assert.Empty(child.Chips);
        Assert.Equal(RiskIndexDimension.Process, child.Dimension);
        Assert.Equal(RiskIndexTabGroup.Devices, child.ClassTab);
        Assert.Equal(DrillReload.Page, DrillState.Reload(state, child));
        Assert.Same(state, state.OpenChild("e:abc"));
        Assert.Same(state, state.OpenChild(null));
    }

    /// <summary>A child cut by the dimension's own kind would list only itself: the dimension falls back and the tab follows the class.</summary>
    [Fact]
    public void TestOpeningAClassChildFallsBackFromTheClassDimensionAndSelectsItsTab()
    {
        var state = DrillState.Start("e:7").WithDimension(RiskIndexDimension.AssetClass).State
            .SelectTab(RiskIndexTabGroup.Clouds);

        var child = state.OpenChild("e:7;c:server");

        Assert.Equal(RiskIndexDimension.Unit, child.Dimension);
        Assert.Equal(RiskIndexTabGroup.Devices, child.ClassTab);
        Assert.DoesNotContain(DrillFilterKind.AssetClass, child.AddableFilterKinds);
    }

    /// <summary>§5.6.2 row 5: the breadcrumb sets the node to a prefix and clears the chips.</summary>
    [Fact]
    public void TestTheBreadcrumbClearsTheChips()
    {
        var state = DrillState.Start("e:7;c:devices;s:V1-DEV").AddFilter(F("k:5"));

        var prefix = state.OpenBreadcrumb("e:7;c:devices");

        Assert.Equal("e:7;c:devices", prefix.NodeText);
        Assert.Empty(prefix.Chips);
        Assert.Equal(DrillReload.Page, DrillState.Reload(state, prefix));
        Assert.Same(state, state.OpenBreadcrumb(""));
        Assert.Equal(DrillReload.Page, DrillState.Reload(null, prefix));
        Assert.Equal(DrillReload.None, DrillState.Reload(prefix, prefix));
    }

    [Fact]
    public void TestComposePutsTheEntityFirstAndTheTermsInCanonicalOrder()
    {
        Assert.True(RiskIndexNodeKey.TryParse("e:7;c:server;k:5", out var node));
        Assert.Equal("e:7;s:INC", DrillState.Compose(node, [F("s:INC")]).ToString());
        Assert.Equal("org", DrillState.Compose(RiskIndexNodeKey.Org, []).ToString());
        Assert.Equal("c:devices;a:none", DrillState.Compose(RiskIndexNodeKey.Org, [F("a:none"), F("c:devices")]).ToString());
        Assert.Equal(["c:server", "k:5"], DrillState.FilterTerms(node).Select(f => f.Term));
    }

    // --- NodeKeyDisplay -------------------------------------------------------------------------------------------------

    private static RiskIndexOverviewDto Overview(RiskIndexNodeRef node, params RiskIndexNodeRef[] path) => new(
        node, path, new System.DateOnly(2026, 10, 5), new System.DateTime(2026, 10, 5, 5, 0, 0, System.DateTimeKind.Utc),
        RiskIndexProvenance.OfficialDerived,
        new RiskIndexProfileSummaryDto(1, 1, "Pilot", null, RiskIndexProfileStatus.Active, null, null, "a", null, false, null),
        RiskIndexMode.Interim, null, [], [], [], new RiskIndexQualitySummaryDto(1, 1, 1, "high", []), 3, [], []);

    private static readonly RiskIndexNodeRef Org = new("org", "Organization", "organization");
    private static readonly RiskIndexNodeRef UnitX = new("e:3", "Unidade X", "organizationUnit");

    /// <summary>§5.1, §5.6.2: entity segments first, then one per filter term, each the prefix of the node up to it.</summary>
    [Fact]
    public void TestTheBreadcrumbHasASegmentPerFilterTermAfterTheEntities()
    {
        var node = new RiskIndexNodeRef("e:9;c:devices;s:V1-DEV", "Lab L1 ∩ Devices ∩ V1-DEV", "subOrganizationUnit∩");

        var segments = NodeKeyDisplay.Breadcrumb(Overview(node, Org, UnitX), Labels, false, RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(["Organização", "Unidade X", "Lab L1", "Dispositivos", "Vision One — dispositivos"], segments.Select(s => s.Label));
        Assert.Equal(["org", "e:3", "e:9", "e:9;c:devices", "e:9;c:devices;s:V1-DEV"], segments.Select(s => s.Key));
        Assert.Equal([false, false, false, false, true], segments.Select(s => s.IsCurrent));
        Assert.Equal("e:9;c:devices", NodeKeyDisplay.ParentKey(segments));
    }

    [Fact]
    public void TestABreadcrumbWithoutFiltersIsTheEntityPath()
    {
        var lab = new RiskIndexNodeRef("e:9", "Lab L1", "subOrganizationUnit");

        var segments = NodeKeyDisplay.Breadcrumb(Overview(lab, Org, UnitX), Labels, false, RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(["org", "e:3", "e:9"], segments.Select(s => s.Key));
        Assert.True(segments[^1].IsCurrent);
        Assert.Null(NodeKeyDisplay.ParentKey([segments[0]]));
    }

    [Fact]
    public void TestTheOrganizationAndUnassignedReadInTheInterfaceLanguage()
    {
        var orgServer = new RiskIndexNodeRef("c:server", "Organization ∩ Server", "organization∩");
        var unassigned = new RiskIndexNodeRef("u;k:5", "Unassigned ∩ k5", "unassigned∩");

        Assert.Equal(["Organização", "Servidor"],
            NodeKeyDisplay.Breadcrumb(Overview(orgServer, Org), Labels, false, RiskIndexTestText.Portuguese, PtBr).Select(s => s.Label));
        Assert.Equal(["Organização", "Não atribuído", "Criticidade 5"],
            NodeKeyDisplay.Breadcrumb(Overview(unassigned, Org), Labels, false, RiskIndexTestText.Portuguese, PtBr).Select(s => s.Label));
        Assert.Equal(["scope", "e:3", "e:3;a:prod"], NodeKeyDisplay.Breadcrumb(
            Overview(new RiskIndexNodeRef("e:3;a:prod", "Unidade X ∩ prod", "organizationUnit∩")), Labels, true,
            RiskIndexTestText.Portuguese, PtBr).Select(s => s.Key));
    }

    [Theory]
    [InlineData("c:server", "Servidor")]
    [InlineData("c:internetfacing", "Internet-facing")]
    [InlineData("c:clouds", "Nuvem")]
    [InlineData("s:V1-DEV", "Vision One — dispositivos")]
    [InlineData("s:XYZ-1", "XYZ-1")] // a source the interface does not name reads as its S39 id
    [InlineData("k:4", "Criticidade 4")]
    [InlineData("a:prod", "Produção")]
    [InlineData("a:none", "Sem ambiente")]
    [InlineData("a:dev", "Ambiente: dev")]
    public void TestEachFilterTermReadsInPortuguese(string term, string label) =>
        Assert.Equal(label, NodeKeyDisplay.TermLabel(F(term), RiskIndexTestText.Portuguese, PtBr));

    [Fact]
    public void TestTheEntityLabelDropsTheServersFilterSuffixOnly()
    {
        Assert.Equal("Unidade X", NodeKeyDisplay.EntityLabel(new RiskIndexNodeRef("e:3;c:server;k:5", "Unidade X ∩ Server ∩ k5", "x"), Labels));
        Assert.Equal("A ∩ B", NodeKeyDisplay.EntityLabel(new RiskIndexNodeRef("e:3;k:5", "A ∩ B ∩ k5", "x"), Labels));
        Assert.Equal("Unidade X", NodeKeyDisplay.EntityLabel(UnitX, Labels));
        Assert.Equal("Meu escopo", NodeKeyDisplay.EntityLabel(new RiskIndexNodeRef("scope", "My scope", "scope"), Labels));
    }

    /// <summary>§5.6.1: a row names the term the dimension adds, or the entity; its type is the entity's or the dimension's kind.</summary>
    [Fact]
    public void TestATreeRowNamesWhatItsDimensionAdds()
    {
        RiskIndexNodeRowDto Row(string key, string label, string type) => new(new RiskIndexNodeRef(key, label, type), 50, "b1",
            RiskIndexDisplayState.Point, RiskIndexProvenance.OfficialDerived, null, 0.9, 10, 0, false, "g", null, null, null);

        var byClass = Row("e:3;c:server;k:5", "Unidade X ∩ Server ∩ k5", "organizationUnit∩");
        Assert.Equal("Servidor", NodeKeyDisplay.RowLabel(byClass, RiskIndexDimension.AssetClass, Labels, RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal("Classe de ativo", NodeKeyDisplay.RowType(byClass, RiskIndexDimension.AssetClass, RiskIndexTestText.Portuguese));

        var byUnit = Row("e:12;k:5", "Lab L1 ∩ k5", "subOrganizationUnit∩");
        Assert.Equal("Lab L1", NodeKeyDisplay.RowLabel(byUnit, RiskIndexDimension.Unit, Labels, RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal("Subunidade", NodeKeyDisplay.RowType(byUnit, RiskIndexDimension.Unit, RiskIndexTestText.Portuguese));

        var unassigned = Row("u", "Unassigned", "unassigned");
        Assert.Equal("Não atribuído", NodeKeyDisplay.RowLabel(unassigned, RiskIndexDimension.Unit, Labels, RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal("Não atribuído", NodeKeyDisplay.RowType(unassigned, RiskIndexDimension.Unit, RiskIndexTestText.Portuguese));
        Assert.Equal("customType", NodeKeyDisplay.EntityTypeLabel("customType", RiskIndexTestText.Portuguese));
    }

    /// <summary>§5.6.3: the seal beside the title names the node's type and each filter term with its kind.</summary>
    [Fact]
    public void TestTheTypeSealNamesTheNodesTypeAndTerms()
    {
        Assert.Equal("Processo", NodeKeyDisplay.TypeSeal(new RiskIndexNodeRef("e:20", "Matrícula", "businessProcess"),
            RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal("Classe de ativo: Dispositivos", NodeKeyDisplay.TypeSeal(new RiskIndexNodeRef("c:devices", "x", "organization∩"),
            RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal("Unidade · Fonte: Vision One — dispositivos", NodeKeyDisplay.TypeSeal(new RiskIndexNodeRef("e:3;s:V1-DEV", "x", "organizationUnit∩"),
            RiskIndexTestText.Portuguese, PtBr));
        Assert.Null(NodeKeyDisplay.TypeSeal(Org, RiskIndexTestText.Portuguese, PtBr));
        Assert.Null(NodeKeyDisplay.TypeSeal(new RiskIndexNodeRef("scope", "My scope", "scope"), RiskIndexTestText.Portuguese, PtBr));
    }

    [Fact]
    public void TestEveryDimensionAndKindHasALabelInBothLanguages()
    {
        foreach (var lookup in new[] { RiskIndexTestText.Portuguese, RiskIndexTestText.English })
        {
            var dimensions = DrillState.AllDimensions.Select(d => NodeKeyDisplay.DimensionLabel(d, lookup)).ToList();
            Assert.Equal(dimensions.Count, dimensions.Distinct().Count());
            Assert.DoesNotContain(dimensions, l => l.StartsWith("RiskExplore", System.StringComparison.Ordinal));
        }

        Assert.Equal("Ambiente", NodeKeyDisplay.DimensionLabel(RiskIndexDimension.Environment, RiskIndexTestText.Portuguese));
        Assert.Equal("Criticidade", NodeKeyDisplay.FilterKindLabel(DrillFilterKind.Criticality, RiskIndexTestText.Portuguese));
    }

    [Fact]
    public void TestEveryKeyTheDrillDownComputesExistsInTheThreeResourceFiles()
    {
        var keys = new List<string>(NodeKeyDisplay.ComputedKeys());
        keys.AddRange(NodeTreeRows.ComputedKeys());
        keys.AddRange(ObjectRowFormatter.ComputedKeys());
        keys.AddRange(RiskApiCodes.ReadPath.Select(RiskApiErrorText.ResourceKey));

        foreach (var file in RiskIndexTestText.ResourceFiles)
        {
            var texts = RiskIndexTestText.Load(file);
            var missing = keys.Where(k => !texts.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{file} lacks: {string.Join(", ", missing)}");
        }
    }
}

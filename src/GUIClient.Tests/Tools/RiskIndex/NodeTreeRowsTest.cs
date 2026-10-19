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
/// T254 (S40 §5.6.1, §5.6.4; acceptance 10): the Explore tree's rows — a note between comparability groups (partial after
/// official ones, then type, categories, provenance), a sort that never crosses a group and keeps indicative and no-data
/// nodes last, cells as text with band, Δ and state, expansion only for entity children, and Ctrl+F's name filter.
/// </summary>
[TestSubject(typeof(NodeTreeRows))]
public class NodeTreeRowsTest
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly NodeLabels Labels = new("Organização", "Meu escopo", "Não atribuído");

    private static readonly IReadOnlyList<RiskIndexBandDto> Bands =
    [
        new("b0", "Baixo", "Low", 0, 1, 0), new("b1", "Médio", "Medium", 1, 3, 31), new("b2", "Alto", "High", 2, 5, 70)
    ];

    private static RiskIndexNodeRowDto Row(string key, string label, string group, int? value = 50, string? band = "b1",
        RiskIndexDisplayState state = RiskIndexDisplayState.Point, RiskIndexProvenance provenance = RiskIndexProvenance.Official,
        int? delta = 4, double? coverage = 0.92, int objects = 10, int shared = 0, bool hasChildren = false,
        double? low = null, double? high = null, double? unassigned = null, string type = "organizationUnit") =>
        new(new RiskIndexNodeRef(key, label, type), value, band, state, provenance, delta, coverage, objects, shared, hasChildren, group,
            low, high, unassigned);

    private static RiskIndexComparabilityGroupDto Group(string key, string type, RiskIndexProvenance provenance,
        params RiskIndexCategory[] categories) => new(key, type, categories, provenance, null);

    private static IReadOnlyList<NodeTreeEntry> Build(RiskIndexNodeChildrenDto children) =>
        NodeTreeRows.Build(children, Bands, Labels, RiskIndexTestText.Portuguese, PtBr, PtBr);

    /// <summary>The mockup of §5.6.1: units, then a partial unit, then "Não atribuído", with a note opening each later group.</summary>
    [Fact]
    public void TestANoteOpensEveryGroupButTheFirst()
    {
        var unit = Group("u|1,2|1", "organizationUnit", RiskIndexProvenance.Official, RiskIndexCategory.Reg, RiskIndexCategory.Exp);
        var partial = Group("u|1,2|3", "organizationUnit", RiskIndexProvenance.PartialRestricted, RiskIndexCategory.Reg, RiskIndexCategory.Exp);
        var none = Group("n|2|2", "unassigned", RiskIndexProvenance.OfficialDerived, RiskIndexCategory.Exp);
        var children = new RiskIndexNodeChildrenDto(new RiskIndexNodeRef("org", "Organization", "organization"), RiskIndexDimension.Unit,
            [unit, partial, none],
            [
                Row("e:1", "Unidade X", unit.Key, hasChildren: true), Row("e:2", "Unidade Z", unit.Key, value: 47),
                Row("e:3", "Unidade Y", partial.Key, provenance: RiskIndexProvenance.PartialRestricted, delta: null),
                Row("u", "Unassigned", none.Key, value: 58, provenance: RiskIndexProvenance.OfficialDerived, unassigned: 0.12, type: "unassigned")
            ], 0);

        var entries = Build(children);

        Assert.Equal([NodeTreeEntryKind.Node, NodeTreeEntryKind.Node, NodeTreeEntryKind.Separator, NodeTreeEntryKind.Node,
            NodeTreeEntryKind.Separator, NodeTreeEntryKind.Node], entries.Select(e => e.Kind));
        Assert.Equal("não comparáveis com as linhas acima: visão parcial", entries[2].Label);
        Assert.Equal("não comparáveis: tipo diferente", entries[4].Label);
        Assert.Equal("Não atribuído", entries[5].Label);
        Assert.Equal("12% do peso", entries[5].StateText);
        Assert.True(entries[5].StateWarning);
        Assert.Equal("parcial", entries[3].StateText);
        Assert.Equal("—", entries[3].DeltaText);
        Assert.True(entries[0].CanExpand);
        Assert.False(entries[1].CanExpand);
        Assert.False(entries[5].CanExpand);
    }

    [Fact]
    public void TestTheNoteNamesTheFirstDifference()
    {
        var a = Group("a", "organizationUnit", RiskIndexProvenance.Official, RiskIndexCategory.Reg, RiskIndexCategory.Exp);
        Assert.Equal("RiskExploreNotComparableCategories",
            NodeTreeRows.SeparatorKey(a, Group("b", "organizationUnit", RiskIndexProvenance.Official, RiskIndexCategory.Exp)));
        Assert.Equal("RiskExploreNotComparableProvenance",
            NodeTreeRows.SeparatorKey(a, a with { Key = "c", Provenance = RiskIndexProvenance.OfficialDerived }));
        Assert.Equal("RiskExploreNotComparableType", NodeTreeRows.SeparatorKey(a, a with { NodeType = "businessProcess" }));
        Assert.Equal("RiskExploreNotComparableType",
            NodeTreeRows.SeparatorKey(a with { Provenance = RiskIndexProvenance.PartialRestricted },
                a with { NodeType = "x", Provenance = RiskIndexProvenance.PartialRestricted }));
    }

    [Fact]
    public void TestTheCellsCarryBandGlyphDeltaAndCoverageAsText()
    {
        var group = Group("g", "organizationUnit", RiskIndexProvenance.Official);
        var entry = Build(new RiskIndexNodeChildrenDto(new RiskIndexNodeRef("org", "Organization", "organization"),
            RiskIndexDimension.Unit, [group], [Row("e:1", "Unidade X", "g", value: 64, band: "b1", delta: -3, coverage: 0.915,
                objects: 1204, shared: 30)], 0)).Single();

        Assert.Equal(("64", "Médio", "b3", MaterialIconKind.AlertOutline), (entry.IcrText, entry.BandLabel, entry.BandClass, entry.BandGlyph));
        Assert.Equal(("−3", "92%", "1.204", "30", "oficial"), (entry.DeltaText, entry.CoverageText, entry.ObjectsText, entry.SharedText,
            entry.StateText));
        Assert.Equal("Unidade", entry.TypeText);
        Assert.False(entry.IsUnranked);
    }

    /// <summary>§5.6.4: an indicative or no-data node has no ICR and no band; its state says why.</summary>
    [Fact]
    public void TestAnIndicativeNodeIsUnrankedWithoutBand()
    {
        var indicative = NodeTreeRows.Row(Row("e:5", "Secretaria", "g", value: 41, band: "b1", state: RiskIndexDisplayState.Indicative,
            low: 20.4, high: 79.2), 0, RiskIndexDimension.Unit, Bands, Labels, RiskIndexTestText.Portuguese, PtBr, PtBr);
        var noData = NodeTreeRows.Row(Row("e:6", "Vazio", "g", value: null, band: null, state: RiskIndexDisplayState.NoData), 0,
            RiskIndexDimension.Unit, Bands, Labels, RiskIndexTestText.Portuguese, PtBr, PtBr);

        Assert.Equal(("—", null, "indicativo 41 [20–79]", true), (indicative.IcrText, indicative.BandLabel, indicative.StateText,
            indicative.IsUnranked));
        Assert.Equal(("—", null, "sem dados", true), (noData.IcrText, noData.BandClass, noData.StateText, noData.IsUnranked));
        Assert.Equal("indicativo", NodeTreeRows.StateText(Row("e:7", "x", "g", state: RiskIndexDisplayState.Indicative),
            RiskIndexTestText.Portuguese, PtBr));
    }

    /// <summary>Along a filter dimension the children are intersections, not entities: they never expand.</summary>
    [Fact]
    public void TestOnlyEntityChildrenAlongAnEntityDimensionExpand()
    {
        var row = Row("e:3;c:server", "Unidade X ∩ Server", "g", hasChildren: true, type: "organizationUnit∩");

        var byClass = NodeTreeRows.Row(row, 0, RiskIndexDimension.AssetClass, Bands, Labels, RiskIndexTestText.Portuguese, PtBr, PtBr);
        var byUnit = NodeTreeRows.Row(row, 0, RiskIndexDimension.Unit, Bands, Labels, RiskIndexTestText.Portuguese, PtBr, PtBr);

        Assert.False(byClass.CanExpand);
        Assert.Equal("Servidor", byClass.Label);
        Assert.True(byUnit.CanExpand);
    }

    /// <summary>§5.6.4 / acceptance 10: a sort never moves a node across groups; unranked nodes stay last; the note leads.</summary>
    [Fact]
    public void TestTheSortStaysInsideEachGroup()
    {
        NodeTreeEntry E(string key, int group, int? value, bool unranked = false, NodeTreeEntryKind kind = NodeTreeEntryKind.Node) =>
            new(kind, key, group, key, "", value, "", null, null, null, "", value / 100.0, "", value ?? 0, "", 0, "", "", false,
                unranked, false);

        var entries = new List<NodeTreeEntry>
        {
            E("a", 0, 30), E("b", 0, 80), E("c", 0, null, unranked: true), E("note", 1, null, kind: NodeTreeEntryKind.Separator),
            E("d", 1, 99), E("e", 1, 10)
        };

        foreach (var descending in new[] { false, true })
        foreach (var column in new[] { NodeTreeSortColumn.Icr, NodeTreeSortColumn.Coverage, NodeTreeSortColumn.Objects })
        {
            var sorted = entries.OrderBy(e => e, Comparer<NodeTreeEntry>.Create((x, y) => NodeTreeRows.Compare(x, y, column, descending)))
                .Select(e => e.Key).ToList();
            Assert.Equal(descending ? ["b", "a", "c", "note", "d", "e"] : ["a", "b", "c", "note", "e", "d"], sorted);
        }

        var byName = entries.OrderBy(e => e, Comparer<NodeTreeEntry>.Create((x, y) => NodeTreeRows.Compare(x, y, NodeTreeSortColumn.Node, true)))
            .Select(e => e.Key).ToList();
        Assert.Equal(["b", "a", "c", "note", "e", "d"], byName);
        Assert.Equal(1, NodeTreeRows.Compare(null, entries[0], NodeTreeSortColumn.Icr, false));
        Assert.Equal(0, NodeTreeRows.Compare(entries[0], entries[0], NodeTreeSortColumn.Icr, false));
    }

    /// <summary>IX-8: Ctrl+F filters by name, accents and case aside; a note stays only with a node of its group, never first.</summary>
    [Fact]
    public void TestTheNameFilterKeepsNotesOnlyWithTheirNodes()
    {
        var unit = Group("u", "organizationUnit", RiskIndexProvenance.Official);
        var partial = Group("p", "organizationUnit", RiskIndexProvenance.PartialRestricted);
        var entries = Build(new RiskIndexNodeChildrenDto(new RiskIndexNodeRef("org", "Organization", "organization"),
            RiskIndexDimension.Unit, [unit, partial],
            [Row("e:1", "Secretaria Acadêmica", "u"), Row("e:2", "Lab", "u"), Row("e:3", "Academia", "p",
                provenance: RiskIndexProvenance.PartialRestricted)], 0));

        Assert.Same(entries, NodeTreeRows.Filter(entries, "  "));
        Assert.Equal(["e:1", "", "e:3"], NodeTreeRows.Filter(entries, "acade").Select(e => e.Key));
        Assert.Equal(["e:3"], NodeTreeRows.Filter(entries, "ACADEMIA").Select(e => e.Key));
        Assert.Empty(NodeTreeRows.Filter(entries, "zzz"));
    }

    [Fact]
    public void TestTheEnvironmentDimensionCountsTheObjectsOutsideIt()
    {
        var org = new RiskIndexNodeRef("org", "Organization", "organization");
        Assert.Equal("1.204 objetos sem ambiente (só hosts têm) ficam fora deste recorte.", NodeTreeRows.OutsideNote(
            new RiskIndexNodeChildrenDto(org, RiskIndexDimension.Environment, [], [], 1204), RiskIndexTestText.Portuguese, PtBr));
        Assert.Null(NodeTreeRows.OutsideNote(new RiskIndexNodeChildrenDto(org, RiskIndexDimension.Environment, [], [], 0),
            RiskIndexTestText.Portuguese, PtBr));
        Assert.Null(NodeTreeRows.OutsideNote(new RiskIndexNodeChildrenDto(org, RiskIndexDimension.Unit, [], [], 9),
            RiskIndexTestText.Portuguese, PtBr));
    }

    [Fact]
    public void TestAMessageLineIsNeitherANodeNorExpandable()
    {
        var message = NodeTreeRows.Message("Carregando…");
        Assert.Equal((NodeTreeEntryKind.Message, false, false, "Carregando…"), (message.Kind, message.IsNode, message.CanExpand, message.Label));
    }
}

using System;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// T254 (S40 §5.9.1, §8.2, §10.9): what the object list asks the server — filters in the API's <c>name=value</c> syntax,
/// only the server's sorts (none by sensitivity or Δ), pages of 50 up to the server's 500, a factor line's preset over
/// node ∩ tab — and how a row reads, with the redaction hint.
/// </summary>
[TestSubject(typeof(ObjectListQuery))]
public class ObjectListQueryTest
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    [Fact]
    public void TestAnEmptyQueryAsksForTheFirstPageOfFiftyInTheServersOrder()
    {
        var query = new ObjectListQuery();

        Assert.Equal((1, 50), (query.Page, query.PageSize));
        Assert.Null(query.FiltersText);
        Assert.Null(query.SortsText);
        Assert.Equal("Ordenado por pontos, maiores primeiro", ObjectListQuery.SortText(query, RiskIndexTestText.Portuguese, PtBr));
    }

    [Fact]
    public void TestTheFiltersUseTheApisNamesAndValues()
    {
        var query = new ObjectListQuery
        {
            Category = RiskIndexCategory.Exp, State = RiskIndexObjectState.Presumed, AssetClass = RiskIndexAssetClass.Server,
            Source = "V1-DEV", Criticality = 5, Label = "portal", Preset = "factor=EXP.V"
        };

        Assert.Equal("category=Exp,state=Presumed,assetClass=Server,source=V1-DEV,criticality=5,label=portal,factor=EXP.V",
            query.FiltersText);
    }

    /// <summary>§5.9.1: the same column toggles; a new one starts ascending for the name, descending for numbers; Δ never sorts.</summary>
    [Fact]
    public void TestTheSortIsTheServersAndGoesBackToTheFirstPage()
    {
        var paged = new ObjectListQuery().Next().Next();

        var byR = paged.SortBy("r");
        Assert.Equal(("-r", 1), (byR.SortsText, byR.Page));
        Assert.Equal("r", byR.SortBy("r").SortsText);
        Assert.Equal("label", byR.SortBy("label").SortsText);
        Assert.Equal("Ordenado por R (decrescente)", ObjectListQuery.SortText(byR, RiskIndexTestText.Portuguese, PtBr));

        Assert.Same(paged, paged.SortBy("delta"));
        Assert.Same(paged, paged.SortBy("sensitivity"));
        Assert.Same(paged, paged.SortBy(null));
        Assert.Equal(["contribution", "criticality", "label", "lastReading", "r"], ObjectListQuery.SortableColumns.Order());
    }

    [Fact]
    public void TestPagesAreOfferedUpToTheServersLimit()
    {
        Assert.Equal([50, 100, 250, 500], ObjectListQuery.PageSizes);
        Assert.All(ObjectListQuery.PageSizes, size => Assert.InRange(size, 1, ObjectListQuery.MaxPageSize));
        Assert.Equal((500, 1), (new ObjectListQuery().Next().WithPageSize(500).PageSize, new ObjectListQuery().WithPageSize(500).Page));
        Assert.Equal(50, new ObjectListQuery().WithPageSize(10_000).PageSize);
        Assert.Equal(1, new ObjectListQuery().Previous().Page);
    }

    [Fact]
    public void TestTheRangeReadsFirstToLastOfTotal()
    {
        var query = new ObjectListQuery().Next();

        var range = query.Range(1204, 50);

        Assert.Equal((51, 100, true, true), (range.First, range.Last, range.HasPrevious, range.HasNext));
        Assert.Equal("51–100 de 1.204", ObjectListQuery.RangeText(range, RiskIndexTestText.Portuguese, PtBr));
        Assert.False(new ObjectListQuery().Range(12, 12).HasNext);
    }

    /// <summary>§5.4: arriving from a category's detail pre-selects it; the other filters clear, the name search stays.</summary>
    [Fact]
    public void TestACategoryDetailPreselectsTheCategory()
    {
        var busy = new ObjectListQuery { State = RiskIndexObjectState.Stale, Label = "srv", PageSize = 100 }.Next();

        var category = busy.ForCategory(RiskIndexCategory.Gov);

        Assert.Equal("category=Gov,label=srv", category.FiltersText);
        Assert.Equal((1, 100), (category.Page, category.PageSize));
    }

    /// <summary>§5.5.1: a factor line's "›" lists node ∩ its tab filtered by its ObjectFilter; anything else is not sent.</summary>
    [Fact]
    public void TestAFactorLineListsTheNodeIntersectedWithItsTab()
    {
        Assert.True(RiskIndexNodeKey.TryParse("e:7", out var node));

        var factor = new ObjectListQuery().ForPreset("factor=EXP.V", "31 achados", RiskIndexTabGroup.Devices);

        Assert.Equal("factor=EXP.V", factor.FiltersText);
        Assert.Equal("e:7;c:devices", factor.NodeKey(node));
        Assert.Equal("31 achados", factor.PresetLabel);
        Assert.Equal("e:7", factor.WithoutPreset().NodeKey(node));
        Assert.Null(factor.WithoutPreset().FiltersText);

        var bad = new ObjectListQuery().ForPreset("factor=EXP.V,category=Exp", null, RiskIndexTabGroup.Devices);
        Assert.Null(bad.FiltersText);
        Assert.Equal("e:7;c:devices", bad.NodeKey(node));
    }

    [Theory]
    [InlineData("factor=EXP.V.exploited", true)]
    [InlineData("seal=above_appetite", true)]
    [InlineData("chip=unassigned-above", true)]
    [InlineData("isPresumed=true", true)]
    [InlineData("chip=default_criticality", true)]
    [InlineData("source=V1-DEV", true)] // a stale source's quality item (T256)
    [InlineData("label=x", false)]
    [InlineData("factor=", false)]
    [InlineData("=EXP.V", false)]
    [InlineData("factor=a=b", false)]
    [InlineData("factor=EXP V", false)]
    [InlineData(null, false)]
    public void TestOnlyOneAcceptedNameValueIsAPreset(string? filter, bool valid) =>
        Assert.Equal(valid, ObjectListQuery.IsValidPreset(filter));

    [Theory]
    [InlineData("  portal  ", "portal")]
    [InlineData("a,b=c", "abc")]
    [InlineData("   ", null)]
    [InlineData(",=", null)]
    [InlineData(null, null)]
    public void TestTheNameSearchDropsTheFilterSyntax(string? text, string? expected) =>
        Assert.Equal(expected, ObjectListQuery.CleanLabel(text));

    [Fact]
    public void TestTheNameSearchIsCapped() =>
        Assert.Equal(ObjectListQuery.MaxLabelLength, ObjectListQuery.CleanLabel(new string('x', 300))!.Length);

    // --- rows ------------------------------------------------------------------------------------------------------------

    private static RiskIndexObjectRowDto Row(string? requires = null, string? source = "cmdb") => new(
        new RiskIndexObjectLabelDto(new RiskIndexObjectRef(RiskIndexObjectKind.Host, "101"), requires is null ? "SRV-01" : "Host #101",
            requires),
        "Server", RiskIndexCategory.Exp, 0.905, 5, source, RiskIndexObjectState.Assessed, 1, new DateTime(2026, 10, 5, 12, 0, 0, DateTimeKind.Utc),
        22.04, false, 1, ["V1-DEV"], "prod");

    [Fact]
    public void TestARowReadsLikeTheSpecsTable()
    {
        var text = ObjectRowFormatter.Format(Row(), RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(("SRV-01", null, "Host", "Servidor", "EXP", "91"), (text.Label, text.LabelHint, text.Kind, text.AssetClass,
            text.Category, text.R));
        Assert.Equal(("5 (CMDB)", "avaliado", "22", "1"), (text.Criticality, text.State, text.Points, text.Shared));
        Assert.Equal("5", ObjectRowFormatter.Criticality(Row(source: null), RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal("5 (novel)", ObjectRowFormatter.Criticality(Row(source: "novel"), RiskIndexTestText.Portuguese, PtBr));
    }

    /// <summary>§5.0, §11.3: a redacted label stays "‹Tipo› #id" and says which permission it needs.</summary>
    [Fact]
    public void TestARedactedRowSaysWhichPermissionItNeeds()
    {
        var text = ObjectRowFormatter.Format(Row(requires: "hosts"), RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal("Host #101", text.Label);
        Assert.Contains("hosts", text.LabelHint);
    }

    [Theory]
    [InlineData(-0.2, "0")]
    [InlineData(0.005, "1")]
    [InlineData(1.4, "100")]
    public void TestRIsOnTheZeroToHundredScale(double r, string expected) => Assert.Equal(expected, ObjectRowFormatter.R(r, PtBr));
}

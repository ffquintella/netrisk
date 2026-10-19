using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using DAL.Enums;
using GUIClient.Models;
using GUIClient.Navigation;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// T255 (S40 §5.0, §10.3, §11.3): the API's navigation destinations as the GUI's <see cref="NavigationTarget"/> per the
/// §10.3 table; the destinations each object kind offers; and how a redacted object reads — "‹Tipo› #id" in the interface's
/// language with "Requer a permissão ‹módulo›", wherever the module names an object.
/// </summary>
[TestSubject(typeof(RiskIndexNavigationMap))]
public class RiskIndexNavigationMapTest
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");

    [Theory]
    [InlineData(RiskIndexNavigationModule.Risk, AvaliableViews.Risk, null)]
    [InlineData(RiskIndexNavigationModule.Host, AvaliableViews.Devices, null)]
    [InlineData(RiskIndexNavigationModule.HostFindings, AvaliableViews.Vulnerabilities, NavigationTarget.HostSubView)]
    [InlineData(RiskIndexNavigationModule.ApplicationFindings, AvaliableViews.Vulnerabilities, NavigationTarget.EntitySubView)]
    [InlineData(RiskIndexNavigationModule.Incident, AvaliableViews.Incidents, null)]
    [InlineData(RiskIndexNavigationModule.Assessment, AvaliableViews.Assessment, null)]
    [InlineData(RiskIndexNavigationModule.Entity, AvaliableViews.Entities, null)]
    public void TestEachApiDestinationOpensTheSection103Module(RiskIndexNavigationModule module, AvaliableViews view, string? subView)
    {
        var target = RiskIndexNavigationMap.ToTarget(new RiskIndexNavigationTargetDto(module, 42, "RiskNav_" + module));

        Assert.Equal(new NavigationTarget(view, 42, subView), target);
        Assert.Equal(view, RiskIndexNavigationMap.ViewOf(module));
    }

    [Fact]
    public void TestEveryApiModuleIsMappedAndAnUnknownOneIsNot()
    {
        Assert.All(Enum.GetValues<RiskIndexNavigationModule>(),
            m => Assert.NotNull(RiskIndexNavigationMap.ToTarget(new RiskIndexNavigationTargetDto(m, 1, ""))));
        Assert.Null(RiskIndexNavigationMap.ToTarget(new RiskIndexNavigationTargetDto((RiskIndexNavigationModule)99, 1, "")));
    }

    /// <summary>The button text key is the one the server sends (<c>RiskIndexReadService.Navigation</c>).</summary>
    [Fact]
    public void TestTheButtonKeysAreTheServersAndExistInTheThreeResourceFiles()
    {
        Assert.Equal("RiskNav_HostFindings", RiskIndexNavigationMap.LabelKey(RiskIndexNavigationModule.HostFindings));
        foreach (var file in RiskIndexTestText.ResourceFiles)
        {
            var texts = RiskIndexTestText.Load(file);
            Assert.All(Enum.GetValues<RiskIndexNavigationModule>(),
                m => Assert.True(texts.ContainsKey(RiskIndexNavigationMap.LabelKey(m)), $"{file}: {m}"));
        }
    }

    [Fact]
    public void TestEachKindOffersItsDestinationsAndVendorObjectsNone()
    {
        Assert.Equal([RiskIndexNavigationModule.Risk], RiskIndexNavigationMap.Destinations(RiskIndexObjectKind.Scenario));
        Assert.Equal([RiskIndexNavigationModule.Host, RiskIndexNavigationModule.HostFindings],
            RiskIndexNavigationMap.Destinations(RiskIndexObjectKind.Host));
        Assert.Equal([RiskIndexNavigationModule.ApplicationFindings, RiskIndexNavigationModule.Entity],
            RiskIndexNavigationMap.Destinations(RiskIndexObjectKind.Application));
        Assert.Equal([RiskIndexNavigationModule.Entity], RiskIndexNavigationMap.Destinations(RiskIndexObjectKind.TargetEntity));

        foreach (var vendor in new[]
                 {
                     RiskIndexObjectKind.ExternalDomain, RiskIndexObjectKind.VendorAsset, RiskIndexObjectKind.Tenant,
                     RiskIndexObjectKind.Supplier
                 })
        {
            Assert.Empty(RiskIndexNavigationMap.Destinations(vendor));
            Assert.True(RiskIndexNavigationMap.IsVendorObject(vendor));
        }

        Assert.False(RiskIndexNavigationMap.IsVendorObject(RiskIndexObjectKind.Host));
    }

    // --- §11.3 redaction as displayed -----------------------------------------------------------------------------------

    [Fact]
    public void TestARedactedObjectReadsAsKindAndIdWithThePermissionItNeeds()
    {
        var redacted = new RiskIndexObjectLabelDto(new RiskIndexObjectRef(RiskIndexObjectKind.Scenario, "12"), "Scenario #12",
            RiskIndexModulePermissions.RiskManagement);

        var shown = ObjectLabelDisplay.Of(redacted, RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(new ObjectLabelText("Cenário #12", "Requer a permissão riskmanagement"), shown);
        Assert.True(shown.IsRedacted);
        Assert.Equal("Scenario #12", ObjectLabelDisplay.Of(redacted, RiskIndexTestText.English, PtBr).Label);
    }

    [Fact]
    public void TestAReadableObjectKeepsItsLabel()
    {
        var label = new RiskIndexObjectLabelDto(new RiskIndexObjectRef(RiskIndexObjectKind.Host, "101"), "srv-01", null);

        Assert.Equal(new ObjectLabelText("srv-01", null), ObjectLabelDisplay.Of(label, RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal("—", ObjectLabelDisplay.Name(null, RiskIndexTestText.Portuguese, PtBr));
    }

    /// <summary>The panels written before T255 name objects through the same display (headline, categories, trend, list).</summary>
    [Fact]
    public void TestTheEarlierPanelsShowRedactedLabelsTheSameWay()
    {
        var redacted = new RiskIndexObjectLabelDto(new RiskIndexObjectRef(RiskIndexObjectKind.Host, "7"), "Host #7", "hosts");
        var row = new RiskIndexObjectRowDto(redacted, "Server", RiskIndexCategory.Exp, 0.5, 3, null, RiskIndexObjectState.Assessed,
            1, null, 2, false, 0, [], null);

        var text = ObjectRowFormatter.Format(row, RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(("Host #7", "Requer a permissão hosts"), (text.Label, text.LabelHint));
        var heldBy = new RiskIndexCategoryDto(RiskIndexCategory.Exp, RiskIndexCategoryState.Available, 0.5, 50, "b1", 1, 1, 10,
            new RiskIndexHeldByDto(redacted, 0.5, null, false, 1), 1, 1, null, null, null, []);
        Assert.StartsWith("Host #7", CategoryRowFormatter.HeldBy(heldBy, RiskIndexTestText.Portuguese, PtBr));
    }

    // --- the card's seals and chips open the object list (§5.2.2) --------------------------------------------------------

    [Fact]
    public void TestASealWithAListFilterOpensItAndAnUnknownFilterStaysText()
    {
        IReadOnlyList<RiskIndexSealDto> seals =
        [
            new("scenarios_above_appetite", 2, new Dictionary<string, string>(), "seal=above_appetite"),
            new("acceptances_expiring", 1, new Dictionary<string, string>(), "unknown=1"),
            new("scenarios_accepted_above_appetite", 1, new Dictionary<string, string>(), null)
        ];

        var links = HeadlineLinks.Seals(seals, RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(3, links.Count);
        Assert.Equal(("seal=above_appetite", true), (links[0].ObjectFilter, links[0].CanOpen));
        Assert.False(links[1].CanOpen);
        Assert.False(links[2].CanOpen);
        Assert.Equal(HeadlineFormatter.Seals(seals, RiskIndexTestText.Portuguese, PtBr), links.Select(l => l.Text));
    }

    [Fact]
    public void TestAChipCarriesTheFilterOfItsServerChip()
    {
        IReadOnlyList<RiskIndexChipDto> chips =
        [
            new("unassigned_above_threshold", new Dictionary<string, string>(), null, "chip=unassigned_above_threshold"),
            new("no_critical_process", new Dictionary<string, string>(), null, null)
        ];

        var links = HeadlineLinks.Chips(chips, [], RiskIndexTestText.Portuguese, PtBr);

        Assert.True(links.Single(l => l.Key == "unassigned_above_threshold").CanOpen);
        Assert.False(links.Single(l => l.Key == "no_critical_process").CanOpen);
    }
}

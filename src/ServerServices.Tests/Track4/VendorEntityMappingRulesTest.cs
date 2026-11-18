using DAL.Enums;
using JetBrains.Annotations;
using Model.Integrations;
using ServerServices.Integrations.EntityMappings;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// T263 (S40 §6.6 "Regras de aplicação"): the pure rules the Vision One sync and the preview share — lowest priority wins,
/// an asset-group rule never attributes a device, the restamp policy decides whether an attributed host moves — and the
/// canonical tag spelling both sides compare.
/// </summary>
[TestSubject(typeof(VendorEntityMappingRules))]
public class VendorEntityMappingRulesTest
{
    private static MappingRule Tag(int id, string value, int entity, int priority,
        RestampPolicy policy = RestampPolicy.OnlyUnassigned) =>
        new(id, VendorMatchKind.V1CustomTag, value, entity, priority, policy);

    [Fact]
    public void TestTheLowestPriorityThatMatchesWins()
    {
        var ordered = VendorEntityMappingRules.Order([Tag(1, "BU: Law", 10, 5), Tag(2, "Site: Rio", 20, 2)]);

        var winner = VendorEntityMappingRules.Winner(ordered, ["bu: law", "Site: Rio"]);

        Assert.Equal(2, winner!.Id);
    }

    [Fact]
    public void TestAPriorityTieGoesToTheOlderRule()
    {
        var ordered = VendorEntityMappingRules.Order([Tag(9, "BU: Law", 10, 1), Tag(3, "Site: Rio", 20, 1)]);

        Assert.Equal(3, VendorEntityMappingRules.Winner(ordered, ["BU: Law", "Site: Rio"])!.Id);
    }

    [Fact]
    public void TestAnAssetGroupRuleNeverAttributesADevice()
    {
        var group = new MappingRule(1, VendorMatchKind.V1AssetGroup, "Servers", 10, 1, RestampPolicy.FollowMapping);

        Assert.Empty(VendorEntityMappingRules.Order([group]));
        Assert.Null(VendorEntityMappingRules.Winner([group], ["Servers"]));
        Assert.False(VendorEntityMappingRules.Matches(group, ["Servers"]));
    }

    [Fact]
    public void TestNoTagsNoWinner()
    {
        Assert.Null(VendorEntityMappingRules.Winner([Tag(1, "BU: Law", 10, 1)], []));
        Assert.Null(VendorEntityMappingRules.Winner([Tag(1, "BU: Law", 10, 1)], ["BU: Medicine"]));
    }

    [Theory]
    [InlineData(null, RestampPolicy.OnlyUnassigned, MappingOutcome.Assign, 10)]
    [InlineData(null, RestampPolicy.FollowMapping, MappingOutcome.Assign, 10)]
    [InlineData(10, RestampPolicy.OnlyUnassigned, MappingOutcome.Keep, 10)]
    [InlineData(10, RestampPolicy.FollowMapping, MappingOutcome.Keep, 10)]
    [InlineData(20, RestampPolicy.OnlyUnassigned, MappingOutcome.Keep, 20)]
    [InlineData(20, RestampPolicy.FollowMapping, MappingOutcome.Reassign, 10)]
    public void TestTheRestampPolicyDecidesWhetherAnAttributedHostMoves(int? current, RestampPolicy policy,
        MappingOutcome expected, int after)
    {
        var rule = Tag(1, "BU: Law", 10, 1, policy);

        Assert.Equal(expected, VendorEntityMappingRules.Decide(current, rule));
        Assert.Equal(after, VendorEntityMappingRules.Apply(current, rule));
    }

    [Theory]
    [InlineData("  BU:Law ", "BU: Law")]
    [InlineData("BU :   Escola  de Direito", "BU: Escola de Direito")]
    [InlineData("Production", "Production")]
    [InlineData(":Production", "Production")]
    [InlineData("BU:", "BU")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void TestTheCanonicalSpellingOfATag(string? raw, string? expected)
    {
        Assert.Equal(expected, VendorTagText.Canonical(raw));
    }

    [Fact]
    public void TestAKeyValueTagReadsAsOneString()
    {
        Assert.Equal("BU: Law", VendorTagText.FromKeyValue(" BU ", "Law"));
        Assert.Equal("Law", VendorTagText.FromKeyValue(null, "Law"));
        Assert.True(VendorTagText.Same("bu:law", "BU: Law"));
        Assert.False(VendorTagText.Same("BU: Law", "BU: Lawyers"));
        Assert.False(VendorTagText.Same(null, null));
    }
}

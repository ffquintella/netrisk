using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using ServerServices.Integrations.EntityMappings;
using Xunit;

namespace ServerServices.Tests.Track4;

/// <summary>
/// M58, T282 (S39 D-20): the per-connection re-stamp policy and the writer precedence it shares with the tag rules. A
/// manual assignment is never moved; a FollowMapping writer moves only what it outranks.
/// </summary>
[TestSubject(typeof(HostEntityRestamp))]
public class HostEntityRestampTest
{
    private const int Target = 7, Other = 9;

    [Fact]
    public void TestThePrecedenceOrder()
    {
        Assert.True(EntitySources.Rank(EntitySources.Manual) > EntitySources.Rank(EntitySources.Cmdb));
        Assert.True(EntitySources.Rank(EntitySources.Cmdb) > EntitySources.Rank(EntitySources.Rule));
        Assert.True(EntitySources.Rank(EntitySources.Rule) > EntitySources.Rank(EntitySources.VisionOne));
        Assert.True(EntitySources.Rank(EntitySources.VisionOne) > EntitySources.Rank(EntitySources.Import));
        Assert.Equal(EntitySources.Rank(null), EntitySources.Rank(EntitySources.Import));
    }

    [Theory]
    // No target: the connection attributes nothing.
    [InlineData(null, null, null, RestampPolicy.FollowMapping, EntitySources.VisionOne, MappingOutcome.Keep)]
    // A host with no entity takes the target under either policy.
    [InlineData(null, null, Target, RestampPolicy.OnlyUnassigned, EntitySources.VisionOne, MappingOutcome.Assign)]
    [InlineData(null, null, Target, RestampPolicy.FollowMapping, EntitySources.Cmdb, MappingOutcome.Assign)]
    // Already there: nothing to do.
    [InlineData(Target, EntitySources.Manual, Target, RestampPolicy.FollowMapping, EntitySources.Cmdb, MappingOutcome.Keep)]
    // OnlyUnassigned never moves an attributed host.
    [InlineData(Other, null, Target, RestampPolicy.OnlyUnassigned, EntitySources.Cmdb, MappingOutcome.Keep)]
    // FollowMapping moves what it outranks or equals ...
    [InlineData(Other, null, Target, RestampPolicy.FollowMapping, EntitySources.VisionOne, MappingOutcome.Reassign)]
    [InlineData(Other, EntitySources.Import, Target, RestampPolicy.FollowMapping, EntitySources.VisionOne, MappingOutcome.Reassign)]
    [InlineData(Other, EntitySources.VisionOne, Target, RestampPolicy.FollowMapping, EntitySources.VisionOne, MappingOutcome.Reassign)]
    [InlineData(Other, EntitySources.Rule, Target, RestampPolicy.FollowMapping, EntitySources.Cmdb, MappingOutcome.Reassign)]
    // ... and never a writer above it, a person above all.
    [InlineData(Other, EntitySources.Manual, Target, RestampPolicy.FollowMapping, EntitySources.Cmdb, MappingOutcome.Keep)]
    [InlineData(Other, EntitySources.Cmdb, Target, RestampPolicy.FollowMapping, EntitySources.VisionOne, MappingOutcome.Keep)]
    [InlineData(Other, EntitySources.Rule, Target, RestampPolicy.FollowMapping, EntitySources.VisionOne, MappingOutcome.Keep)]
    public void TestTheConnectionPolicy(int? current, string? source, int? target, RestampPolicy policy, string writer,
        MappingOutcome expected)
    {
        Assert.Equal(expected, HostEntityRestamp.Decide(current, source, target, policy, writer));
    }

    [Fact]
    public void TestApplyingRecordsTheWriterOnlyWhenTheHostMoves()
    {
        var moved = new Host { EntityId = Other, EntitySource = EntitySources.Import };
        Assert.Equal(MappingOutcome.Reassign,
            HostEntityRestamp.ApplyTo(moved, Target, RestampPolicy.FollowMapping, EntitySources.Cmdb));
        Assert.Equal(((int?)Target, EntitySources.Cmdb), (moved.EntityId, moved.EntitySource));

        var kept = new Host { EntityId = Other, EntitySource = EntitySources.Manual };
        Assert.Equal(MappingOutcome.Keep,
            HostEntityRestamp.ApplyTo(kept, Target, RestampPolicy.FollowMapping, EntitySources.Cmdb));
        Assert.Equal(((int?)Other, EntitySources.Manual), (kept.EntityId, kept.EntitySource));
    }

    /// <summary>
    /// The tag rules share the precedence (T263 rules gained it here): a FollowMapping rule used to move any host,
    /// including one a person had assigned.
    /// </summary>
    [Theory]
    [InlineData(null, MappingOutcome.Reassign)]
    [InlineData(EntitySources.VisionOne, MappingOutcome.Reassign)]
    [InlineData(EntitySources.Rule, MappingOutcome.Reassign)]
    [InlineData(EntitySources.Cmdb, MappingOutcome.Keep)]
    [InlineData(EntitySources.Manual, MappingOutcome.Keep)]
    public void TestAFollowMappingRuleRespectsTheWriterPrecedence(string? source, MappingOutcome expected)
    {
        var rule = new MappingRule(1, VendorMatchKind.V1CustomTag, "BU: Law", Target, 1, RestampPolicy.FollowMapping);

        Assert.Equal(expected, VendorEntityMappingRules.Decide(Other, rule, source));

        var host = new Host { EntityId = Other, EntitySource = source };
        VendorEntityMappingRules.ApplyTo(host, rule);
        Assert.Equal(expected == MappingOutcome.Keep ? Other : Target, host.EntityId);
        Assert.Equal(expected == MappingOutcome.Keep ? source : EntitySources.Rule, host.EntitySource);
    }
}

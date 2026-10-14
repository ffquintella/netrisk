using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using DAL.Context;
using JetBrains.Annotations;
using Model.Entities;
using ServerServices.Security;
using Xunit;

namespace ServerServices.Tests.Security;

/// <summary>
/// The pure parts of T292: the tree snapshot that turns claims into a hierarchical scope (<see cref="EntityTree"/>),
/// the cache that holds it (<see cref="EntityHierarchy"/>), and T241's risk-context change detection
/// (<see cref="RiskContextProperties"/>). The end-to-end behaviour is <c>EntityScopeHierarchyTest</c>.
/// </summary>
[TestSubject(typeof(EntityTree))]
public class EntityTreeTest
{
    //   1 ─┬─ 10 ── 11 ── 12
    //      └─ 20 ── 21
    private static readonly EntityTreeRow[] Tree =
    [
        new(1, null), new(10, 1), new(11, 10), new(12, 11), new(20, 1), new(21, 20)
    ];

    private static EntityTree Build(params EntityTreeRow[] rows) => EntityTree.Build(rows, 1);

    [Fact]
    public void TestAClaimGrantsItselfAndEveryDescendantOnly()
    {
        var scope = Build(Tree).ScopeFor([10]);

        Assert.Equal(new[] { 10 }, scope.ClaimedEntityIds);
        Assert.Equal(new[] { 10, 11, 12 }, scope.EntityIds.Order());
        Assert.False(scope.Allows(1));
        Assert.False(scope.Allows(20));
        Assert.True(scope.Allows(12));
    }

    [Fact]
    public void TestNoClaimIsDenyAll()
    {
        Assert.Same(EntityScope.DenyAll, Build(Tree).ScopeFor([]));
    }

    [Fact]
    public void TestAClaimTheSnapshotDoesNotHoldStillGrantsItself()
    {
        // An entity created after the snapshot loaded: the claim is honoured, as the flat scope honoured it.
        Assert.Equal(new[] { 99 }, Build(Tree).ScopeFor([99]).EntityIds);
    }

    [Fact]
    public void TestOverlappingClaimsAreCountedOnce()
    {
        var scope = Build(Tree).ScopeFor([10, 11, 11]);

        Assert.Equal(new[] { 10, 11 }, scope.ClaimedEntityIds);
        Assert.Equal(new[] { 10, 11, 12 }, scope.EntityIds.Order());
    }

    [Fact]
    public void TestASelfParentIsMalformedAndGrantsOnlyItself()
    {
        var tree = Build([..Tree, new(30, 30), new(31, 30)]);

        Assert.Equal(new[] { 30, 31 }, tree.MalformedIds.Order());
        Assert.Equal(new[] { 30 }, tree.ScopeFor([30]).EntityIds);
        Assert.Equal(new[] { 31 }, tree.ScopeFor([31]).EntityIds);
    }

    [Fact]
    public void TestACycleNeitherHangsNorGrantsItsOtherMembers()
    {
        // 40 → 41 → 42 → 40, and 43 hangs below the cycle.
        var tree = Build([..Tree, new(40, 42), new(41, 40), new(42, 41), new(43, 41)]);

        Assert.Equal(new[] { 40, 41, 42, 43 }, tree.MalformedIds.Order());
        foreach (var member in new[] { 40, 41, 42, 43 })
            Assert.Equal(new[] { member }, tree.ScopeFor([member]).EntityIds);

        // The well-formed part is untouched.
        Assert.Equal(new[] { 10, 11, 12 }, tree.ScopeFor([10]).EntityIds.Order());
    }

    [Fact]
    public void TestAParentWithNoRowIsARoot()
    {
        var tree = Build([new(50, 999), new(51, 50)]);

        Assert.Empty(tree.MalformedIds);
        Assert.Equal(new[] { 50, 51 }, tree.ScopeFor([50]).EntityIds.Order());
    }

    [Fact]
    public void TestADeepChainIsExpandedWithoutRecursion()
    {
        const int depth = 50_000;
        var rows = Enumerable.Range(1, depth).Select(i => new EntityTreeRow(i, i == 1 ? null : i - 1)).ToArray();

        var tree = Build(rows);

        Assert.Equal(depth, tree.ScopeFor([1]).EntityIds.Count);
        Assert.True(tree.IsSelfOrDescendant(depth, 1));
    }

    [Fact]
    public void TestIsSelfOrDescendantWalksUpOnly()
    {
        var tree = Build(Tree);

        Assert.True(tree.IsSelfOrDescendant(12, 10));
        Assert.True(tree.IsSelfOrDescendant(10, 10));
        Assert.False(tree.IsSelfOrDescendant(10, 12));
        Assert.False(tree.IsSelfOrDescendant(21, 10));
    }

    [Fact]
    public void TestTheScopeOfAClaimSetIsMemoisedPerSnapshot()
    {
        var tree = Build(Tree);

        Assert.Same(tree.ScopeFor([11, 10]), tree.ScopeFor([10, 11]));
        Assert.NotSame(tree.ScopeFor([10]), Build(Tree).ScopeFor([10]));
    }

    [Fact]
    public void TestAFlatScopeIsUnchanged()
    {
        var scope = EntityScope.ForEntities([10, 20]);

        Assert.Equal(scope.EntityIds, scope.ClaimedEntityIds);
        Assert.Equal("10,20", scope.ToString());
        Assert.Equal("10 (+2 descendants)", Build(Tree).ScopeFor([10]).ToString());
    }

    // --- the cache -------------------------------------------------------------------------------------------

    [TestSubject(typeof(EntityHierarchy))]
    public class Cache
    {
        private DateTime _now = new(2026, 10, 13, 12, 0, 0, DateTimeKind.Utc);
        private int _loads;

        private IReadOnlyCollection<EntityTreeRow> Load()
        {
            _loads++;
            return Tree;
        }

        private EntityHierarchy New() => new(TimeSpan.FromSeconds(30), () => _now);

        [Fact]
        public void TestTheTreeIsLoadedOnceWithinItsTimeToLive()
        {
            var hierarchy = New();

            var first = hierarchy.GetTree(Load);
            _now = _now.AddSeconds(29);

            Assert.Same(first, hierarchy.GetTree(Load));
            Assert.Equal(1, _loads);
        }

        [Fact]
        public void TestTheTreeIsReloadedAfterItsTimeToLive()
        {
            var hierarchy = New();

            var first = hierarchy.GetTree(Load);
            _now = _now.AddSeconds(30);

            Assert.NotSame(first, hierarchy.GetTree(Load));
            Assert.Equal(2, _loads);
        }

        [Fact]
        public void TestAnInvalidationForcesAReloadAndMovesTheRevision()
        {
            var hierarchy = New();

            var first = hierarchy.GetTree(Load);
            var revision = hierarchy.Revision;
            hierarchy.Invalidate();

            Assert.True(hierarchy.Revision > revision);
            Assert.NotSame(first, hierarchy.GetTree(Load));
            Assert.Equal(2, _loads);
        }

        /// <summary>A load that an invalidation overtakes is used once but not kept: it may predate the change.</summary>
        [Fact]
        public void TestALoadRacedByAnInvalidationIsNotKept()
        {
            var hierarchy = New();

            hierarchy.GetTree(() =>
            {
                hierarchy.Invalidate(); // the re-parent lands while the rows are being read
                return Load();
            });
            hierarchy.GetTree(Load);

            Assert.Equal(2, _loads);
        }

        [Fact]
        public void TestConcurrentReadersShareOneLoad()
        {
            var hierarchy = New();
            using var gate = new ManualResetEventSlim();

            var threads = Enumerable.Range(0, 8).Select(_ => new Thread(() =>
            {
                gate.Wait();
                hierarchy.GetTree(() =>
                {
                    Interlocked.Increment(ref _loads);
                    Thread.Sleep(20);
                    return Tree;
                });
            })).ToList();

            threads.ForEach(t => t.Start());
            gate.Set();
            threads.ForEach(t => t.Join());

            Assert.Equal(1, _loads);
        }
    }

    // --- T241: what counts as a risk-context change ------------------------------------------------------------

    [TestSubject(typeof(RiskContextProperties))]
    public class RiskContextChanges
    {
        private static (string, string?) P(string key, string? value) => (key, value);

        [Fact]
        public void TestTheThreeNamesAreTheRiskContext()
        {
            Assert.Equal(new[] { "criticality", "internetFacing", "securityClassification" },
                RiskContextProperties.Names.Order(StringComparer.Ordinal));
            Assert.False(RiskContextProperties.IsRiskContext("name"));
            Assert.False(RiskContextProperties.IsRiskContext("Criticality"));
        }

        [Fact]
        public void TestAnUnchangedEchoIsNoChange()
        {
            (string, string?)[] stored = [P("criticality", "3"), P("name", "a"), P("internetFacing", "True")];
            (string, string?)[] incoming = [P("name", "renamed"), P("internetFacing", " true "), P("criticality", "3")];

            Assert.Empty(RiskContextProperties.Changed(stored, incoming));
        }

        [Fact]
        public void TestSettingChangingAndClearingAreChanges()
        {
            Assert.Equal(new[] { "criticality" }, RiskContextProperties.Changed([], [P("criticality", "4")]));
            Assert.Equal(new[] { "criticality" }, RiskContextProperties.Changed([P("criticality", "3")], [P("criticality", "4")]));
            Assert.Equal(new[] { "securityClassification" },
                RiskContextProperties.Changed([P("securityClassification", "7")], []));
        }

        [Fact]
        public void TestABlankValueIsTheSameAsAbsent()
        {
            Assert.Empty(RiskContextProperties.Changed([], [P("criticality", ""), P("internetFacing", null)]));
        }

        [Fact]
        public void TestOtherPropertiesNeverCount()
        {
            Assert.Empty(RiskContextProperties.Changed([P("name", "a")], [P("name", "b"), P("organizationUnit", "10")]));
        }
    }
}

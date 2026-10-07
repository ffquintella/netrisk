using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Tools.Risks;
using Xunit;

namespace Tools.Tests.Risks;

/// <summary>
/// Stage 9.1 (S41 §8, K1–K8) — the critical-process coverage metric of Phase 7.
///
/// The two properties the methodology names for this stage are here: the metric counts processes
/// <b>marked</b> critical, not all of them (K1, K3, K4), and an empty denominator is "not computable",
/// not 0 % and not 100 % (K2).
/// </summary>
[TestSubject(typeof(CriticalProcessCoverageCalculator))]
public class CriticalProcessCoverageCalculatorTest
{
    private static readonly DateTime Now = new(2026, 10, 6, 12, 0, 0, DateTimeKind.Utc);

    private static KeyValuePair<string, string> P(string type, string value) => new(type, value);

    private static RiskChainNode Process(int id, string? criticality, bool? active = true,
        params KeyValuePair<string, string>[] more)
    {
        var properties = new List<KeyValuePair<string, string>> { P("name", $"Process {id}") };
        if (criticality is not null) properties.Add(P("criticality", criticality));
        if (active is not null) properties.Add(P("isActive", active.Value ? "True" : "False"));
        properties.AddRange(more);
        return new RiskChainNode(id, "businessProcess", null, properties);
    }

    private static RiskChainNode Node(int id, string definition, int? parent = null,
        params KeyValuePair<string, string>[] properties) =>
        new(id, definition, parent, properties.Append(P("name", $"{definition}-{id}")));

    private static Model.Risks.Chain.CriticalProcessCoverageDto Compute(IEnumerable<RiskChainNode> nodes,
        IEnumerable<(int RiskId, int EntityId)> links, IEnumerable<int>? open = null, bool restricted = false,
        IReadOnlyDictionary<int, int>? biaMtpd = null)
    {
        var linkList = links.ToList();
        var openSet = (open ?? linkList.Select(l => l.RiskId)).ToHashSet();
        return CriticalProcessCoverageCalculator.Compute(new RiskChainGraph(nodes), linkList, openSet, Now, restricted,
            biaMtpd);
    }

    /// <summary>K1 — only critical processes enter: criticalities 5, 4 and 2, all with risks, give a
    /// denominator of two.</summary>
    [Fact]
    public void TestOnlyCriticalProcessesEnterTheDenominator()
    {
        var result = Compute([Process(10, "5"), Process(11, "4"), Process(12, "2")],
            [(1, 10), (2, 11), (3, 12)]);

        Assert.Equal(2, result.CriticalProcessCount);
        Assert.Equal(2, result.CoveredCount);
        Assert.Equal(1m, result.CoverageRatio);
        Assert.Equal(new[] { 10, 11 }, result.Rows.Select(r => r.ProcessId).ToArray());
        Assert.Equal(4, result.Threshold);
        Assert.Equal(Now, result.ComputedAt);
    }

    /// <summary>K2 — no critical process: the ratio is null, neither 0 % nor 100 %.</summary>
    [Fact]
    public void TestNoCriticalProcessIsNotComputable()
    {
        var result = Compute([Process(10, "2"), Process(11, "3")], [(1, 10)]);

        Assert.Equal(0, result.CriticalProcessCount);
        Assert.Equal(0, result.CoveredCount);
        Assert.Null(result.CoverageRatio);
        Assert.Empty(result.Rows);

        var empty = Compute([], []);
        Assert.Null(empty.CoverageRatio);
    }

    /// <summary>K3 — a critical process that is inactive is out of the metric entirely.</summary>
    [Fact]
    public void TestAnInactiveCriticalProcessIsLeftOut()
    {
        var result = Compute([Process(10, "5", active: false), Process(11, "5"), Process(12, null, active: false)],
            [(1, 10)]);

        Assert.Equal(1, result.CriticalProcessCount);
        Assert.Equal(0, result.CoveredCount);
        Assert.Equal(11, Assert.Single(result.Rows).ProcessId);
        Assert.Equal(0, result.ProcessesWithoutCriticality);
    }

    [Fact]
    public void TestAProcessWithNoIsActivePropertyTakesTheSchemaDefaultOfActive()
    {
        var result = Compute([Process(10, "5", active: null)], [(1, 10)]);

        Assert.Equal(1, result.CriticalProcessCount);
        Assert.Equal(1, result.CoveredCount);
    }

    /// <summary>K4 — the parse: absent or invalid is "without criticality"; 4 and 5 are critical; 1–3
    /// are declared and not critical.</summary>
    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("  ", null)]
    [InlineData("0", null)]
    [InlineData("6", null)]
    [InlineData("-1", null)]
    [InlineData("abc", null)]
    [InlineData("4.5", null)]
    [InlineData("1", 1)]
    [InlineData("2", 2)]
    [InlineData("3", 3)]
    [InlineData("4", 4)]
    [InlineData("5", 5)]
    public void TestCriticalityParsing(string? raw, int? expected)
    {
        Assert.Equal(expected, CriticalProcessCoverageCalculator.ParseCriticality(raw));
    }

    [Fact]
    public void TestInvalidCriticalitiesAreCountedApartAndOutOfTheDenominator()
    {
        var result = Compute(
        [
            Process(1, null), Process(2, ""), Process(3, "0"), Process(4, "6"), Process(5, "abc"),
            Process(6, "4"), Process(7, "5"), Process(8, "1"), Process(9, "2"), Process(10, "3")
        ], [(1, 6)]);

        Assert.Equal(5, result.ProcessesWithoutCriticality);
        Assert.Equal(2, result.CriticalProcessCount);
        Assert.Equal(new[] { 7, 6 }, result.Rows.Select(r => r.ProcessId).ToArray());
        Assert.Equal(0.5m, result.CoverageRatio);
    }

    /// <summary>K5 — coverage by a direct link, by a service, by an application with no service, and by
    /// an activity.</summary>
    [Fact]
    public void TestCoverageByEachKindOfLink()
    {
        var result = Compute(
        [
            Process(10, "5"),
            Process(11, "5"), Node(21, "itService", null, P("processes", "11")),
            Process(12, "5", true, P("applications", "32")), Node(32, "application"),
            Process(13, "5"), Node(43, "activity", 13)
        ],
        [(1, 10), (2, 21), (3, 32), (4, 43)]);

        Assert.Equal(4, result.CoveredCount);

        var direct = result.Rows.Single(r => r.ProcessId == 10);
        Assert.Equal((1, 0), (direct.DirectOpenRiskCount, direct.InferredOpenRiskCount));

        foreach (var id in new[] { 11, 12, 13 })
        {
            var row = result.Rows.Single(r => r.ProcessId == id);
            Assert.True(row.Covered);
            Assert.Equal((0, 1), (row.DirectOpenRiskCount, row.InferredOpenRiskCount));
        }
    }

    /// <summary>K6 — a risk linked to the process and to the service serving it counts once, as direct.</summary>
    [Fact]
    public void TestARiskLinkedBothWaysCountsOnceAsDirect()
    {
        var result = Compute(
            [Process(10, "4"), Node(20, "itService", null, P("processes", "10"))],
            [(1, 10), (1, 20), (2, 20)]);

        var row = Assert.Single(result.Rows);
        Assert.Equal(1, row.DirectOpenRiskCount);
        Assert.Equal(1, row.InferredOpenRiskCount);
    }

    /// <summary>K7 — a critical process whose only service is a dangling reference is not covered; a
    /// link to an entity that is not in the graph is ignored.</summary>
    [Fact]
    public void TestADanglingServiceDoesNotCover()
    {
        var result = Compute(
            [Process(10, "5"), Node(20, "itService", null, P("processes", "404"))],
            [(1, 20), (2, 405)]);

        var row = Assert.Single(result.Rows);
        Assert.False(row.Covered);
        Assert.Equal(0, result.CoveredCount);
        Assert.Equal(0m, result.CoverageRatio);
    }

    /// <summary>K8 — the ratio is the exact decimal quotient: one of three is 1/3, not 0.33.</summary>
    [Fact]
    public void TestTheRatioIsExact()
    {
        var result = Compute([Process(10, "5"), Process(11, "5"), Process(12, "4")], [(1, 10)]);

        Assert.Equal(3, result.CriticalProcessCount);
        Assert.Equal(1, result.CoveredCount);
        Assert.Equal(1m / 3m, result.CoverageRatio);
    }

    /// <summary>Only open risks cover: the open set is what the service builds from <c>status &lt;&gt; 'Closed'</c>.</summary>
    [Fact]
    public void TestAClosedRiskDoesNotCover()
    {
        var result = Compute([Process(10, "5")], [(1, 10)], open: []);

        Assert.False(Assert.Single(result.Rows).Covered);
    }

    [Fact]
    public void TestTheScopeFlagIsCarriedThrough()
    {
        Assert.True(Compute([], [], restricted: true).IsScopeRestricted);
        Assert.False(Compute([], [], restricted: false).IsScopeRestricted);
    }

    // --- Stage 9.3 (S43 §8, K9–K12): the effective criticality ---------------------------------

    /// <summary>K9 — a BIA MTPD of two hours makes a process declared 2 critical, with source BIA.</summary>
    [Fact]
    public void TestK9_ABiaMtpdMakesALowDeclaredProcessCritical()
    {
        var result = Compute([Process(10, "2")], [(1, 10)], biaMtpd: new Dictionary<int, int> { [10] = 120 });

        var row = Assert.Single(result.Rows);
        Assert.Equal(5, row.Criticality);
        Assert.Equal(Model.Continuity.CriticalitySource.Bia, row.CriticalitySource);
        Assert.Equal(1, result.CriticalProcessCount);
    }

    /// <summary>K10 — a process declared 5 with an MTPD of ten days is not critical: the BIA wins.</summary>
    [Fact]
    public void TestK10_ALongBiaMtpdOverridesAHighDeclaredCriticality()
    {
        var result = Compute([Process(10, "5")], [(1, 10)], biaMtpd: new Dictionary<int, int> { [10] = 14_400 });

        Assert.Empty(result.Rows);
        Assert.Equal(0, result.CriticalProcessCount);
        Assert.Null(result.CoverageRatio);
        Assert.Equal(0, result.ProcessesWithoutCriticality);
    }

    /// <summary>K11 — a process with no MTPD in the map (a BIA with only RTO/RPO, or none) falls back
    /// to its declared criticality, with source Declared.</summary>
    [Fact]
    public void TestK11_WithoutAnMtpdTheDeclaredCriticalityApplies()
    {
        var result = Compute([Process(10, "4"), Process(11, "5")], [(1, 10)],
            biaMtpd: new Dictionary<int, int> { [11] = 60 });

        var declared = result.Rows.Single(r => r.ProcessId == 10);
        Assert.Equal(4, declared.Criticality);
        Assert.Equal(Model.Continuity.CriticalitySource.Declared, declared.CriticalitySource);
        Assert.Equal(Model.Continuity.CriticalitySource.Bia, result.Rows.Single(r => r.ProcessId == 11).CriticalitySource);
    }

    /// <summary>K12 — neither an MTPD nor a valid declared criticality: "without criticality", outside
    /// the denominator; an MTPD alone is enough to leave that group.</summary>
    [Fact]
    public void TestK12_NeitherMtpdNorDeclaredIsWithoutCriticality()
    {
        var result = Compute([Process(10, null), Process(11, "abc"), Process(12, null)], [],
            biaMtpd: new Dictionary<int, int> { [12] = 3_000 });

        Assert.Equal(2, result.ProcessesWithoutCriticality);
        Assert.Equal(0, result.CriticalProcessCount);
    }
}

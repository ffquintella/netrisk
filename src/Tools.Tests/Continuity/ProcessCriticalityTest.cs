using JetBrains.Annotations;
using Model.Continuity;
using Tools.Continuity;
using Xunit;

namespace Tools.Tests.Continuity;

/// <summary>
/// Stage 9.3 (S43 §8, PC1–PC6) — a process's criticality from its BIA's MTPD, by the S39 §5.2 mapping.
/// The case the methodology names: an absent MTPD is neither 0 (criticality 5) nor infinite
/// (criticality 1) — PC5.
/// </summary>
[TestSubject(typeof(ProcessCriticality))]
public class ProcessCriticalityTest
{
    /// <summary>PC1 — the bounds are inclusive: 4 h, 24 h, 72 h and 7 d each belong to the higher band.</summary>
    [Theory]
    [InlineData(240, 5)]
    [InlineData(241, 4)]
    [InlineData(1_440, 4)]
    [InlineData(1_441, 3)]
    [InlineData(4_320, 3)]
    [InlineData(4_321, 2)]
    [InlineData(10_080, 2)]
    [InlineData(10_081, 1)]
    [InlineData(525_600, 1)]
    public void TestPC1_TheMappingBoundsAreInclusive(int mtpd, int expected)
    {
        Assert.Equal(expected, ProcessCriticality.FromMtpd(mtpd));
    }

    /// <summary>PC2 — an MTPD of 0 is a value — no tolerance at all — and maps to 5.</summary>
    [Fact]
    public void TestPC2_AnMtpdOfZeroIsTheMostCritical()
    {
        Assert.Equal((5, CriticalitySource.Bia), ProcessCriticality.Resolve(0, null));
    }

    /// <summary>PC3 — a declared MTPD wins over a divergent declared criticality.</summary>
    [Theory]
    [InlineData(120, "1", 5)]
    [InlineData(20_000, "5", 1)]
    public void TestPC3_TheBiaWinsOverTheDeclaredProperty(int mtpd, string declared, int expected)
    {
        Assert.Equal((expected, CriticalitySource.Bia), ProcessCriticality.Resolve(mtpd, declared));
    }

    /// <summary>PC4 — without an MTPD, the declared property applies, labelled as such.</summary>
    [Fact]
    public void TestPC4_WithoutAnMtpdTheDeclaredValueApplies()
    {
        Assert.Equal((3, CriticalitySource.Declared), ProcessCriticality.Resolve(null, "3"));
    }

    /// <summary>
    /// PC5 (the methodology's case) — no MTPD and no valid declared value: nothing. Not 5, which reading
    /// the absent MTPD as 0 would give, and not 1, which reading it as infinite would give.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("abc")]
    [InlineData("6")]
    public void TestPC5_AbsentIsNeitherFiveNorOne(string? declared)
    {
        var (value, source) = ProcessCriticality.Resolve(null, declared);

        Assert.Null(value);
        Assert.Null(source);
    }

    /// <summary>PC6 — the BIA owns the criticality exactly when it declares an MTPD (the predicate
    /// T241/M52 uses for <c>409 criticality_owned_by_bia</c>).</summary>
    [Fact]
    public void TestPC6_TheBiaOwnsTheCriticalityOnlyWithAnMtpd()
    {
        Assert.True(ProcessCriticality.IsOwnedByBia(0));
        Assert.True(ProcessCriticality.IsOwnedByBia(600));
        Assert.False(ProcessCriticality.IsOwnedByBia(null));
    }
}

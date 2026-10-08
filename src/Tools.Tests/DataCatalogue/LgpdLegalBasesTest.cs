using System;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Tools.DataCatalogue;
using Xunit;

namespace Tools.Tests.DataCatalogue;

/// <summary>
/// Stage 9.11 (S52 §4.3, §8 B1–B3) — the LGPD legal bases: every value names its article and inciso, sensitive data admits
/// exactly the eight of art. 11, and the two questions the findings ask (legal obligation, legitimate interest).
/// </summary>
[TestSubject(typeof(LgpdLegalBases))]
public class LgpdLegalBasesTest
{
    /// <summary>B1 — every defined basis has its citation, and an undefined value is refused.</summary>
    [Fact]
    public void TestB1_EveryBasisNamesItsArticle()
    {
        var all = Enum.GetValues<LgpdLegalBasis>();
        Assert.Equal(18, all.Length);
        Assert.All(all, b => Assert.StartsWith("LGPD art. ", LgpdLegalBases.ArticleOf(b)));
        Assert.Equal(18, all.Select(LgpdLegalBases.ArticleOf).Distinct().Count());

        Assert.Equal("LGPD art. 7º, IX", LgpdLegalBases.ArticleOf(LgpdLegalBasis.Art7LegitimateInterest));
        Assert.Equal("LGPD art. 11, II, g", LgpdLegalBases.ArticleOf(LgpdLegalBasis.Art11FraudPrevention));
        Assert.Throws<ArgumentOutOfRangeException>(() => LgpdLegalBases.ArticleOf((LgpdLegalBasis)19));
    }

    /// <summary>B2 — the bases sensitive data admits are the eight of art. 11, and only those.</summary>
    [Fact]
    public void TestB2_TheArticle11BasesAreEightAndOnlyThose()
    {
        var article11 = Enum.GetValues<LgpdLegalBasis>().Where(LgpdLegalBases.IsArticle11).ToList();

        Assert.Equal(8, article11.Count);
        Assert.All(article11, b => Assert.StartsWith("LGPD art. 11", LgpdLegalBases.ArticleOf(b)));
        Assert.All(Enum.GetValues<LgpdLegalBasis>().Except(article11),
            b => Assert.StartsWith("LGPD art. 7º", LgpdLegalBases.ArticleOf(b)));
    }

    /// <summary>B3 — a legal obligation under either article; legitimate interest is art. 7º, IX alone.</summary>
    [Fact]
    public void TestB3_LegalObligationAndLegitimateInterest()
    {
        Assert.Equal([LgpdLegalBasis.Art7LegalObligation, LgpdLegalBasis.Art11LegalObligation],
            Enum.GetValues<LgpdLegalBasis>().Where(LgpdLegalBases.IsLegalObligation));
        Assert.Equal([LgpdLegalBasis.Art7LegitimateInterest],
            Enum.GetValues<LgpdLegalBasis>().Where(LgpdLegalBases.IsLegitimateInterest));
    }
}

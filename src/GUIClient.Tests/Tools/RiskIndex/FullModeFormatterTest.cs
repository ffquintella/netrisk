using System;
using System.Globalization;
using System.Linq;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// M59 (S40 §5.3, §5.10–§5.12; T286, T287): the full-mode panels read as the spec wrote them, from the server's figures only
/// — Top Risks with Gate A and the next decision, the monetary panel with "n de N" and the portfolio tail, M40's confidence
/// beside the quality, the trend's reference lines — and every key they compute is declared in all three resource files.
/// </summary>
[TestSubject(typeof(FullModeFormatter))]
public class FullModeFormatterTest
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TextLookup Pt = RiskIndexTestText.Portuguese;
    private static readonly RiskIndexNodeRef Unit = new("e:10", "Unidade X", "organizationUnit");
    private static readonly DateOnly D = new(2026, 10, 5);

    private static RiskIndexTopRiskDto Item(int rank, bool gateA, int[] conditions, int? confidence = 1) =>
        new(rank, new RiskIndexObjectLabelDto(new RiskIndexObjectRef(DAL.Enums.RiskIndexObjectKind.Scenario, rank.ToString()),
                "R" + rank, null), gateA, conditions, 1, 8.0, 6.4, 1_240_000, 1, confidence, "Rising", "GateAEscalation",
            null);

    [Fact]
    public void TestTopRisksReadsGateAWithItsFlagsAndTheNextDecision()
    {
        var dto = new RiskIndexTopRisksDto(Unit, D, true, null, 2,
            [Item(1, true, [2, 12]), Item(2, false, [], confidence: null)]);

        var rows = FullModeFormatter.TopRisks(dto, Pt, PtBr);

        Assert.Null(FullModeFormatter.TopRisksState(dto, Pt));
        Assert.Equal(("R1", "Portão A (flags 2)", "residual 6,4 · inerente 8,0", "E[L] 1.240.000", "confirmada",
            "escalonamento do Portão A"), (rows[0].Scenario, rows[0].GateA, rows[0].Scores, rows[0].Loss, rows[0].Confidence,
            rows[0].NextDecision));
        Assert.Equal(("—", "não declarada", false), (rows[1].GateA, rows[1].Confidence, rows[1].HasGateA));
    }

    /// <summary>S39 §1.2 item 5: the interim mode shows why there is no list — never an approximation.</summary>
    [Fact]
    public void TestTheInterimTopRisksSpaceSaysWhyItIsEmpty()
    {
        var interim = new RiskIndexTopRisksDto(Unit, D, false, "requires_full_mode", 0, []);
        var empty = new RiskIndexTopRisksDto(Unit, D, true, null, 0, []);

        Assert.Empty(FullModeFormatter.TopRisks(interim, Pt, PtBr));
        Assert.StartsWith("Top Risks — a lista do M43", FullModeFormatter.TopRisksState(interim, Pt));
        Assert.Equal("Nenhum cenário aberto deste nó está na lista Top Risks.", FullModeFormatter.TopRisksState(empty, Pt));
    }

    /// <summary>S40 §5.10: never a bare total; a sum without quantified scenarios reads "0 de N"; P95/CVaR are M45's, intervals kept.</summary>
    [Fact]
    public void TestTheMonetaryPanelSaysHowManyAreQuantifiedAndShowsM45sTail()
    {
        var dto = new RiskIndexMonetaryDto(Unit, D, RiskIndexProvenance.Official, 41, new RiskIndexLossSumDto(null, 0, 41, 0, false),
            new RiskIndexLossSumDto(1_240_000, 3, 41, 0.09, true),
            new RiskIndexPortfolioTailDto(870_000, 2_100_000, 2_000_000, 2_200_000, 3_100_000, 2_900_000, 3_300_000, 2_600_000,
                3_500_000, 3, 0, "AssumedIndependent", "exceeds", 100_000, 1), null);

        var lines = FullModeFormatter.Monetary(dto, Pt, PtBr);

        Assert.Equal("nenhum cenário quantificado (0 de 41)", lines.Residual);
        Assert.Equal("1.240.000 · 3 de 41 cenários quantificados · 9% do peso de REG · parcial", lines.Inherent);
        Assert.Equal("P95 2.100.000 [2.000.000–2.200.000] · CVaR95 3.100.000 [2.900.000–3.300.000] · 3 cenários · " +
                     "independência assumida (nenhuma correlação declarada)", lines.Portfolio);
        Assert.Equal("Portão B do portfólio: acima da tolerância", lines.GateB);
    }

    [Fact]
    public void TestTheInterimMonetaryPanelSaysThePortfolioNeedsTheFullMode()
    {
        var dto = new RiskIndexMonetaryDto(Unit, D, RiskIndexProvenance.Official, 6, new RiskIndexLossSumDto(null, 0, 6, 0, false),
            new RiskIndexLossSumDto(null, 0, 6, 0, false), null, "requires_full_mode");

        var lines = FullModeFormatter.Monetary(dto, Pt, PtBr);

        Assert.Equal("não disponível no modo interino (M45 no modo pleno)", lines.Portfolio);
        Assert.Null(lines.GateB);
    }

    [Fact]
    public void TestTheEvidenceConfidenceAndTheReferenceLinesReadAsTheSpecWroteThem()
    {
        var trend = new RiskIndexTrendDto(Unit, 30, DAL.Enums.RiskIndexCategory.Reg, [], [], [], [], [], [], 50, 20.4);

        Assert.Equal("confirmada 3 · indicativa 2 · hipótese 1 · não declarada 4",
            FullModeFormatter.EvidenceConfidence(new RiskIndexEvidenceConfidenceDto(3, 2, 1, 4), Pt, PtBr));
        Assert.Equal([("teto de apetite 50", false), ("nível-alvo 20", true)],
            FullModeFormatter.ReferenceLines(trend, Pt, PtBr).Select(l => (l.Label, l.IsTarget)));
    }

    /// <summary>The quality panel shows M40's distribution in full mode, beside the quality and never in it (T287).</summary>
    [Fact]
    public void TestTheQualityPanelShowsTheDistributionInFullMode()
    {
        var quality = new RiskIndexQualityDto(0.9, 1, 0.9, "high", 0, false, 0, [], 0, null, null, null, null, null, 0, "available",
            new RiskIndexEvidenceConfidenceDto(1, 0, 0, 2));

        var item = QualityStripFormatter.Panel(quality, false, null, Pt, PtBr).Single(i => i.Key == QualityStripFormatter.EvidenceConfidence);

        Assert.Equal("confirmada 1 · indicativa 0 · hipótese 0 · não declarada 2", item.Value);
    }

    [Fact]
    public void TestEveryKeyTheFullModePanelsComputeIsDeclaredInEveryLanguage()
    {
        string[] keys =
        [
            "RiskTopRisks_Unavailable_requires_full_mode", "RiskTopRisks_Unavailable_track9_service_unavailable", "RiskTopRisks_Empty",
            "RiskTopRisks_GateA", "RiskTopRisks_GateAFlagsFMT", "RiskTopRisks_ScoresFMT", "RiskTopRisks_LossFMT",
            "RiskTopRisks_Confidence_1", "RiskTopRisks_Confidence_2", "RiskTopRisks_Confidence_3", "RiskTopRisks_Confidence_none",
            "RiskMonetary_NoneQuantifiedFMT", "RiskMonetary_SumFMT", "RiskMonetary_Partial", "RiskMonetary_PortfolioFMT",
            "RiskMonetary_P95FMT", "RiskMonetary_Cvar95FMT",
            "RiskMonetary_Dependence_AssumedIndependent", "RiskMonetary_Dependence_Declared", "RiskMonetary_GateBFMT",
            "RiskQuality_EvidenceDistributionFMT", "RiskQuality_Evidence_available", "RiskTrendAppetiteLineFMT", "RiskTrendTargetLineFMT",
            "RiskProfileMode", "RiskCategoryReason_requires_activity_asset_links", "RiskGovernance_State_see_top_risks",
            "RiskGovernance_State_see_seals", "RiskIssue_ter_requires_full_mode", "RiskIssue_unknown_mode",
            .. Enum.GetNames<Model.RiskFlags.NextDecisionKind>().Select(n => "RiskTopRisks_Next_" + n),
            .. new[] { "requires_full_mode", "portfolio_too_large", "no_quantified_scenarios", "portfolio_not_computable",
                "track9_service_unavailable" }.Select(k => "RiskMonetary_PortfolioUnavailable_" + k),
            .. new[] { "exceeds", "within", "not_assessable", "not_configured" }.Select(k => "RiskGateB_" + k),
            .. HeadlineFormatter.KnownSealKeys.Select(k => "RiskSeal_" + k + "MSG")
        ];

        foreach (var file in RiskIndexTestText.ResourceFiles)
        {
            var texts = RiskIndexTestText.Load(file);
            Assert.All(keys, k => Assert.True(texts.ContainsKey(k), $"{file} lacks {k}"));
        }
    }
}

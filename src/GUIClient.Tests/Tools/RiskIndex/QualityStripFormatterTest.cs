using System;
using System.Globalization;
using System.Linq;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// T256 (S40 §5.12.1–§5.12.2, S39 §8.6): the always-visible quality strip and its detail — the same items whatever the
/// values, ⚠ past the threshold, "—" with why for an unmeasured count (never a made-up zero), and each "›" leading to the
/// object list's filter the server accepts, or to the Unassigned node from the organization only.
/// </summary>
[TestSubject(typeof(QualityStripFormatter))]
public class QualityStripFormatterTest
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TextLookup Pt = RiskIndexTestText.Portuguese;
    private static readonly DateTime Read = new(2026, 9, 27, 12, 0, 0, DateTimeKind.Utc);

    private static RiskIndexQualityDto Quality(double unassigned = 0.12, bool warning = true, int? orphans = 18,
        params RiskIndexStaleSourceDto[] stale) =>
        new(0.92, 0.99, 0.91, "high", unassigned, warning, 0.34, stale, 7, orphans is null ? null : 3, orphans, null, null,
            orphans is null ? null : 141, 5, "requires_m40");

    [Fact]
    public void TestTheStripHasTheSpecItemsInOrder()
    {
        var items = QualityStripFormatter.Strip(Quality(stale: new RiskIndexStaleSourceDto(RiskIndexSources.V1Device, Read)),
            isOrganization: true, Pt, PtBr);

        Assert.Equal([QualityStripFormatter.Unassigned, QualityStripFormatter.DefaultCriticality, QualityStripFormatter.StaleSource,
            QualityStripFormatter.PresumedCritical, QualityStripFormatter.OrphanApplications, QualityStripFormatter.SscPseudoHosts,
            QualityStripFormatter.ExitedBadReading], items.Select(i => i.Key));
        Assert.Equal(("Não atribuído", "12%", true), (items[0].Label, items[0].Value, items[0].IsWarning));
        Assert.Equal(("Criticidade padrão", "34%"), (items[1].Label, items[1].Value));
        Assert.Equal("Fonte obsoleta: Vision One — dispositivos, " + Read.ToLocalTime().ToString("d", PtBr), items[2].Label);
        Assert.Equal(("", true), (items[2].Value, items[2].IsWarning));
        Assert.Equal(("Críticos presumidos", "7"), (items[3].Label, items[3].Value));
        Assert.Equal("18", items[4].Value);
    }

    /// <summary>S39 §8.6: the snapshot does not store these counts yet — "—" with why, never 0.</summary>
    [Fact]
    public void TestAnUnmeasuredCountIsADashWithWhyNotAZero()
    {
        var items = QualityStripFormatter.Strip(Quality(orphans: null), isOrganization: false, Pt, PtBr);

        var orphans = items.Single(i => i.Key == QualityStripFormatter.OrphanApplications);
        var pseudo = items.Single(i => i.Key == QualityStripFormatter.SscPseudoHosts);
        Assert.Equal(("—", true, "Medido só na raiz da organização"), (orphans.Value, orphans.IsUnavailable, orphans.Hint));
        Assert.Equal(("—", true, "Não disponível: o snapshot ainda não grava esta contagem"), (pseudo.Value, pseudo.IsUnavailable, pseudo.Hint));
        Assert.False(orphans.CanOpen);
    }

    [Fact]
    public void TestNoStaleSourceIsStillAnItemWithZero()
    {
        var item = Assert.Single(QualityStripFormatter.Strip(Quality(), true, Pt, PtBr),
            i => i.Key == QualityStripFormatter.StaleSourcesNone);

        Assert.Equal(("Fontes obsoletas", "0", false), (item.Label, item.Value, item.IsWarning));
        Assert.False(item.CanOpen);
    }

    [Fact]
    public void TestEachItemOpensAListTheServerAcceptsOrTheUnassignedNode()
    {
        var items = QualityStripFormatter.Strip(Quality(stale: new RiskIndexStaleSourceDto(RiskIndexSources.SscTotal, null)), true, Pt,
            PtBr);

        Assert.Equal(RiskIndexNodeKey.UnassignedTerm, items[0].NodeKey);
        Assert.Equal("chip=default_criticality", items[1].ObjectFilter);
        Assert.Equal("source=SSC-TOT", items[2].ObjectFilter);
        Assert.EndsWith(", —", items[2].Label);
        Assert.Equal("chip=presumed_critical", items[3].ObjectFilter);
        Assert.All(items.Take(4), i => Assert.True(i.CanOpen));
        Assert.All(items.Where(i => i.ObjectFilter is not null), i => Assert.True(ObjectListQuery.IsValidPreset(i.ObjectFilter)));
    }

    /// <summary>The Unassigned objects are the organization's: an entity node, or none of them, leaves no "›".</summary>
    [Theory]
    [InlineData(false, 0.12)]
    [InlineData(true, 0.0)]
    public void TestTheUnassignedNodeOpensOnlyFromTheOrganizationWithSome(bool isOrganization, double fraction)
    {
        var item = QualityStripFormatter.Strip(Quality(unassigned: fraction, warning: false), isOrganization, Pt, PtBr)[0];

        Assert.Null(item.NodeKey);
        Assert.False(item.CanOpen);
        Assert.Null(item.Hint);
    }

    [Fact]
    public void TestThePanelAddsTheDetailRowsAndTheThreshold()
    {
        var rows = QualityStripFormatter.Panel(Quality(stale: new RiskIndexStaleSourceDto(RiskIndexSources.V1Device, Read)), true, 0.10,
            Pt, PtBr);

        Assert.Equal([QualityStripFormatter.Unassigned, QualityStripFormatter.DefaultCriticality, QualityStripFormatter.StaleSource,
            QualityStripFormatter.PresumedCritical, QualityStripFormatter.DivergentLinks, QualityStripFormatter.OrphanApplications,
            QualityStripFormatter.SscPseudoHosts, QualityStripFormatter.ExitedBadReading,
            QualityStripFormatter.FindingStatusDisagreements, QualityStripFormatter.ResidualAboveInherent,
            QualityStripFormatter.EvidenceConfidence], rows.Select(r => r.Key));
        Assert.Equal(("Não atribuído (fração por peso)", "12%", "10%"), (rows[0].Label, rows[0].Value, rows[0].Threshold));
        Assert.Equal("1 fonte", rows[2].Value);
        Assert.Equal(("3", "141"), (rows[4].Value, rows[8].Value));
        Assert.Equal(("5", "chip=residual_above_inherent"), (rows[9].Value, rows[9].ObjectFilter));
        Assert.Equal(("não disponível (requer M40)", true), (rows[10].Value, rows[10].IsUnavailable));
        Assert.All(rows.Skip(1), r => Assert.Equal("", r.Threshold));
    }

    /// <summary>The server refuses that list without riskmanagement (§11.3): the count stays, its "›" does not.</summary>
    [Fact]
    public void TestResidualAboveInherentOpensItsListOnlyWithRiskManagement()
    {
        var redacted = QualityStripFormatter.Panel(Quality(), true, null, Pt, PtBr, mayReadRegister: false)
            .Single(r => r.Key == QualityStripFormatter.ResidualAboveInherent);
        var named = QualityStripFormatter.Panel(Quality(), true, null, Pt, PtBr, mayReadRegister: true)
            .Single(r => r.Key == QualityStripFormatter.ResidualAboveInherent);

        Assert.Equal(("5", null, false), (redacted.Value, redacted.ObjectFilter, redacted.CanOpen));
        Assert.Equal(("5", true), (named.Value, named.CanOpen));
    }

    [Fact]
    public void TestWithoutTheProfileTheThresholdIsBlankNotInvented()
    {
        var rows = QualityStripFormatter.Panel(Quality(), true, null, Pt, PtBr);

        Assert.Equal("", rows[0].Threshold);
        Assert.True(rows[0].IsWarning);
    }

    [Fact]
    public void TestEveryComputedKeyIsInTheThreeResourceFiles()
    {
        var keys = QualityStripFormatter.ComputedKeys()
            .Concat(
            [
                "RiskQualityStripTitle", "RiskQualityStripHelp", "RiskQualityStripPartial", "RiskQualityStripMyScope",
                "RiskQualityStripOpenHint", "RiskQualityStripOpenNodeHint", "RiskQualityPanelTitle", "RiskQualityPanelColItem",
                "RiskQualityPanelColValue", "RiskQualityPanelColThreshold", "RiskQualityPanelNote", "RiskMonetaryTitle",
                "RiskMonetary_ResidualLabel", "RiskMonetary_ResidualUnavailable", "RiskMonetary_InherentLabel",
                "RiskMonetary_InherentUnavailable", "RiskMonetary_P95Label", "RiskMonetary_P95Unavailable", "RiskMonetaryNote",
                "RiskTopRisksTitle", "RiskTopRisksPlaceholder"
            ]).ToList();

        foreach (var file in RiskIndexTestText.ResourceFiles)
        {
            var texts = RiskIndexTestText.Load(file);
            var missing = keys.Where(k => !texts.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{file} lacks: {string.Join(", ", missing)}");
        }
    }
}

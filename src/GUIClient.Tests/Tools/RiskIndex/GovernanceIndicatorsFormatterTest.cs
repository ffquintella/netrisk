using System.Globalization;
using System.Linq;
using GUIClient.Models;
using GUIClient.Tools.RiskIndex;
using JetBrains.Annotations;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// T256 (S40 §5.2.5): the governance and review indicators beside the index — the sentences of the spec, "—" where the
/// server has no value, the states of M43 and M45/M46, and a "›" to the owning module only, disabled with the reason when
/// the user may not open it. Campaigns have no desktop module, so no "›".
/// </summary>
[TestSubject(typeof(GovernanceIndicatorsFormatter))]
public class GovernanceIndicatorsFormatterTest
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly TextLookup Pt = RiskIndexTestText.Portuguese;

    private static readonly RiskIndexGovernanceIndicatorsDto Full =
        new(3, 30, 1, 0.82, 9, 1, 0.71, "requires_m43", "requires_m45_m46");

    [Fact]
    public void TestTheIndicatorsReadAsTheSpecWroteThem()
    {
        var items = GovernanceIndicatorsFormatter.Items(Full, _ => true, Pt, PtBr);

        Assert.Equal(["acceptances", "segregation", "campaigns", "sla", "flags", "gate_b"], items.Select(i => i.Key));
        Assert.Equal("Aceitações válidas que vencem em ≤ 30 d: 3", items[0].Text);
        Assert.Equal("Uso de justificativa de exceção à segregação: 1 em 90 d", items[1].Text);
        Assert.Equal("Campanhas: conclusão 82% · tempo médio até decidir 9 d · vencidas 1", items[2].Text);
        Assert.Equal("Conformidade de SLA: 71%", items[3].Text);
        Assert.Equal("Flags (M43): não disponível (requer M43)", items[4].Text);
        Assert.Equal("Portão B: não disponível (requer M45/M46)", items[5].Text);
    }

    [Fact]
    public void TestAMissingValueIsADashNotAZero()
    {
        var items = GovernanceIndicatorsFormatter.Items(new RiskIndexGovernanceIndicatorsDto(0, 30, 0, null, null, 0, null, "x", "y"),
            _ => true, Pt, PtBr);

        Assert.Equal("Campanhas: conclusão — · tempo médio até decidir — · vencidas 0", items[2].Text);
        Assert.Equal("Conformidade de SLA: —", items[3].Text);
        Assert.StartsWith("Nenhum achado aberto com prazo de SLA no nó", items[3].Hint);
        // A state key the client does not know reads as itself, never as a resource key.
        Assert.Equal(("Flags (M43): x", "Portão B: y"), (items[4].Text, items[5].Text));
    }

    [Fact]
    public void TestEachCountOpensItsOwningModuleOnlyWhenAllowed()
    {
        var allowed = GovernanceIndicatorsFormatter.Items(Full, _ => true, Pt, PtBr);
        var onlyRisks = GovernanceIndicatorsFormatter.Items(Full, view => view == AvaliableViews.Risk, Pt, PtBr);

        Assert.Equal([AvaliableViews.Risk, AvaliableViews.Risk, null, AvaliableViews.Vulnerabilities, null, null],
            allowed.Select(i => i.Module));
        Assert.All(allowed.Where(i => i.HasModule), i => Assert.True(i.CanOpen));
        Assert.All(allowed.Where(i => !i.HasModule), i => Assert.False(i.CanOpen));
        Assert.StartsWith("Abre o módulo", allowed[0].Hint);

        var sla = onlyRisks[3];
        Assert.False(sla.CanOpen);
        Assert.StartsWith("Requer acesso ao módulo", sla.Hint);
        Assert.True(onlyRisks[0].CanOpen);
    }

    [Fact]
    public void TestEveryComputedKeyIsInTheThreeResourceFiles()
    {
        var keys = GovernanceIndicatorsFormatter.ComputedKeys()
            .Concat(["RiskGovernanceTitle", "RiskGovernanceVolumeNote", "RiskGovernanceLiveNote", "RiskGovernancePartial",
                "RiskGovernanceMyScope", "Risks", "Vulnerabilities"])
            .ToList();

        foreach (var file in RiskIndexTestText.ResourceFiles)
        {
            var texts = RiskIndexTestText.Load(file);
            var missing = keys.Where(k => !texts.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{file} lacks: {string.Join(", ", missing)}");
        }
    }
}

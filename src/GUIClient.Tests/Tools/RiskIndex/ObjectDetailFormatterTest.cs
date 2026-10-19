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
/// T255 (S40 §5.9.2, §10.3, §11.3): the object detail reads the server's factor readings and their freshness, the signals
/// (at most 50 per factor), the register's inputs beside the stored residual, and the destinations — each enabled by the
/// navigation bar's rule and offered by the server, disabled with "Requer a permissão ‹módulo›" otherwise. Without the
/// owner module's permission, the register content gives way to that note.
/// </summary>
[TestSubject(typeof(ObjectDetailFormatter))]
public class ObjectDetailFormatterTest
{
    private static readonly CultureInfo PtBr = CultureInfo.GetCultureInfo("pt-BR");
    private static readonly DateOnly Day = new(2026, 10, 5);

    private static RiskIndexFactorDto Factor(string key, int signals = 1, bool redacted = false) =>
        new(key, "V", 0.928, 1, 1, 0.928, new DateTime(2026, 10, 4, 22, 10, 0, DateTimeKind.Utc), "importers",
            Enumerable.Range(0, signals).Select(i => new RiskIndexSignalDto(0.9, ["m_net"], redacted ? null : "achado #" + i, null)).ToList());

    private static RiskIndexObjectDetailDto Detail(RiskIndexObjectKind kind = RiskIndexObjectKind.Application, string label = "APP-03",
        string? permission = null, IReadOnlyList<RiskIndexNavigationTargetDto>? navigation = null,
        IReadOnlyList<RiskIndexFactorDto>? factors = null, RiskIndexScenarioInputsDto? scenario = null, bool? internet = true) =>
        new(new RiskIndexObjectLabelDto(new RiskIndexObjectRef(kind, "30"), label, permission), "Application", Day,
            new RiskIndexCriticalityDto(5, null, 0, 5, "manual", "M. Souza", null, true), internet,
            [
                new RiskIndexObjectCategoryDto(RiskIndexCategory.Exp, 0.928, RiskIndexObjectState.Assessed, "Assets", 1,
                    RiskIndexContributionBranch.HeldBy, factors ?? [Factor("EXP.V")])
            ],
            scenario, null,
            [new RiskIndexNodeRef("e:20", "P-Matrícula", "businessProcess"), new RiskIndexNodeRef("e:21", "P-Pesquisa", "businessProcess")],
            [], navigation ?? []);

    [Fact]
    public void TestTheHeaderNamesKindClassNodesCriticalityAndContext()
    {
        var header = ObjectDetailFormatter.Header(Detail(), RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal("Detalhe do objeto — APP-03", header.Title);
        Assert.Equal("Snapshot 05/10/2026", header.Snapshot);
        Assert.StartsWith("Tipo: ", header.KindAndClass);
        Assert.Equal("Pertence a 2 nó(s): P-Matrícula, P-Pesquisa", header.Nodes);
        Assert.Equal("Criticidade efetiva 5 = máx(própria 5, herdada —) + dados 0", header.Criticality);
        Assert.Contains("(declarada, não BIA)", header.CriticalityOrigin);
        Assert.EndsWith("autor: M. Souza", header.CriticalityOrigin);
        Assert.Equal("Internet-facing: sim", header.InternetFacing);
        Assert.Null(header.VendorReference);
        Assert.Null(header.RedactionNote);

        Assert.Null(ObjectDetailFormatter.Header(Detail(internet: null), RiskIndexTestText.Portuguese, PtBr).InternetFacing);
        Assert.Equal("Não pertence a nenhum nó do mapa",
            ObjectDetailFormatter.Header(Detail() with { Attribution = [] }, RiskIndexTestText.Portuguese, PtBr).Nodes);
    }

    [Fact]
    public void TestAVendorObjectShowsItsIdForLackOfAScreen()
    {
        var header = ObjectDetailFormatter.Header(Detail(RiskIndexObjectKind.VendorAsset, "fqdn.example"),
            RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal("Id no fornecedor: 30", header.VendorReference);
        Assert.Empty(ObjectDetailFormatter.Destinations(Detail(RiskIndexObjectKind.VendorAsset), _ => true,
            RiskIndexTestText.Portuguese, PtBr));
    }

    [Fact]
    public void TestTheCategoriesAndFactorsReadTheServersValuesAndFreshness()
    {
        var detail = Detail();

        var category = Assert.Single(ObjectDetailFormatter.Categories(detail, RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal(("EXP", "0,928", "1,00", "piso: sustentado"), (category.Category, category.R, category.Weight, category.Branch));
        Assert.Equal("ativos", category.Population);

        var factor = Assert.Single(ObjectDetailFormatter.Factors(detail, RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal(("EXP", "EXP.V", "V", "0,928", "1,00", "1,0", "0,928"),
            (factor.Category, factor.Factor, factor.Type, factor.X, factor.V, factor.UF, factor.FEff));
        Assert.Equal(new DateTime(2026, 10, 4, 22, 10, 0, DateTimeKind.Utc).ToLocalTime().ToString("g", PtBr), factor.Reading);
        Assert.Equal("importers", factor.Source);
    }

    [Fact]
    public void TestTheSignalsAreCappedAtFiftyPerFactor()
    {
        var signals = ObjectDetailFormatter.Signals(Detail(factors: [Factor("EXP.V", 53)]), RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(51, signals.Count);
        Assert.Equal("EXP.V · s 0,900 · m_net · achado #0", signals[0]);
        Assert.Equal("EXP.V · e mais 3 sinal(is) não listado(s)", signals[^1]);
    }

    /// <summary>§11.3: finding signals without the vulnerabilities permission keep their values, not their text.</summary>
    [Fact]
    public void TestRedactedSignalsSayWhichPermissionTheirTextNeeds()
    {
        var signals = ObjectDetailFormatter.Signals(Detail(factors: [Factor("EXP.V", 1, redacted: true)]),
            RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal(["EXP.V · s 0,900 · m_net", "EXP.V · Requer a permissão vulnerabilities"], signals);
    }

    [Fact]
    public void TestTheRegisterInputsShowTheEffectiveResidualBesideTheStoredOne()
    {
        var inputs = new RiskIndexScenarioInputsDto(5, 1, 12, 0.84, 0.88, 90, 30, 0.5, 0.2, 0.9);

        var lines = ObjectDetailFormatter.Scenario(Detail(RiskIndexObjectKind.Scenario, scenario: inputs),
            RiskIndexTestText.Portuguese, PtBr)!;

        Assert.Equal("Impacto 5 · rank 1 de 12 · T 90 d · atraso 30 d · cred 0,50 · σ_ref 0,20", lines[0]);
        Assert.StartsWith("r_eff 0,90 ao lado do residual armazenado 0,84 (r_inh 0,88)", lines[1]);
        Assert.Contains("o residual armazenado não é alterado", lines[1]);

        var untreated = ObjectDetailFormatter.Scenario(Detail(RiskIndexObjectKind.Scenario, scenario: inputs with { RRes = null }),
            RiskIndexTestText.Portuguese, PtBr)!;
        Assert.Equal("Tratamento não avaliado", untreated[1]);
        Assert.Null(ObjectDetailFormatter.Scenario(Detail(), RiskIndexTestText.Portuguese, PtBr));
    }

    /// <summary>§5.9.2 "Redação": no register content, decision state or destination without riskmanagement.</summary>
    [Fact]
    public void TestWithoutTheOwnersPermissionTheRegisterContentIsANote()
    {
        var redacted = Detail(RiskIndexObjectKind.Scenario, "Scenario #30", "riskmanagement");

        Assert.Equal("Requer a permissão riskmanagement",
            ObjectDetailFormatter.Header(redacted, RiskIndexTestText.Portuguese, PtBr).RedactionNote);
        Assert.Equal("Detalhe do objeto — Cenário #30", ObjectDetailFormatter.Header(redacted, RiskIndexTestText.Portuguese, PtBr).Title);
        Assert.Equal(["Requer a permissão riskmanagement"], ObjectDetailFormatter.Scenario(redacted, RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal("Requer a permissão riskmanagement", ObjectDetailFormatter.Decision(redacted, RiskIndexTestText.Portuguese, PtBr));

        // The server sends no destination it withheld: the button stays, disabled, with the reason.
        var destination = Assert.Single(ObjectDetailFormatter.Destinations(redacted, _ => false, RiskIndexTestText.Portuguese, PtBr));
        Assert.Equal((RiskIndexNavigationModule.Risk, "Abrir risco", false, "Requer a permissão riskmanagement"),
            (destination.Module, destination.Label, destination.IsEnabled, destination.DisabledReason));
        Assert.Null(destination.Target);
        Assert.Equal("—", ObjectDetailFormatter.Decision(Detail(RiskIndexObjectKind.Scenario, "R1"), RiskIndexTestText.Portuguese, PtBr));
    }

    /// <summary>§10.3: enabled only when the server offers the destination and the bar's rule lets the user open the module.</summary>
    [Fact]
    public void TestADestinationIsEnabledByTheNavigationBarsRuleAndTheServersOffer()
    {
        var detail = Detail(navigation:
        [
            new RiskIndexNavigationTargetDto(RiskIndexNavigationModule.ApplicationFindings, 30, "RiskNav_ApplicationFindings"),
            new RiskIndexNavigationTargetDto(RiskIndexNavigationModule.Entity, 30, "RiskNav_Entity")
        ]);

        var destinations = ObjectDetailFormatter.Destinations(detail, view => view == AvaliableViews.Entities,
            RiskIndexTestText.Portuguese, PtBr);

        Assert.Equal([RiskIndexNavigationModule.ApplicationFindings, RiskIndexNavigationModule.Entity], destinations.Select(d => d.Module));
        var findings = destinations[0];
        Assert.False(findings.IsEnabled);
        Assert.Null(findings.Target);
        Assert.Equal("Requer a permissão vulnerabilities", findings.DisabledReason);
        var entity = destinations[1];
        Assert.True(entity.IsEnabled);
        Assert.Null(entity.DisabledReason);
        Assert.Equal(new NavigationTarget(AvaliableViews.Entities, 30), entity.Target);
        Assert.Equal("Abrir entidade", entity.Label);
    }

    [Fact]
    public void TestEveryKeyTheDetailComputesExistsInTheThreeResourceFiles()
    {
        foreach (var file in RiskIndexTestText.ResourceFiles)
        {
            var texts = RiskIndexTestText.Load(file);
            var missing = ObjectDetailFormatter.ComputedKeys().Where(k => !texts.ContainsKey(k)).ToList();
            Assert.True(missing.Count == 0, $"{file} lacks: {string.Join(", ", missing)}");
        }
    }
}

using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using JetBrains.Annotations;
using Model.RiskIndex;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// The typed mirror of a profile's <c>parameters</c> (S39 §12.2): the defaults are the "Equilibrado" profile, every key
/// is serialized under its S39 name, a document round-trips, an unknown key is refused and a missing one reads as its
/// default. Validation of ranges and pace rules is T250's, not tested here.
/// </summary>
[TestSubject(typeof(RiskIndexParameters))]
public class RiskIndexParametersTest
{
    private static JsonObject DefaultDocument() =>
        JsonNode.Parse(RiskIndexParametersJson.Serialize(new RiskIndexParameters()))!.AsObject();

    [Fact]
    public void TestTheDefaultWeightsAreTheBalancedPresetAndSumToOne()
    {
        var omega = new RiskIndexParameters().Omega;

        Assert.Equal((0.30, 0.30, 0.15, 0.10, 0.15, 0.0), (omega.Reg, omega.Exp, omega.Ame, omega.Ctl, omega.Gov, omega.Ter));
        Assert.Equal(1.0, omega.Reg + omega.Exp + omega.Ame + omega.Ctl + omega.Gov + omega.Ter, 6);
    }

    [Fact]
    public void TestTheDefaultAggregationBandsAndFloorsAreTheSpecs()
    {
        var p = new RiskIndexParameters();

        Assert.Equal((4, 0.80, 0.70, 0.30, 10), (p.P, p.Tau, p.TauG, p.Rho, p.KSignals));
        Assert.Equal(new[] { 0, 31, 70 }, p.Bands.Select(b => b.LowerInclusive));
        Assert.Equal(new[] { "Baixo", "Médio", "Alto" }, p.Bands.Select(b => b.LabelPtBr));
        Assert.Equal(new[] { "Low", "Medium", "High" }, p.Bands.Select(b => b.LabelEnUs));
        Assert.Equal(70, p.GateAFloor);
        Assert.Equal(0.0, p.AcceptanceDiscount);

        // The joint tail lock of S39 §12.4 holds for the defaults: 0.80 × 0.70 × 70 = 39.2 ≥ 31.
        Assert.True(p.Tau * p.TauG * p.Bands[^1].LowerInclusive >= p.Bands[1].LowerInclusive);
    }

    [Fact]
    public void TestTheDefaultRegisterCopiesHaveAReachableVeryHighAndADescendingCadence()
    {
        var p = new RiskIndexParameters();

        Assert.Equal((0.0, 4.0, 7.0, 9.0), (p.RegisterLimits.Low, p.RegisterLimits.Medium, p.RegisterLimits.High, p.RegisterLimits.VeryHigh));
        Assert.Equal((30, 90, 120, 240), (p.ReviewCadence.VeryHigh, p.ReviewCadence.High, p.ReviewCadence.Medium, p.ReviewCadence.Low));
        Assert.Equal(RiskIndexCadenceBasis.InherentRisk, p.CadenceBasis);
    }

    [Fact]
    public void TestTheDefaultCeilingsQuotasAndAnchorsAreTheSpecs()
    {
        var p = new RiskIndexParameters();

        Assert.Equal(15, p.FactorCeilings.Count);
        Assert.Equal(0.8, p.FactorCeilings[RiskIndexFactors.ExpCloud]);
        Assert.Equal(0.6, p.FactorCeilings[RiskIndexFactors.CtlResponse]);
        Assert.Equal(1.0, p.FactorCeilings[RiskIndexFactors.GovAppetite]);

        Assert.All(p.PopulationQuotas.Values, quotas => Assert.Equal(1.0, quotas.Values.Sum(), 6));
        Assert.Equal(new[] { "CTL", "GOV" }, p.PopulationQuotas.Keys.Order());

        Assert.Equal(new[] { 0.20, 0.50, 0.75, 0.95 }, Enumerable.Range(1, 4).Select(s => p.SeverityAnchors[s]));
        Assert.Equal(0.0, p.SscAnchors[100]);
        Assert.Equal(1.0, p.SscAnchors[0]);
        Assert.Equal(0.50, p.IncidentCategoryAnchors["not_specified"]);
        Assert.True(p.NoExploitationCeiling < p.ExploitationFloor);
        Assert.True(p.IncidentWindowDays >= 3 * p.IncidentHalfLifeDays);
    }

    /// <summary>Every enabled source has a window or needs none, and none of the [N] sources is enabled by default.</summary>
    [Fact]
    public void TestTheDefaultSourcesExcludeTheOnesThatNeedANewIntegration()
    {
        var p = new RiskIndexParameters();

        Assert.Contains(RiskIndexSources.V1Device, p.EnabledSources);
        Assert.Contains(RiskIndexSources.TenableNessus, p.EnabledSources);
        Assert.DoesNotContain(RiskIndexSources.TenableAes, p.EnabledSources);
        Assert.DoesNotContain(RiskIndexSources.V1InternetFacing, p.EnabledSources);
        Assert.DoesNotContain(RiskIndexSources.NearMisses, p.EnabledSources);
        Assert.Equal(p.EnabledSources.Count, p.EnabledSources.Distinct().Count());
        Assert.Equal(365, p.FreshnessDays[RiskIndexSources.Assessments]);
        Assert.Equal(7, p.FreshnessDays[RiskIndexSources.V1Device]);
        Assert.All(p.FreshnessDays.Values, w => Assert.InRange(w, 1, 730));
    }

    [Theory]
    [InlineData("faixas")]
    [InlineData("omega")]
    [InlineData("u")]
    [InlineData("cotas_populacao")]
    [InlineData("tau_g")]
    [InlineData("k_sinais")]
    [InlineData("crit_padrao_nao_producao")]
    [InlineData("ancoras_ssc")]
    [InlineData("frescor_W")]
    [InlineData("fontes_habilitadas")]
    [InlineData("cadencia_revisao")]
    [InlineData("limites_registro")]
    [InlineData("base_cadencia")]
    [InlineData("s")]
    [InlineData("desconto_aceite")]
    [InlineData("piso_portao_a")]
    [InlineData("residuo_max")]
    public void TestEveryKeyIsSerializedUnderItsS39Name(string key)
    {
        Assert.True(DefaultDocument().ContainsKey(key), $"The parameters document has no '{key}'.");
    }

    [Fact]
    public void TestNestedKeysUseTheS39Names()
    {
        var doc = DefaultDocument();

        Assert.Equal(0.30, doc["omega"]!["REG"]!.GetValue<double>());
        Assert.Equal(0.5, doc["s"]!["APT_BRK_TREATED"]!.GetValue<double>());
        Assert.Equal(9.0, doc["limites_registro"]!["very_high"]!.GetValue<double>());
        Assert.Equal("Alto", doc["faixas"]![2]!["rotulo_pt_br"]!.GetValue<string>());
        Assert.Equal(0.5, doc["cotas_populacao"]!["GOV"]!["Scenarios"]!.GetValue<double>());
        Assert.Equal("InherentRisk", doc["base_cadencia"]!.GetValue<string>());
    }

    [Fact]
    public void TestADocumentRoundTrips()
    {
        var original = new RiskIndexParameters
        {
            P = 8, Tau = 0.90, TauG = 0.85,
            Omega = new RiskIndexCategoryWeights { Reg = 0.45, Exp = 0.20, Ame = 0.10, Ctl = 0.05, Gov = 0.20 },
            CadenceBasis = RiskIndexCadenceBasis.ResidualRisk,
            NonProductionEnvironments = ["dev", "qa"]
        };

        var json = RiskIndexParametersJson.Serialize(original);
        var read = RiskIndexParametersJson.Deserialize(json);

        Assert.Equal((8, 0.90, 0.85), (read.P, read.Tau, read.TauG));
        Assert.Equal(original.Omega, read.Omega);
        Assert.Equal(RiskIndexCadenceBasis.ResidualRisk, read.CadenceBasis);
        Assert.Equal(new[] { "dev", "qa" }, read.NonProductionEnvironments);
        Assert.Equal(original.Bands, read.Bands);
        Assert.Equal(original.SeverityAnchors, read.SeverityAnchors);
        Assert.Equal(json, RiskIndexParametersJson.Serialize(read));
    }

    /// <summary>An approved profile must mean exactly what was approved: a misspelt key is refused, never dropped.</summary>
    [Theory]
    [InlineData("""{"tau_G": 0.5}""")]
    [InlineData("""{"omega": {"REG": 0.3, "XYZ": 0.1}}""")]
    public void TestAnUnknownKeyIsRefused(string json)
    {
        Assert.ThrowsAny<JsonException>(() => RiskIndexParametersJson.Deserialize(json));
    }

    [Fact]
    public void TestAMissingKeyReadsAsItsDefault()
    {
        var read = RiskIndexParametersJson.Deserialize("""{"p": 6}""");

        Assert.Equal(6, read.P);
        Assert.Equal(0.80, read.Tau);
        Assert.Equal(3, read.Bands.Count);
    }

    [Fact]
    public void TestANullDocumentIsRefused()
    {
        Assert.Throws<JsonException>(() => RiskIndexParametersJson.Deserialize("null"));
    }
}

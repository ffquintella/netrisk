using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using DAL.Enums;
using GUIClient.Tools.RiskIndex;
using Model.RiskIndex;
using Xunit;

namespace GUIClient.Tests.Tools.RiskIndex;

/// <summary>
/// M55, T260 (S40 §6.2.2, §12.4): the draft editor's rows come from the server's schema and the draft's own maps, every key
/// of S39 §12.2 is editable somewhere, and every edit round-trips through the strict parameters serializer the server stores.
/// </summary>
public class ProfileParameterRowsTest
{
    private static readonly RiskIndexParameterSchemaDto Schema = RiskIndexProfileRules.Schema();

    private static IReadOnlyList<ProfileParameterRowSpec> Rows(RiskIndexParameters? p = null) =>
        ProfileParameterRows.Build(Schema, p ?? new RiskIndexParameters());

    private static ProfileParameterRowSpec Row(string key) => Rows().Single(r => r.Key == key);

    [Fact]
    public void TestEverySchemaKeyIsARowOrEditedElsewhere()
    {
        var rows = Rows();
        foreach (var item in Schema.Items)
        {
            if (ProfileParameterRows.EditedElsewhere.Contains(item.Key)) continue;
            Assert.True(rows.Any(r => r.Key == item.Key || r.Key.StartsWith(item.Key + ".")),
                $"Schema key {item.Key} has no row in the draft editor.");
        }

        Assert.DoesNotContain(rows, r => ProfileParameterRows.EditedElsewhere.Contains(r.Path[0]));
    }

    [Fact]
    public void TestRowsFollowTheGroupOrderOfTheSpec()
    {
        var groups = Rows().Select(r => r.Group).Distinct().ToList();
        var expected = ProfileParameterRows.GroupOrder.Where(groups.Contains).ToList();
        Assert.Equal(expected, groups);
    }

    [Fact]
    public void TestMapEntriesAreRowsOfTheirOwn()
    {
        var rows = Rows();
        Assert.Contains(rows, r => r.Key == "u.EXP.D" && r.Path.SequenceEqual(["u", "EXP.D"]) && r.Minimum == 0.5 && r.Maximum == 1);
        Assert.Contains(rows, r => r.Key == "cotas_populacao.GOV.Scenarios" && r.Path.Count == 3 && r.SubLabel == "GOV · Scenarios");
        Assert.Contains(rows, r => r.Key == "ancoras_ssc.100");
        Assert.Contains(rows, r => r.Key == "cadencia_revisao.very_high" && r.Kind == ProfileParameterKind.Integer);
        Assert.Equal(ProfileParameterKind.CidrList, Row("faixas_publicas_internas").Kind);
        Assert.Equal(ProfileParameterKind.IntegerList, Row("niveis_sensiveis").Kind);
        Assert.Equal(ProfileParameterKind.Choice, Row("base_cadencia").Kind);
        Assert.Equal(ProfileParameterKind.Boolean, Row("heuristica_ip_publico").Kind);
    }

    [Fact]
    public void TestNestedScalarsReadTheirObjectAndOptionalWeightsReachZero()
    {
        Assert.Equal(["omega", "REG"], ProfileParameterRows.PathOf("omega.REG"));
        Assert.Equal(["s", "APT_BRK"], ProfileParameterRows.PathOf("s.APT_BRK"));
        Assert.Equal(["tau_g"], ProfileParameterRows.PathOf("tau_g"));
        Assert.Equal(0.20, Row("omega.REG").Minimum);
        Assert.Equal(0, Row("omega.EXP").Minimum);
        Assert.True(Row("omega.TER").Locked);
        Assert.Equal("RiskParamLocked_TerUntilM48", Row("omega.TER").LockedReasonKey);
    }

    [Fact]
    public void TestANumberEditRoundTripsThroughTheStrictSerializer()
    {
        var p = new RiskIndexParameters();
        var edited = ProfileParameterRows.WithNumber(p, Row("omega.REG"), 0.32);
        edited = ProfileParameterRows.WithNumber(edited, Row("p"), 6.0);
        edited = ProfileParameterRows.WithNumber(edited, Row("u.EXP.N"), 0.9);
        edited = ProfileParameterRows.WithNumber(edited, Row("cotas_populacao.GOV.Scenarios"), 0.6);

        Assert.Equal(0.32, edited.Omega.Reg);
        Assert.Equal(6, edited.P);
        Assert.Equal(0.9, edited.FactorCeilings["EXP.N"]);
        Assert.Equal(0.6, edited.PopulationQuotas["GOV"]["Scenarios"]);
        // The integer key keeps an integer token: the strict serializer would refuse 6.0 for an int.
        var json = RiskIndexParametersJson.Serialize(edited);
        Assert.Equal(json, RiskIndexParametersJson.Serialize(RiskIndexParametersJson.Deserialize(json)));
        Assert.Contains("\"p\":6,", json);
        Assert.Equal(0.30, p.Omega.Reg); // the input is never mutated
    }

    [Fact]
    public void TestBooleanChoiceAndListEdits()
    {
        var p = ProfileParameterRows.WithBoolean(new RiskIndexParameters(), Row("heuristica_ip_publico"), true);
        p = ProfileParameterRows.WithChoice(p, Row("base_cadencia"), nameof(RiskIndexCadenceBasis.ResidualRisk));
        p = ProfileParameterRows.WithList(p, Row("ambientes_nao_producao"), "dev; qa, dev\nstaging")!;
        p = ProfileParameterRows.WithList(p, Row("niveis_sensiveis"), "3, 4")!;
        p = ProfileParameterRows.WithList(p, Row("faixas_publicas_internas"), "200.20.0.0/16")!;

        Assert.True(p.PublicIpHeuristic);
        Assert.Equal(RiskIndexCadenceBasis.ResidualRisk, p.CadenceBasis);
        Assert.Equal(["dev", "qa", "staging"], p.NonProductionEnvironments);
        Assert.Equal([3, 4], p.SensitiveLevels);
        Assert.Equal(["200.20.0.0/16"], p.InternalPublicRanges);
        Assert.Equal("dev, qa, staging", ProfileParameterRows.ReadText(p, Row("ambientes_nao_producao").Path));
        Assert.True(ProfileParameterRows.ReadBoolean(p, Row("heuristica_ip_publico").Path));
    }

    [Theory]
    [InlineData("niveis_sensiveis", "3, high")]
    [InlineData("faixas_publicas_internas", "200.20.0.0/99")]
    [InlineData("faixas_publicas_internas", "not-a-range")]
    public void TestAListItemThatDoesNotParseIsRefusedNotDropped(string key, string text)
    {
        Assert.Null(ProfileParameterRows.WithList(new RiskIndexParameters(), Row(key), text));
    }

    [Fact]
    public void TestRowIssuesNameTheRowOrItsEntryFamilyAndCompositeIssuesStayInTheGroup()
    {
        var draft = new RiskIndexParameters
        {
            Omega = new RiskIndexCategoryWeights { Reg = 0.30, Exp = 0.30, Ame = 0.15, Ctl = 0.10, Gov = 0.20 },
            PopulationQuotas = new Dictionary<string, IReadOnlyDictionary<string, double>>
            {
                ["GOV"] = new Dictionary<string, double> { ["Scenarios"] = 0.7, ["Assets"] = 0.5 },
                ["CTL"] = new Dictionary<string, double> { ["Assessments"] = 0.5, ["Scenarios"] = 0.5 }
            },
            P = 20
        };
        var issues = RiskIndexProfileRules.Validate(RiskIndexMode.Interim, draft, null, null, null).Errors;
        var rows = Rows(draft);
        var keys = rows.Select(r => r.Key).ToList();

        Assert.Contains(ProfileParameterRows.IssuesForRow("p", issues), i => i.Code == "parameter_out_of_range");
        Assert.Contains(ProfileParameterRows.IssuesForRow("cotas_populacao.GOV.Scenarios", issues), i => i.Code == "quota_sum");
        Assert.DoesNotContain(ProfileParameterRows.IssuesForRow("omega.REG", issues), i => i.Code == "omega_sum");
        Assert.Contains(ProfileParameterRows.IssuesForGroup("weights", issues, keys), i => i.Code == "omega_sum");
        Assert.DoesNotContain(ProfileParameterRows.IssuesForGroup("weights", issues, keys), i => i.Code == "quota_sum");
    }

    [Fact]
    public void TestTheJointTailLockIsAnIssueOfTheBandsGroup()
    {
        var draft = new RiskIndexParameters
        {
            Tau = 0.60, TauG = 0.50,
            Bands =
            [
                new RiskIndexBandDefinition { LabelPtBr = "Baixo", LabelEnUs = "Low", LowerInclusive = 0 },
                new RiskIndexBandDefinition { LabelPtBr = "Alto", LabelEnUs = "High", LowerInclusive = 40 },
                new RiskIndexBandDefinition { LabelPtBr = "Crítico", LabelEnUs = "Critical", LowerInclusive = 90 }
            ]
        };
        var issues = RiskIndexProfileRules.Validate(RiskIndexMode.Interim, draft, null, null, null).Errors;

        // τ·τ_G·L_top = 0.6·0.5·90 = 27 < L_2 = 40.
        var tail = Assert.Single(issues, i => i.Code == "tail_lock");
        Assert.Equal("tau_g", tail.ParameterKey);
        Assert.Contains(ProfileParameterRows.IssuesForRow("tau_g", issues), i => i.Code == "tail_lock");
    }

    [Fact]
    public void TestChangedGroupsMarkTheEditedGroupsAndTheSources()
    {
        var saved = new RiskIndexParameters();
        var edited = saved with { Tau = 0.85, FreshnessDays = RiskSourceCatalogue.WithWindow(saved, "V1-DEV", 10).FreshnessDays };

        var groups = ProfileParameterRows.ChangedGroups(saved, edited);

        Assert.Contains("aggregation", groups);
        Assert.Contains("sources", groups);
        Assert.DoesNotContain("weights", groups);
        Assert.Empty(ProfileParameterRows.ChangedGroups(saved, saved));
    }

    [Fact]
    public void TestDisplayShowsNumbersWithoutJsonQuotes()
    {
        Assert.Equal("—", ProfileParameterRows.Display(null));
        Assert.Equal("InherentRisk", ProfileParameterRows.Display(JsonValue.Create("InherentRisk")));
        Assert.Equal("[1,2]", ProfileParameterRows.Display(new JsonArray(1, 2)));
    }
}

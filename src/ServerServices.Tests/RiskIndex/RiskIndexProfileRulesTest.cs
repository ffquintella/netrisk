using System.Collections.Generic;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Model.RiskIndex;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// M53, T250 (S39 §12.2–§12.4; S40 §6.2.3, §12.2 <c>RiskIndexProfileRulesTest</c>): the structural and cross-field rules
/// invalidate a draft; the pace rules only mark it a rebaseline proposal; the first profile of all has no pace rule and
/// always needs the Board.
/// </summary>
[TestSubject(typeof(RiskIndexProfileRules))]
public class RiskIndexProfileRulesTest
{
    private static readonly RiskIndexParameters Default = new();

    private static RiskIndexValidationResultDto Validate(RiskIndexParameters draft, RiskIndexParameters? baseline = null,
        RiskIndexParameters? quarter = null, RiskIndexMode mode = RiskIndexMode.Interim, RiskIndexMode? baselineMode = null,
        string? preset = null, string? baselinePreset = null) =>
        RiskIndexProfileRules.Validate(mode, draft, baseline is null ? null : baselineMode ?? RiskIndexMode.Interim, baseline, quarter,
            preset, baselinePreset);

    private static IEnumerable<string> Codes(RiskIndexValidationResultDto r) => r.Errors.Select(e => e.Code);

    [Fact]
    public void TestTheDefaultProfileAndTheActivatablePresetsAreValid()
    {
        Assert.True(Validate(Default).IsValid);
        foreach (var preset in RiskIndexPresets.All.Where(RiskIndexPresets.IsActivatable))
            Assert.True(Validate(RiskIndexPresets.Parameters(preset)).IsValid, preset);
    }

    [Fact]
    public void TestPureMeanIsNotAValidProfile()
    {
        var result = Validate(RiskIndexPresets.Parameters(RiskIndexPresets.PureMean));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, e => e.ParameterKey == "p");
        Assert.False(RiskIndexPresets.IsActivatable(RiskIndexPresets.PureMean));
    }

    [Fact]
    public void TestTheWeightsSumToOneOverAtLeastThreeCategories()
    {
        var sum = Validate(Default with { Omega = Default.Omega with { Reg = 0.40 } });
        var two = Validate(Default with { Omega = new RiskIndexCategoryWeights { Reg = 0.5, Exp = 0.5, Ame = 0, Ctl = 0, Gov = 0 } });
        var ter = Validate(Default with { Omega = Default.Omega with { Ter = 0.05, Gov = 0.10 } });

        Assert.Contains("omega_sum", Codes(sum));
        Assert.Contains("omega_min_categories", Codes(two));
        Assert.Contains(ter.Errors, e => e is { Code: "locked_parameter", ParameterKey: "omega.TER" });
    }

    /// <summary>S39 §12.4: τ 0.60 with τ_G 0.50 leaves a lone R = 1 object at 21 &lt; 31; τ 0.60 needs τ_G ≥ 0.74.</summary>
    [Theory]
    [InlineData(0.60, 0.50, false)]
    [InlineData(0.60, 0.73, false)]
    [InlineData(0.60, 0.74, true)]
    [InlineData(0.80, 0.70, true)]
    public void TestTheJointTailLock(double tau, double tauG, bool valid)
    {
        var result = Validate(Default with { Tau = tau, TauG = tauG });

        Assert.Equal(valid, !Codes(result).Contains("tail_lock"));
    }

    [Fact]
    public void TestTheRegisterBandsPresetNeedsTauTimesTauGAtLeastZeroPointFortyFive()
    {
        RiskIndexBandDefinition Band(string label, int lower) => new() { LabelPtBr = label, LabelEnUs = label, LowerInclusive = lower };
        var register = new List<RiskIndexBandDefinition> { Band("A", 0), Band("B", 40), Band("C", 90) };

        Assert.Contains("tail_lock", Codes(Validate(Default with { Bands = register, Tau = 0.62, TauG = 0.70 })));
        Assert.DoesNotContain("tail_lock", Codes(Validate(Default with { Bands = register, Tau = 0.65, TauG = 0.70 })));
    }

    [Fact]
    public void TestTheLocksCannotBeMoved()
    {
        Assert.Contains(Validate(Default with { AcceptanceDiscount = 0.1 }).Errors,
            e => e is { Code: "locked_parameter", ParameterKey: "desconto_aceite" });
        Assert.Contains(Validate(Default with { GateAFloor = 60 }).Errors, e => e.ParameterKey == "piso_portao_a");
    }

    [Fact]
    public void TestTheCrossFieldRules()
    {
        Assert.Contains("exploitation_order", Codes(Validate(Default with { NoExploitationCeiling = 0.95, ExploitationFloor = 0.90 })));
        Assert.Contains("incident_window_too_short", Codes(Validate(Default with { IncidentHalfLifeDays = 60, IncidentWindowDays = 120 })));
        Assert.Contains("ssc_anchor_endpoints", Codes(Validate(Default with
        {
            SscAnchors = new Dictionary<int, double> { [100] = 0.1, [0] = 1 }
        })));
        Assert.Contains("anchors_not_monotonic", Codes(Validate(Default with
        {
            SeverityAnchors = new Dictionary<int, double> { [1] = 0.5, [2] = 0.4, [3] = 0.75, [4] = 0.95 }
        })));
        Assert.Contains("quota_sum", Codes(Validate(Default with
        {
            PopulationQuotas = new Dictionary<string, IReadOnlyDictionary<string, double>>
            {
                ["GOV"] = new Dictionary<string, double> { ["Scenarios"] = 0.6, ["Assets"] = 0.6 }
            }
        })));
        Assert.Contains("unknown_source", Codes(Validate(Default with { EnabledSources = ["NOPE"] })));
        Assert.Contains("register_copies_incoherent", Codes(Validate(Default with
        {
            ReviewCadence = new RiskIndexReviewCadence { VeryHigh = 90, High = 30, Medium = 120, Low = 240 }
        })));
        Assert.Contains("bands_invalid", Codes(Validate(Default with
        {
            Bands = [new RiskIndexBandDefinition { LabelPtBr = "A", LabelEnUs = "A", LowerInclusive = 5 }]
        })));
        Assert.Contains("full_mode_prerequisites_missing", Codes(Validate(Default, mode: RiskIndexMode.Full)));
    }

    [Fact]
    public void TestAValueOutsideItsRangeIsRefusedWithItsKey()
    {
        var result = Validate(Default with { P = 17, MA = 0.1 });

        Assert.Contains(result.Errors, e => e is { Code: "parameter_out_of_range", ParameterKey: "p" });
        Assert.Contains(result.Errors, e => e is { Code: "parameter_out_of_range", ParameterKey: "m_a" });
    }

    // --- pace rules (S39 §12.4) ----------------------------------------------------------------------------------------

    [Fact]
    public void TestTheFirstProfileHasNoPaceRuleAndNeedsTheBoard()
    {
        var result = Validate(RiskIndexPresets.Parameters(RiskIndexPresets.RegisterCentred), preset: RiskIndexPresets.RegisterCentred);

        Assert.True(result.IsValid);
        Assert.False(result.RequiresRebaseline);
        Assert.True(result.RequiresBoardRef);
    }

    /// <summary>S40 §12.2: ω_REG +0.10 is submitted as a rebaseline proposal, never refused.</summary>
    [Fact]
    public void TestOmegaBeyondThePaceIsARebaselineNotAnError()
    {
        var draft = Default with { Omega = Default.Omega with { Reg = 0.40, Exp = 0.20 } };

        var result = Validate(draft, Default);

        Assert.True(result.IsValid);
        Assert.True(result.RequiresRebaseline);
        Assert.Equal(new[] { RiskIndexRebaselineTrigger.OmegaPace }, result.RebaselineTriggers);
        Assert.Equal(new[] { "omega" }, result.ChangedKeys); // the vector counts as one parameter
        Assert.False(result.RequiresBoardRef);
    }

    [Fact]
    public void TestOmegaWithinThePaceIsNotARebaseline()
    {
        var result = Validate(Default with { Omega = Default.Omega with { Reg = 0.35, Exp = 0.25 } }, Default);

        Assert.False(result.RequiresRebaseline);
    }

    /// <summary>The quarter's baseline: two approvals in one quarter cannot move ω by ±0.10 in two steps of 0.05.</summary>
    [Fact]
    public void TestThePaceIsMeasuredAgainstTheQuartersBaseline()
    {
        var quarterStart = Default;
        var afterFirstStep = Default with { Omega = Default.Omega with { Reg = 0.35, Exp = 0.25 } };
        var secondStep = Default with { Omega = Default.Omega with { Reg = 0.40, Exp = 0.20 } };

        var result = Validate(secondStep, afterFirstStep, quarterStart);

        Assert.Contains(RiskIndexRebaselineTrigger.OmegaPace, result.RebaselineTriggers);
    }

    [Fact]
    public void TestMoreThanTwoParametersIsARebaseline()
    {
        var two = Validate(Default with { P = 5, Rho = 0.2 }, Default);
        var three = Validate(Default with { P = 5, Rho = 0.2, MA = 0.5 }, Default);

        Assert.False(two.RequiresRebaseline);
        Assert.Equal(new[] { RiskIndexRebaselineTrigger.ParameterCount }, three.RebaselineTriggers);
    }

    [Fact]
    public void TestAPresetOrAModeChangeIsAlwaysARebaseline()
    {
        var preset = Validate(Default, Default, preset: RiskIndexPresets.Conservative, baselinePreset: RiskIndexPresets.Balanced);
        var mode = RiskIndexProfileRules.Validate(RiskIndexMode.Interim, Default, RiskIndexMode.Full, Default, null);

        Assert.Contains(RiskIndexRebaselineTrigger.PresetChange, preset.RebaselineTriggers);
        Assert.Contains(RiskIndexRebaselineTrigger.ModeChange, mode.RebaselineTriggers);
        Assert.Contains(mode.Warnings, w => w.Code == "mode_change");
    }

    [Fact]
    public void TestChangingTheBandsNeedsTheBoard()
    {
        var bands = Default.Bands.Select(b => b.LowerInclusive == 70 ? b with { LowerInclusive = 75 } : b).ToList();

        Assert.True(Validate(Default with { Bands = bands }, Default).RequiresBoardRef);
        Assert.False(Validate(Default, Default).RequiresBoardRef);
    }

    // --- diff, schema, replay --------------------------------------------------------------------------------------------

    [Fact]
    public void TestTheDiffAndTheReplayableKeys()
    {
        var draft = Default with { Omega = Default.Omega with { Reg = 0.35, Exp = 0.25 }, Tau = 0.85 };

        var diff = RiskIndexProfileRules.Diff(Default, draft);

        Assert.Contains(diff, c => c is { Key: "omega.REG", Changed: true, Group: "weights" });
        Assert.Contains(diff, c => c is { Key: "tau", Changed: true });
        Assert.Contains(diff, c => c is { Key: "p", Changed: false });
        Assert.True(RiskIndexProfileRules.IsReplayable(RiskIndexProfileRules.ChangedKeys(Default, draft)));
        Assert.False(RiskIndexProfileRules.IsReplayable(RiskIndexProfileRules.ChangedKeys(Default, Default with { InternetMultiplier = 1.3 })));
    }

    [Fact]
    public void TestTheSchemaListsEveryScalarWithItsDefault()
    {
        var schema = RiskIndexProfileRules.Schema();

        Assert.Contains(schema.Items, i => i is { Key: "tau", Min: "0.6", Max: "0.95", Default: "0.8" });
        Assert.Contains(schema.Items, i => i is { Key: "desconto_aceite", Locked: true });
        Assert.Contains(schema.Items, i => i.Key == "faixas");
        Assert.Equal(schema.Items.Count, schema.Items.Select(i => i.Key).Distinct().Count());
    }
}

using System;
using System.Linq;
using DAL.Enums;
using JetBrains.Annotations;
using Model.RiskFlags;
using Xunit;
using GateA = Tools.RiskFlags.GateA;

namespace Tools.Tests.RiskFlags;

/// <summary>
/// Stage 9.5 (S46 §4.1, §4.7, §8 GA1–GA3, RS2) — the Gate A predicate and the catalogue it reads. These are
/// what the ICR (S39 PA-1) consumes, so their contents are pinned here, not only exercised.
/// </summary>
[TestSubject(typeof(GateA))]
public class GateATest
{
    /// <summary>GA1 — flags 1, 2, 3 and the condition "no legitimate acceptance" are Gate A; nothing else is.</summary>
    [Fact]
    public void TestGA1_OnlyFlags123AndNoLegitimateAcceptanceAreConditions()
    {
        Assert.Equal(
            [RiskFlagCode.HumanSafety, RiskFlagCode.LegalRegulatory, RiskFlagCode.KnownExploitation,
                RiskFlagCode.NoLegitimateAcceptance],
            Enum.GetValues<RiskFlagCode>().Where(GateA.IsCondition));

        // The catalogue says the same thing the predicate does.
        Assert.All(RiskFlagCatalogue.All, d => Assert.Equal(GateA.IsCondition(d.Code), d.NonDiscretionary));
    }

    /// <summary>GA2 — flag 4 never triggers Gate A, whatever its weight (S46 D5, the answer to S43 R9).</summary>
    [Fact]
    public void TestGA2_Flag4IsNeverGateA()
    {
        Assert.False(GateA.Holds([RiskFlagCode.CriticalProcessContinuity]));
        Assert.False(GateA.Holds([RiskFlagCode.CriticalProcessContinuity, RiskFlagCode.SensitiveData,
            RiskFlagCode.ArtificialIntelligence, RiskFlagCode.HighUncertainty]));
        Assert.True(GateA.Holds([RiskFlagCode.CriticalProcessContinuity, RiskFlagCode.KnownExploitation]));
        Assert.False(GateA.Holds([]));
    }

    /// <summary>GA3 — conditions come back in code order without repeats, and round-trip through storage.</summary>
    [Fact]
    public void TestGA3_ConditionsAreOrderedDistinctAndRoundTrip()
    {
        var conditions = GateA.ConditionsAmong([RiskFlagCode.NoLegitimateAcceptance, RiskFlagCode.KnownExploitation,
            RiskFlagCode.SensitiveData, RiskFlagCode.HumanSafety, RiskFlagCode.KnownExploitation]);

        Assert.Equal([RiskFlagCode.HumanSafety, RiskFlagCode.KnownExploitation, RiskFlagCode.NoLegitimateAcceptance],
            conditions);
        Assert.Equal("1,3,12", GateA.Format(conditions));
        Assert.Equal(conditions, GateA.Parse("12, 3,1,3"));
        Assert.Equal([RiskFlagCode.HumanSafety], GateA.Parse("1,x,99,"));
        Assert.Empty(GateA.Parse(null));
    }

    /// <summary>The refusal names the action, each condition and its basis, and says Gate A precedes the appetite.</summary>
    [Fact]
    public void TestTheRefusalNamesTheConditionsAndTheirBases()
    {
        var text = GateA.Explain(GateAAction.Accept,
            [(RiskFlagCode.HumanSafety, "declared: patients"), (RiskFlagCode.NoLegitimateAcceptance, "")]);

        Assert.Contains("cannot be accepted", text);
        Assert.Contains("flag 1 (Life, health or human safety): declared: patients", text);
        Assert.Contains("Risk without legitimate acceptance", text);
        Assert.Contains("before the risk appetite", text);
        Assert.Contains("cannot be closed", GateA.Explain(GateAAction.Close, [(RiskFlagCode.KnownExploitation, "x")]));
        Assert.Contains("cannot be deleted", GateA.Explain(GateAAction.Delete, [(RiskFlagCode.KnownExploitation, "x")]));
    }

    /// <summary>
    /// RS2 — exactly the eleven methodology flags, numbered 1–11 in order, plus the Gate A condition with no
    /// number; and the origin of each is the one S46 §4.1 declares.
    /// </summary>
    [Fact]
    public void TestRS2_TheCatalogueHasTheElevenFlagsAndTheirDeclaredOrigins()
    {
        Assert.Equal(Enumerable.Range(1, 11).Cast<int?>(), RiskFlagCatalogue.Flags.Select(f => f.Number));
        Assert.Equal(Enumerable.Range(1, 11), RiskFlagCatalogue.Flags.Select(f => (int)f.Code));
        Assert.Null(RiskFlagCatalogue.NoLegitimateAcceptance.Number);
        Assert.Equal(12, RiskFlagCatalogue.All.Count);
        Assert.All(RiskFlagCatalogue.All, d => Assert.True(d.Declarable));

        Assert.Equal(RiskFlagDerivation.Kev, RiskFlagCatalogue.Find(RiskFlagCode.KnownExploitation)!.Derivation);
        Assert.Equal(RiskFlagDerivation.Bia, RiskFlagCatalogue.Find(RiskFlagCode.CriticalProcessContinuity)!.Derivation);
        Assert.Equal(RiskFlagDerivation.DataClassification, RiskFlagCatalogue.Find(RiskFlagCode.SensitiveData)!.Derivation);
        // Stage 9.7 (S48 §4.8, D7): flag 8 became derivable from the inherent tail, and no longer names a later stage.
        Assert.Equal(RiskFlagDerivation.TailStatistics,
            RiskFlagCatalogue.Find(RiskFlagCode.LowProbabilityCatastrophic)!.Derivation);
        Assert.Null(RiskFlagCatalogue.Find(RiskFlagCode.LowProbabilityCatastrophic)!.DerivableIn);
        Assert.Equal(
            [RiskFlagCode.KnownExploitation, RiskFlagCode.CriticalProcessContinuity, RiskFlagCode.SensitiveData,
             RiskFlagCode.LowProbabilityCatastrophic, RiskFlagCode.ArtificialIntelligence],
            RiskFlagCatalogue.All.Where(d => d.Derivation != RiskFlagDerivation.None).Select(d => d.Code));

        // Stage 9.10 (S51 D11): flag 7 stays declared — the concentration report is the declarer's evidence — and no longer
        // names a later stage that would derive it.
        Assert.Equal(RiskFlagDerivation.None, RiskFlagCatalogue.Find(RiskFlagCode.ThirdPartyConcentration)!.Derivation);
        Assert.Null(RiskFlagCatalogue.Find(RiskFlagCode.ThirdPartyConcentration)!.DerivableIn);
        Assert.Contains("/ThirdParties/Concentration", RiskFlagCatalogue.Find(RiskFlagCode.ThirdPartyConcentration)!.Description);

        // Stage 9.11 (S52 D6, D7): flag 5 is derived from the classification and from the LGPD catalogue, and names no later
        // stage; flag 2 — a Gate A condition — stays declared, with the risk's legal requirements as the evidence.
        var flag5 = RiskFlagCatalogue.Find(RiskFlagCode.SensitiveData)!;
        Assert.Null(flag5.DerivableIn);
        Assert.Contains("LGPD data catalogue", flag5.Description);
        var flag2 = RiskFlagCatalogue.Find(RiskFlagCode.LegalRegulatory)!;
        Assert.Equal(RiskFlagDerivation.None, flag2.Derivation);
        Assert.True(flag2.NonDiscretionary);
        Assert.Null(flag2.DerivableIn);
        Assert.Contains("/DataCatalogue/Risks/", flag2.Description);

        // Stage 9.12 (S53 §4.6, amending S46 §4.1): flag 11 is derived from the AI model inventory — a link to a model that
        // is not retired — names no later stage, stays declarable and is not a Gate A condition.
        var flag11 = RiskFlagCatalogue.Find(RiskFlagCode.ArtificialIntelligence)!;
        Assert.Equal(RiskFlagDerivation.AiModelInventory, flag11.Derivation);
        Assert.Null(flag11.DerivableIn);
        Assert.True(flag11.Declarable);
        Assert.False(flag11.NonDiscretionary);
        Assert.Contains("/AiModels/Risks/", flag11.Description);
        Assert.All(RiskFlagCatalogue.All, d => Assert.Null(d.DerivableIn));

        Assert.False(RiskFlagCatalogue.IsDefined((RiskFlagCode)13));
        Assert.False(RiskFlagCatalogue.IsDefined((RiskFlagCode)0));
    }
}

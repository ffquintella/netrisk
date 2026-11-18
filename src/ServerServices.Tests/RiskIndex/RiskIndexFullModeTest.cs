using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using NSubstitute;
using Model.RiskIndex;
using ServerServices.RiskIndex;
using ServerServices.RiskIndex.Closure;
using ServerServices.RiskIndex.Engine;
using Xunit;

namespace ServerServices.Tests.RiskIndex;

/// <summary>
/// M59 (S39 §15.1): the full mode consumes Track 9 — the M39 chain (T284), the M41 BIA criticality, the M42 prioritization
/// and the M49 data sensitivity (T285), M43's Gate A (T286), M45/M46's Gate B, M40's confidence and M44's target (T287) and
/// the M48 register (T288). Each case runs the same data in both modes: the interim snapshot must not move.
/// </summary>
[TestSubject(typeof(RiskIndexObjectCollector))]
public class RiskIndexFullModeTest : RiskIndexSeedTestBase
{
    // The chain: the host TST-03 (crit 1) runs APP2 (crit 2), which implements SVC (crit 3), which serves P2 (declared 2,
    // BIA MTPD 24 h → 4) and handles the data record D60.
    private const int Svc = 50, P2 = 21, App2 = 31, D60 = 60, Act = 22;

    public RiskIndexFullModeTest()
    {
        SeedUnscoped(ctx =>
        {
            var id = 7000;
            Entity(ctx, Svc, "itService", null);
            AddEntityProperties(ctx, Svc, ref id, ("processes", P2.ToString()), ("applications", App2.ToString()),
                ("data", D60.ToString()), ("criticality", "3"));
            Entity(ctx, P2, "businessProcess", UnitX);
            AddEntityProperties(ctx, P2, ref id, ("criticality", "2"));
            Entity(ctx, Act, "activity", P2);
            Entity(ctx, App2, "application", null);
            AddEntityProperties(ctx, App2, ref id, ("criticality", "2"));
            Entity(ctx, D60, "organizationData", null);
            ctx.BusinessImpactAnalyses.Add(new BusinessImpactAnalysis
            {
                Id = 1, EntityId = P2, MtpdMinutes = 1_440, AssessedAt = Now.AddDays(-30), CreatedAt = Now.AddDays(-30)
            });
            ctx.HostApplicationLinks.Add(new HostApplicationLink
            {
                Id = 1, HostId = Tst, EntityId = App2, Source = AssetContextSources.Cmdb, CreatedAt = Now.AddDays(-60),
                LastSeenAt = Now.AddDays(-2)
            });
        });
    }

    private static void Entity(DAL.Context.AuditableContext ctx, int id, string definition, int? parent) =>
        ctx.Entities.Add(new Entity
        {
            Id = id, DefinitionName = definition, DefinitionVersion = "2.7", Status = "active", Parent = parent,
            Created = Now.AddYears(-1), Updated = Now.AddYears(-1)
        });

    private static RiskIndexParameters Parameters(params string[] extra)
    {
        var sources = RiskIndexSources.DefaultEnabled();
        sources.AddRange(extra);
        return new RiskIndexParameters { EnabledSources = sources };
    }

    private async Task<RiskIndexObjectSet> CollectAsync(RiskIndexMode mode, RiskIndexParameters? p = null,
        RiskIndexCollectionOptions? options = null)
    {
        await using var db = OpenContext();
        return await RiskIndexObjectCollector.CollectAsync(db, p ?? Parameters(RiskIndexSources.CmdbRelations), mode, Now, options);
    }

    private static RiskIndexObjectRow Row(RiskIndexObjectSet set, RiskIndexObjectKind kind, int id, RiskIndexCategory category) =>
        set.Rows.Single(r => r.Key == new IcrObjectKey(kind, id.ToString()) && r.Category == category);

    // ---------------- T284: the M39 chain ----------------

    [Fact]
    public async Task TestT284AHostRunningAnApplicationIsAttributedToItAndToTheDataItsServiceHandlesInFullModeOnly()
    {
        var interim = Row(await CollectAsync(RiskIndexMode.Interim), RiskIndexObjectKind.Host, Tst, RiskIndexCategory.Exp);
        var full = Row(await CollectAsync(RiskIndexMode.Full), RiskIndexObjectKind.Host, Tst, RiskIndexCategory.Exp);

        Assert.Equal([UnitX], interim.Attribution);
        Assert.Equal([UnitX, App2, D60], full.Attribution);
    }

    [Fact]
    public async Task TestT284WithoutCmdbRelTheHostKeepsItsOwnAttributionAndCriticality()
    {
        var full = Row(await CollectAsync(RiskIndexMode.Full, Parameters()), RiskIndexObjectKind.Host, Tst, RiskIndexCategory.Exp);

        Assert.Equal([UnitX], full.Attribution);
        Assert.Equal(1, full.Criticality);
    }

    /// <summary>S39 §5.2: crit_herdada is the highest of the application, the service and the process — P2 at its BIA's 4.</summary>
    [Fact]
    public async Task TestT284AndT285TheHostInheritsTheHighestChainCriticalityThroughTheBia()
    {
        var interim = Row(await CollectAsync(RiskIndexMode.Interim), RiskIndexObjectKind.Host, Tst, RiskIndexCategory.Exp);
        var full = Row(await CollectAsync(RiskIndexMode.Full), RiskIndexObjectKind.Host, Tst, RiskIndexCategory.Exp);

        Assert.Equal((1, "unknown"), (interim.Criticality, interim.CriticalitySource));
        Assert.Equal((4, RiskIndexObjectCollector.InheritedCriticalitySource), (full.Criticality, full.CriticalitySource));
        Assert.True(full.Weight > interim.Weight);
    }

    /// <summary>S39 §5.2 Δ_dados in full mode: a served data record the M49 catalogue marks sensitive adds the step.</summary>
    [Fact]
    public async Task TestT285ADataRecordTheCatalogueMarksSensitiveAddsTheClassificationStep()
    {
        SeedUnscoped(ctx => ctx.DataCatalogueEntries.Add(new DataCatalogueEntry
        {
            Id = 1, EntityId = D60, PersonalData = PersonalDataCategory.SensitivePersonal, CreatedAt = Now.AddDays(-10)
        }));

        var full = Row(await CollectAsync(RiskIndexMode.Full), RiskIndexObjectKind.Host, Tst, RiskIndexCategory.Exp);
        var interim = Row(await CollectAsync(RiskIndexMode.Interim), RiskIndexObjectKind.Host, Tst, RiskIndexCategory.Exp);

        Assert.Equal(5, full.Criticality);
        Assert.Equal(1, interim.Criticality);
    }

    [Fact]
    public async Task TestT284TheFullModeClosureOfAProcessFollowsTheChainDownToItsServiceApplicationAndData()
    {
        await using var db = OpenContext();
        var interim = await RiskIndexObjectCollector.ReadEntityMapAsync(db);
        var full = await RiskIndexObjectCollector.ReadEntityMapAsync(db, default, RiskIndexMode.Full);

        Assert.DoesNotContain(Svc, interim.Closure(P2).Members);
        Assert.Superset(new HashSet<int> { P2, Act, Svc, App2, D60 }, full.Closure(P2).Members.ToHashSet());
        Assert.True(full.Closure(P2).LinkedOnlyThroughEav);
        Assert.Contains(Svc, full.Closure(UnitX).Members);
        Assert.Equal([App2, D60], full.Closure(Svc).Members.Where(m => m != Svc).Order());
    }

    /// <summary>M39 links a risk to its process, service and data (T146): the full mode attributes it to them too.</summary>
    [Fact]
    public async Task TestT284AScenarioIsAttributedToItsChainLinksInFullModeOnly()
    {
        SeedUnscoped(ctx => ctx.RiskChainLinks.Add(new RiskChainLink
        {
            Id = 1, RiskId = 1, ChainLevel = RiskChainLevel.ItService, EntityId = Svc, CreatedAt = Now.AddDays(-3)
        }));

        var interim = Row(await CollectAsync(RiskIndexMode.Interim), RiskIndexObjectKind.Scenario, 1, RiskIndexCategory.Reg);
        var full = Row(await CollectAsync(RiskIndexMode.Full), RiskIndexObjectKind.Scenario, 1, RiskIndexCategory.Reg);

        Assert.DoesNotContain(Svc, interim.Attribution);
        Assert.Contains(Svc, full.Attribution);
    }

    /// <summary>
    /// S39 §10.2: a full-mode process holds its assets through the chain (EXP no longer partial); M39 has no activity–asset
    /// link, so an activity's EXP stays unavailable — for that reason, not "requires M39".
    /// </summary>
    [Fact]
    public async Task TestT284AProcessExpIsWholeAndAnActivityExpIsUnavailableForWantOfALink()
    {
        var set = await CollectAsync(RiskIndexMode.Full);
        NodeMembers Members(int id, string type) => new()
        {
            Node = new RiskIndexNodeDefinition { ScopeKind = RiskIndexScopeKind.Entity, ScopeRef = id.ToString(), EntityId = id, EntityType = type },
            Closure = set.Map.Closure(id).Members,
            Objects = NodeMembership.ObjectsOf(set.Rows.GroupBy(r => r.Key)
                .Select(g => new ObjectAttribution(g.Key, g.SelectMany(r => r.Attribution).Distinct().ToList(), null)), set.Map.Closure(id).Members)
        };

        var process = RiskIndexDayCalculator.BuildNodeInput(Members(P2, "businessProcess"), set, RiskIndexMode.Full, Parameters())
            .Categories.Single(c => c.Category == RiskIndexCategory.Exp);
        var activity = RiskIndexDayCalculator.BuildNodeInput(Members(Act, "activity"), set, RiskIndexMode.Full, Parameters())
            .Categories.Single(c => c.Category == RiskIndexCategory.Exp);

        Assert.False(process.Partial);
        Assert.Contains(process.Objects, o => o.Key == new IcrObjectKey(RiskIndexObjectKind.Host, Tst.ToString()));
        Assert.True(activity.ForcedUnavailable);
        Assert.Equal(RiskIndexDayCalculator.NoActivityAssetLinkNote, activity.Note);
    }

    // ---------------- T285: the BIA and the M42 prioritization ----------------

    [Fact]
    public async Task TestT285ABiaCriticalityWinsOverTheDeclaredOneInFullModeOnly()
    {
        await using var db = OpenContext();
        var interim = await RiskIndexObjectCollector.ReadEntityMapAsync(db);
        var full = await RiskIndexObjectCollector.ReadEntityMapAsync(db, default, RiskIndexMode.Full);

        Assert.Equal(((int?)2, (string?)EntityMap.CriticalitySourceEntity), interim.CriticalityWithSource(P2));
        Assert.Equal(((int?)4, (string?)EntityMap.CriticalitySourceBia), full.CriticalityWithSource(P2));
        Assert.Equal(((int?)5, (string?)EntityMap.CriticalitySourceEntity), full.CriticalityWithSource(Enrolment));
    }

    /// <summary>
    /// S39 §3.1 full mode: M42's T165 decides <c>m_x</c> — EPSS 0.20 is "likely" at T165's 0.10 whatever the profile's interim
    /// <c>epss_limiar</c> (0.30 here), and the tier is kept as a flag. The interim keeps its own threshold.
    /// </summary>
    [Fact]
    public async Task TestT285TheT165PrioritizationReplacesTheInterimEpssThreshold()
    {
        SeedUnscoped(ctx =>
        {
            var finding = Finding(WsAdm, "nessus", cvss3: 5.0f, cve: "CVE-2026-0100");
            finding.EpssScore = 0.20;
            ctx.Vulnerabilities.Add(finding);
        });
        var p = Parameters() with { EpssThreshold = 0.30 };

        var interim = Row(await CollectAsync(RiskIndexMode.Interim, p), RiskIndexObjectKind.Host, WsAdm, RiskIndexCategory.Exp);
        var full = Row(await CollectAsync(RiskIndexMode.Full, p), RiskIndexObjectKind.Host, WsAdm, RiskIndexCategory.Exp);

        Assert.DoesNotContain("m_x", interim.Signals.Single().Flags);
        Assert.Contains("m_x", full.Signals.Single().Flags);
        Assert.Contains("p3", full.Signals.Single().Flags);
        Assert.True(full.R > interim.R);
    }

    /// <summary>S39 §3.1, D-11: Vision One's exploit attempts, persisted apart (T275), are observed exploitation in full mode.</summary>
    [Fact]
    public async Task TestT285VisionOneExploitAttemptsTakeTheExploitationFloorInFullModeOnly()
    {
        SeedUnscoped(ctx =>
        {
            var finding = Finding(WsAdm, "trendmicro-visionone", cvss3: 6.0f, cve: "CVE-2026-0101");
            finding.ExploitAttemptCount = 3;
            ctx.Vulnerabilities.Add(finding);
        });

        var interim = Row(await CollectAsync(RiskIndexMode.Interim), RiskIndexObjectKind.Host, WsAdm, RiskIndexCategory.Exp);
        var full = Row(await CollectAsync(RiskIndexMode.Full), RiskIndexObjectKind.Host, WsAdm, RiskIndexCategory.Exp);

        Assert.DoesNotContain("exploited", interim.Signals.Single().Flags);
        Assert.Contains("exploited", full.Signals.Single().Flags);
        Assert.Equal(0.95, full.Signals.Single().Value, 6);
    }

    // ---------------- T286, T287: Gate A, Gate B, confidence, target ----------------

    [Fact]
    public async Task TestT286AndT287TheScenarioCarriesTrack9sVerdictsInFullModeOnly()
    {
        SeedUnscoped(ctx =>
        {
            ctx.RiskFlags.Add(new RiskFlag { Id = 1, RiskId = 1, Flag = RiskFlagCode.LegalRegulatory, Declared = true, DeclaredReason = "LGPD", CreatedAt = Now.AddDays(-2) });
            ctx.RiskFlags.Add(new RiskFlag { Id = 2, RiskId = 1, Flag = RiskFlagCode.SensitiveData, Derived = true, CreatedAt = Now.AddDays(-2) });
            ctx.RiskTargets.Add(new RiskTarget { Id = 1, RiskId = 1, TargetScore = 4.0m, Rationale = "plan", CreatedAt = Now.AddDays(-2) });
            ctx.Risks.Find(1)!.EvidenceConfidence = EvidenceConfidence.Indicative;
        });

        var interim = Row(await CollectAsync(RiskIndexMode.Interim), RiskIndexObjectKind.Scenario, 1, RiskIndexCategory.Reg)
            .ScenarioInputs!;
        var full = Row(await CollectAsync(RiskIndexMode.Full), RiskIndexObjectKind.Scenario, 1, RiskIndexCategory.Reg)
            .ScenarioInputs!;

        Assert.Null(interim.GateAConditions);
        Assert.Null(interim.GateBTail);
        Assert.Equal([(int)RiskFlagCode.LegalRegulatory], full.GateAConditions!);
        Assert.Equal(((int?)EvidenceConfidence.Indicative, 4.0), (full.EvidenceConfidence, full.TargetScore));
        Assert.Equal((RiskIndexGateBStates.NotConfigured, RiskIndexGateBStates.NotConfigured), (full.GateBTail, full.GateBIndicators));
    }

    /// <summary>S48 §4.7.2, S49 §4.8: Gate B as the decision path evaluates it — a tail above tolerance, a breached KRI.</summary>
    [Fact]
    public async Task TestT287GateBOnTheTailAndByIndicatorIsReadAsTheDecisionPathEvaluatesIt()
    {
        SeedUnscoped(ctx =>
        {
            ctx.RiskAppetiteTailLimits.Add(new RiskAppetiteTailLimit { Id = 1, AppetiteId = 1, MaxScenarioP95 = 100_000m, Rationale = "board", CreatedAt = Now.AddDays(-30) });
            ctx.RiskTailStatistics.Add(new RiskTailStatistics
            {
                Id = 1, RiskId = 1, Run = TailRun.Residual, Iterations = 10_000, Seed = 1, ConfidenceLevel = 0.95m, ExpectedLoss = 50_000,
                P95 = 250_000, P95CiLow = 240_000, P95CiHigh = 260_000, Cvar95 = 400_000, ComputedAt = Now.AddDays(-1), CreatedAt = Now.AddDays(-1)
            });
            ctx.Kris.Add(new Kri
            {
                Id = 1, Name = "Downtime", Category = KriCategory.Unavailability, Source = "ops", Unit = "h",
                Direction = KriDirection.HigherIsWorse, ToleranceThreshold = 4, ToleranceRationale = "SLA", MaxReadingAgeDays = 30,
                CreatedAt = Now.AddDays(-60)
            });
            ctx.KriRisks.Add(new KriRisk { Id = 1, KriId = 1, RiskId = 1, CreatedAt = Now.AddDays(-60) });
            ctx.KriReadings.Add(new KriReading { Id = 1, KriId = 1, Value = 9, ObservedAt = Now.AddDays(-2), CreatedAt = Now.AddDays(-2) });
        });

        var full = Row(await CollectAsync(RiskIndexMode.Full), RiskIndexObjectKind.Scenario, 1, RiskIndexCategory.Reg)
            .ScenarioInputs!;

        Assert.Equal((RiskIndexGateBStates.Exceeds, RiskIndexGateBStates.Exceeds), (full.GateBTail, full.GateBIndicators));
    }

    /// <summary>
    /// S39 §7 PA-1: every node holding a scenario on which M43's Gate A predicate holds is floored at φ_A, with the
    /// unfloored value kept and the trigger named; never in interim mode, never on an asset intersection.
    /// </summary>
    [Fact]
    public void TestT286TheGateAFloorLiftsTheHeadlineOfEveryNodeHoldingTheTriggerInFullModeOnly()
    {
        var map = EntityMap.Build([new EntityMapRow(UnitX, "organizationUnit", null)], []);
        var scenario = new IcrObjectKey(RiskIndexObjectKind.Scenario, "7");
        var host = new IcrObjectKey(RiskIndexObjectKind.Host, "8");
        var rows = new List<RiskIndexObjectRow>
        {
            new()
            {
                Key = scenario, Category = RiskIndexCategory.Reg, Population = RiskIndexPopulation.Scenarios, R = 0.3, Weight = 1,
                State = RiskIndexObjectState.Assessed, Attribution = [UnitX], ScopeEntityId = UnitX,
                ScenarioInputs = new RiskIndexScenarioInputs { GateAConditions = [(int)RiskFlagCode.HumanSafety] }
            },
            new()
            {
                Key = host, Category = RiskIndexCategory.Exp, Population = RiskIndexPopulation.Assets, R = 0.2, Weight = 1,
                State = RiskIndexObjectState.Assessed, Attribution = [UnitX], ScopeEntityId = UnitX, AssetClass = RiskIndexAssetClass.Server
            }
        };
        var set = new RiskIndexObjectSet
        {
            Rows = rows, Map = map,
            SourceEnabled = new Dictionary<RiskIndexCategory, bool> { [RiskIndexCategory.Reg] = true, [RiskIndexCategory.Exp] = true }
        };
        RiskIndexDay Day(RiskIndexMode mode) => RiskIndexDayCalculator.Compose(set,
            new RiskIndexDayRequest { Parameters = new RiskIndexParameters(), Mode = mode, SnapshotInstant = Now }, RiskIndexComposeOptions.Replay);

        var full = Day(RiskIndexMode.Full);
        var interim = Day(RiskIndexMode.Interim);
        var unit = full.Nodes.Single(n => n.Node.EntityId == UnitX).Result;
        var servers = full.Nodes.Single(n => n.Node.AssetClass == RiskIndexAssetClass.Server).Result;

        Assert.True(unit.Unfloored < 70);
        Assert.Equal((70d, 70), (unit.Floored!.Value, unit.Displayed!.Value));
        var reason = Assert.Single(unit.FloorReasons);
        Assert.Equal((scenario, (int)RiskFlagCode.HumanSafety), (reason.Scenario, reason.Conditions.Single()));
        Assert.Empty(servers.FloorReasons);
        Assert.Equal(servers.Unfloored, servers.Floored);
        var interimUnit = interim.Nodes.Single(n => n.Node.EntityId == UnitX).Result;
        Assert.Equal(interimUnit.Unfloored, interimUnit.Floored);
        Assert.Empty(interimUnit.FloorReasons);
    }

    // ---------------- T288: the third-party register ----------------

    private void SeedSupplier(int id, ThirdPartyStatus status, (HecvatAnswer Answer, bool Critical)[]? hecvat = null) =>
        SeedUnscoped(ctx =>
        {
            ctx.ThirdParties.Add(new ThirdParty { Id = id, Name = $"Vendor {id}", Status = status, EntityId = UnitX, CreatedAt = Now.AddYears(-1) });
            ctx.ThirdPartyLinks.Add(new ThirdPartyLink { Id = id, ThirdPartyId = id, EntityId = Svc, Kind = ThirdPartyLinkKind.ItService, CreatedAt = Now.AddYears(-1) });
            if (hecvat is null) return;
            ctx.ThirdPartyAssessments.Add(new ThirdPartyAssessment
            {
                Id = id, ThirdPartyId = id, Variant = HecvatVariant.Lite, FrameworkVersion = "3.0", ExpectedQuestionCount = hecvat.Length,
                RespondedAt = Now.AddDays(-20), ValidUntil = Now.AddYears(1), CreatedAt = Now.AddDays(-20)
            });
            for (var i = 0; i < hecvat.Length; i++)
                ctx.ThirdPartyAssessmentAnswers.Add(new ThirdPartyAssessmentAnswer
                {
                    Id = id * 100 + i, AssessmentId = id, QuestionId = $"Q-{i:00}", Answer = hecvat[i].Answer,
                    PreferredAnswer = HecvatAnswer.Yes, Weight = 1, Critical = hecvat[i].Critical, CreatedAt = Now.AddDays(-20)
                });
        });

    private static RiskIndexCollectionOptions Concentration(int supplier, int processes) => new()
    {
        SupplierConcentration = new Dictionary<int, RiskIndexSupplierConcentration>
        {
            [supplier] = new(processes, Enumerable.Repeat(P2, Math.Min(1, processes)).ToList())
        }
    };

    [Fact]
    public async Task TestT288ASupplierIsScoredOnItsHecvatAndConcentrationInFullModeWithTheRegisterOnly()
    {
        var answers = Enumerable.Repeat((HecvatAnswer.Yes, false), 9).Append((HecvatAnswer.No, false)).ToArray();
        SeedSupplier(1, ThirdPartyStatus.Active, answers);
        var p = Parameters(RiskIndexSources.CmdbRelations, RiskIndexSources.ThirdPartyRegister);

        var full = await CollectAsync(RiskIndexMode.Full, p, Concentration(1, 2));
        var interim = await CollectAsync(RiskIndexMode.Interim, p, Concentration(1, 2));
        var off = await CollectAsync(RiskIndexMode.Full, Parameters(), Concentration(1, 2));

        var row = Row(full, RiskIndexObjectKind.Supplier, 1, RiskIndexCategory.Ter);
        Assert.Equal((RiskIndexPopulation.Suppliers, RiskIndexObjectState.Assessed), (row.Population, row.State));
        Assert.Equal(0.1, row.Factors.Single(f => f.Factor == RiskIndexFactors.TerDiligence).X, 6);
        Assert.Equal(0.75, row.Factors.Single(f => f.Factor == RiskIndexFactors.TerConcentration).X, 6);
        Assert.Equal((4, RiskIndexObjectCollector.InheritedCriticalitySource), (row.Criticality, row.CriticalitySource));
        Assert.Equal([UnitX, Svc], row.Attribution);
        Assert.True(full.SourceEnabled[RiskIndexCategory.Ter]);
        Assert.DoesNotContain(interim.Rows, r => r.Category == RiskIndexCategory.Ter);
        Assert.False(interim.SourceEnabled[RiskIndexCategory.Ter]);
        Assert.DoesNotContain(off.Rows, r => r.Category == RiskIndexCategory.Ter);
    }

    [Fact]
    public async Task TestT288ACriticalHecvatFailureReadsAsFullDiligenceRisk()
    {
        SeedSupplier(2, ThirdPartyStatus.Active, Enumerable.Repeat((HecvatAnswer.Yes, false), 9).Append((HecvatAnswer.No, true)).ToArray());

        var full = await CollectAsync(RiskIndexMode.Full, Parameters(RiskIndexSources.ThirdPartyRegister));

        var diligence = Row(full, RiskIndexObjectKind.Supplier, 2, RiskIndexCategory.Ter).Factors.Single(f => f.Factor == RiskIndexFactors.TerDiligence);
        Assert.Equal(1.0, diligence.X);
    }

    /// <summary>
    /// T203 consumed, not re-derived: the day calculator reads M48's concentration through <c>IThirdPartiesService</c> in full
    /// mode with TER-REG, and never asks for it otherwise.
    /// </summary>
    [Fact]
    public async Task TestT288TheDayCalculatorConsumesM48sConcentration()
    {
        SeedSupplier(6, ThirdPartyStatus.Active);
        var thirdParties = NSubstitute.Substitute.For<ServerServices.Interfaces.IThirdPartiesService>();
        thirdParties.GetConcentrationAsync().Returns(new Model.ThirdParties.ThirdPartyConcentrationReportDto
        {
            Suppliers = new Model.ThirdParties.ConcentrationDimensionDto
            {
                Entries =
                [
                    new Model.ThirdParties.ConcentrationEntryDto
                    {
                        ThirdPartyId = 6, DependentCriticalProcessCount = 1,
                        CriticalProcesses = [new Model.ThirdParties.CriticalProcessRefDto { EntityId = P2 }]
                    }
                ]
            }
        });
        var calculator = new RiskIndexDayCalculator(GetService<ServerServices.Services.IDalService>(), thirdParties);

        var interim = await calculator.ComputeAsync(new RiskIndexDayRequest
        {
            Parameters = Parameters(RiskIndexSources.ThirdPartyRegister), Mode = RiskIndexMode.Interim, SnapshotInstant = Now
        });
        await thirdParties.DidNotReceive().GetConcentrationAsync();
        var full = await calculator.ComputeAsync(new RiskIndexDayRequest
        {
            Parameters = Parameters(RiskIndexSources.ThirdPartyRegister), Mode = RiskIndexMode.Full, SnapshotInstant = Now
        });

        var row = full.Objects.Single(r => r.Key == new IcrObjectKey(RiskIndexObjectKind.Supplier, "6"));
        Assert.Equal(0.5, row.Factors.Single(f => f.Factor == RiskIndexFactors.TerConcentration).X, 6);
        Assert.Equal(4, row.Criticality);
        Assert.DoesNotContain(interim.Objects, r => r.Category == RiskIndexCategory.Ter);
    }

    /// <summary>S51 D7: no current, complete HECVAT is no diligence reading — the supplier is presumed, never compliant.</summary>
    [Fact]
    public async Task TestT288ASupplierWithoutACompleteHecvatIsPresumedAndOneNotSupplyingIsLeftOut()
    {
        SeedSupplier(3, ThirdPartyStatus.Active, [(HecvatAnswer.Yes, false), (HecvatAnswer.Unanswered, false)]);
        SeedSupplier(4, ThirdPartyStatus.Prospective);
        SeedSupplier(5, ThirdPartyStatus.Terminated);

        var full = await CollectAsync(RiskIndexMode.Full, Parameters(RiskIndexSources.ThirdPartyRegister));

        var row = Row(full, RiskIndexObjectKind.Supplier, 3, RiskIndexCategory.Ter);
        Assert.Equal(RiskIndexObjectState.Presumed, row.State);
        Assert.True(row.R > 0);
        Assert.DoesNotContain(row.Factors, f => f.Factor == RiskIndexFactors.TerDiligence);
        Assert.DoesNotContain(full.Rows, r => r.Key.Kind == RiskIndexObjectKind.Supplier && r.Key.Ref is "4" or "5");
    }
}

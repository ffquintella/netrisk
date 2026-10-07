using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.EntityFrameworkCore;
using Model.Continuity;
using Model.Exceptions;
using Model.RiskFlags;
using NSubstitute;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.RiskFlags;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.5 (S46 §8) — <see cref="RiskFlagsService"/> against the real model, scope filters and audit
/// interceptor on the EF in-memory provider: the guards F1–F6, the derivations D1–D6, the reversions R1–R3
/// (T173), the escalation E1–E3, the decision DC1–DC4, Top Risks TOP1–TOP5, the query Q1, the evidence EV1
/// and the schema RS1.
///
/// The organisation is the Stage 9.1 one (<see cref="RiskChainTestBase"/>): process 10 "Enrolment"
/// (criticality 5), service 20, data 50, units 100 and 200, user 7.
/// </summary>
[TestSubject(typeof(RiskFlagsService))]
public class RiskFlagsServiceInMemoryTest : RiskChainTestBase
{
    private const int Other = 8;
    private const int Level = 70;
    private const string Log4Shell = "CVE-2021-44228";

    private readonly INotificationEventPublisher _publisher = Substitute.For<INotificationEventPublisher>();

    public RiskFlagsServiceInMemoryTest()
    {
        SeedUnscoped(ctx => ctx.Users.Add(new User
        {
            Value = Other, Name = "manager", Login = "manager", Enabled = true, Type = "local", Salt = "s",
            Password = System.Text.Encoding.UTF8.GetBytes("p"), Email = "manager@x.test"
        }));
    }

    /// <summary>The service with a recording publisher, so the escalation can be counted.</summary>
    private RiskFlagsService Svc => new(GetService<Serilog.ILogger>(), GetService<IDalService>(),
        GetService<IContinuityService>(), _publisher);

    private Task Escalations(int count) => _publisher.Received(count).RiskGateAEscalatedAsync(Arg.Any<Risk>(),
        Arg.Any<double?>(), Arg.Any<IReadOnlyList<string>>(), Arg.Any<string>());

    private void OwnedBy(int riskId, int owner) =>
        SeedUnscoped(ctx => ctx.Risks.Single(r => r.Id == riskId).Owner = owner);

    private void Finding(int id, int riskId, string? cves, int entityId = UnitA,
        FindingStatus status = FindingStatus.Active) =>
        SeedUnscoped(ctx =>
        {
            var finding = new Vulnerability
            {
                Id = id, Title = $"Finding {id}", Cves = cves, EntityId = entityId, LifecycleStatus = status, Status = 1,
                DetectionCount = 1, FirstDetection = DateTime.UtcNow.AddDays(-10), LastDetection = DateTime.UtcNow
            };
            ctx.Vulnerabilities.Add(finding);
            ctx.Risks.Single(r => r.Id == riskId).Vulnerabilities.Add(finding);
        });

    private void Kev(string cve) => SeedUnscoped(ctx => ctx.KevEntries.Add(new KevEntry
    {
        CveId = cve, VulnerabilityName = "RCE", DateAdded = DateTime.UtcNow.AddDays(-100), CreatedAt = DateTime.UtcNow
    }));

    private void Delist(string cve) =>
        SeedUnscoped(ctx => ctx.KevEntries.Single(k => k.CveId == cve).DelistedAt = DateTime.UtcNow);

    private RiskFlag? Row(int riskId, RiskFlagCode code) =>
        Read(ctx => ctx.RiskFlags.SingleOrDefault(f => f.RiskId == riskId && f.Flag == code));

    private List<AuditLog> Audit(string type) =>
        Read(ctx => ctx.AuditLogs.Where(a => a.EntityType == type).OrderBy(a => a.Id).ToList());

    private static RiskFlagDeclarationRequest Because(string reason = "Patients depend on the system.") =>
        new() { Reason = reason };

    private static RiskFlagWithdrawalRequest Withdrawal(string reason = "Re-assessed with the clinical team.") =>
        new() { Reason = reason };

    /// <summary>A process with a declared RTO and, optionally, a failed restoration test: weight 1.0 or 0.5.</summary>
    private async Task ThreatenProcessAsync(int process, bool confirmed)
    {
        var continuity = GetService<IContinuityService>();
        await continuity.SaveBiaAsync(process, new BusinessImpactAnalysisRequest { RtoMinutes = 240 }, Author);
        if (confirmed)
            await continuity.RecordRestorationTestAsync(process, new RestorationTestCreateRequest
            {
                TestedAt = DateTime.UtcNow.AddDays(-1), Outcome = RestorationTestOutcome.Failed
            }, Author);
    }

    private void Classify(int dataId, string levelName, bool sensitive) => SeedUnscoped(ctx =>
    {
        AddEntity(ctx, Level, "securityClassificationLevel", levelName, ("sensitive", sensitive ? "True" : "False"));
        ctx.EntitiesProperties.Add(new EntitiesProperty
        {
            Id = 900_000 + dataId, Entity = dataId, Type = RiskFlagSchema.SecurityClassificationProperty,
            Value = Level.ToString(), OldValue = "", Name = RiskFlagSchema.SecurityClassificationProperty
        });
    });

    // --- F1–F6: guards ----------------------------------------------------------------------------

    /// <summary>F1 — a missing or over-long reason and an undefined code are refused, naming the field; nothing written.</summary>
    [Theory]
    [InlineData(1, null, "Reason")]
    [InlineData(1, "   ", "Reason")]
    [InlineData(1, "LONG", "Reason")]
    [InlineData(13, "ok", "code")]
    [InlineData(0, "ok", "code")]
    public async Task TestF1_AnInvalidDeclarationIsRefused(int code, string? reason, string field)
    {
        AddRisk(1, UnitA);
        if (reason == "LONG") reason = new string('x', RiskFlagCatalogue.MaxReasonLength + 1);

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.DeclareAsync(1, (RiskFlagCode)code, new RiskFlagDeclarationRequest { Reason = reason }, Author));

        Assert.Equal(field, ex.ParameterName);
        Assert.Empty(Read(ctx => ctx.RiskFlags.ToList()));
    }

    /// <summary>F2 — declaring what is already declared is refused rather than silently replacing the reason.</summary>
    [Fact]
    public async Task TestF2_DeclaringTwiceIsRefused()
    {
        AddRisk(1, UnitA);
        await Svc.DeclareAsync(1, RiskFlagCode.SystemicSinglePointOfFailure, Because("first"), Author);

        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            Svc.DeclareAsync(1, RiskFlagCode.SystemicSinglePointOfFailure, Because("second"), Author));

        Assert.Equal("first", Row(1, RiskFlagCode.SystemicSinglePointOfFailure)!.DeclaredReason);
    }

    /// <summary>F3 — withdrawing what is not declared is refused, including a flag that is only derived.</summary>
    [Fact]
    public async Task TestF3_WithdrawingAnUndeclaredFlagIsRefused()
    {
        AddRisk(1, UnitA);
        Finding(5, 1, Log4Shell);
        Kev(Log4Shell);
        await Svc.RefreshAsync(1);

        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            Svc.WithdrawAsync(1, RiskFlagCode.HumanSafety, Withdrawal(), Other));
        await Assert.ThrowsAsync<InvalidStateTransitionException>(() =>
            Svc.WithdrawAsync(1, RiskFlagCode.KnownExploitation, Withdrawal(), Other));

        var withdrawal = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.WithdrawAsync(1, RiskFlagCode.KnownExploitation, Withdrawal(""), Other));
        Assert.Equal("Reason", withdrawal.ParameterName);
    }

    /// <summary>
    /// F4 — the risk's owner cannot withdraw a Gate A declaration, and switching the segregation break-glass
    /// on does not change that (S46 D8); somebody unrelated can.
    /// </summary>
    [Fact]
    public async Task TestF4_TheOwnerCannotWithdrawAGateACondition()
    {
        AddRisk(1, UnitA);
        OwnedBy(1, Author);
        SeedUnscoped(ctx =>
        {
            ctx.Settings.Add(new Setting { Name = RiskWorkflowService.SegregationSetting, Value = "true" });
            ctx.Settings.Add(new Setting { Name = RiskWorkflowService.BreakGlassSetting, Value = "true" });
        });
        await Svc.DeclareAsync(1, RiskFlagCode.HumanSafety, Because(), Author);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Svc.WithdrawAsync(1, RiskFlagCode.HumanSafety, Withdrawal(), Author));

        Assert.Equal("segregation_of_duties", ex.RuleName);
        Assert.True(Row(1, RiskFlagCode.HumanSafety)!.Declared);

        var state = await Svc.WithdrawAsync(1, RiskFlagCode.HumanSafety, Withdrawal(), Other);

        Assert.False(state.Flags.Single(f => f.Code == RiskFlagCode.HumanSafety).IsSet);
        Assert.False(state.GateA.Holds);
        Assert.Equal(Other, Row(1, RiskFlagCode.HumanSafety)!.DeclaredById);
    }

    /// <summary>F5 — a flag that is not a Gate A condition is withdrawn by whoever declared it.</summary>
    [Fact]
    public async Task TestF5_TheOwnerMayWithdrawANonGateAFlag()
    {
        AddRisk(1, UnitA);
        OwnedBy(1, Author);
        await Svc.DeclareAsync(1, RiskFlagCode.EmergingRapidGrowth, Because(), Author);

        var state = await Svc.WithdrawAsync(1, RiskFlagCode.EmergingRapidGrowth, Withdrawal(), Author);

        Assert.False(state.Flags.Single(f => f.Code == RiskFlagCode.EmergingRapidGrowth).Declared);
    }

    /// <summary>F6 — another entity's risk is not found, for every read and write.</summary>
    [Fact]
    public async Task TestF6_ARiskOutsideTheScopeIsNotFound()
    {
        AddRisk(1, UnitB);
        ScopeTo(UnitA);

        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetAsync(1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.RefreshAsync(1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetDecisionsAsync(1));
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Svc.DeclareAsync(1, RiskFlagCode.HumanSafety, Because(), Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Svc.RecordDecisionAsync(1, new RiskDecisionRequest { Decision = RiskDecisionKind.TreatInCycle, Reason = "x" },
                Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Svc.EnsureGateAAllowsAsync(1, GateAAction.Accept));

        Assert.Empty(Read(ctx => ctx.RiskFlags.ToList()));
    }

    // --- D1–D6: derivation ------------------------------------------------------------------------

    /// <summary>D1 — a KEV-listed CVE on an open linked finding derives flag 3 with its basis, and Gate A holds.</summary>
    [Fact]
    public async Task TestD1_AKevListedFindingDerivesFlag3()
    {
        AddRisk(1, UnitA);
        Finding(5, 1, $"{Log4Shell}, CVE-2020-0001");
        Kev(Log4Shell);

        var state = await Svc.RefreshAsync(1);

        var flag = state.Flags.Single(f => f.Code == RiskFlagCode.KnownExploitation);
        Assert.True(flag is { IsSet: true, Derived: true, Declared: false });
        Assert.Equal($"KEV {Log4Shell} on finding #5", flag.DerivedBasis);
        Assert.Equal("Derived basis found.", flag.DerivedNote);
        Assert.Equal(RiskFlagDerivation.Kev, flag.Derivation);
        Assert.True(state.GateA.Holds);
        Assert.Equal([RiskFlagCode.KnownExploitation], state.GateA.Conditions);

        Assert.Equal(AuditLogAction.Create, Assert.Single(Audit(nameof(RiskFlag))).Action);
    }

    /// <summary>D2 — a finding that is not open (false positive, mitigated) is no basis.</summary>
    [Theory]
    [InlineData(FindingStatus.FalsePositive)]
    [InlineData(FindingStatus.Mitigated)]
    [InlineData(FindingStatus.RiskAccepted)]
    public async Task TestD2_AFindingThatIsNotOpenIsNoBasis(FindingStatus status)
    {
        AddRisk(1, UnitA);
        Finding(5, 1, Log4Shell, status: status);
        Kev(Log4Shell);

        var state = await Svc.RefreshAsync(1);

        Assert.False(state.Flags.Single(f => f.Code == RiskFlagCode.KnownExploitation).IsSet);
        Assert.Null(Row(1, RiskFlagCode.KnownExploitation));
    }

    /// <summary>
    /// D3 — flag 4 from the BIA: a confirmed threat on a linked critical process weighs 1.0; reaching a critical
    /// process through a provider it depends on, with only an unverified threat, weighs 0.5 — and neither is Gate A.
    /// </summary>
    [Fact]
    public async Task TestD3_ACriticalProcessUnderThreatDerivesFlag4WithItsWeight()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        await ThreatenProcessAsync(Process, confirmed: true);
        await ThreatenProcessAsync(Process2, confirmed: false);
        SeedUnscoped(ctx => ctx.BiaDependencies.Add(new BiaDependency
            { DependentEntityId = Process2, ProviderEntityId = Service2, CreatedAt = DateTime.UtcNow }));
        AddLink(1, Process);
        AddLink(2, Service2);

        var direct = (await Svc.RefreshAsync(1)).Flags.Single(f => f.Code == RiskFlagCode.CriticalProcessContinuity);
        var viaProvider = (await Svc.RefreshAsync(2)).Flags.Single(f => f.Code == RiskFlagCode.CriticalProcessContinuity);

        Assert.True(direct.Derived);
        Assert.Equal(1.0m, direct.DerivedWeight);
        Assert.Contains("critical process 'Enrolment' (#10), threat weight 1.0 (confirmed)", direct.DerivedBasis);

        Assert.True(viaProvider.Derived);
        Assert.Equal(0.5m, viaProvider.DerivedWeight);
        Assert.Contains("'Research' (#11)", viaProvider.DerivedBasis);
        Assert.Contains("(unverified)", viaProvider.DerivedBasis);

        Assert.False((await Svc.GetAsync(1)).GateA.Holds);
    }

    /// <summary>D4 — data classified at a level marked sensitive derives flag 5; an unmarked level does not.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task TestD4_SensitiveClassificationDerivesFlag5(bool sensitive)
    {
        AddRisk(1, UnitA);
        Classify(Data, "Restricted", sensitive);
        AddLink(1, Data);

        var flag = (await Svc.RefreshAsync(1)).Flags.Single(f => f.Code == RiskFlagCode.SensitiveData);

        Assert.Equal(sensitive, flag.Derived);
        if (sensitive)
            Assert.Equal("data 'Student records' (#50) classified 'Restricted' (#70), sensitive", flag.DerivedBasis);
    }

    /// <summary>D5 — withdrawing a declaration never clears a derivation: Gate A stays for the KEV item (D2).</summary>
    [Fact]
    public async Task TestD5_ADeclarationDoesNotSuppressADerivation()
    {
        AddRisk(1, UnitA);
        Finding(5, 1, Log4Shell);
        Kev(Log4Shell);
        await Svc.DeclareAsync(1, RiskFlagCode.KnownExploitation, Because("SOC saw exploitation attempts."), Author);

        var state = await Svc.WithdrawAsync(1, RiskFlagCode.KnownExploitation, Withdrawal(), Other);

        var flag = state.Flags.Single(f => f.Code == RiskFlagCode.KnownExploitation);
        Assert.True(flag is { Declared: false, Derived: true, IsSet: true });
        Assert.True(state.GateA.Holds);
    }

    /// <summary>D6 — a reconciliation with nothing to change saves nothing and leaves no trail.</summary>
    [Fact]
    public async Task TestD6_AnUnchangedReconciliationWritesNothing()
    {
        AddRisk(1, UnitA);
        Finding(5, 1, Log4Shell);
        Kev(Log4Shell);
        await Svc.RefreshAsync(1);

        // Read before counting: the test's own Read helper saves through the seeding context.
        var audit = Read(ctx => ctx.AuditLogs.Count());
        var saves = SaveChangesCount;

        await Svc.RefreshAsync(1);
        await Svc.RefreshAllAsync();

        Assert.Equal(saves, SaveChangesCount);
        Assert.Equal(audit, Read(ctx => ctx.AuditLogs.Count()));
    }

    // --- R1–R3: reversion with a trail (T173) -----------------------------------------------------

    /// <summary>
    /// R1 (T173) — the CVE leaves KEV: flag 3 reverts to false, the note says what was lost, and the field-level
    /// trail records the change as the system, not silently and not as whoever asked.
    /// </summary>
    [Fact]
    public async Task TestR1_ADelistedCveRevertsFlag3WithATrail()
    {
        AddRisk(1, UnitA);
        Finding(5, 1, Log4Shell);
        Kev(Log4Shell);
        await Svc.RefreshAsync(1);
        Delist(Log4Shell);

        var state = await Svc.RefreshAsync(1);

        var flag = state.Flags.Single(f => f.Code == RiskFlagCode.KnownExploitation);
        Assert.False(flag.IsSet);
        Assert.Null(flag.DerivedBasis);
        Assert.Equal($"Derived basis lost: KEV {Log4Shell} on finding #5", flag.DerivedNote);
        Assert.NotNull(flag.DerivedChangedAt);
        Assert.False(state.GateA.Holds);

        var trail = Audit(nameof(RiskFlag)).Where(a => a.Action == AuditLogAction.Update).ToList();
        var derived = Assert.Single(trail, a => a.Field == nameof(RiskFlag.Derived));
        Assert.Equal(("true", "false"), (derived.OldValue, derived.NewValue));
        Assert.Equal("system", derived.Actor);
        Assert.Null(derived.UserId);
        Assert.Contains(trail, a => a.Field == nameof(RiskFlag.DerivedBasis) && a.OldValue!.Contains(Log4Shell) &&
                                    a.NewValue == null);
        Assert.Contains(trail, a => a.Field == nameof(RiskFlag.DerivedNote) &&
                                    a.NewValue!.StartsWith("Derived basis lost", StringComparison.Ordinal));

        // The row stays, so the trail hangs off a stable id.
        Assert.NotNull(Row(1, RiskFlagCode.KnownExploitation));

        // And the risk's own trail shows it.
        var riskTrail = await GetService<IAuditTrailService>().GetForRiskAsync(1);
        Assert.Contains(riskTrail, a => a.EntityType == nameof(RiskFlag) && a.Field == nameof(RiskFlag.Derived));
    }

    /// <summary>R2 (T173) — the BIA is deleted: flag 4 reverts to false with a trail, through the nightly pass too.</summary>
    [Fact]
    public async Task TestR2_ADeletedBiaRevertsFlag4WithATrail()
    {
        AddRisk(1, UnitA);
        await ThreatenProcessAsync(Process, confirmed: true);
        AddLink(1, Process);
        await Svc.RefreshAllAsync();
        Assert.True(Row(1, RiskFlagCode.CriticalProcessContinuity)!.Derived);

        await GetService<IContinuityService>().DeleteBiaAsync(Process);
        var summary = await Svc.RefreshAllAsync();

        var row = Row(1, RiskFlagCode.CriticalProcessContinuity)!;
        Assert.False(row.Derived);
        Assert.Null(row.DerivedWeight);
        Assert.StartsWith("Derived basis lost: critical process 'Enrolment'", row.DerivedNote);
        Assert.Equal(1, summary.FlagsReverted);

        var reverted = Audit(nameof(RiskFlag)).Single(a => a.Field == nameof(RiskFlag.Derived) &&
                                                           a.Action == AuditLogAction.Update);
        Assert.Equal(("true", "false", "system"), (reverted.OldValue, reverted.NewValue, reverted.Actor));
        Assert.Contains(Audit(nameof(RiskFlag)), a => a.Field == nameof(RiskFlag.DerivedWeight) && a.NewValue == null);
    }

    /// <summary>
    /// R3 — a scoped user refreshing a risk does not revert a flag whose basis is a finding outside their scope:
    /// the derivation reads organisation-wide (D9).
    /// </summary>
    [Fact]
    public async Task TestR3_AScopedRefreshDoesNotRevertABasisTheUserCannotSee()
    {
        AddRisk(1, UnitA);
        Finding(5, 1, Log4Shell, entityId: UnitB);
        Kev(Log4Shell);
        await Svc.RefreshAsync(1);

        ScopeTo(UnitA);
        var state = await Svc.RefreshAsync(1);

        Assert.True(state.Flags.Single(f => f.Code == RiskFlagCode.KnownExploitation).Derived);
        Assert.True(state.GateA.Holds);
        Assert.DoesNotContain(Audit(nameof(RiskFlag)), a => a.Action == AuditLogAction.Update);
    }

    // --- E1–E3: escalation ------------------------------------------------------------------------

    /// <summary>
    /// E1 — a Gate A onset records the automatic act-immediately decision, marks the risk for review and raises
    /// exactly one notification, then records when.
    /// </summary>
    [Fact]
    public async Task TestE1_AGateAOnsetEscalatesOnce()
    {
        AddRisk(1, UnitA, score: 1f);

        await Svc.DeclareAsync(1, RiskFlagCode.HumanSafety, Because(), Author);

        await Escalations(1);
        var decision = Assert.Single(Read(ctx => ctx.RiskDecisions.ToList()));
        Assert.Equal((RiskDecisionKind.ActImmediately, RiskDecisionSource.GateA, "1"),
            (decision.Decision, decision.Source, decision.GateAConditions));
        Assert.Null(decision.DecidedById);
        Assert.NotNull(decision.EscalatedAt);
        Assert.Contains("Patients depend on the system.", decision.Reason);

        var risk = Read(ctx => ctx.Risks.Single(r => r.Id == 1));
        Assert.True(risk.ReviewRequested);
        Assert.StartsWith("Gate A condition reached", risk.ReviewRequestedReason);

        // The declaration is attributed to the person; the automatic decision to the system.
        Assert.Contains(Audit(nameof(RiskFlag)), a => a.Action == AuditLogAction.Create && a.UserId == Author);
        Assert.Contains(Audit(nameof(RiskDecision)), a => a.Action == AuditLogAction.Create && a.UserId == null);
    }

    /// <summary>E2 — Gate A still holding is not a new onset: no second decision, no second notification.</summary>
    [Fact]
    public async Task TestE2_AGateAThatKeepsHoldingIsNotReEscalated()
    {
        AddRisk(1, UnitA);
        Finding(5, 1, Log4Shell);
        Kev(Log4Shell);

        await Svc.RefreshAsync(1);
        await Svc.RefreshAsync(1);
        await Svc.RefreshAllAsync();
        await Svc.DeclareAsync(1, RiskFlagCode.LegalRegulatory, Because("LGPD art. 46."), Author);

        await Escalations(1);
        Assert.Single(Read(ctx => ctx.RiskDecisions.ToList()));
    }

    /// <summary>E3 — falling to false and back is a new onset, escalated again.</summary>
    [Fact]
    public async Task TestE3_FallingAndReturningIsANewOnset()
    {
        AddRisk(1, UnitA);
        await Svc.DeclareAsync(1, RiskFlagCode.NoLegitimateAcceptance, Because("Nobody may accept a breach of law."), Author);
        await Svc.WithdrawAsync(1, RiskFlagCode.NoLegitimateAcceptance, Withdrawal(), Other);
        await Svc.DeclareAsync(1, RiskFlagCode.NoLegitimateAcceptance, Because("Confirmed by legal."), Author);

        await Escalations(2);
        Assert.Equal(2, Read(ctx => ctx.RiskDecisions.Count(d => d.Source == RiskDecisionSource.GateA)));
    }

    // --- DC1–DC4: the decision --------------------------------------------------------------------

    /// <summary>DC1 — with Gate A, only "act immediately" may be recorded.</summary>
    [Theory]
    [InlineData(RiskDecisionKind.TreatInCycle)]
    [InlineData(RiskDecisionKind.MonitorAccept)]
    [InlineData(RiskDecisionKind.Archive)]
    public async Task TestDC1_WithGateAOnlyActImmediatelyIsAccepted(RiskDecisionKind kind)
    {
        AddRisk(1, UnitA);
        await Svc.DeclareAsync(1, RiskFlagCode.HumanSafety, Because(), Author);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Svc.RecordDecisionAsync(1, new RiskDecisionRequest { Decision = kind, Reason = "Within appetite." }, Other));
        Assert.Equal(RiskFlagsService.GateARule, ex.RuleName);

        var decision = await Svc.RecordDecisionAsync(1,
            new RiskDecisionRequest { Decision = RiskDecisionKind.ActImmediately, Reason = "Isolate the device." }, Other);
        Assert.Equal([RiskFlagCode.HumanSafety], decision.GateAConditions);
    }

    /// <summary>
    /// DC2 — "act immediately" on a low-score risk with no Gate A is recorded, escalated and notified: the
    /// decision is not a severity band (T171).
    /// </summary>
    [Fact]
    public async Task TestDC2_ActImmediatelyOnALowScoreRiskIsEscalated()
    {
        AddRisk(1, UnitA, score: 0.5f);

        var decision = await Svc.RecordDecisionAsync(1,
            new RiskDecisionRequest { Decision = RiskDecisionKind.ActImmediately, Reason = "Board instruction." }, Other);

        Assert.Equal((RiskDecisionKind.ActImmediately, RiskDecisionSource.Declared, Other),
            (decision.Decision, decision.Source, decision.DecidedById!.Value));
        Assert.NotNull(decision.EscalatedAt);
        Assert.Empty(decision.GateAConditions);
        await _publisher.Received(1).RiskGateAEscalatedAsync(Arg.Is<Risk>(r => r.Id == 1), Arg.Any<double?>(),
            Arg.Is<IReadOnlyList<string>>(c => c.Count == 0), "Board instruction.");
        Assert.True(Read(ctx => ctx.Risks.Single(r => r.Id == 1)).ReviewRequested);
    }

    /// <summary>DC3 — a very high score with no decision stays undecided: nothing derives "act immediately" from severity.</summary>
    [Fact]
    public async Task TestDC3_AHighScoreIsNotActImmediately()
    {
        AddRisk(1, UnitA, score: 25f);

        await Svc.RefreshAsync(1);

        Assert.Null((await Svc.GetAsync(1)).CurrentDecision);
        Assert.Empty(Read(ctx => ctx.RiskDecisions.ToList()));
        var top = Assert.Single((await Svc.GetTopRisksAsync(10)).Items);
        Assert.Null(top.Decision);
        Assert.Equal(NextDecisionKind.DecisionPending, top.NextDecision.Kind);
        await Escalations(0);
    }

    /// <summary>DC4 — a decision needs a defined kind and a reason; a non-immediate one is recorded without escalation.</summary>
    [Fact]
    public async Task TestDC4_ADecisionNeedsAKindAndAReason()
    {
        AddRisk(1, UnitA);

        Assert.Equal("Decision", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.RecordDecisionAsync(1, new RiskDecisionRequest { Reason = "x" }, Other))).ParameterName);
        Assert.Equal("Decision", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.RecordDecisionAsync(1, new RiskDecisionRequest { Decision = (RiskDecisionKind)9, Reason = "x" }, Other)))
            .ParameterName);
        Assert.Equal("Reason", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.RecordDecisionAsync(1, new RiskDecisionRequest { Decision = RiskDecisionKind.TreatInCycle }, Other)))
            .ParameterName);

        var decision = await Svc.RecordDecisionAsync(1,
            new RiskDecisionRequest { Decision = RiskDecisionKind.TreatInCycle, Reason = "Next sprint." }, Other);

        Assert.Null(decision.EscalatedAt);
        Assert.Equal(RiskDecisionKind.TreatInCycle, (await Svc.GetAsync(1)).CurrentDecision!.Decision);
        Assert.Single(await Svc.GetDecisionsAsync(1));
        await Escalations(0);
    }

    // --- TOP1–TOP5: Top Risks ---------------------------------------------------------------------

    /// <summary>
    /// TOP1 — Gate A first, then act immediately, then the owner's business rank, then E[L]; the ordinal score
    /// only breaks ties (S46 D12).
    /// </summary>
    [Fact]
    public async Task TestTOP1_TheOrderIsGateADecisionRankExpectedLossThenScore()
    {
        AddRisk(1, UnitA, score: 9f);   // highest score, nothing else
        AddRisk(2, UnitA, score: 2f);   // Gate A
        AddRisk(3, UnitA, score: 3f);   // act immediately
        AddRisk(4, UnitA, score: 1f);   // business rank 1
        AddRisk(5, UnitA, score: 1f);   // E[L] 1 000 000
        AddRisk(6, UnitA, score: 8f);
        SeedUnscoped(ctx =>
        {
            ctx.Risks.Single(r => r.Id == 4).BusinessRank = 1;
            ctx.RiskScorings.Single(s => s.Id == 5).QuantAleMean = 1_000_000;
        });
        await Svc.DeclareAsync(2, RiskFlagCode.HumanSafety, Because(), Author);
        await Svc.RecordDecisionAsync(3,
            new RiskDecisionRequest { Decision = RiskDecisionKind.ActImmediately, Reason = "Now." }, Other);

        var top = await Svc.GetTopRisksAsync(10);

        Assert.Equal([2, 3, 4, 5, 1, 6], top.Items.Select(i => i.RiskId));
        Assert.Equal(Enumerable.Range(1, 6), top.Items.Select(i => i.Rank));
        Assert.Equal(6, top.OpenRisks);
        Assert.True(top.Items[0].GateA);
        Assert.Equal(RiskDecisionKind.ActImmediately, top.Items[0].Decision);
        Assert.Equal(1_000_000, top.Items[3].ExpectedAnnualLoss);

        Assert.Equal([2, 3], (await Svc.GetTopRisksAsync(2)).Items.Select(i => i.RiskId));
    }

    /// <summary>TOP2 — a closed risk is not a Top Risk, whatever it carries.</summary>
    [Fact]
    public async Task TestTOP2_ClosedRisksAreLeftOut()
    {
        AddRisk(1, UnitA, status: "Closed", score: 9f);
        AddRisk(2, UnitA);

        var top = await Svc.GetTopRisksAsync(10);

        Assert.Equal([2], top.Items.Select(i => i.RiskId));
        Assert.Equal(1, top.OpenRisks);
    }

    /// <summary>TOP3 — the list holds 1 to 50 rows.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(51)]
    public async Task TestTOP3_TheLimitIsBounded(int limit)
    {
        Assert.Equal("limit", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.GetTopRisksAsync(limit))).ParameterName);
    }

    /// <summary>TOP4 — each row carries the trend, the evidence confidence, the owner and the next decision.</summary>
    [Fact]
    public async Task TestTOP4_ARowCarriesTrendConfidenceOwnerAndNextDecision()
    {
        AddRisk(1, UnitA, score: 7f);
        OwnedBy(1, Author);
        var expiry = DateTime.UtcNow.AddDays(20);
        SeedUnscoped(ctx =>
        {
            ctx.Risks.Single(r => r.Id == 1).EvidenceConfidence = EvidenceConfidence.Indicative;
            ctx.RiskScoringHistories.Add(new RiskScoringHistory
                { RiskId = 1, CalculatedRisk = 4f, LastUpdate = DateTime.UtcNow.AddDays(-120) });
            ctx.RiskScoringHistories.Add(new RiskScoringHistory
                { RiskId = 1, CalculatedRisk = 7f, LastUpdate = DateTime.UtcNow.AddDays(-1) });
            ctx.RiskAcceptances.Add(new RiskAcceptance
            {
                Name = "a", RiskId = 1, BusinessJustification = "b", AuthorizingManagerId = Other, StartDate = DateTime.UtcNow,
                ExpiresAt = expiry, Status = RiskAcceptanceStatus.Active, CreatedAt = DateTime.UtcNow, EntityId = UnitA
            });
        });

        var row = Assert.Single((await Svc.GetTopRisksAsync(10)).Items);

        Assert.Equal(RiskTrendDirection.Rising, row.Trend.Direction);
        Assert.Equal(3.0, row.Trend.Delta);
        Assert.Equal(EvidenceConfidence.Indicative, row.Confidence);
        Assert.Equal((Author, "analyst"), (row.OwnerId!.Value, row.OwnerName));
        Assert.Equal(7.0, row.Inherent);
        Assert.Equal(NextDecisionKind.AcceptanceExpiry, row.NextDecision.Kind);
        Assert.Equal(expiry, row.NextDecision.DueAt);
    }

    /// <summary>TOP5 — the list is the caller's scope only; the Gate A row's next decision is the escalation.</summary>
    [Fact]
    public async Task TestTOP5_TheListIsScopedAndGateAIsTheNextDecision()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitB, score: 9f);
        await Svc.DeclareAsync(1, RiskFlagCode.LegalRegulatory, Because("LGPD."), Author);

        ScopeTo(UnitA);
        var top = await Svc.GetTopRisksAsync(10);

        var row = Assert.Single(top.Items);
        Assert.Equal(1, row.RiskId);
        Assert.Equal(NextDecisionKind.GateAEscalation, row.NextDecision.Kind);
        Assert.True(row.NextDecision.Overdue);
        Assert.Equal([RiskFlagCode.LegalRegulatory], row.GateAConditions);
    }

    // --- Q1, EV1, RS1 -----------------------------------------------------------------------------

    /// <summary>Q1 — risks are queryable by flag and by Gate A, within the caller's scope.</summary>
    [Fact]
    public async Task TestQ1_RisksAreQueryableByFlagAndByGateA()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        AddRisk(3, UnitB);
        await Svc.DeclareAsync(1, RiskFlagCode.HumanSafety, Because(), Author);
        await Svc.DeclareAsync(2, RiskFlagCode.ArtificialIntelligence, Because("Chatbot triage."), Author);
        await Svc.DeclareAsync(3, RiskFlagCode.HumanSafety, Because(), Author);

        Assert.Equal([1, 3], (await Svc.GetFlaggedAsync(RiskFlagCode.HumanSafety, null)).Select(r => r.RiskId));
        Assert.Equal([2], (await Svc.GetFlaggedAsync(null, false)).Select(r => r.RiskId));
        Assert.Equal([1, 3], (await Svc.GetFlaggedAsync(null, true)).Select(r => r.RiskId));
        Assert.Empty(await Svc.GetFlaggedAsync(RiskFlagCode.ArtificialIntelligence, true));

        ScopeTo(UnitA);
        Assert.Equal([1], (await Svc.GetFlaggedAsync(RiskFlagCode.HumanSafety, null)).Select(r => r.RiskId));

        await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.GetFlaggedAsync((RiskFlagCode)99, null));
    }

    /// <summary>
    /// EV1 — the written reason a Gate A declaration was withdrawn with is persisted and exported in the
    /// governance evidence pack: the only way such a risk becomes acceptable leaves evidence (S46 §4.6, D8).
    /// </summary>
    [Fact]
    public async Task TestEV1_AWithdrawalReasonIsExportedInTheEvidencePack()
    {
        AddRisk(1, UnitA);
        var from = DateTime.UtcNow.AddMinutes(-1);
        await Svc.DeclareAsync(1, RiskFlagCode.HumanSafety, Because(), Author);
        await Svc.WithdrawAsync(1, RiskFlagCode.HumanSafety, Withdrawal("Device decommissioned on 2026-10-01."), Other);

        var pack = await GetService<IAuditTrailService>().GetEvidencePackAsync(null, from, DateTime.UtcNow.AddMinutes(1),
            "auditor");

        Assert.Contains(pack.Changes, c => c.EntityType == nameof(RiskFlag) && c.Field == nameof(RiskFlag.DeclaredReason) &&
                                           c.NewValue == "Device decommissioned on 2026-10-01.");
        Assert.Contains(pack.Changes, c => c.EntityType == nameof(RiskFlag) && c.Field == nameof(RiskFlag.Declared) &&
                                           c.NewValue == "false");
        Assert.Contains(pack.Changes, c => c.EntityType == nameof(RiskDecision));
    }

    /// <summary>
    /// RS1 — the names the flag 5 derivation reads exist in the configuration the API loads, with the types it
    /// expects: a rename on either side would turn flag 5 off without an error anywhere.
    /// </summary>
    [Fact]
    public async Task TestRS1_FlagSchemaMatchesTheEntitiesConfiguration()
    {
        var configuration = await GetService<IEntitiesService>().GetEntitiesConfigurationAsync();
        var definitions = configuration.Definitions;

        var level = definitions[RiskFlagSchema.SecurityClassificationLevelDefinition].Properties[RiskFlagSchema.SensitiveProperty];
        Assert.Equal("Boolean", level.Type);
        Assert.True(level.Nullable);

        var classification = definitions[Tools.Risks.RiskChainSchema.DataDefinition]
            .Properties[RiskFlagSchema.SecurityClassificationProperty];
        Assert.Equal($"Definition({RiskFlagSchema.SecurityClassificationLevelDefinition})", classification.Type);
        Assert.False(classification.Multiple);
    }

    /// <summary>The audit allowlist carries both new types (S46 §4.10).</summary>
    [Fact]
    public void TestFlagsAndDecisionsAreInTheAuditedScope()
    {
        Assert.Contains(nameof(RiskFlag), DAL.Auditing.GovernanceAuditInterceptor.AuditedTypes);
        Assert.Contains(nameof(RiskDecision), DAL.Auditing.GovernanceAuditInterceptor.AuditedTypes);
        Assert.Equal(12, Svc.GetCatalogue().Count);
    }
}

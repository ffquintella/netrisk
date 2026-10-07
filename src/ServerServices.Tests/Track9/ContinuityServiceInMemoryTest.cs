using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Security.Claims;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using DAL.Context;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.Continuity;
using Model.Exceptions;
using NSubstitute;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using Tools.Continuity;
using Tools.Risks;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.3 (S43 §8) — <see cref="ContinuityService"/> against the real model on the EF in-memory
/// provider: B1–B8, D1–D7, R1–R7, P1–P5, M1, G1–G4, A1–A3, CV1, Y0 and Y1.
///
/// The organisation is the Stage 9.1 one (<see cref="RiskChainTestBase"/>): processes 10 "Enrolment"
/// (declared criticality 5) and 11 "Research" (declared 4), services 20 and 21, applications 30 and 40,
/// data 50, units 100 and 200, user 7.
/// </summary>
[TestSubject(typeof(ContinuityService))]
public class ContinuityServiceInMemoryTest : RiskChainTestBase
{
    private IContinuityService Continuity => GetService<IContinuityService>();

    private Task<BusinessImpactAnalysisWriteResult> Declare(int entityId, int? mtpd = null, int? rto = null,
        int? rpo = null) =>
        Continuity.SaveBiaAsync(entityId,
            new BusinessImpactAnalysisRequest { MtpdMinutes = mtpd, RtoMinutes = rto, RpoMinutes = rpo }, Author);

    private Task<RestorationTestDto> RecordTest(int entityId, int daysAgo,
        RestorationTestOutcome outcome = RestorationTestOutcome.Succeeded, int? rto = null, int? rpo = null) =>
        Continuity.RecordRestorationTestAsync(entityId, new RestorationTestCreateRequest
        {
            TestedAt = DateTime.UtcNow.AddDays(-daysAgo), Outcome = outcome, AchievedRtoMinutes = rto,
            AchievedRpoMinutes = rpo
        }, Author);

    private List<AuditLog> Audit(string type) =>
        Read(ctx => ctx.AuditLogs.Where(a => a.EntityType == type).OrderBy(a => a.Id).ToList());

    private void SeedSettings(string days, string weight) => SeedUnscoped(ctx =>
    {
        ctx.Settings.Add(new Setting { Name = ContinuitySettingKeys.RestorationTestValidityDays, Value = days });
        ctx.Settings.Add(new Setting { Name = ContinuitySettingKeys.UnverifiedThreatWeight, Value = weight });
    });

    private static ClaimsPrincipal With(params Claim[] claims) => Principal(claims);

    // --- B1–B8: the BIA ---------------------------------------------------------------------------

    /// <summary>B1 — the first PUT creates with its author and a UTC analysis date; the second updates.</summary>
    [Fact]
    public async Task TestB1_APutCreatesAndASecondOneUpdates()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);

        var created = await Declare(Process, mtpd: 480, rto: 240, rpo: 60);

        Assert.True(created.Created);
        Assert.Equal((480, 240, 60), (created.Bia.MtpdMinutes, created.Bia.RtoMinutes, created.Bia.RpoMinutes));
        Assert.Equal(Author, created.Bia.CreatedById);
        Assert.Equal(DateTimeKind.Utc, created.Bia.AssessedAt.Kind);
        Assert.InRange(created.Bia.AssessedAt, before, DateTime.UtcNow.AddSeconds(1));
        Assert.Null(created.Bia.UpdatedAt);

        var updated = await Declare(Process, mtpd: 480, rto: 120, rpo: 60);

        Assert.False(updated.Created);
        Assert.Equal(created.Bia.Id, updated.Bia.Id);
        Assert.Equal(120, updated.Bia.RtoMinutes);
        Assert.NotNull(updated.Bia.UpdatedAt);
        Assert.Equal(Author, updated.Bia.UpdatedById);
        Assert.Single(Read(ctx => ctx.BusinessImpactAnalyses.ToList()));
    }

    /// <summary>B2 — a negative duration and one past 365 days are refused, naming the field; nothing written.</summary>
    [Theory]
    [InlineData(-1, null, null, "MtpdMinutes")]
    [InlineData(null, 525_601, null, "RtoMinutes")]
    [InlineData(null, null, -5, "RpoMinutes")]
    public async Task TestB2_AnOutOfRangeDurationIsRefused(int? mtpd, int? rto, int? rpo, string field)
    {
        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Declare(Process, mtpd, rto, rpo));

        Assert.Equal(field, ex.ParameterName);
        Assert.Empty(Read(ctx => ctx.BusinessImpactAnalyses.ToList()));
    }

    /// <summary>B3 — a BIA declaring nothing is refused: absence is the missing row, not an empty one.</summary>
    [Fact]
    public async Task TestB3_DeclaringNothingIsRefused()
    {
        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Declare(Process));

        Assert.Equal("Bia", ex.ParameterName);
        Assert.Empty(Read(ctx => ctx.BusinessImpactAnalyses.ToList()));
    }

    /// <summary>B4 — an RTO above the MTPD on the same BIA is a broken rule.</summary>
    [Fact]
    public async Task TestB4_AnRtoAboveTheMtpdIsRefused()
    {
        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Declare(Process, mtpd: 240, rto: 241));

        Assert.Equal(ContinuityService.RtoExceedsMtpdRule, ex.RuleName);
        Assert.Empty(Read(ctx => ctx.BusinessImpactAnalyses.ToList()));
    }

    /// <summary>B5 — a missing entity is not found; a unit or an application has no BIA.</summary>
    [Fact]
    public async Task TestB5_OnlyProcessesAndServicesHaveABia()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => Declare(9999, rto: 60));

        foreach (var notSubject in new[] { UnitA, PortalApp })
        {
            var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Declare(notSubject, rto: 60));
            Assert.Equal(ContinuityService.EntityNotBiaSubjectRule, ex.RuleName);
        }

        Assert.True((await Declare(Service, rto: 60)).Created);
    }

    /// <summary>B6 — an analysis date in the future and notes past 4 000 characters are refused.</summary>
    [Fact]
    public async Task TestB6_AFutureDateAndOverlongNotesAreRefused()
    {
        var future = await Assert.ThrowsAsync<InvalidParameterException>(() => Continuity.SaveBiaAsync(Process,
            new BusinessImpactAnalysisRequest { RtoMinutes = 60, AssessedAt = DateTime.UtcNow.AddDays(1) }, Author));
        Assert.Equal("AssessedAt", future.ParameterName);

        var notes = await Assert.ThrowsAsync<InvalidParameterException>(() => Continuity.SaveBiaAsync(Process,
            new BusinessImpactAnalysisRequest { RtoMinutes = 60, Notes = new string('x', 4_001) }, Author));
        Assert.Equal("Notes", notes.ParameterName);

        Assert.Empty(Read(ctx => ctx.BusinessImpactAnalyses.ToList()));
    }

    /// <summary>B7 — deleting the BIA returns the node to "absent"; its dependencies and tests stay; a
    /// second delete is not found.</summary>
    [Fact]
    public async Task TestB7_DeletingTheBiaMakesTheNodeAbsentAgain()
    {
        await Declare(Process, rto: 240);
        await Continuity.AddDependencyAsync(Process, new BiaDependencyCreateRequest { ProviderEntityId = Service }, Author);
        await RecordTest(Process, 5, rto: 60);

        await Continuity.DeleteBiaAsync(Process);

        var subject = (await Continuity.GetSubjectsAsync()).Single(s => s.EntityId == Process);
        Assert.False(subject.HasBia);
        Assert.Equal(ObjectiveVerificationStatus.Absent, subject.RtoStatus);
        Assert.Equal(ObjectiveVerificationStatus.Absent, subject.RpoStatus);
        Assert.Single(Read(ctx => ctx.BiaDependencies.ToList()));
        Assert.Single(Read(ctx => ctx.RestorationTests.ToList()));

        await Assert.ThrowsAsync<DataNotFoundException>(() => Continuity.DeleteBiaAsync(Process));
    }

    /// <summary>
    /// B8 — a scoped caller is refused every one of the seven writes with the same exception, for an
    /// existing entity and a missing one, and the service never opens a context to find out which is
    /// which: no write route is an existence oracle.
    /// </summary>
    [Fact]
    public async Task TestB8_AScopedCallerIsRefusedEveryWriteBeforeAnyQuery()
    {
        var dal = Substitute.For<IDalService>();
        dal.GetCurrentEntityScope().Returns(EntityScope.ForEntities([UnitA]));
        var service = new ContinuityService(Substitute.For<Serilog.ILogger>(), dal);

        foreach (var entityId in new[] { Process, 9999 })
        {
            var writes = new Func<Task>[]
            {
                () => service.SaveBiaAsync(entityId, new BusinessImpactAnalysisRequest { RtoMinutes = 60 }, Author),
                () => service.DeleteBiaAsync(entityId),
                () => service.AddDependencyAsync(entityId, new BiaDependencyCreateRequest { ProviderEntityId = Service }, Author),
                () => service.DeleteDependencyAsync(entityId, 1),
                () => service.RecordRestorationTestAsync(entityId, new RestorationTestCreateRequest
                    { TestedAt = DateTime.UtcNow.AddDays(-1), Outcome = RestorationTestOutcome.Succeeded, AchievedRtoMinutes = 5 }, Author),
                () => service.VoidRestorationTestAsync(entityId, 1, new RestorationTestVoidRequest { Reason = "Recorded on the wrong node" }, Author),
                () => service.SaveSettingsAsync(new ContinuitySettingsRequest { RestorationTestValidityDays = 30, UnverifiedThreatWeight = 0.5m })
            };

            foreach (var write in writes)
            {
                var ex = await Assert.ThrowsAsync<PermissionInvalidException>(write);
                Assert.Equal("global_scope", ex.Permission);
            }
        }

        dal.DidNotReceive().GetContext(Arg.Any<bool>(), Arg.Any<bool>());
    }

    // --- D1–D7: dependencies ----------------------------------------------------------------------

    /// <summary>D1 — process → service, service → service and service → process are all dependencies.</summary>
    [Fact]
    public async Task TestD1_AnyPairOfProcessesAndServicesCanDepend()
    {
        var a = await Continuity.AddDependencyAsync(Process, new BiaDependencyCreateRequest { ProviderEntityId = Service, Description = " portal " }, Author);
        var b = await Continuity.AddDependencyAsync(Service, new BiaDependencyCreateRequest { ProviderEntityId = Service2 }, Author);
        var c = await Continuity.AddDependencyAsync(Service2, new BiaDependencyCreateRequest { ProviderEntityId = Process2 }, Author);

        Assert.Equal(("Enrolment", "Student portal", "portal"), (a.DependentName, a.ProviderName, a.Description));
        Assert.Equal(Author, a.CreatedById);
        Assert.Equal((Service, Service2), (b.DependentEntityId, b.ProviderEntityId));
        Assert.Equal((Service2, Process2), (c.DependentEntityId, c.ProviderEntityId));
        Assert.Equal(3, Read(ctx => ctx.BiaDependencies.Count()));
    }

    /// <summary>D2 — a node cannot depend on itself.</summary>
    [Fact]
    public async Task TestD2_ASelfDependencyIsRefused()
    {
        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Continuity.AddDependencyAsync(Process, new BiaDependencyCreateRequest { ProviderEntityId = Process }, Author));

        Assert.Equal(ContinuityService.SelfDependencyRule, ex.RuleName);
    }

    /// <summary>D3 — declaring the same dependency twice is a conflict.</summary>
    [Fact]
    public async Task TestD3_ARepeatedDependencyIsAConflict()
    {
        var request = new BiaDependencyCreateRequest { ProviderEntityId = Service };
        await Continuity.AddDependencyAsync(Process, request, Author);

        await Assert.ThrowsAsync<DataAlreadyExistsException>(() => Continuity.AddDependencyAsync(Process, request, Author));
        Assert.Single(Read(ctx => ctx.BiaDependencies.ToList()));
    }

    /// <summary>D4 — a missing provider is not found; an application cannot be a provider.</summary>
    [Fact]
    public async Task TestD4_TheProviderMustBeAnExistingSubject()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Continuity.AddDependencyAsync(Process, new BiaDependencyCreateRequest { ProviderEntityId = 9999 }, Author));

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Continuity.AddDependencyAsync(Process, new BiaDependencyCreateRequest { ProviderEntityId = Crm }, Author));
        Assert.Equal(ContinuityService.EntityNotBiaSubjectRule, ex.RuleName);

        Assert.Empty(Read(ctx => ctx.BiaDependencies.ToList()));
    }

    /// <summary>D5 — P → S and then S → P are both accepted; the profile reports the cycle.</summary>
    [Fact]
    public async Task TestD5_ACycleIsAcceptedAndReported()
    {
        await Continuity.AddDependencyAsync(Process, new BiaDependencyCreateRequest { ProviderEntityId = Service }, Author);
        await Continuity.AddDependencyAsync(Service, new BiaDependencyCreateRequest { ProviderEntityId = Process }, Author);

        var profile = await Continuity.GetProfileAsync(Process, Admin());

        Assert.Equal([Service], profile.Cascade.CycleMembers);
        Assert.True(profile.Subject.InCycle);
        Assert.DoesNotContain(profile.Threat.Items, i => i.ViaEntityId == Process);
    }

    /// <summary>D6 — a dependency deleted through another node's route is not found and stays.</summary>
    [Fact]
    public async Task TestD6_AnotherNodesDependencyIsNotFound()
    {
        var dependency = await Continuity.AddDependencyAsync(Process, new BiaDependencyCreateRequest { ProviderEntityId = Service }, Author);

        await Assert.ThrowsAsync<DataNotFoundException>(() => Continuity.DeleteDependencyAsync(Process2, dependency.Id));
        Assert.Single(Read(ctx => ctx.BiaDependencies.ToList()));

        await Continuity.DeleteDependencyAsync(Process, dependency.Id);
        Assert.Empty(Read(ctx => ctx.BiaDependencies.ToList()));
    }

    /// <summary>D7 — a description past 500 characters is refused.</summary>
    [Fact]
    public async Task TestD7_AnOverlongDescriptionIsRefused()
    {
        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Continuity.AddDependencyAsync(Process,
            new BiaDependencyCreateRequest { ProviderEntityId = Service, Description = new string('d', 501) }, Author));

        Assert.Equal("Description", ex.ParameterName);
    }

    // --- R1–R7: restoration tests -----------------------------------------------------------------

    /// <summary>R1 — a recorded test copies the declared RTO/RPO as evidence and names its recorder.</summary>
    [Fact]
    public async Task TestR1_ATestCopiesTheDeclaredObjectives()
    {
        await Declare(Process, rto: 240, rpo: 60);

        var test = await RecordTest(Process, 3, rto: 200, rpo: 30);

        Assert.Equal((240, 60), (test.DeclaredRtoMinutes, test.DeclaredRpoMinutes));
        Assert.Equal((200, 30), (test.AchievedRtoMinutes, test.AchievedRpoMinutes));
        Assert.Equal(Author, test.RecordedById);
        Assert.Null(test.VoidedAt);
    }

    /// <summary>R2 — a test cannot be recorded before it is run.</summary>
    [Fact]
    public async Task TestR2_AFutureTestIsRefused()
    {
        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Continuity.RecordRestorationTestAsync(Process,
            new RestorationTestCreateRequest
            {
                TestedAt = DateTime.UtcNow.AddHours(1), Outcome = RestorationTestOutcome.Succeeded, AchievedRtoMinutes = 10
            }, Author));

        Assert.Equal("TestedAt", ex.ParameterName);
        Assert.Empty(Read(ctx => ctx.RestorationTests.ToList()));
    }

    /// <summary>R3 — a successful test that measured nothing is refused; a failed one may measure nothing.</summary>
    [Fact]
    public async Task TestR3_ASuccessMustMeasureSomething()
    {
        await Assert.ThrowsAsync<InvalidParameterException>(() => RecordTest(Process, 1));

        var failed = await RecordTest(Process, 1, RestorationTestOutcome.Failed);
        Assert.Equal(RestorationTestOutcome.Failed, failed.Outcome);
    }

    /// <summary>R4 — a negative measure and an outcome outside the enum are refused.</summary>
    [Fact]
    public async Task TestR4_ANegativeMeasureOrAnUnknownOutcomeIsRefused()
    {
        var negative = await Assert.ThrowsAsync<InvalidParameterException>(() => RecordTest(Process, 1, rto: -1));
        Assert.Equal("AchievedRtoMinutes", negative.ParameterName);

        var outcome = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            RecordTest(Process, 1, (RestorationTestOutcome)9, rto: 5));
        Assert.Equal("Outcome", outcome.ParameterName);

        Assert.Empty(Read(ctx => ctx.RestorationTests.ToList()));
    }

    /// <summary>R5 — a test on a node with no BIA is kept; the node stays absent until a BIA is declared.</summary>
    [Fact]
    public async Task TestR5_ATestBeforeTheBiaIsKeptAndTheNodeStaysAbsent()
    {
        var test = await RecordTest(Service2, 2, rto: 30);

        Assert.Null(test.DeclaredRtoMinutes);
        var subject = (await Continuity.GetSubjectsAsync()).Single(s => s.EntityId == Service2);
        Assert.Equal(ObjectiveVerificationStatus.Absent, subject.RtoStatus);

        await Declare(Service2, rto: 60);
        Assert.Equal(ObjectiveVerificationStatus.Met,
            (await Continuity.GetSubjectsAsync()).Single(s => s.EntityId == Service2).RtoStatus);
    }

    /// <summary>R6 — voiding with a reason takes the test out of the verification; a short reason is
    /// refused; voiding again is a broken rule.</summary>
    [Fact]
    public async Task TestR6_VoidingNeedsAReasonAndHappensOnce()
    {
        await Declare(Process, rto: 240);
        var test = await RecordTest(Process, 2, rto: 60);
        Assert.Equal(ObjectiveVerificationStatus.Met, (await Continuity.GetProfileAsync(Process, Admin())).RtoVerification.Status);

        var shortReason = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Continuity.VoidRestorationTestAsync(Process, test.Id, new RestorationTestVoidRequest { Reason = "oops" }, Author));
        Assert.Equal("Reason", shortReason.ParameterName);

        var voided = await Continuity.VoidRestorationTestAsync(Process, test.Id,
            new RestorationTestVoidRequest { Reason = "  Recorded against the wrong process  " }, Author);
        Assert.NotNull(voided.VoidedAt);
        Assert.Equal(Author, voided.VoidedById);
        Assert.Equal("Recorded against the wrong process", voided.VoidReason);

        var verification = (await Continuity.GetProfileAsync(Process, Admin())).RtoVerification;
        Assert.Equal((ObjectiveVerificationStatus.Unverified, VerificationReason.NoTest), (verification.Status, verification.Reason));

        var again = await Assert.ThrowsAsync<RuleBrokenException>(() => Continuity.VoidRestorationTestAsync(Process, test.Id,
            new RestorationTestVoidRequest { Reason = "Recorded against the wrong process" }, Author));
        Assert.Equal(ContinuityService.AlreadyVoidedRule, again.RuleName);

        // Kept, not deleted.
        Assert.Single(await Continuity.GetRestorationTestsAsync(Process));
    }

    /// <summary>R7 — voiding another node's test through this node's route is not found.</summary>
    [Fact]
    public async Task TestR7_AnotherNodesTestIsNotFound()
    {
        var test = await RecordTest(Process, 2, rto: 60);

        await Assert.ThrowsAsync<DataNotFoundException>(() => Continuity.VoidRestorationTestAsync(Process2, test.Id,
            new RestorationTestVoidRequest { Reason = "Recorded against the wrong process" }, Author));

        Assert.Null(Read(ctx => ctx.RestorationTests.Single()).VoidedAt);
    }

    // --- P1–P5: the profile -----------------------------------------------------------------------

    /// <summary>P1 (the methodology's case, T160) — a declared RTO with no test is unverified, a threat at
    /// the configured weight, and never met.</summary>
    [Fact]
    public async Task TestP1_ADeclaredRtoWithoutATestIsUnverifiedAndAHalfWeightThreat()
    {
        await Declare(Process, rto: 240);

        var profile = await Continuity.GetProfileAsync(Process, Admin());

        Assert.Equal((ObjectiveVerificationStatus.Unverified, VerificationReason.NoTest),
            (profile.RtoVerification.Status, profile.RtoVerification.Reason));
        Assert.NotEqual(ObjectiveVerificationStatus.Met, profile.RtoVerification.Status);

        var item = Assert.Single(profile.Threat.Items, i => i is { Reason: ContinuityThreatReason.Unverified, Objective: ContinuityObjective.Rto });
        Assert.Equal(0.5m, item.Weight);
        Assert.Equal(0.5m, profile.Threat.UnverifiedWeight);
        Assert.Equal(0.5m, profile.Subject.ThreatWeight);
    }

    /// <summary>P2 (the methodology's case, T160) — a process with no BIA is absent: its criticality is
    /// the declared one, or none at all — never 0, never infinite.</summary>
    [Fact]
    public async Task TestP2_AProcessWithoutBiaIsAbsent()
    {
        SeedUnscoped(ctx => AddEntity(ctx, 12, "businessProcess", "Library", ("isActive", "True")));

        var declared = await Continuity.GetProfileAsync(Process2, Admin());
        Assert.False(declared.Subject.HasBia);
        Assert.Null(declared.Bia);
        Assert.Equal(ObjectiveVerificationStatus.Absent, declared.RtoVerification.Status);
        Assert.Equal((4, CriticalitySource.Declared), (declared.Subject.EffectiveCriticality, declared.Subject.CriticalitySource));
        Assert.Empty(declared.Threat.Items);

        var none = await Continuity.GetProfileAsync(12, Admin());
        Assert.Null(none.Subject.EffectiveCriticality);
        Assert.Null(none.Subject.CriticalitySource);
        Assert.Null(none.DeclaredCriticality);
    }

    /// <summary>P3 — a provider looser than its dependent shows the conflict in its profile and in the list.</summary>
    [Fact]
    public async Task TestP3_ACascadeConflictIsReported()
    {
        await Declare(Process, rto: 240);
        await Declare(Service, rto: 480);
        await Continuity.AddDependencyAsync(Process, new BiaDependencyCreateRequest { ProviderEntityId = Service }, Author);

        var profile = await Continuity.GetProfileAsync(Service, Admin());
        var conflict = Assert.Single(profile.Cascade.Conflicts);
        Assert.Equal((480, 240, Process, "Enrolment"),
            (conflict.DeclaredMinutes, conflict.RequiredMinutes, conflict.BindingEntityId, conflict.BindingName));

        Assert.Equal(1, (await Continuity.GetSubjectsAsync()).Single(s => s.EntityId == Service).CascadeConflictCount);

        var process = await Continuity.GetProfileAsync(Process, Admin());
        Assert.Contains(process.Threat.Items, i => i is { Reason: ContinuityThreatReason.ProviderExceedsRequirement, ViaEntityId: Service });
        Assert.Equal(1.0m, process.Threat.ThreatWeight);
    }

    /// <summary>P4 — a cycle through the real service returns, with its members.</summary>
    [Fact]
    public async Task TestP4_ACycleThroughTheServiceReturns()
    {
        await Declare(Process, rto: 240);
        await Declare(Service, rto: 120);
        await Continuity.AddDependencyAsync(Process, new BiaDependencyCreateRequest { ProviderEntityId = Service }, Author);
        await Continuity.AddDependencyAsync(Service, new BiaDependencyCreateRequest { ProviderEntityId = Service2 }, Author);
        await Continuity.AddDependencyAsync(Service2, new BiaDependencyCreateRequest { ProviderEntityId = Process }, Author);

        var profile = await Continuity.GetProfileAsync(Process, Admin());

        Assert.Equal([Service, Service2], profile.Cascade.CycleMembers);
        Assert.Equal(2, profile.Cascade.Providers.Count);
        Assert.Equal(2, profile.Cascade.Dependents.Count);
    }

    /// <summary>P5 — the caller flags follow the permission <b>and</b> global scope.</summary>
    [Fact]
    public async Task TestP5_TheCallerFlagsFollowPermissionAndScope()
    {
        var admin = await Continuity.GetProfileAsync(Process, Admin());
        Assert.True(admin.CallerCanManageBia);
        Assert.True(admin.CallerCanRecordTests);

        var manager = await Continuity.GetProfileAsync(Process, With(new Claim("Permission", "bia_manage")));
        Assert.True(manager.CallerCanManageBia);
        Assert.False(manager.CallerCanRecordTests);

        var tester = await Continuity.GetProfileAsync(Process, With(new Claim("Permission", "restoration_test_record")));
        Assert.False(tester.CallerCanManageBia);
        Assert.True(tester.CallerCanRecordTests);

        var reader = await Continuity.GetProfileAsync(Process, RiskManager());
        Assert.False(reader.CallerCanManageBia);
        Assert.False(reader.CallerCanRecordTests);

        ScopeTo(UnitA);
        var scoped = await Continuity.GetProfileAsync(Process, Admin());
        Assert.False(scoped.CallerCanManageBia);
        Assert.False(scoped.CallerCanRecordTests);
    }

    // --- M1: the metric ---------------------------------------------------------------------------

    /// <summary>
    /// M1 — counts by status and reason, a denominator of declared objectives only, subjects without a BIA
    /// outside it, inactive subjects out, a null ratio with nothing declared, and the critical-process cut
    /// by effective criticality.
    /// </summary>
    [Fact]
    public async Task TestM1_TheMetricCountsOnlyWhatIsDeclared()
    {
        SeedUnscoped(ctx => AddEntity(ctx, 12, "businessProcess", "Archived", ("isActive", "False"), ("criticality", "5")));

        await Declare(Process, rto: 240);              // unverified, no test
        await Declare(Process2, rto: 60);               // met
        await RecordTest(Process2, 10, rto: 45);
        await Declare(Service, rto: 120);              // not met
        await RecordTest(Service, 10, RestorationTestOutcome.Failed);
        await Declare(12, rto: 60);                    // inactive: out of the metric

        var metric = await Continuity.GetRestorationVerificationMetricAsync();

        Assert.Equal((3, 1, 1, 1, 1), (metric.All.Rto.Declared, metric.All.Rto.Met, metric.All.Rto.NotMet,
            metric.All.Rto.Unverified, metric.All.Rto.UnverifiedNoTest));
        Assert.Equal(1m / 3m, metric.All.Rto.MetRatio);
        Assert.Equal(1, metric.All.SubjectsWithoutBia);          // Service2
        Assert.Equal(0, metric.All.Rpo.Declared);
        Assert.Null(metric.All.Rpo.MetRatio);

        Assert.Equal((2, 1, 1), (metric.CriticalProcesses.Rto.Declared, metric.CriticalProcesses.Rto.Met,
            metric.CriticalProcesses.Rto.Unverified));
        Assert.DoesNotContain(metric.Rows, r => r.EntityId == 12);
        Assert.Equal(365, metric.ValidityDays);
        Assert.Equal(0.5m, metric.UnverifiedWeight);
    }

    // --- G1–G4: the parameters --------------------------------------------------------------------

    /// <summary>G1 — no rows read as the defaults with both keys reported; an invalid row reads as the
    /// default, is reported and logged.</summary>
    [Fact]
    public async Task TestG1_MissingOrInvalidParametersReadAsTheDefaults()
    {
        var missing = await Continuity.GetSettingsAsync();
        Assert.Equal((365, 0.5m), (missing.RestorationTestValidityDays, missing.UnverifiedThreatWeight));
        Assert.Equal([ContinuitySettingKeys.RestorationTestValidityDays, ContinuitySettingKeys.UnverifiedThreatWeight],
            missing.FallbackApplied);

        SeedSettings("abc", "0.25");
        var logger = Substitute.For<Serilog.ILogger>();
        var invalid = await new ContinuityService(logger, GetService<IDalService>()).GetSettingsAsync();

        Assert.Equal((365, 0.25m), (invalid.RestorationTestValidityDays, invalid.UnverifiedThreatWeight));
        Assert.Equal([ContinuitySettingKeys.RestorationTestValidityDays], invalid.FallbackApplied);
        logger.Received().Warning(Arg.Any<string>(), Arg.Any<List<string>>());
    }

    /// <summary>G2 — a valid PUT stores both rows in one save, and the next read uses them: 30 days makes a
    /// 40-day-old test stale, weight 0.25 makes the unverified threat 0.25.</summary>
    [Fact]
    public async Task TestG2_SavedParametersDriveTheNextRead()
    {
        SeedSettings("365", "0.5");
        await Declare(Process, rto: 240);
        await RecordTest(Process, 40, rto: 60);
        Assert.Equal(ObjectiveVerificationStatus.Met, (await Continuity.GetProfileAsync(Process, Admin())).RtoVerification.Status);

        var saves = SaveChangesCount;
        var saved = await Continuity.SaveSettingsAsync(new ContinuitySettingsRequest
            { RestorationTestValidityDays = 30, UnverifiedThreatWeight = 0.25m });
        Assert.Equal(1, SaveChangesCount - saves);
        Assert.Equal((30, 0.25m), (saved.RestorationTestValidityDays, saved.UnverifiedThreatWeight));
        Assert.Empty(saved.FallbackApplied);

        var profile = await Continuity.GetProfileAsync(Process, Admin());
        Assert.Equal((ObjectiveVerificationStatus.Unverified, VerificationReason.Stale),
            (profile.RtoVerification.Status, profile.RtoVerification.Reason));
        Assert.Equal(30, profile.RtoVerification.ValidityDays);
        Assert.Equal(0.25m, profile.Threat.ThreatWeight);

        var metric = await Continuity.GetRestorationVerificationMetricAsync();
        Assert.Equal(0.25m, metric.ThreatenedCriticalProcessesWeighted);
    }

    /// <summary>G3 — an out-of-range PUT is refused naming the key, and neither row changes — not even the
    /// valid one in the same request.</summary>
    [Theory]
    [InlineData(0, 0.5, ContinuitySettingKeys.RestorationTestValidityDays)]
    [InlineData(1096, 0.5, ContinuitySettingKeys.RestorationTestValidityDays)]
    [InlineData(30, 0.0, ContinuitySettingKeys.UnverifiedThreatWeight)]
    [InlineData(30, 1.01, ContinuitySettingKeys.UnverifiedThreatWeight)]
    [InlineData(30, 0.255, ContinuitySettingKeys.UnverifiedThreatWeight)]
    public async Task TestG3_AnOutOfRangeSaveChangesNothing(int days, double weight, string key)
    {
        SeedSettings("365", "0.5");

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Continuity.SaveSettingsAsync(
            new ContinuitySettingsRequest { RestorationTestValidityDays = days, UnverifiedThreatWeight = (decimal)weight }));
        Assert.Equal(key, ex.ParameterName);

        var rows = Read(ctx => ctx.Settings.ToDictionary(s => s.Name, s => s.Value));
        Assert.Equal("365", rows[ContinuitySettingKeys.RestorationTestValidityDays]);
        Assert.Equal("0.5", rows[ContinuitySettingKeys.UnverifiedThreatWeight]);
    }

    /// <summary>G4 — two critical processes, one confirmed and one only unverified: 1 + 1, weighted 1.5.</summary>
    [Fact]
    public async Task TestG4_TheWeightedCountAddsTheUnverifiedAtItsWeight()
    {
        await Declare(Process, rto: 240);
        await RecordTest(Process, 3, RestorationTestOutcome.Failed);
        await Declare(Process2, rto: 240);

        var metric = await Continuity.GetRestorationVerificationMetricAsync();

        Assert.Equal(1, metric.ThreatenedCriticalProcessesConfirmed);
        Assert.Equal(1, metric.ThreatenedCriticalProcessesUnverifiedOnly);
        Assert.Equal(1.5m, metric.ThreatenedCriticalProcessesWeighted);
    }

    // --- A1–A3: the trail -------------------------------------------------------------------------

    /// <summary>A1 — every continuity write is audited; changing the RTO writes its field row.</summary>
    [Fact]
    public async Task TestA1_EveryContinuityWriteIsAudited()
    {
        await Declare(Process, rto: 240);
        await Declare(Process, rto: 120);
        var dependency = await Continuity.AddDependencyAsync(Process, new BiaDependencyCreateRequest { ProviderEntityId = Service }, Author);
        await Continuity.DeleteDependencyAsync(Process, dependency.Id);
        var test = await RecordTest(Process, 2, rto: 60);
        await Continuity.VoidRestorationTestAsync(Process, test.Id,
            new RestorationTestVoidRequest { Reason = "Recorded against the wrong process" }, Author);
        await Continuity.DeleteBiaAsync(Process);

        var bia = Audit(nameof(BusinessImpactAnalysis));
        Assert.Single(bia, a => a.Action == AuditLogAction.Create);
        var rto = Assert.Single(bia, a => a.Action == AuditLogAction.Update && a.Field == nameof(BusinessImpactAnalysis.RtoMinutes));
        Assert.Equal(("240", "120"), (rto.OldValue, rto.NewValue));
        Assert.Single(bia, a => a.Action == AuditLogAction.Delete);

        var dependencies = Audit(nameof(BiaDependency));
        Assert.Single(dependencies, a => a.Action == AuditLogAction.Create);
        Assert.Single(dependencies, a => a.Action == AuditLogAction.Delete);

        var tests = Audit(nameof(RestorationTest));
        Assert.Single(tests, a => a.Action == AuditLogAction.Create);
        Assert.Contains(tests, a => a.Action == AuditLogAction.Update && a.Field == nameof(RestorationTest.VoidReason));
    }

    /// <summary>A2 — saving the parameters writes one row per key, naming it, with the old and new values.</summary>
    [Fact]
    public async Task TestA2_SavingTheParametersIsAuditedByKey()
    {
        SeedSettings("365", "0.5");

        await Continuity.SaveSettingsAsync(new ContinuitySettingsRequest { RestorationTestValidityDays = 30, UnverifiedThreatWeight = 0.25m });

        // Seeding the two keys was itself audited (two creates, as Data/90.sql's inserts would be);
        // the save adds exactly one update per key.
        var all = Audit("Setting");
        Assert.Equal(2, all.Count(r => r.Action == AuditLogAction.Create));

        var rows = all.Where(r => r.Action == AuditLogAction.Update).ToList();
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(0, r.EntityId));
        Assert.Contains(rows, r => (r.Field, r.OldValue, r.NewValue) == (ContinuitySettingKeys.RestorationTestValidityDays, "365", "30"));
        Assert.Contains(rows, r => (r.Field, r.OldValue, r.NewValue) == (ContinuitySettingKeys.UnverifiedThreatWeight, "0.5", "0.25"));
    }

    /// <summary>A3 (negative) — the backup password, in the same table, never reaches the trail.</summary>
    [Fact]
    public void TestA3_TheBackupPasswordIsNeverAudited()
    {
        var configurations = GetService<IConfigurationsService>();

        configurations.UpdateBackupPassword("first-secret-value");
        configurations.UpdateBackupPassword("second-secret-value");

        var all = Read(ctx => ctx.AuditLogs.ToList());
        Assert.DoesNotContain(all, a => a.EntityType == "Setting");
        Assert.DoesNotContain(all, a => (a.OldValue ?? "").Contains("secret-value") || (a.NewValue ?? "").Contains("secret-value"));
    }

    // --- CV1: the coverage metric of Stage 9.1 ----------------------------------------------------

    /// <summary>CV1 — the critical-process coverage uses the effective criticality: a BIA MTPD of two hours
    /// makes "Research" (declared 4) a 5 from the BIA; "Enrolment" (declared 5, no BIA) stays declared.</summary>
    [Fact]
    public async Task TestCV1_TheCoverageUsesTheEffectiveCriticality()
    {
        await Declare(Process2, mtpd: 120);

        var coverage = await Chain.GetCriticalProcessCoverageAsync();

        var research = coverage.Rows.Single(r => r.ProcessId == Process2);
        Assert.Equal((5, CriticalitySource.Bia), (research.Criticality, research.CriticalitySource));
        var enrolment = coverage.Rows.Single(r => r.ProcessId == Process);
        Assert.Equal((5, CriticalitySource.Declared), (enrolment.Criticality, enrolment.CriticalitySource));
    }

    // --- Y0, Y1 -----------------------------------------------------------------------------------

    private static string SourceOf(string relative, [CallerFilePath] string thisFile = "")
    {
        var src = new FileInfo(thisFile).Directory!.Parent!.Parent!.FullName;
        var path = Path.Combine(src, relative);
        Assert.True(File.Exists(path), $"{path} is missing.");

        var code = File.ReadAllText(path);
        code = Regex.Replace(code, @"/\*.*?\*/", " ", RegexOptions.Singleline);
        code = Regex.Replace(code, @"//[^\n]*", " ");
        return code;
    }

    /// <summary>Y0 — nothing in the continuity service switches the scope filters off.</summary>
    [Fact]
    public void TestY0_NoContinuityQueryBypassesTheScopeFilters()
    {
        var code = SourceOf("ServerServices/Governance/ContinuityService.cs");

        Assert.DoesNotContain("IgnoreQueryFilters", code);
        Assert.DoesNotContain("bypassEntityScope", code);
    }

    /// <summary>Y1 — the definitions and properties the service reads exist in the loaded configuration with
    /// the types it expects.</summary>
    [Fact]
    public async Task TestY1_TheSubjectDefinitionsMatchTheEntitiesConfiguration()
    {
        var definitions = (await GetService<IEntitiesService>().GetEntitiesConfigurationAsync()).Definitions;

        foreach (var definition in new[] { RiskChainSchema.ProcessDefinition, RiskChainSchema.ItServiceDefinition })
        {
            Assert.True(definitions.ContainsKey(definition), $"'{definition}' is not configured.");
            Assert.True(definitions[definition].Properties.ContainsKey(RiskChainSchema.NameProperty));
            Assert.Equal("Boolean", definitions[definition].Properties[RiskChainSchema.IsActiveProperty].Type);
        }

        var criticality = definitions[RiskChainSchema.ProcessDefinition].Properties[RiskChainSchema.CriticalityProperty];
        Assert.Equal("Integer", criticality.Type);
        Assert.True(criticality.Nullable);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using DAL.Exceptions;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Governance;
using Model.Monitoring;
using Model.RiskFlags;
using Model.TailRisk;
using NSubstitute;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.8 (S49 §8) — <see cref="MonitoringService"/>, Gate B by indicator in
/// <see cref="RiskWorkflowService"/>/<see cref="RiskAcceptancesService"/> and the <see cref="MethodologyMetricsService"/>
/// panel, against the real model, scope filters and audit interceptor on the EF in-memory provider: guards V1–V11, the
/// KRI register K1–K3, the stale KRI S1, idempotence I1–I6, declared events E1–E5, Gate B GB1–GB9 and the panel M1–M6.
///
/// The organisation is the Stage 9.1 one (<see cref="RiskChainTestBase"/>): units 100 and 200, user 7. The two cases the
/// track names for this stage are S1 (a KRI with no recent reading reads stale, everywhere) and I1 (a KRI breached for
/// thirty days raises one reassessment per risk, not thirty).
///
/// The in-memory provider does not enforce unique indexes: the database half of the idempotence (one event per opening
/// reading, one trigger per event and risk) is Track9KriReassessmentSchemaTests Q2, which needs Docker.
/// </summary>
[TestSubject(typeof(MonitoringService))]
public class MonitoringServiceInMemoryTest : RiskChainTestBase
{
    private const int Cro = 1;
    private const int Owner = 2;
    private const int GlobalAppetite = 1;

    private MonitoringService? _svc;

    /// <summary>One instance per test, so a test can move its clock.</summary>
    private MonitoringService Svc => _svc ??= (MonitoringService)GetService<IMonitoringService>();

    private IRiskAcceptancesService Acceptances => GetService<IRiskAcceptancesService>();
    private IRiskWorkflowService Workflow => GetService<IRiskWorkflowService>();
    private IRiskFlagsService Flags => GetService<IRiskFlagsService>();
    private IMethodologyMetricsService Metrics => GetService<IMethodologyMetricsService>();

    public MonitoringServiceInMemoryTest()
    {
        SeedUnscoped(ctx =>
        {
            ctx.Users.Add(NewUser(Cro, "cro", admin: true));
            ctx.Users.Add(NewUser(Owner, "owner"));
            ctx.Settings.Add(new Setting { Name = RiskWorkflowService.SegregationSetting, Value = "true" });
        });
    }

    // --- seeding -------------------------------------------------------------------------------------

    private static User NewUser(int id, string name, bool admin = false) => new()
    {
        Value = id, Name = name, Login = name, Enabled = true, Admin = admin, Type = "local", Salt = "s",
        Password = Encoding.UTF8.GetBytes("p"), Email = $"{name}@example.test"
    };

    /// <summary>A risk owned by <see cref="Owner"/>, so <see cref="Cro"/> may accept it.</summary>
    private void OwnedRisk(int id, int? unit = UnitA, float score = 5f)
    {
        AddRisk(id, unit, score: score);
        SeedUnscoped(ctx =>
        {
            var risk = ctx.Risks.Single(r => r.Id == id);
            risk.Owner = Owner;
            risk.Manager = Owner;
            risk.SubmittedBy = Owner;
        });
    }

    private void Appetite(double ceiling = 10) =>
        SeedUnscoped(ctx => ctx.RiskAppetites.Add(new RiskAppetite
        {
            Id = GlobalAppetite, EntityId = null, MaxAcceptableResidual = ceiling, DualApprovalThreshold = ceiling,
            CreatedAt = DateTime.UtcNow
        }));

    private static RiskAcceptanceRequest Acceptance() => new()
    {
        Name = "Exception", BusinessJustification = "Insured and monitored.", ExpiresAt = DateTime.UtcNow.AddDays(90)
    };

    /// <summary>Hours down per month: worse when higher, tolerance 8, warning 6, readings current for 31 days.</summary>
    private static KriRequest Definition(int? entityId = null, int maxAge = 31) => new()
    {
        Name = "ERP hours down", Description = "Unplanned downtime of the ERP in the month.",
        Category = KriCategory.Unavailability, Source = "Zabbix monthly availability report", Unit = "hours",
        Direction = KriDirection.HigherIsWorse, ToleranceThreshold = 8m, WarningThreshold = 6m,
        ToleranceRationale = "Board minute 2026/07: at most one working day a month.", MaxReadingAgeDays = maxAge,
        OwnerId = Owner, EntityId = entityId
    };

    private async Task<int> NewKri(int? entityId = null, int maxAge = 31) =>
        (await Svc.CreateKriAsync(Definition(entityId, maxAge), Cro)).Id;

    private Task<KriDetailDto> Read(int kriId, decimal value, DateTime observedAt, MonitoringService? svc = null) =>
        (svc ?? Svc).RecordReadingAsync(kriId, new KriReadingRequest { Value = value, ObservedAt = observedAt, Note = "report" },
            Author);

    /// <summary>A KRI linked to the risks, with one reading: beyond the tolerance by default, observed a day ago.</summary>
    private async Task<int> LinkedKri(decimal value, int[] riskIds, int? entityId = null, double daysAgo = 1)
    {
        var kri = await NewKri(entityId);
        foreach (var risk in riskIds) await Svc.LinkRiskAsync(kri, risk, Author);
        await Read(kri, value, DateTime.UtcNow.AddDays(-daysAgo));
        return kri;
    }

    private List<ReassessmentEvent> Events() => Read(ctx => ctx.ReassessmentEvents.OrderBy(e => e.Id).ToList());

    private List<RiskReassessmentTrigger> Triggers() =>
        Read(ctx => ctx.RiskReassessmentTriggers.OrderBy(t => t.Id).ToList());

    private Risk RiskRow(int id) => Read(ctx => ctx.Risks.Single(r => r.Id == id));

    private List<AuditLog> Audit(string type) =>
        Read(ctx => ctx.AuditLogs.Where(a => a.EntityType == type).OrderBy(a => a.Id).ToList());

    private void Review(int riskId, DateTime submittedAt, int id) =>
        SeedUnscoped(ctx => ctx.MgmtReviews.Add(new MgmtReview
        {
            Id = id, RiskId = riskId, SubmissionDate = submittedAt, Review = 1, Reviewer = Cro, NextStep = 1,
            Comments = "Reassessed.", NextReview = DateOnly.FromDateTime(submittedAt.AddDays(90))
        }));

    // --- V1–V11: guards ------------------------------------------------------------------------------

    /// <summary>V1 — every invalid definition is refused naming the field, and nothing is written.</summary>
    [Theory]
    [InlineData("no-name", "Name")]
    [InlineData("long-name", "Name")]
    [InlineData("no-category", "Category")]
    [InlineData("undefined-category", "Category")]
    [InlineData("no-source", "Source")]
    [InlineData("no-unit", "Unit")]
    [InlineData("no-direction", "Direction")]
    [InlineData("no-tolerance", "ToleranceThreshold")]
    [InlineData("huge-tolerance", "ToleranceThreshold")]
    [InlineData("warning-beyond-tolerance", "WarningThreshold")]
    [InlineData("warning-wrong-side-lower", "WarningThreshold")]
    [InlineData("no-rationale", "ToleranceRationale")]
    [InlineData("age-zero", "MaxReadingAgeDays")]
    [InlineData("age-too-long", "MaxReadingAgeDays")]
    [InlineData("owner-zero", "OwnerId")]
    public async Task TestV1_AnInvalidDefinitionIsRefusedNamingTheField(string scenario, string field)
    {
        var request = Definition();
        switch (scenario)
        {
            case "no-name": request.Name = "  "; break;
            case "long-name": request.Name = new string('x', 201); break;
            case "no-category": request.Category = null; break;
            case "undefined-category": request.Category = (KriCategory)9; break;
            case "no-source": request.Source = null; break;
            case "no-unit": request.Unit = ""; break;
            case "no-direction": request.Direction = null; break;
            case "no-tolerance": request.ToleranceThreshold = null; break;
            case "huge-tolerance": request.ToleranceThreshold = 2_000_000_000_000m; break;
            case "warning-beyond-tolerance": request.WarningThreshold = 9m; break;
            case "warning-wrong-side-lower":
                request.Direction = KriDirection.LowerIsWorse;
                request.ToleranceThreshold = 95m;
                request.WarningThreshold = 90m;
                break;
            case "no-rationale": request.ToleranceRationale = null; break;
            case "age-zero": request.MaxReadingAgeDays = 0; break;
            case "age-too-long": request.MaxReadingAgeDays = 367; break;
            default: request.OwnerId = 0; break;
        }

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.CreateKriAsync(request, Cro));

        Assert.Equal(field, ex.ParameterName);
        Assert.Equal(0, Read(ctx => ctx.Kris.Count()));
    }

    /// <summary>V2 — every invalid reading is refused naming the field, and nothing is written.</summary>
    [Theory]
    [InlineData("no-value", "Value")]
    [InlineData("huge-value", "Value")]
    [InlineData("no-date", "ObservedAt")]
    [InlineData("future", "ObservedAt")]
    [InlineData("before-2000", "ObservedAt")]
    [InlineData("long-note", "Note")]
    public async Task TestV2_AnInvalidReadingIsRefusedNamingTheField(string scenario, string field)
    {
        var kri = await NewKri();
        var request = new KriReadingRequest { Value = 3m, ObservedAt = DateTime.UtcNow.AddDays(-1) };
        switch (scenario)
        {
            case "no-value": request.Value = null; break;
            case "huge-value": request.Value = -2_000_000_000_000m; break;
            case "no-date": request.ObservedAt = null; break;
            case "future": request.ObservedAt = DateTime.UtcNow.AddHours(1); break;
            case "before-2000": request.ObservedAt = new DateTime(1999, 12, 31, 0, 0, 0, DateTimeKind.Utc); break;
            default: request.Note = new string('x', 1001); break;
        }

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.RecordReadingAsync(kri, request, Author));

        Assert.Equal(field, ex.ParameterName);
        Assert.Equal(0, Read(ctx => ctx.KriReadings.Count()));
    }

    /// <summary>
    /// V3 — a missing KRI, reading, risk, owner or entity, and a KRI or risk outside the caller's scope, are not found;
    /// nothing is written.
    /// </summary>
    [Fact]
    public async Task TestV3_AMissingOrOutOfScopeRecordIsNotFound()
    {
        AddRisk(1, UnitA);
        var kriOfA = await NewKri(UnitA);
        var reading = new KriReadingRequest { Value = 3m, ObservedAt = DateTime.UtcNow };

        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetKriAsync(999));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.RecordReadingAsync(999, reading, Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.LinkRiskAsync(999, 1, Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.LinkRiskAsync(kriOfA, 999, Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.UnlinkRiskAsync(kriOfA, 1, Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Svc.VoidReadingAsync(kriOfA, 999, new KriReadingVoidRequest { Reason = "typo" }, Author));
        var unknownOwner = Definition();
        unknownOwner.OwnerId = 999;
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.CreateKriAsync(unknownOwner, Cro));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.CreateKriAsync(Definition(entityId: 999), Cro));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetTriggersAsync(999, false));

        ScopeTo(UnitB);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetKriAsync(kriOfA));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.RecordReadingAsync(kriOfA, reading, Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.LinkRiskAsync(kriOfA, 1, Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetTriggersAsync(1, false));
        Assert.Empty(await Svc.GetKrisAsync(includeRetired: true));

        Assert.Equal(0, Read(ctx => ctx.KriReadings.Count() + ctx.KriRisks.Count()));
    }

    /// <summary>V4 — a retired KRI takes no reading, no link and no change.</summary>
    [Fact]
    public async Task TestV4_ARetiredKriRefusesReadingsLinksAndChanges()
    {
        AddRisk(1, UnitA);
        var kri = await NewKri();
        await Svc.RetireKriAsync(kri, Cro);

        var reading = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Svc.RecordReadingAsync(kri, new KriReadingRequest { Value = 3m, ObservedAt = DateTime.UtcNow }, Author));
        var link = await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.LinkRiskAsync(kri, 1, Author));
        var update = await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.UpdateKriAsync(kri, Definition(), Cro));

        Assert.All(new[] { reading, link, update }, ex => Assert.Equal(MonitoringService.KriRetiredRule, ex.RuleName));
        Assert.Equal(0, Read(ctx => ctx.KriReadings.Count() + ctx.KriRisks.Count()));
    }

    /// <summary>V5 — a KRI of another entity than the risk cannot govern it; an organization-wide or same-entity one can.</summary>
    [Fact]
    public async Task TestV5_AKriOfAnotherEntityCannotBeLinked()
    {
        AddRisk(1, UnitA);
        var ofB = await NewKri(UnitB);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.LinkRiskAsync(ofB, 1, Author));
        Assert.Equal(MonitoringService.KriEntityMismatchRule, ex.RuleName);

        await Svc.LinkRiskAsync(await NewKri(UnitA), 1, Author);
        await Svc.LinkRiskAsync(await NewKri(), 1, Author);
        Assert.Equal(2, Read(ctx => ctx.KriRisks.Count()));
    }

    /// <summary>V6 — a voiding needs a reason, and is not repeated.</summary>
    [Fact]
    public async Task TestV6_AVoidingNeedsAReasonAndHappensOnce()
    {
        var kri = await NewKri();
        var detail = await Read(kri, 3m, DateTime.UtcNow.AddDays(-1));
        var readingId = Assert.Single(detail.Readings).Id;

        var missing = await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.VoidReadingAsync(kri, readingId, new KriReadingVoidRequest { Reason = " " }, Author));
        Assert.Equal("Reason", missing.ParameterName);

        await Svc.VoidReadingAsync(kri, readingId, new KriReadingVoidRequest { Reason = "Typed into the wrong KRI." }, Author);

        var twice = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Svc.VoidReadingAsync(kri, readingId, new KriReadingVoidRequest { Reason = "Again." }, Author));
        Assert.Equal(MonitoringService.ReadingAlreadyVoidedRule, twice.RuleName);
    }

    /// <summary>V7 — every invalid event is refused naming the field, and nothing is written.</summary>
    [Theory]
    [InlineData("no-type", "Type")]
    [InlineData("undefined-type", "Type")]
    [InlineData("no-title", "Title")]
    [InlineData("long-description", "Description")]
    [InlineData("no-date", "OccurredAt")]
    [InlineData("future", "OccurredAt")]
    [InlineData("no-risks", "RiskIds")]
    [InlineData("empty-risks", "RiskIds")]
    [InlineData("bad-risk-id", "RiskIds")]
    [InlineData("too-many-risks", "RiskIds")]
    [InlineData("incident-on-another-type", "IncidentId")]
    public async Task TestV7_AnInvalidEventIsRefusedNamingTheField(string scenario, string field)
    {
        AddRisk(1, UnitA);
        var request = new ReassessmentEventRequest
        {
            Type = ReassessmentTriggerType.NewRegulation, Title = "LGPD resolution 2/2026",
            OccurredAt = DateTime.UtcNow.AddDays(-2), RiskIds = [1]
        };
        switch (scenario)
        {
            case "no-type": request.Type = null; break;
            case "undefined-type": request.Type = (ReassessmentTriggerType)7; break;
            case "no-title": request.Title = null; break;
            case "long-description": request.Description = new string('x', 2001); break;
            case "no-date": request.OccurredAt = null; break;
            case "future": request.OccurredAt = DateTime.UtcNow.AddDays(1); break;
            case "no-risks": request.RiskIds = null; break;
            case "empty-risks": request.RiskIds = []; break;
            case "bad-risk-id": request.RiskIds = [1, 0]; break;
            case "too-many-risks": request.RiskIds = Enumerable.Range(1, 501).ToList(); break;
            default: request.IncidentId = 5; break;
        }

        var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.DeclareEventAsync(request, Author));

        Assert.Equal(field, ex.ParameterName);
        Assert.Empty(Events());
    }

    /// <summary>V8 — one risk outside the caller's scope makes the whole declaration a 404: nothing written, nothing flagged.</summary>
    [Fact]
    public async Task TestV8_ARiskOutsideTheScopeIsNotFoundAndNothingIsWritten()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitB);

        ScopeTo(UnitA);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.DeclareEventAsync(new ReassessmentEventRequest
        {
            Type = ReassessmentTriggerType.ArchitectureOrTechnologyChange, Title = "ERP moved to the cloud",
            OccurredAt = DateTime.UtcNow, RiskIds = [1, 2]
        }, Author));

        Assert.Empty(Events());
        Assert.False(RiskRow(1).ReviewRequested);
    }

    /// <summary>V9 — an incident has one event: a second declaration is a 409 naming the first, even one the caller cannot see.</summary>
    [Fact]
    public async Task TestV9_AnIncidentIsDeclaredOnce()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitB);
        SeedUnscoped(ctx => ctx.Incidents.Add(new Incident
        {
            Id = 5, Name = "2026-5", Description = "Ransomware on the file server", EntityId = UnitA,
            Kind = IncidentKind.Incident
        }));

        var first = await Svc.DeclareEventAsync(IncidentEvent(2), Author);
        Assert.Equal(5, first.IncidentId);

        // The first event triggers a risk of unit B only, so a unit A caller cannot see it — and is still told of it.
        ScopeTo(UnitA);
        Assert.Empty(await Svc.GetEventsAsync(null, null));

        var ex = await Assert.ThrowsAsync<DataAlreadyExistsException>(() => Svc.DeclareEventAsync(IncidentEvent(1), Author));
        Assert.Equal(first.Id.ToString(), ex.Identification);
        Assert.Single(Events());

        ScopeToEverything();
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.DeclareEventAsync(IncidentEvent(1, incidentId: 99), Author));
    }

    private static ReassessmentEventRequest IncidentEvent(int riskId, int incidentId = 5) => new()
    {
        Type = ReassessmentTriggerType.SignificantIncidentOrNearMiss, Title = "Ransomware on the file server",
        OccurredAt = DateTime.UtcNow.AddDays(-1), IncidentId = incidentId, RiskIds = [riskId]
    };

    /// <summary>
    /// V10 — a caller scoped to several entities cannot write an organization-wide KRI, nor one of an entity outside the
    /// scope: the write guard refuses it (403 through the middleware).
    /// </summary>
    [Fact]
    public async Task TestV10_AScopedCallerCannotWriteAKriOutsideTheScope()
    {
        ScopeTo(UnitA, Process);

        await Assert.ThrowsAsync<EntityScopeViolationException>(() => Svc.CreateKriAsync(Definition(), Cro));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => Svc.CreateKriAsync(Definition(UnitB), Cro));
        Assert.Equal(0, Read(ctx => ctx.Kris.Count()));

        var own = await Svc.CreateKriAsync(Definition(UnitA), Cro);
        Assert.Equal(UnitA, own.EntityId);
    }

    /// <summary>
    /// V12 — an organization-wide KRI gates every unit's risks: a scoped caller reads it and links their own risk to it,
    /// but cannot record or void its readings (403 through the middleware); an unrestricted caller can.
    /// </summary>
    [Fact]
    public async Task TestV12_AScopedCallerCannotWriteTheHistoryOfAnOrganizationWideKri()
    {
        AddRisk(1, UnitA);
        var kri = await NewKri();
        var first = await Read(kri, 3m, DateTime.UtcNow.AddDays(-2));

        ScopeTo(UnitA);
        Assert.Equal(kri, (await Svc.GetKriAsync(kri)).Id);
        await Svc.LinkRiskAsync(kri, 1, Author);

        await Assert.ThrowsAsync<EntityScopeViolationException>(() => Read(kri, 12m, DateTime.UtcNow.AddDays(-1)));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() =>
            Svc.VoidReadingAsync(kri, first.Readings[0].Id, new KriReadingVoidRequest { Reason = "Not ours." }, Author));
        Assert.Equal(1, Read(ctx => ctx.KriReadings.Count(r => r.VoidedAt == null)));

        // Of its own unit, the same caller writes the history.
        var own = await NewKri(UnitA);
        Assert.Single((await Read(own, 3m, DateTime.UtcNow)).Readings);

        ScopeToEverything();
        Assert.Equal(2, (await Read(kri, 12m, DateTime.UtcNow.AddDays(-1))).Readings.Count);
    }

    /// <summary>
    /// V13 — a KRI cannot be moved to an entity while it governs a risk of another one (S49 §4.3, D6), whether or not the
    /// caller can see that risk; moving it to the risks' own entity, or keeping it organization-wide, is fine.
    /// </summary>
    [Fact]
    public async Task TestV13_AKriCannotMoveAwayFromTheEntityOfItsRisks()
    {
        AddRisk(1, UnitA);
        var kri = await NewKri();
        await Svc.LinkRiskAsync(kri, 1, Author);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.UpdateKriAsync(kri, Definition(UnitB), Cro));
        Assert.Equal(MonitoringService.KriEntityMismatchRule, ex.RuleName);
        Assert.Null(Read(ctx => ctx.Kris.Single(k => k.Id == kri)).EntityId);

        Assert.Equal(UnitA, (await Svc.UpdateKriAsync(kri, Definition(UnitA), Cro)).EntityId);
        Assert.Null((await Svc.UpdateKriAsync(kri, Definition(), Cro)).EntityId);
    }

    /// <summary>
    /// V14 — the unit of a risk can remove a gate it cannot see: a KRI of unit B came to govern a risk now in unit A (the
    /// risk moved), and unit A unlinks it through the risk alone. The KRI itself stays invisible and unchanged.
    /// </summary>
    [Fact]
    public async Task TestV14_ARisksUnitCanUnlinkAKriItCannotSee()
    {
        OwnedRisk(1, unit: UnitB);
        var kri = await LinkedKri(12m, [1], entityId: UnitB);
        SeedUnscoped(ctx => ctx.Risks.Single(r => r.Id == 1).EntityId = UnitA);

        ScopeTo(UnitA);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetKriAsync(kri));
        await Svc.UnlinkRiskAsync(kri, 1, Author);

        Assert.Equal(IndicatorAppetiteState.NotConfigured, (await Workflow.EvaluateAppetiteAsync(1)).Indicators.State);
        Assert.Equal(0, Read(ctx => ctx.KriRisks.Count()));
        Assert.Contains(Audit(nameof(KriRisk)), a => a.Action == AuditLogAction.Delete && a.UserId == Author);
    }

    /// <summary>V11 — an event whose every risk is closed would reassess nothing, and is refused.</summary>
    [Fact]
    public async Task TestV11_AnEventOnClosedRisksOnlyIsRefused()
    {
        AddRisk(1, UnitA, status: RiskWorkflowService.StatusClosed);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.DeclareEventAsync(new ReassessmentEventRequest
        {
            Type = ReassessmentTriggerType.NewAiModel, Title = "Chatbot deployed", OccurredAt = DateTime.UtcNow,
            RiskIds = [1]
        }, Author));

        Assert.Equal(MonitoringService.NoOpenRiskRule, ex.RuleName);
        Assert.Empty(Events());
    }

    // --- K1–K3: the register ------------------------------------------------------------------------

    /// <summary>K1 — define, read with its state, change, retire (idempotent), and the list hides the retired.</summary>
    [Fact]
    public async Task TestK1_AKriIsDefinedReadChangedAndRetired()
    {
        var created = await Svc.CreateKriAsync(Definition(), Cro);
        Assert.Equal((KriState.NoReading, 8m, 31, Cro), (created.Status.State, created.ToleranceThreshold,
            created.MaxReadingAgeDays, created.UpdatedById!.Value));

        var defaults = Definition();
        defaults.MaxReadingAgeDays = null;
        Assert.Equal(MonitoringLimits.DefaultMaxReadingAgeDays, (await Svc.CreateKriAsync(defaults, Cro)).MaxReadingAgeDays);

        var changed = Definition();
        changed.ToleranceThreshold = 4m;
        changed.WarningThreshold = 3m;
        var updated = await Svc.UpdateKriAsync(created.Id, changed, Cro);
        Assert.Equal(4m, updated.ToleranceThreshold);
        Assert.NotNull(updated.UpdatedAt);

        var retired = await Svc.RetireKriAsync(created.Id, Cro);
        Assert.Equal(KriState.Retired, retired.Status.State);
        var again = await Svc.RetireKriAsync(created.Id, Cro);
        Assert.Equal(retired.RetiredAt, again.RetiredAt);

        Assert.DoesNotContain(await Svc.GetKrisAsync(false), k => k.Id == created.Id);
        Assert.Contains(await Svc.GetKrisAsync(true), k => k.Id == created.Id);
    }

    /// <summary>
    /// K2 — a reading is evaluated at once; a voided one stays in the history, marked, and stops counting; a reading a
    /// few minutes "in the future" (clock skew) is recorded at the server's now so it counts at once.
    /// </summary>
    [Fact]
    public async Task TestK2_AReadingCountsAndAVoidedOneStaysButStopsCounting()
    {
        var kri = await NewKri();

        await Read(kri, 3m, DateTime.UtcNow.AddDays(-3));
        var breached = await Read(kri, 12m, DateTime.UtcNow.AddMinutes(2));
        Assert.Equal(KriState.Breached, breached.Status.State);
        Assert.True(breached.Readings[0].BeyondTolerance);
        Assert.True(breached.Readings[0].ObservedAt <= DateTime.UtcNow);

        var voided = await Svc.VoidReadingAsync(kri, breached.Readings[0].Id,
            new KriReadingVoidRequest { Reason = "Typed 12 for 1.2." }, Author);

        Assert.Equal(KriState.WithinTolerance, voided.Status.State);
        Assert.Equal(2, voided.Readings.Count);
        var row = voided.Readings.Single(r => r.VoidedAt != null);
        Assert.Equal(("Typed 12 for 1.2.", Author), (row.VoidReason!, row.VoidedById!.Value));
    }

    /// <summary>K3 — the definition, the readings and their voiding, and the links are audited with the person.</summary>
    [Fact]
    public async Task TestK3_TheRegisterIsAuditedWithThePerson()
    {
        AddRisk(1, UnitA);
        var kri = await NewKri();
        var detail = await Read(kri, 3m, DateTime.UtcNow.AddDays(-1));
        await Svc.VoidReadingAsync(kri, detail.Readings[0].Id, new KriReadingVoidRequest { Reason = "Wrong month." }, Author);
        await Svc.LinkRiskAsync(kri, 1, Author);
        await Svc.UnlinkRiskAsync(kri, 1, Author);

        Assert.NotEmpty(Audit(nameof(Kri)));
        Assert.All(Audit(nameof(Kri)), a => Assert.Equal(Cro, a.UserId));
        Assert.Contains(Audit(nameof(KriReading)), a => a.Field == nameof(KriReading.VoidReason) && a.UserId == Author);
        Assert.Contains(Audit(nameof(KriRisk)), a => a.Action == AuditLogAction.Delete && a.UserId == Author);
    }

    // --- S1: the stale KRI (T193) -------------------------------------------------------------------

    /// <summary>
    /// S1 (T193) — end to end: a KRI whose only reading, within tolerance, is 40 days old reads stale in its own detail,
    /// makes Gate B by indicator not assessable (never within) on the risk it governs, counts as stale in the panel, and
    /// opens no breach episode.
    /// </summary>
    [Fact]
    public async Task TestS1_AKriWithNoRecentReadingReadsStaleEverywhere()
    {
        OwnedRisk(1);
        var kri = await LinkedKri(2m, [1], daysAgo: 40);

        Assert.Equal(KriState.Stale, (await Svc.GetKriAsync(kri)).Status.State);

        var appetite = await Workflow.EvaluateAppetiteAsync(1);
        Assert.Equal(IndicatorAppetiteState.NotAssessable, appetite.Indicators.State);
        Assert.Equal([IndicatorNotAssessableReason.Stale], appetite.Indicators.Reasons);
        Assert.Equal(KriState.Stale, Assert.Single(appetite.Indicators.Kris).State);

        var panel = await Metrics.GetAsync();
        Assert.Equal((1, 1, 0), (panel.Kris.Active, panel.Kris.Stale, panel.Kris.WithinTolerance));

        var summary = await Svc.EvaluateAllAsync();
        Assert.Equal((1, 1, 0), (summary.KrisEvaluated, summary.Stale, summary.EpisodesOpened));
        Assert.Empty(Events());
        Assert.False(RiskRow(1).ReviewRequested);
    }

    // --- I1–I6: idempotence (T193) ------------------------------------------------------------------

    /// <summary>
    /// I1 (T193) — a KRI that stays beyond its tolerance for thirty days, evaluated every day and read again every week,
    /// raises one breach episode and one reassessment per governed risk — not thirty — announces the breach once and each
    /// trigger once, and keeps the first reason on the risk.
    /// </summary>
    [Fact]
    public async Task TestI1_AKriBreachedForThirtyDaysRaisesOneReassessmentPerRisk()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        var publisher = Substitute.For<INotificationEventPublisher>();
        var day0 = DateTime.UtcNow.AddDays(-35);
        var day = day0;
        var svc = new MonitoringService(GetService<Serilog.ILogger>(), GetService<IDalService>(), publisher)
            { Clock = () => day };

        var kri = (await svc.CreateKriAsync(Definition(), Cro)).Id;
        await svc.LinkRiskAsync(kri, 1, Author);
        await svc.LinkRiskAsync(kri, 2, Author);
        await Read(kri, 12m, day0, svc);

        var firstReason = RiskRow(1).ReviewRequestedReason;
        var firstAt = RiskRow(1).ReviewRequestedAt;

        for (var d = 1; d <= 30; d++)
        {
            day = day0.AddDays(d);
            if (d % 7 == 0) await Read(kri, 10m + d, day, svc);
            else await svc.EvaluateAllAsync();
        }

        var episode = Assert.Single(Events());
        Assert.Equal((ReassessmentEventOrigin.KriBreach, ReassessmentTriggerType.NewDataOrKriBreach),
            (episode.Origin, episode.TriggerType));
        Assert.Null(episode.KriBreachEndedAt);
        Assert.Equal(new[] { 1, 2 }, Triggers().Select(t => t.RiskId).OrderBy(id => id));
        Assert.All(Triggers(), t => Assert.Equal(episode.Id, t.EventId));

        Assert.True(RiskRow(1).ReviewRequested);
        Assert.Equal((firstReason, firstAt), (RiskRow(1).ReviewRequestedReason, RiskRow(1).ReviewRequestedAt));
        Assert.Contains("Mandatory reassessment", firstReason);

        await publisher.Received(1).KriToleranceBreachedAsync(Arg.Any<Kri>(), 12m, Arg.Any<DateTime>(), 2);
        await publisher.Received(2).RiskReassessmentTriggeredAsync(Arg.Any<Risk>(), Arg.Any<double?>(),
            Arg.Any<ReassessmentEvent>());
    }

    /// <summary>I2 — recovery ends the episode; a new breach after it is another episode, with another trigger per risk.</summary>
    [Fact]
    public async Task TestI2_RecoveryEndsTheEpisodeAndANewBreachIsAnotherOne()
    {
        AddRisk(1, UnitA);
        var kri = await LinkedKri(12m, [1], daysAgo: 3);

        await Read(kri, 5m, DateTime.UtcNow.AddDays(-2));
        Assert.NotNull(Assert.Single(Events()).KriBreachEndedAt);

        await Read(kri, 9m, DateTime.UtcNow.AddDays(-1));

        Assert.Equal(2, Events().Count);
        Assert.Null(Events()[1].KriBreachEndedAt);
        Assert.Equal(2, Triggers().Count(t => t.RiskId == 1));
    }

    /// <summary>I3 — a risk linked during the episode is triggered at once, and the others are not triggered again.</summary>
    [Fact]
    public async Task TestI3_ARiskLinkedDuringTheEpisodeGetsItsTriggerOnce()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        var kri = await LinkedKri(12m, [1]);

        await Svc.LinkRiskAsync(kri, 2, Author);
        await Svc.LinkRiskAsync(kri, 2, Author); // idempotent
        await Svc.EvaluateAllAsync();

        Assert.Single(Events());
        Assert.Equal(new[] { 1, 2 }, Triggers().Select(t => t.RiskId).OrderBy(id => id));
        Assert.Equal(1, Read(ctx => ctx.KriRisks.Count(l => l.RiskId == 2)));
    }

    /// <summary>
    /// I4 — voiding the reading that opened the episode while it is still breached does not open a second one; voiding the
    /// recovery reading that ended it reopens the same episode rather than starting another — even when its opening
    /// reading is voided too. Either shortcut would raise a second reassessment of the risk for one cause.
    /// </summary>
    [Fact]
    public async Task TestI4_VoidingAReadingNeverDuplicatesTheEpisode()
    {
        AddRisk(1, UnitA);
        var kri = await LinkedKri(12m, [1], daysAgo: 4);
        var opening = Assert.Single(Events()).KriReadingId!.Value;
        await Read(kri, 13m, DateTime.UtcNow.AddDays(-3));

        await Svc.VoidReadingAsync(kri, opening, new KriReadingVoidRequest { Reason = "Duplicate import." }, Author);
        await Svc.EvaluateAllAsync();
        Assert.Single(Events());
        Assert.Single(Triggers());

        var recovery = await Read(kri, 2m, DateTime.UtcNow.AddDays(-2));
        Assert.NotNull(Assert.Single(Events()).KriBreachEndedAt);

        await Svc.VoidReadingAsync(kri, recovery.Readings[0].Id,
            new KriReadingVoidRequest { Reason = "The monitor was down, not the ERP up." }, Author);

        var reopened = Assert.Single(Events());
        Assert.Null(reopened.KriBreachEndedAt);
        Assert.Equal(opening, reopened.KriReadingId);
        Assert.Single(Triggers());
        Assert.Equal(KriState.Breached, (await Svc.GetKriAsync(kri)).Status.State);
    }

    /// <summary>I5 — a closed risk the KRI governs is not triggered.</summary>
    [Fact]
    public async Task TestI5_AClosedRiskIsNotTriggered()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA, status: RiskWorkflowService.StatusClosed);

        await LinkedKri(12m, [1, 2]);

        Assert.Equal(1, Assert.Single(Triggers()).RiskId);
        Assert.False(RiskRow(2).ReviewRequested);
    }

    /// <summary>I6 — a breached KRI that goes stale keeps its episode open: no reading shows it recovered.</summary>
    [Fact]
    public async Task TestI6_StaleDoesNotEndTheEpisode()
    {
        AddRisk(1, UnitA);
        var kri = await LinkedKri(12m, [1]);

        Svc.Clock = () => DateTime.UtcNow.AddDays(45);
        var summary = await Svc.EvaluateAllAsync();

        Assert.Equal((1, 0, 0), (summary.Stale, summary.EpisodesClosed, summary.EpisodesOpened));
        Assert.Null(Assert.Single(Events()).KriBreachEndedAt);
        Assert.Equal(KriState.Stale, (await Svc.GetKriAsync(kri)).Status.State);
        Assert.Single(Triggers());
    }

    /// <summary>
    /// I7 — a genuine new breach recorded after an episode ended is a new episode, even when the episode ended only
    /// because its opening reading was voided: R0 = 5 (d-10), R1 = 12 (d-5) opens E1, a review answers it, R1 is voided
    /// (E1 ends), then R3 = 15 (d-1) arrives. Folding R3 into E1 — the run R3 starts was recorded after E1 ended — would
    /// raise no trigger, no kri.breached and no review request for a real breach.
    /// </summary>
    [Fact]
    public async Task TestI7_AGenuineBreachAfterAVoidedOneOpensANewEpisode()
    {
        AddRisk(1, UnitA);
        var publisher = Substitute.For<INotificationEventPublisher>();
        var now = DateTime.UtcNow;
        var clock = now.AddMinutes(-30);
        var svc = new MonitoringService(GetService<Serilog.ILogger>(), GetService<IDalService>(), publisher)
            { Clock = () => clock };

        var kri = (await svc.CreateKriAsync(Definition(), Cro)).Id;
        await svc.LinkRiskAsync(kri, 1, Author);
        await Read(kri, 5m, now.AddDays(-10), svc);
        var r1 = (await Read(kri, 12m, now.AddDays(-5), svc)).Readings[0].Id;
        var first = Assert.Single(Events());

        // The management review that answers the trigger also clears the review request, as MgmtReviewsService does.
        clock = now.AddMinutes(-20);
        Review(1, clock, 701);
        SeedUnscoped(ctx => ctx.Risks.Single(r => r.Id == 1).ReviewRequested = false);

        clock = now.AddMinutes(-10);
        await svc.VoidReadingAsync(kri, r1, new KriReadingVoidRequest { Reason = "Imported twice." }, Author);
        Assert.NotNull(Assert.Single(Events()).KriBreachEndedAt);

        clock = now;
        await Read(kri, 15m, now.AddDays(-1), svc);

        Assert.Equal(2, Events().Count);
        Assert.NotNull(Events()[0].KriBreachEndedAt);
        Assert.Null(Events()[1].KriBreachEndedAt);
        Assert.NotEqual(first.KriReadingId, Events()[1].KriReadingId);

        var pending = Assert.Single(await svc.GetTriggersAsync(1, pendingOnly: true));
        Assert.Equal(Events()[1].Id, pending.EventId);
        Assert.True(RiskRow(1).ReviewRequested);
        await publisher.Received(2).KriToleranceBreachedAsync(Arg.Any<Kri>(), Arg.Any<decimal>(), Arg.Any<DateTime>(),
            Arg.Any<int>());
    }

    // --- E1–E5: declared events ---------------------------------------------------------------------

    /// <summary>
    /// E1 — a declared event raises its trigger on each open risk, flags it for review, skips (and names) the closed one,
    /// and keeps the reason of a risk already flagged.
    /// </summary>
    [Fact]
    public async Task TestE1_ADeclaredEventTriggersEachOpenRisk()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        AddRisk(3, UnitA, status: RiskWorkflowService.StatusClosed);
        await GetService<IRisksService>().RequestReviewAsync(2, "Auditor asked.");

        var created = await Svc.DeclareEventAsync(new ReassessmentEventRequest
        {
            Type = ReassessmentTriggerType.SupplierAcquisitionOrMigration, Title = "Payroll outsourced to ACME",
            Description = "Contract signed; data moves in November.", OccurredAt = DateTime.UtcNow.AddDays(-1),
            RiskIds = [1, 2, 3, 1]
        }, Author);

        Assert.Equal((ReassessmentEventOrigin.Declared, Author), (created.Origin, created.DeclaredById!.Value));
        Assert.Equal(new[] { 1, 2 }, created.Triggers.Select(t => t.RiskId).OrderBy(id => id));
        Assert.All(created.Triggers, t => Assert.Equal(ReassessmentTriggerState.Pending, t.State));
        Assert.Equal([3], created.SkippedClosedRiskIds);

        Assert.StartsWith("Mandatory reassessment (new supplier, acquisition or migration)", RiskRow(1).ReviewRequestedReason);
        Assert.Equal("Auditor asked.", RiskRow(2).ReviewRequestedReason);
        Assert.False(RiskRow(3).ReviewRequested);
        Assert.Contains(Audit(nameof(ReassessmentEvent)), a => a.UserId == Author);
    }

    /// <summary>E2 — applying an event to more risks is idempotent: a risk it already triggered is listed, not triggered again.</summary>
    [Fact]
    public async Task TestE2_ApplyingAnEventToMoreRisksIsIdempotent()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        var created = await Svc.DeclareEventAsync(new ReassessmentEventRequest
        {
            Type = ReassessmentTriggerType.NewRegulation, Title = "ANPD resolution", OccurredAt = DateTime.UtcNow,
            RiskIds = [1]
        }, Author);

        var added = await Svc.AddEventRisksAsync(created.Id, new ReassessmentRisksRequest { RiskIds = [1, 2] }, Author);
        Assert.Equal([1], added.AlreadyTriggeredRiskIds);
        Assert.Equal(2, added.Triggers.Count);

        var again = await Svc.AddEventRisksAsync(created.Id, new ReassessmentRisksRequest { RiskIds = [1, 2] }, Author);
        Assert.Equal([1, 2], again.AlreadyTriggeredRiskIds);
        Assert.Equal(2, Triggers().Count);
    }

    /// <summary>E3 — a KRI breach event takes its risks from the KRI's links, not from a request.</summary>
    [Fact]
    public async Task TestE3_AKriEventCannotBeAppliedByHand()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        await LinkedKri(12m, [1]);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Svc.AddEventRisksAsync(Events()[0].Id, new ReassessmentRisksRequest { RiskIds = [2] }, Author));

        Assert.Equal(MonitoringService.EventFromKriRule, ex.RuleName);
        Assert.Single(Triggers());
    }

    /// <summary>E4 — a trigger is answered by the first management review submitted after it, never by one before.</summary>
    [Fact]
    public async Task TestE4_ATriggerIsAnsweredByTheNextManagementReview()
    {
        AddRisk(1, UnitA);
        Review(1, DateTime.UtcNow.AddDays(-10), 501);

        await Svc.DeclareEventAsync(new ReassessmentEventRequest
        {
            Type = ReassessmentTriggerType.ArchitectureOrTechnologyChange, Title = "Identity provider replaced",
            OccurredAt = DateTime.UtcNow.AddDays(-12), RiskIds = [1]
        }, Author);

        var pending = Assert.Single(await Svc.GetTriggersAsync(1, pendingOnly: true));
        Assert.Null(pending.AnsweredByReviewId);

        Review(1, DateTime.UtcNow.AddMinutes(5), 502);
        Review(1, DateTime.UtcNow.AddDays(3), 503);

        var answered = Assert.Single(await Svc.GetTriggersAsync(1, pendingOnly: false));
        Assert.Equal((ReassessmentTriggerState.Answered, 502), (answered.State, answered.AnsweredByReviewId!.Value));
        Assert.Empty(await Svc.GetTriggersAsync(1, pendingOnly: true));

        SeedUnscoped(ctx => ctx.MgmtReviews.RemoveRange(ctx.MgmtReviews.Where(m => m.Id != 501)));
        SeedUnscoped(ctx => ctx.Risks.Single(r => r.Id == 1).Status = RiskWorkflowService.StatusClosed);
        Assert.Equal(ReassessmentTriggerState.RiskClosed, Assert.Single(await Svc.GetTriggersAsync(1, false)).State);
    }

    /// <summary>E5 — the lists follow the scope: an event is visible through a trigger the caller can see, and filters by type.</summary>
    [Fact]
    public async Task TestE5_TheListsFollowTheScope()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitB);
        await Svc.DeclareEventAsync(new ReassessmentEventRequest
        {
            Type = ReassessmentTriggerType.NewRegulation, Title = "Regulation for B", OccurredAt = DateTime.UtcNow,
            RiskIds = [2]
        }, Author);
        await Svc.DeclareEventAsync(new ReassessmentEventRequest
        {
            Type = ReassessmentTriggerType.NewAiModel, Title = "Model for A and B", OccurredAt = DateTime.UtcNow,
            RiskIds = [1, 2]
        }, Author);

        Assert.Equal(2, (await Svc.GetEventsAsync(null, null)).Count);
        Assert.Single(await Svc.GetEventsAsync(ReassessmentTriggerType.NewAiModel, 10));
        await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.GetEventsAsync(null, 0));
        await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.GetEventsAsync((ReassessmentTriggerType)9, null));

        ScopeTo(UnitA);
        var visible = Assert.Single(await Svc.GetEventsAsync(null, null));
        Assert.Equal("Model for A and B", visible.Title);
        Assert.Equal(1, Assert.Single(visible.Triggers).RiskId);
        Assert.Equal(1, Assert.Single(await Svc.GetTriggersAsync(null, pendingOnly: true)).RiskId);
    }

    // --- GB1–GB9: Gate B by indicator ---------------------------------------------------------------

    /// <summary>GB1 — a linked KRI beyond its tolerance exceeds Gate B, and the acceptance is refused with nothing written.</summary>
    [Fact]
    public async Task TestGB1_AnExceedingIndicatorRefusesTheAcceptance()
    {
        OwnedRisk(1);
        Appetite();
        var kri = await LinkedKri(12m, [1]);

        var appetite = await Workflow.EvaluateAppetiteAsync(1);
        Assert.Equal(IndicatorAppetiteState.ExceedsTolerance, appetite.Indicators.State);
        var gate = Assert.Single(appetite.Indicators.Kris);
        Assert.Equal((kri, KriState.Breached, true, 12m), (gate.KriId, gate.State, gate.Exceeds, gate.Value!.Value));

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.CreateAsync(1, Acceptance(), Cro));
        Assert.Equal(RiskAcceptancesService.IndicatorToleranceRule, ex.RuleName);
        Assert.Contains("ERP hours down", ex.Message);
        Assert.Equal(0, Read(ctx => ctx.RiskAcceptances.Count()));
    }

    /// <summary>GB2 — within tolerance, the acceptance goes through.</summary>
    [Fact]
    public async Task TestGB2_AnIndicatorWithinToleranceAccepts()
    {
        OwnedRisk(1);
        Appetite();
        await LinkedKri(7m, [1]);

        Assert.Equal(IndicatorAppetiteState.WithinTolerance, (await Workflow.EvaluateAppetiteAsync(1)).Indicators.State);
        Assert.Equal(RiskAcceptanceStatus.Active, (await Acceptances.CreateAsync(1, Acceptance(), Cro)).Status);
    }

    /// <summary>GB3 — stale is not assessable and does not refuse (S49 D4, as S48 D12); it is never "within".</summary>
    [Fact]
    public async Task TestGB3_AStaleIndicatorIsNotAssessableAndDoesNotRefuse()
    {
        OwnedRisk(1);
        Appetite();
        await LinkedKri(7m, [1], daysAgo: 40);

        var appetite = await Workflow.EvaluateAppetiteAsync(1);
        Assert.Equal(IndicatorAppetiteState.NotAssessable, appetite.Indicators.State);
        Assert.Equal(RiskAcceptanceStatus.Active, (await Acceptances.CreateAsync(1, Acceptance(), Cro)).Status);
    }

    /// <summary>GB4 — ceiling and indicator both exceeded: the ceiling answers first; the order of S49 §3.2 is unchanged.</summary>
    [Fact]
    public async Task TestGB4_TheOrdinalCeilingIsCheckedBeforeTheIndicator()
    {
        OwnedRisk(1, score: 9f);
        Appetite(ceiling: 4);
        await LinkedKri(12m, [1]);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.CreateAsync(1, Acceptance(), Cro));
        Assert.Equal("risk_appetite_ceiling", ex.RuleName);
    }

    /// <summary>GB5 — Gate A precedes Gate B by indicator.</summary>
    [Fact]
    public async Task TestGB5_GateAPrecedesTheIndicator()
    {
        OwnedRisk(1);
        Appetite();
        await LinkedKri(12m, [1]);
        await Flags.DeclareAsync(1, RiskFlagCode.HumanSafety,
            new RiskFlagDeclarationRequest { Reason = "The pump controller doses patients." }, Author);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.CreateAsync(1, Acceptance(), Cro));
        Assert.Equal(RiskFlagsService.GateARule, ex.RuleName);
    }

    /// <summary>GB6 — no KRI linked: not configured, and the acceptance behaves as before.</summary>
    [Fact]
    public async Task TestGB6_WithoutAnIndicatorNothingChanges()
    {
        OwnedRisk(1);
        Appetite();
        await NewKri(); // exists, governs nothing

        Assert.Equal(IndicatorAppetiteState.NotConfigured, (await Workflow.EvaluateAppetiteAsync(1)).Indicators.State);
        Assert.Equal(RiskAcceptanceStatus.Active, (await Acceptances.CreateAsync(1, Acceptance(), Cro)).Status);
    }

    /// <summary>GB7 — a renewal is refused by the indicator too.</summary>
    [Fact]
    public async Task TestGB7_ARenewalIsRefusedByTheIndicator()
    {
        OwnedRisk(1);
        Appetite();
        var kri = await LinkedKri(7m, [1]);
        var accepted = await Acceptances.CreateAsync(1, Acceptance(), Cro);

        await Read(kri, 11m, DateTime.UtcNow.AddHours(-1));

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.RenewAsync(accepted.Id, Acceptance(), Cro));
        Assert.Equal(RiskAcceptancesService.IndicatorToleranceRule, ex.RuleName);
    }

    /// <summary>
    /// GB8 — the gate does not depend on who asks (S49 D6): a KRI the caller cannot see (it belongs to another unit since
    /// the risk moved) still exceeds the gate of a risk the caller can see. Reading the KRIs through the caller's scope
    /// would make this risk acceptable to one approver and not to another.
    /// </summary>
    [Fact]
    public async Task TestGB8_TheGateDoesNotDependOnTheCallersScope()
    {
        OwnedRisk(1, unit: UnitB);
        Appetite();
        await LinkedKri(12m, [1], entityId: UnitB);
        SeedUnscoped(ctx => ctx.Risks.Single(r => r.Id == 1).EntityId = UnitA);

        ScopeTo(UnitA);
        Assert.Empty(await Svc.GetKrisAsync(true)); // the KRI itself is invisible to this caller

        var appetite = await Workflow.EvaluateAppetiteAsync(1);
        Assert.Equal(IndicatorAppetiteState.ExceedsTolerance, appetite.Indicators.State);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.CreateAsync(1, Acceptance(), Cro));
        Assert.Equal(RiskAcceptancesService.IndicatorToleranceRule, ex.RuleName);
    }

    /// <summary>GB9 — with no appetite configured at all, the indicator still governs: its tolerance is its own (S49 D5).</summary>
    [Fact]
    public async Task TestGB9_WithoutAnAppetiteTheIndicatorStillGoverns()
    {
        OwnedRisk(1);
        await LinkedKri(12m, [1]);

        var appetite = await Workflow.EvaluateAppetiteAsync(1);
        Assert.False(appetite.AppetiteConfigured);
        Assert.Equal(TailAppetiteState.NotConfigured, appetite.Tail.State);
        Assert.Equal(IndicatorAppetiteState.ExceedsTolerance, appetite.Indicators.State);

        var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Acceptances.CreateAsync(1, Acceptance(), Cro));
        Assert.Equal(RiskAcceptancesService.IndicatorToleranceRule, ex.RuleName);
    }

    // --- M1–M6: the panel ---------------------------------------------------------------------------

    /// <summary>M1 — the ten metrics in order, with the availability §3.3 declares and the stage of each missing one.</summary>
    [Fact]
    public async Task TestM1_ThePanelListsTheTenMetricsWithTheirAvailability()
    {
        var panel = await Metrics.GetAsync();

        Assert.Equal(Enumerable.Range(1, 10).Select(i => $"M{i}"), panel.Metrics.Select(m => m.Code));

        MetricAvailability Of(MethodologyMetric m) => panel.Metrics.Single(x => x.Metric == m).Availability;
        Assert.Equal(MetricAvailability.Partial, Of(MethodologyMetric.CriticalProcessCoverage));
        Assert.Equal(MetricAvailability.Available, Of(MethodologyMetric.OwnerAndEvidence));
        Assert.Equal(MetricAvailability.Available, Of(MethodologyMetric.DiscoveryToDecision));
        Assert.Equal(MetricAvailability.Partial, Of(MethodologyMetric.ControlEffectiveness));
        Assert.Equal(MetricAvailability.Available, Of(MethodologyMetric.KevRemediation));
        Assert.Equal(MetricAvailability.Available, Of(MethodologyMetric.RestorationVerification));

        // No risk has tail statistics in an empty register: the aggregate is honestly not available.
        Assert.Equal(MetricAvailability.NotAvailable, Of(MethodologyMetric.AggregateExposure));

        Assert.Equal(new[] { "9.10", "9.9", "9.12" }, panel.Metrics
            .Where(m => m.Metric is MethodologyMetric.ThirdPartyConcentration or MethodologyMetric.ReopenedAndUnforeseen
                or MethodologyMetric.ArtificialIntelligence)
            .Select(m => m.Stage));
        // Stage 9.9 (S50 §4.4) delivered M9 — the archive and the incident backtesting —, Stage 9.10 (S51 §4.8) M8 — the
        // third-party concentration — and Stage 9.12 (S53 §4.9) M10 — the AI model evaluations —, each amending S49 §3.3: all
        // three are computable, and read their value as not computable (null) in an empty register. No metric waits.
        Assert.Equal(MetricAvailability.Available, Of(MethodologyMetric.ReopenedAndUnforeseen));
        Assert.Equal(MetricAvailability.Available, Of(MethodologyMetric.ThirdPartyConcentration));
        Assert.Null(panel.Metrics.Single(m => m.Metric == MethodologyMetric.ThirdPartyConcentration).Value);
        Assert.Equal(MetricAvailability.Available, Of(MethodologyMetric.ArtificialIntelligence));
        Assert.Null(panel.Metrics.Single(m => m.Metric == MethodologyMetric.ArtificialIntelligence).Value);
    }

    /// <summary>M2 — owner and evidence over open risks; a hypothesis is not evidence; no open risk is null, never 100 %.</summary>
    [Fact]
    public async Task TestM2_OwnerAndEvidence()
    {
        Assert.Null((await Metrics.GetAsync()).Metrics.Single(m => m.Metric == MethodologyMetric.OwnerAndEvidence).Value);

        OwnedRisk(1);
        OwnedRisk(2);
        AddRisk(3, UnitA);
        OwnedRisk(4);
        SeedUnscoped(ctx =>
        {
            ctx.Risks.Single(r => r.Id == 1).EvidenceConfidence = EvidenceConfidence.Confirmed;
            ctx.Risks.Single(r => r.Id == 2).EvidenceConfidence = EvidenceConfidence.Hypothesis;
            ctx.Risks.Single(r => r.Id == 3).EvidenceConfidence = EvidenceConfidence.Confirmed;
            ctx.Risks.Single(r => r.Id == 4).EvidenceConfidence = EvidenceConfidence.Indicative;
            ctx.Risks.Single(r => r.Id == 4).Status = RiskWorkflowService.StatusClosed;
        });

        var metric = (await Metrics.GetAsync()).Metrics.Single(m => m.Metric == MethodologyMetric.OwnerAndEvidence);
        Assert.Equal((1d, 3d), (metric.Numerator!.Value, metric.Denominator!.Value));
        Assert.Equal(1d / 3, metric.Value!.Value, 6);
    }

    /// <summary>M3 — submission to the first Phase 4 decision, mean in days; undecided open risks are counted, not averaged.</summary>
    [Fact]
    public async Task TestM3_DiscoveryToDecision()
    {
        var now = DateTime.UtcNow;
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        AddRisk(3, UnitA);
        SeedUnscoped(ctx =>
        {
            ctx.Risks.Single(r => r.Id == 1).SubmissionDate = now.AddDays(-10);
            ctx.Risks.Single(r => r.Id == 2).SubmissionDate = now.AddDays(-20);
            ctx.Risks.Single(r => r.Id == 3).SubmissionDate = now.AddDays(-30);
            ctx.RiskDecisions.AddRange(
                Decision(1, now.AddDays(-4)), Decision(1, now.AddDays(-2)), Decision(2, now.AddDays(-10)));
        });

        var metric = (await Metrics.GetAsync()).Metrics.Single(m => m.Metric == MethodologyMetric.DiscoveryToDecision);

        Assert.Equal(8d, metric.Value!.Value, 1);
        Assert.Equal(2d, metric.Numerator!.Value);
        Assert.Contains("1 open risk(s) have none; the oldest was submitted 30 day(s) ago", metric.Detail);
    }

    private static RiskDecision Decision(int riskId, DateTime at) => new()
    {
        RiskId = riskId, Decision = RiskDecisionKind.TreatInCycle, Source = RiskDecisionSource.Declared,
        Reason = "Treat in this cycle.", DecidedAt = at, DecidedById = Author
    };

    /// <summary>
    /// M4 — a source that refuses (the portfolio's size limit) or fails makes its metric not available, logged; the panel
    /// itself never fails for one number (S49 D12).
    /// </summary>
    [Fact]
    public async Task TestM4_AFailingSourceDoesNotFailThePanel()
    {
        var tail = Substitute.For<ITailRiskService>();
        tail.AggregatePortfolioAsync(Arg.Any<PortfolioTailRequest>())
            .Returns<PortfolioTailDto>(_ => throw new InvalidParameterException("RiskIds", "600 open risks are in scope."));
        var chain = Substitute.For<IRiskChainService>();
        chain.GetCriticalProcessCoverageAsync().Returns<Model.Risks.Chain.CriticalProcessCoverageDto>(
            _ => throw new InvalidOperationException("boom"));

        var panel = await new MethodologyMetricsService(GetService<Serilog.ILogger>(), GetService<IDalService>(), chain,
            GetService<IExploitationSignalsService>(), GetService<IContinuityService>(), tail,
            GetService<IBacktestingService>(), GetService<IThirdPartiesService>(), GetService<IAiGovernanceService>()).GetAsync();

        var exposure = panel.Metrics.Single(m => m.Metric == MethodologyMetric.AggregateExposure);
        Assert.Equal((MetricAvailability.NotAvailable, "600 open risks are in scope."), (exposure.Availability, exposure.Detail));

        var coverage = panel.Metrics.Single(m => m.Metric == MethodologyMetric.CriticalProcessCoverage);
        Assert.Equal(MetricAvailability.NotAvailable, coverage.Availability);
        Assert.Contains("failed", coverage.Detail);
        Assert.Equal(10, panel.Metrics.Count);
    }

    /// <summary>M5 — the mechanism: KRIs by state, and triggers pending, answered and the mean days to the answer.</summary>
    [Fact]
    public async Task TestM5_TheMechanismHealth()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitA);
        await LinkedKri(12m, [1]); // breached
        await LinkedKri(5m, []); // within (7 would be past the warning of 6)
        await NewKri(); // no reading
        await Svc.RetireKriAsync(await NewKri(), Cro); // retired
        await Svc.DeclareEventAsync(new ReassessmentEventRequest
        {
            Type = ReassessmentTriggerType.NewRegulation, Title = "ANPD resolution", OccurredAt = DateTime.UtcNow,
            RiskIds = [2]
        }, Author);
        Review(2, DateTime.UtcNow.AddDays(2), 601);

        var panel = await Metrics.GetAsync();

        Assert.Equal((3, 1, 1, 1, 1), (panel.Kris.Active, panel.Kris.Breached, panel.Kris.WithinTolerance,
            panel.Kris.NoReading, panel.Kris.Retired));
        Assert.Equal((2, 1, 1), (panel.Reassessment.EventsLast90Days, panel.Reassessment.Pending, panel.Reassessment.Answered));
        Assert.Equal(2d, panel.Reassessment.MeanDaysToAnswer!.Value, 0);
    }

    /// <summary>M6 — the panel is the caller's: a unit sees its own risks in the ratios.</summary>
    [Fact]
    public async Task TestM6_ThePanelFollowsTheScope()
    {
        OwnedRisk(1);
        OwnedRisk(2, unit: UnitB);

        ScopeTo(UnitA);
        var metric = (await Metrics.GetAsync()).Metrics.Single(m => m.Metric == MethodologyMetric.OwnerAndEvidence);

        Assert.Equal(1d, metric.Denominator!.Value);
    }
}

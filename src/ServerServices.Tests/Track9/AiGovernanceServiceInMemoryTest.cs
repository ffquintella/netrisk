using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using DAL.Exceptions;
using JetBrains.Annotations;
using Model.AiGovernance;
using Model.DecisionCycle;
using Model.Exceptions;
using Model.Monitoring;
using Model.ThirdParties;
using ServerServices.Governance;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.12 (S53 §8) — AI governance end to end on the in-memory provider: the inventory and its guards (I1–I6), the data
/// a model uses read against the Stage 9.11 catalogue (DA1–DA2), the readings (M1–M3), the evaluation proportional to the
/// risk tier (E1), the edge case T215 names — <b>a model with no recorded evaluation is not treated as evaluated</b>
/// (T215b, T215c) —, the overrides the S27 edge case asks to be recordable with author and reason, and the override rate
/// computed from them (O1–O3), the register's risks (R1–R3), the findings (F1–F2), the methodology panel's M10 (P1), the
/// histories (HI1) and the structural rule that the service decides nothing (Y0).
///
/// The organization is <see cref="DecisionCycleTestBase"/>'s — data record 50 "Student records", process 10, service 20,
/// units 100 and 200, the administrator 1, the owner 2, members 3–5 and the third-line auditor 9.
/// </summary>
[TestSubject(typeof(AiGovernanceService))]
public class AiGovernanceServiceInMemoryTest : DecisionCycleTestBase
{
    private IAiGovernanceService Svc => GetService<IAiGovernanceService>();

    private IThirdPartiesService ThirdParties => GetService<IThirdPartiesService>();

    // --- builders ------------------------------------------------------------------------------------------------------

    private static AiModelRequest Model(string name = "Admissions triage", AiModelStatus? status = AiModelStatus.Production,
        AiModelRiskTier? tier = AiModelRiskTier.Minimal, AiHumanOversight? oversight = AiHumanOversight.NoReview,
        int? entityId = null, string version = "1.0") => new()
    {
        Name = name, Purpose = "Ranks applications for an admissions officer.", Kind = AiModelKind.Classification,
        Source = AiModelSource.InHouse, Version = version, Status = status, RiskTier = tier, HumanOversight = oversight,
        OwnerId = Owner, EntityId = entityId, MaxEvaluationAgeDays = 90
    };

    private async Task<int> NewModel(AiModelRequest? request = null) => (await Svc.CreateAsync(request ?? Model(), Cro)).Id;

    private static AiModelReadingRequest Reading(AiModelMetric metric, decimal value = 0.9m, DateTime? measuredAt = null) =>
        new() { Metric = metric, Value = value, MeasuredAt = measuredAt ?? DateTime.UtcNow.AddDays(-1), Method = "Holdout set" };

    private static AiModelOverrideRequest Override(DateTime? at = null) => new()
    {
        OccurredAt = at ?? DateTime.UtcNow.AddDays(-2), ModelOutput = "Reject the application",
        HumanDecision = "Admit the application", Reason = "The transcript was misread by the model."
    };

    private static AiGovernanceReasonRequest Because(string reason = "Replaced by the vendor's new model.") => new() { Reason = reason };

    private static List<AiModelFindingCode> Codes(AiModelDto model) => model.Findings.Select(f => f.Code).ToList();

    private static AiModelMetricStateDto MetricOf(AiModelDto model, AiModelMetric metric) =>
        model.Evaluation.Metrics.Single(m => m.Metric == metric);

    private int ModelCount() => Read(ctx => ctx.AiModels.Count());

    // --- I1–I6: the inventory (T212) -----------------------------------------------------------------------------------

    /// <summary>I1 — a model is registered with purpose, kind, source, version, tier and oversight; the trail has its author.</summary>
    [Fact]
    public async Task TestI1_AModelIsRegisteredAndReadBack()
    {
        var created = await Svc.CreateAsync(Model(status: null), Cro);

        Assert.Equal(("Admissions triage", AiModelStatus.Proposed, "1.0"), (created.Name, created.Status, created.Version));
        Assert.Equal((AiModelKind.Classification, AiModelSource.InHouse, Owner), (created.Kind, created.Source, created.OwnerId));
        Assert.Equal((Cro, 90), (created.CreatedById, created.MaxEvaluationAgeDays));
        Assert.Contains(Audit(nameof(AiModel)), a => a.Action == AuditLogAction.Create && a.UserId == Cro && a.EntityId == created.Id);

        var updated = await Svc.UpdateAsync(created.Id, Model(status: AiModelStatus.Pilot, version: "1.1"), MemberA);
        Assert.Equal((AiModelStatus.Pilot, "1.1", MemberA), (updated.Status, updated.Version, updated.UpdatedById));
        Assert.Contains(Audit(nameof(AiModel)), a => a.Action == AuditLogAction.Update && a.Field == nameof(AiModel.Version) &&
                                                     a.OldValue == "1.0" && a.NewValue == "1.1" && a.UserId == MemberA);

        // One row per model, case-insensitively — a second "admissions TRIAGE" is a conflict.
        await Assert.ThrowsAsync<DataAlreadyExistsException>(() => Svc.CreateAsync(Model("admissions TRIAGE"), Cro));
        Assert.Equal(1, ModelCount());
    }

    /// <summary>I2 — every invalid field is refused naming it, and nothing is written.</summary>
    [Fact]
    public async Task TestI2_AnInvalidModelIsRefusedNamingTheField()
    {
        var cases = new (Action<AiModelRequest> Break, string Field)[]
        {
            (r => r.Name = " ", "Name"),
            (r => r.Name = new string('n', AiGovernanceLimits.MaxNameLength + 1), "Name"),
            (r => r.Purpose = null, "Purpose"),
            (r => r.Purpose = new string('p', AiGovernanceLimits.MaxPurposeLength + 1), "Purpose"),
            (r => r.Purpose = "Answers ana.silva@fgv.br about her grades", "Purpose"),
            (r => r.Kind = null, "Kind"),
            (r => r.Kind = (AiModelKind)8, "Kind"),
            (r => r.Source = null, "Source"),
            (r => r.Source = (AiModelSource)4, "Source"),
            (r => r.ThirdPartyId = 0, "ThirdPartyId"),
            (r => r.ThirdPartyId = 5, "ThirdPartyId"),
            (r => r.Version = "", "Version"),
            (r => r.Version = new string('v', AiGovernanceLimits.MaxVersionLength + 1), "Version"),
            (r => r.VersionSince = DateTime.UtcNow.AddDays(1), "VersionSince"),
            (r => r.Status = AiModelStatus.Retired, "Status"),
            (r => r.Status = (AiModelStatus)9, "Status"),
            (r => r.RiskTier = (AiModelRiskTier)4, "RiskTier"),
            (r => r.HumanOversight = (AiHumanOversight)4, "HumanOversight"),
            (r => r.OwnerId = 0, "OwnerId"),
            (r => r.EntityId = -1, "EntityId"),
            (r => r.MaxEvaluationAgeDays = null, "MaxEvaluationAgeDays"),
            (r => r.MaxEvaluationAgeDays = 0, "MaxEvaluationAgeDays"),
            (r => r.MaxEvaluationAgeDays = AiGovernanceLimits.MaxEvaluationAgeDays + 1, "MaxEvaluationAgeDays"),
            (r => r.Notes = "Contact 123.456.789-09 for access", "Notes")
        };

        foreach (var (breakIt, field) in cases)
        {
            var request = Model();
            breakIt(request);

            var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.CreateAsync(request, Cro));
            Assert.Equal(field, ex.ParameterName);
            // A text carrying a person's data is refused without echoing it.
            Assert.DoesNotContain("ana.silva", ex.Message);
            Assert.DoesNotContain("123.456.789-09", ex.Message);
        }

        Assert.Equal(0, ModelCount());
    }

    /// <summary>
    /// I3 — the owner is a person (MIGR-TI/IA: "human roles, never AI"): an existing, enabled user, never the third line; the
    /// unit must exist.
    /// </summary>
    [Fact]
    public async Task TestI3_TheOwnerIsAnEnabledUserWhoIsNotTheThirdLine()
    {
        var missing = Model();
        missing.OwnerId = 999;
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.CreateAsync(missing, Cro));

        SeedUnscoped(ctx => ctx.Users.Single(u => u.Value == MemberC).Enabled = false);
        var disabled = Model();
        disabled.OwnerId = MemberC;
        Assert.Equal("OwnerId", (await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.CreateAsync(disabled, Cro))).ParameterName);

        var auditor = Model();
        auditor.OwnerId = Auditor;
        Assert.Equal(ThirdLineAssurance.CannotApproveRule,
            (await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.CreateAsync(auditor, Cro))).RuleName);

        var nowhere = Model();
        nowhere.EntityId = 9999;
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.CreateAsync(nowhere, Cro));

        Assert.Equal(0, ModelCount());
    }

    /// <summary>
    /// I4 — the vendor is a registered third party the caller can see, only on a vendor model; naming it makes the third party
    /// in use, so it is not deleted (ThirdPartyReferences, amending S51 §4.9).
    /// </summary>
    [Fact]
    public async Task TestI4_TheVendorIsAThirdPartyInUse()
    {
        var vendor = (await ThirdParties.CreateAsync(new ThirdPartyRequest { Name = "OpenModels Inc", Status = ThirdPartyStatus.Active }, Cro)).Id;
        var hidden = (await ThirdParties.CreateAsync(new ThirdPartyRequest { Name = "Unit B vendor", EntityId = UnitB }, Cro)).Id;

        var request = Model();
        request.Source = AiModelSource.Vendor;
        request.ThirdPartyId = 4242;
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.CreateAsync(request, Cro));

        request.ThirdPartyId = vendor;
        var model = await Svc.CreateAsync(request, Cro);
        Assert.Equal((vendor, "OpenModels Inc", false), (model.ThirdPartyId, model.ThirdPartyName, model.ThirdPartyHidden));
        Assert.DoesNotContain(AiModelFindingCode.VendorUnregistered, Codes(model));

        var inUse = await Assert.ThrowsAsync<RuleBrokenException>(() => ThirdParties.DeleteAsync(vendor, Cro));
        Assert.Equal(ThirdPartiesService.InUseRule, inUse.RuleName);
        Assert.Contains("vendor of 1 inventoried AI model", inUse.Message);

        // A scoped caller cannot name a vendor outside their scope: it is not found.
        ScopeTo(UnitA);
        var scoped = Model("Unit A chatbot", entityId: UnitA);
        scoped.Source = AiModelSource.Vendor;
        scoped.ThirdPartyId = hidden;
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.CreateAsync(scoped, Cro));

        // ...and a vendor somebody broader named reads hidden — by id, never by name.
        ScopeToEverything();
        var broad = Model("Unit A assistant", entityId: UnitA);
        broad.Source = AiModelSource.Vendor;
        broad.ThirdPartyId = hidden;
        var named = await Svc.CreateAsync(broad, Cro);
        ScopeTo(UnitA);
        var seen = await Svc.GetModelAsync(named.Id);
        Assert.Equal((hidden, (string?)null, true), (seen.ThirdPartyId, seen.ThirdPartyName, seen.ThirdPartyHidden));

        // A vendor model with no registered vendor is a finding, not a refusal.
        ScopeToEverything();
        var unregistered = Model("Vendor bot");
        unregistered.Source = AiModelSource.Vendor;
        Assert.Contains(AiModelFindingCode.VendorUnregistered, Codes(await Svc.CreateAsync(unregistered, Cro)));
    }

    /// <summary>
    /// I5 — scope: the organization's models and the caller's unit's are read; another unit's is not found; a scoped caller
    /// writes neither the organization's model nor another unit's — not its record, data, readings, overrides or retirement.
    /// </summary>
    [Fact]
    public async Task TestI5_ScopeGovernsReadsAndEveryWrite()
    {
        var org = await NewModel(Model("Organization chatbot"));
        var unitA = await NewModel(Model("Unit A model", entityId: UnitA));
        var unitB = await NewModel(Model("Unit B model", entityId: UnitB));
        var orgReading = (await Svc.RecordReadingAsync(org, Reading(AiModelMetric.Drift), Cro)).Id;
        var orgOverride = (await Svc.RecordOverrideAsync(org, Override(), Cro)).Id;

        ScopeTo(UnitA);
        Assert.Equal(["Organization chatbot", "Unit A model"],
            (await Svc.GetModelsAsync(null, false, false)).Select(m => m.Name).ToArray());
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetModelAsync(unitB));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetHistoryAsync(unitB, 10));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.RecordReadingAsync(unitB, Reading(AiModelMetric.Drift), Owner));

        foreach (var write in new Func<Task>[]
                 {
                     () => Svc.CreateAsync(Model("New unit B model", entityId: UnitB), Owner),
                     () => Svc.UpdateAsync(org, Model("Organization chatbot", entityId: UnitA), Owner),
                     () => Svc.RetireAsync(org, Because(), Owner),
                     () => Svc.SetDataAsync(org, new AiModelDataRequest { Data = [] }, Owner),
                     () => Svc.RecordReadingAsync(org, Reading(AiModelMetric.Drift), Owner),
                     () => Svc.VoidReadingAsync(org, orgReading, Because(), Owner),
                     () => Svc.RecordOverrideAsync(org, Override(), Owner),
                     () => Svc.VoidOverrideAsync(org, orgOverride, Because(), Owner)
                 })
            await Assert.ThrowsAsync<EntityScopeViolationException>(write);

        // Within scope, the unit's own model is written; a caller holding one unit files a model with no unit there (the
        // write guard's rule for every IEntityScoped record), so it never creates the organization's model.
        Assert.Equal(AiModelStatus.Pilot,
            (await Svc.UpdateAsync(unitA, Model("Unit A model", AiModelStatus.Pilot, entityId: UnitA), Owner)).Status);
        Assert.Equal(UnitA, (await Svc.CreateAsync(Model("New unit model"), Owner)).EntityId);

        ScopeToEverything();
        Assert.Equal(4, ModelCount());
        Assert.Null(Assert.Single(Read(ctx => ctx.AiModelMetricReadings.ToList())).VoidedAt);
        Assert.Null(Assert.Single(Read(ctx => ctx.AiModelOverrides.ToList())).VoidedAt);
    }

    /// <summary>
    /// I6 — retired with a reason, never deleted; frozen: no edit, data, reading, override or new risk link; listed only when
    /// asked; and the service has no delete.
    /// </summary>
    [Fact]
    public async Task TestI6_ARetiredModelIsFrozenAndKept()
    {
        var id = await NewModel();

        Assert.Equal("Reason", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.RetireAsync(id, Because("short"), Cro))).ParameterName);

        var retired = await Svc.RetireAsync(id, Because(), MemberA);
        Assert.Equal((AiModelStatus.Retired, MemberA), (retired.Status, retired.RetiredById));
        Assert.NotNull(retired.RetiredAt);
        Assert.Empty(retired.Findings);

        AddRisk(1, UnitA);
        foreach (var write in new Func<Task>[]
                 {
                     () => Svc.RetireAsync(id, Because(), Cro),
                     () => Svc.UpdateAsync(id, Model(), Cro),
                     () => Svc.SetDataAsync(id, new AiModelDataRequest { Data = [] }, Cro),
                     () => Svc.RecordReadingAsync(id, Reading(AiModelMetric.Drift), Cro),
                     () => Svc.RecordOverrideAsync(id, Override(), Cro),
                     () => Svc.LinkRiskAsync(id, 1, new AiModelRiskLinkRequest(), Cro)
                 })
            Assert.Equal(AiGovernanceService.RetiredRule, (await Assert.ThrowsAsync<RuleBrokenException>(write)).RuleName);

        Assert.Empty(await Svc.GetModelsAsync(null, false, false));
        Assert.Single(await Svc.GetModelsAsync(null, true, false));
        Assert.Single(await Svc.GetModelsAsync(AiModelStatus.Retired, false, false));
        Assert.Equal(1, ModelCount());

        Assert.DoesNotContain(typeof(IAiGovernanceService).GetMethods(), m => m.Name.StartsWith("Delete", StringComparison.Ordinal));
    }

    // --- DA1–DA2: the data a model uses (T212) -------------------------------------------------------------------------

    /// <summary>
    /// DA1 — the data is declared whole: an empty list is a declaration; a data record only (404 missing, 422 not data), no
    /// duplicate; a use that stays keeps its id; the declaration is in the trail.
    /// </summary>
    [Fact]
    public async Task TestDA1_TheDataIsDeclaredWhole()
    {
        var id = await NewModel();
        Assert.Contains(AiModelFindingCode.DataUndeclared, Codes(await Svc.GetModelAsync(id)));

        var none = await Svc.SetDataAsync(id, new AiModelDataRequest { Data = [] }, Cro);
        Assert.Empty(none.Data);
        Assert.NotNull(none.DataDeclaredAt);
        Assert.DoesNotContain(AiModelFindingCode.DataUndeclared, Codes(none));

        await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.SetDataAsync(id, new AiModelDataRequest(), Cro));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.SetDataAsync(id, new AiModelDataRequest
            { Data = [new AiModelDataLinkRequest { EntityId = 9999, Usage = AiModelDataUsage.Input }] }, Cro));
        foreach (var notData in new[] { Process, Service, UnitA })
            Assert.Equal(AiGovernanceService.DataTargetRule, (await Assert.ThrowsAsync<RuleBrokenException>(() =>
                Svc.SetDataAsync(id, new AiModelDataRequest
                    { Data = [new AiModelDataLinkRequest { EntityId = notData, Usage = AiModelDataUsage.Input }] }, Cro))).RuleName);
        Assert.Equal("Data", (await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.SetDataAsync(id,
            new AiModelDataRequest
            {
                Data =
                [
                    new AiModelDataLinkRequest { EntityId = Data, Usage = AiModelDataUsage.Input },
                    new AiModelDataLinkRequest { EntityId = Data, Usage = AiModelDataUsage.Input }
                ]
            }, Cro))).ParameterName);
        Assert.Equal("Usage", (await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.SetDataAsync(id,
            new AiModelDataRequest { Data = [new AiModelDataLinkRequest { EntityId = Data }] }, Cro))).ParameterName);

        var first = await Svc.SetDataAsync(id, new AiModelDataRequest
        {
            Data =
            [
                new AiModelDataLinkRequest { EntityId = Data, Usage = AiModelDataUsage.Training },
                new AiModelDataLinkRequest { EntityId = Data, Usage = AiModelDataUsage.Input }
            ]
        }, Cro);
        Assert.Equal(2, first.Data.Count);
        var training = first.Data.Single(d => d.Usage == AiModelDataUsage.Training);
        Assert.Equal("Student records", training.Name);

        var second = await Svc.SetDataAsync(id, new AiModelDataRequest
            { Data = [new AiModelDataLinkRequest { EntityId = Data, Usage = AiModelDataUsage.Training }] }, MemberA);
        Assert.Equal(training.Id, Assert.Single(second.Data).Id);
        Assert.Contains(Audit(nameof(AiModelDataLink)), a => a.Action == AuditLogAction.Delete && a.UserId == MemberA);
    }

    /// <summary>
    /// DA2 — the data's LGPD catalogue is read through the node it is keyed by, with no column on any link (S53 D4): an
    /// uncatalogued record is a finding, and cataloguing it clears the finding and shows its personal-data category.
    /// </summary>
    [Fact]
    public async Task TestDA2_TheCatalogueIsReadThroughTheDataNode()
    {
        var id = await NewModel();
        var model = await Svc.SetDataAsync(id, new AiModelDataRequest
            { Data = [new AiModelDataLinkRequest { EntityId = Data, Usage = AiModelDataUsage.Training }] }, Cro);

        var link = Assert.Single(model.Data);
        Assert.Equal((false, (PersonalDataCategory?)null), (link.Catalogued, link.PersonalData));
        Assert.Contains(AiModelFindingCode.DataNotCatalogued, Codes(model));

        await GetService<IDataCatalogueService>().SaveRecordAsync(Data, new Model.DataCatalogue.DataCatalogueEntryRequest
        {
            PersonalData = PersonalDataCategory.SensitivePersonal, Purposes = [], Locations = []
        }, Cro);

        var catalogued = await Svc.GetModelAsync(id);
        Assert.Equal((true, PersonalDataCategory.SensitivePersonal),
            (catalogued.Data.Single().Catalogued, catalogued.Data.Single().PersonalData));
        Assert.DoesNotContain(AiModelFindingCode.DataNotCatalogued, Codes(catalogued));
    }

    // --- T215b, T215c: a model with no recorded evaluation is not evaluated --------------------------------------------

    /// <summary>
    /// T215b — a model in use with no recorded evaluation is <b>not evaluated</b>: the whole and every metric read not
    /// evaluated, with no value — never zero, never a pass —; the finding says so; the list and the risk view carry the
    /// state; and the panel's M10 counts it as not evaluated, never as evaluated.
    /// </summary>
    [Fact]
    public async Task TestT215b_AModelWithNoRecordedEvaluationIsNotEvaluated()
    {
        var id = await NewModel(Model(tier: AiModelRiskTier.High, oversight: AiHumanOversight.EveryOutput));
        AddRisk(1, UnitA);
        await Svc.LinkRiskAsync(id, 1, new AiModelRiskLinkRequest(), Cro);

        var model = await Svc.GetModelAsync(id);

        Assert.Equal(AiModelEvaluationState.NotEvaluated, model.Evaluation.State);
        Assert.Equal(6, model.Evaluation.Metrics.Count);
        Assert.All(model.Evaluation.Metrics, m =>
        {
            Assert.Equal(AiMetricState.NotEvaluated, m.State);
            Assert.Null(m.Value);
            Assert.Null(m.MeasuredAt);
            Assert.Null(m.ReadingId);
        });
        Assert.Equal(6, model.Evaluation.RequiredMetrics.Count);
        Assert.Contains(AiModelFindingCode.NotEvaluated, Codes(model));

        Assert.Equal(AiModelEvaluationState.NotEvaluated, Assert.Single(await Svc.GetModelsAsync(null, false, true)).EvaluationState);
        Assert.Equal(AiModelEvaluationState.NotEvaluated, Assert.Single((await Svc.GetRiskModelsAsync(1)).Models).EvaluationState);

        var summary = await Svc.GetMetricsSummaryAsync();
        Assert.Equal((1, 0, 1), (summary.InUse, summary.Evaluated, summary.NotEvaluated));
        Assert.All(summary.EvaluatedByMetric, c => Assert.Equal((1, 0), (c.Required, c.Evaluated)));

        var m10 = (await GetService<IMethodologyMetricsService>().GetAsync()).Metrics
            .Single(m => m.Metric == MethodologyMetric.ArtificialIntelligence);
        Assert.Equal((MetricAvailability.Available, (double?)0, (double?)1), (m10.Availability, m10.Numerator, m10.Denominator));
        Assert.Equal(0d, m10.Value);
        Assert.Contains("1 not evaluated", m10.Detail);
    }

    /// <summary>
    /// T215c — what does not count as an evaluation of the current version: a voided reading, a reading of an earlier
    /// version (shown as the last evaluated version), and a reading of a metric the tier does not require. An old reading is
    /// stale, never evaluated. Only a current reading of every required metric evaluates the model.
    /// </summary>
    [Fact]
    public async Task TestT215c_OnlyCurrentReadingsOfTheCurrentVersionEvaluate()
    {
        // Minimal tier, no human review: drift alone is required.
        var id = await NewModel();

        var voided = await Svc.RecordReadingAsync(id, Reading(AiModelMetric.Drift, 0.05m), Cro);
        await Svc.VoidReadingAsync(id, voided.Id, Because("Measured on the wrong window."), Cro);
        await Svc.RecordReadingAsync(id, Reading(AiModelMetric.Accuracy, 0.95m), Cro);

        var model = await Svc.GetModelAsync(id);
        Assert.Equal(AiModelEvaluationState.NotEvaluated, model.Evaluation.State);
        Assert.Equal(AiMetricState.NotEvaluated, MetricOf(model, AiModelMetric.Drift).State);
        Assert.Null(MetricOf(model, AiModelMetric.Drift).Value);
        Assert.False(MetricOf(model, AiModelMetric.Accuracy).Required);
        Assert.Equal(AiMetricState.Evaluated, MetricOf(model, AiModelMetric.Accuracy).State);

        await Svc.RecordReadingAsync(id, Reading(AiModelMetric.Drift, 0.07m), Cro);
        model = await Svc.GetModelAsync(id);
        Assert.Equal(AiModelEvaluationState.Evaluated, model.Evaluation.State);
        Assert.Equal(0.07m, MetricOf(model, AiModelMetric.Drift).Value);
        Assert.DoesNotContain(model.Findings, f => f.Code is AiModelFindingCode.NotEvaluated
            or AiModelFindingCode.EvaluationIncomplete or AiModelFindingCode.EvaluationStale);

        // A new version starts not evaluated, and says which version last was.
        model = await Svc.UpdateAsync(id, Model(version: "2.0"), Cro);
        Assert.Equal(AiModelEvaluationState.NotEvaluated, model.Evaluation.State);
        Assert.Equal(("1.0", (decimal?)null), (MetricOf(model, AiModelMetric.Drift).LastEvaluatedVersion,
            MetricOf(model, AiModelMetric.Drift).Value));
        Assert.Contains(AiModelFindingCode.NotEvaluated, Codes(model));

        // An old reading is stale — never evaluated.
        var old = await NewModel(Model("Old evaluation"));
        await Svc.RecordReadingAsync(old, Reading(AiModelMetric.Drift, 0.07m, DateTime.UtcNow.AddDays(-91)), Cro);
        model = await Svc.GetModelAsync(old);
        Assert.Equal((AiModelEvaluationState.Stale, AiMetricState.Stale),
            (model.Evaluation.State, MetricOf(model, AiModelMetric.Drift).State));
        Assert.Contains(AiModelFindingCode.EvaluationStale, Codes(model));
        Assert.Equal(1, (await Svc.GetMetricsSummaryAsync()).Stale);
    }

    /// <summary>
    /// T215d (review of M50) — a new version is not evaluated on data from before it existed: once the version changes, a
    /// reading measured, an override-rate period started or an override that occurred before the switch is refused, not
    /// attributed to it; a version that comes back (1.0 → 2.0 → 1.0) does not count its first deployment's readings; a
    /// declared start is honoured, and a new version cannot start before the one it replaces.
    /// </summary>
    [Fact]
    public async Task TestT215d_ANewVersionIsNotEvaluatedOnDataFromBeforeIt()
    {
        var id = await NewModel(Model(oversight: AiHumanOversight.EveryOutput));
        var lastMonth = DateTime.UtcNow.AddDays(-30);
        await Svc.RecordReadingAsync(id, Reading(AiModelMetric.Drift, 0.05m, lastMonth), Cro);

        var switched = await Svc.UpdateAsync(id, Model(oversight: AiHumanOversight.EveryOutput, version: "2.0"), Cro);
        Assert.NotNull(switched.VersionSince);
        Assert.Equal(switched.VersionSince, switched.Evaluation.VersionSince);

        Assert.Equal("MeasuredAt", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.RecordReadingAsync(id, Reading(AiModelMetric.Drift, 0.05m, lastMonth), Cro))).ParameterName);
        Assert.Equal("PeriodStart", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.RecordReadingAsync(id, new AiModelReadingRequest
            {
                Metric = AiModelMetric.HumanOverrideRate, PeriodStart = lastMonth, PeriodEnd = DateTime.UtcNow.AddMinutes(-1),
                SampleSize = 12
            }, Cro))).ParameterName);
        Assert.Equal("OccurredAt", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.RecordOverrideAsync(id, Override(lastMonth), Cro))).ParameterName);
        Assert.Equal(AiModelEvaluationState.NotEvaluated, (await Svc.GetModelAsync(id)).Evaluation.State);

        // Back to 1.0: its first deployment's reading does not evaluate it.
        var back = await Svc.UpdateAsync(id, Model(oversight: AiHumanOversight.EveryOutput, version: "1.0"), Cro);
        Assert.Equal(AiMetricState.NotEvaluated, MetricOf(back, AiModelMetric.Drift).State);
        Assert.Equal("1.0", MetricOf(back, AiModelMetric.Drift).LastEvaluatedVersion);

        // A new version cannot start before the one it replaces — a client echoing the old start back is refused.
        var echo = Model(oversight: AiHumanOversight.EveryOutput, version: "3.0");
        echo.VersionSince = back.VersionSince;
        Assert.Equal("VersionSince", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.UpdateAsync(id, echo, Cro))).ParameterName);

        // A declared start is honoured: a model registered as in use since last quarter accepts readings since then only.
        var declared = Model("Declared start");
        declared.VersionSince = DateTime.UtcNow.AddDays(-90);
        var other = await NewModel(declared);
        await Svc.RecordReadingAsync(other, Reading(AiModelMetric.Drift, 0.05m, lastMonth), Cro);
        Assert.Equal("MeasuredAt", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.RecordReadingAsync(other, Reading(AiModelMetric.Drift, 0.05m, DateTime.UtcNow.AddDays(-91)), Cro))).ParameterName);
        Assert.Single(Read(ctx => ctx.AiModelMetricReadings.Where(r => r.ModelId == other).ToList()));
    }

    /// <summary>
    /// E1 — what a model must report is proportional to its declared tier, and the override rate follows the oversight; an
    /// undeclared tier is held to the high tier's metrics and undeclared oversight to the override rate.
    /// </summary>
    [Theory]
    [InlineData(AiModelRiskTier.Minimal, AiHumanOversight.NoReview, new[] { AiModelMetric.Drift })]
    [InlineData(AiModelRiskTier.Minimal, AiHumanOversight.Sampled, new[] { AiModelMetric.Drift, AiModelMetric.HumanOverrideRate })]
    [InlineData(AiModelRiskTier.Limited, AiHumanOversight.NoReview, new[] { AiModelMetric.Accuracy, AiModelMetric.Drift })]
    [InlineData(AiModelRiskTier.High, AiHumanOversight.NoReview,
        new[] { AiModelMetric.Accuracy, AiModelMetric.Precision, AiModelMetric.Recall, AiModelMetric.Calibration, AiModelMetric.Drift })]
    [InlineData(null, AiHumanOversight.NoReview,
        new[] { AiModelMetric.Accuracy, AiModelMetric.Precision, AiModelMetric.Recall, AiModelMetric.Calibration, AiModelMetric.Drift })]
    [InlineData(AiModelRiskTier.Minimal, null, new[] { AiModelMetric.Drift, AiModelMetric.HumanOverrideRate })]
    public async Task TestE1_TheRequiredMetricsFollowTheTierAndTheOversight(AiModelRiskTier? tier, AiHumanOversight? oversight,
        AiModelMetric[] required)
    {
        var id = await NewModel(Model(tier: tier, oversight: oversight));

        var model = await Svc.GetModelAsync(id);
        Assert.Equal(required, model.Evaluation.RequiredMetrics);

        // Reading every required metric but one leaves the model incomplete; the last one evaluates it.
        var start = DateTime.UtcNow.AddDays(-10);
        foreach (var metric in required)
        {
            Assert.NotEqual(AiModelEvaluationState.Evaluated, (await Svc.GetModelAsync(id)).Evaluation.State);
            if (metric == AiModelMetric.HumanOverrideRate)
                await Svc.RecordReadingAsync(id, new AiModelReadingRequest
                    { Metric = metric, PeriodStart = start, PeriodEnd = DateTime.UtcNow.AddMinutes(-1), SampleSize = 50 }, Cro);
            else
                await Svc.RecordReadingAsync(id, Reading(metric, 0.1m), Cro);

            if (metric != required[^1])
                Assert.Equal(AiModelEvaluationState.Incomplete, (await Svc.GetModelAsync(id)).Evaluation.State);
        }

        Assert.Equal(AiModelEvaluationState.Evaluated, (await Svc.GetModelAsync(id)).Evaluation.State);
    }

    // --- M1–M3: readings (T214) ----------------------------------------------------------------------------------------

    /// <summary>
    /// M1 — a reading records the model's version (never the payload's), its author and its method; it is listed newest first,
    /// voided with a reason and kept, and voided twice is refused.
    /// </summary>
    [Fact]
    public async Task TestM1_AReadingIsOfTheCurrentVersionAndVoidedNotDeleted()
    {
        var id = await NewModel(Model(version: "3.2"));

        var older = await Svc.RecordReadingAsync(id, Reading(AiModelMetric.Recall, 0.81m, DateTime.UtcNow.AddDays(-5)), MemberA);
        var newer = await Svc.RecordReadingAsync(id, Reading(AiModelMetric.Recall, 0.83m), MemberB);

        Assert.Equal(("3.2", MemberA, "Holdout set"), (older.ModelVersion, older.RecordedById, older.Method));
        Assert.Equal([newer.Id, older.Id], (await Svc.GetReadingsAsync(id, AiModelMetric.Recall, false)).Select(r => r.Id));
        Assert.Empty(await Svc.GetReadingsAsync(id, AiModelMetric.Drift, false));

        await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.VoidReadingAsync(id, newer.Id, Because("no"), Cro));
        var voided = await Svc.VoidReadingAsync(id, newer.Id, Because("Measured on the training set."), Cro);
        Assert.Equal(Cro, voided.VoidedById);
        Assert.Equal(AiGovernanceService.ReadingVoidedRule, (await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Svc.VoidReadingAsync(id, newer.Id, Because("Measured on the training set."), Cro))).RuleName);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.VoidReadingAsync(id, 9999, Because(), Cro));

        Assert.Equal([older.Id], (await Svc.GetReadingsAsync(id, null, false)).Select(r => r.Id));
        Assert.Equal(2, (await Svc.GetReadingsAsync(id, null, true)).Count);
        Assert.Contains(Audit(nameof(AiModelMetricReading)), a => a.Action == AuditLogAction.Update &&
                                                                 a.Field == nameof(AiModelMetricReading.VoidedAt) && a.UserId == Cro);
    }

    /// <summary>M2 — every invalid reading is refused naming the field, and nothing is written.</summary>
    [Fact]
    public async Task TestM2_AnInvalidReadingIsRefusedNamingTheField()
    {
        var id = await NewModel();
        var now = DateTime.UtcNow;

        var cases = new (AiModelReadingRequest Request, string Field)[]
        {
            (new AiModelReadingRequest { Value = 0.5m, MeasuredAt = now }, "Metric"),
            (new AiModelReadingRequest { Metric = (AiModelMetric)7, Value = 0.5m, MeasuredAt = now }, "Metric"),
            (new AiModelReadingRequest { Metric = AiModelMetric.Accuracy, MeasuredAt = now }, "Value"),
            (new AiModelReadingRequest { Metric = AiModelMetric.Accuracy, Value = 1.01m, MeasuredAt = now }, "Value"),
            (new AiModelReadingRequest { Metric = AiModelMetric.Calibration, Value = -0.1m, MeasuredAt = now }, "Value"),
            (new AiModelReadingRequest { Metric = AiModelMetric.Drift, Value = 1000.5m, MeasuredAt = now }, "Value"),
            (new AiModelReadingRequest { Metric = AiModelMetric.Recall, Value = 0.1234567m, MeasuredAt = now }, "Value"),
            (new AiModelReadingRequest { Metric = AiModelMetric.Recall, Value = 0.5m }, "MeasuredAt"),
            (new AiModelReadingRequest { Metric = AiModelMetric.Recall, Value = 0.5m, MeasuredAt = now.AddDays(1) }, "MeasuredAt"),
            (new AiModelReadingRequest { Metric = AiModelMetric.Recall, Value = 0.5m, MeasuredAt = now, PeriodStart = now.AddDays(-1) }, "PeriodEnd"),
            (new AiModelReadingRequest { Metric = AiModelMetric.Recall, Value = 0.5m, MeasuredAt = now, PeriodEnd = now }, "PeriodStart"),
            (new AiModelReadingRequest { Metric = AiModelMetric.Recall, Value = 0.5m, MeasuredAt = now, PeriodStart = now, PeriodEnd = now.AddDays(-1) }, "PeriodEnd"),
            (new AiModelReadingRequest { Metric = AiModelMetric.Recall, Value = 0.5m, MeasuredAt = now, PeriodStart = now.AddDays(-1), PeriodEnd = now.AddDays(1) }, "PeriodEnd"),
            (new AiModelReadingRequest { Metric = AiModelMetric.Recall, Value = 0.5m, MeasuredAt = now, SampleSize = 0 }, "SampleSize"),
            (new AiModelReadingRequest { Metric = AiModelMetric.Recall, Value = 0.5m, MeasuredAt = now, Method = new string('m', 501) }, "Method"),
            (new AiModelReadingRequest { Metric = AiModelMetric.Recall, Value = 0.5m, MeasuredAt = now, EvidenceReference = new string('e', 501) }, "EvidenceReference")
        };

        foreach (var (request, field) in cases)
            Assert.Equal(field, (await Assert.ThrowsAsync<InvalidParameterException>(() =>
                Svc.RecordReadingAsync(id, request, Cro))).ParameterName);

        // Drift is a statistic, not a fraction: 3.5 is a valid PSI.
        Assert.Equal(3.5m, (await Svc.RecordReadingAsync(id, Reading(AiModelMetric.Drift, 3.5m), Cro)).Value);
        Assert.Single(Read(ctx => ctx.AiModelMetricReadings.ToList()));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.RecordReadingAsync(9999, Reading(AiModelMetric.Drift), Cro));
    }

    // --- O1–O3: overrides and the override rate (T214, S27 edge case) -------------------------------------------------

    /// <summary>
    /// O1 (the S27 edge case) — a person's decision contrary to the model is recorded with its author — the caller — and its
    /// reason, for the version in use; the trail has the author; it is voided with a reason, never deleted. A reason under
    /// ten characters, a missing output or decision, a future date, and a text carrying a person's data are refused.
    /// </summary>
    [Fact]
    public async Task TestO1_AnOverrideIsRecordedWithAuthorAndReason()
    {
        var id = await NewModel(Model(oversight: AiHumanOversight.EveryOutput, version: "4.0"));

        var recorded = await Svc.RecordOverrideAsync(id, Override(), MemberA);

        Assert.Equal((MemberA, "The transcript was misread by the model.", "4.0"),
            (recorded.RecordedById, recorded.Reason, recorded.ModelVersion));
        Assert.Equal(("Reject the application", "Admit the application"), (recorded.ModelOutput, recorded.HumanDecision));
        var trail = Assert.Single(Audit(nameof(AiModelOverride)), a => a.Action == AuditLogAction.Create);
        Assert.Equal((MemberA, recorded.Id), (trail.UserId, trail.EntityId));
        Assert.Equal(1, (await Svc.GetModelAsync(id)).OverrideCount);

        foreach (var (breakIt, field) in new (Action<AiModelOverrideRequest> Break, string Field)[]
                 {
                     (r => r.Reason = "Wrong.", "Reason"),
                     (r => r.Reason = null, "Reason"),
                     (r => r.ModelOutput = " ", "ModelOutput"),
                     (r => r.HumanDecision = null, "HumanDecision"),
                     (r => r.OccurredAt = null, "OccurredAt"),
                     (r => r.OccurredAt = DateTime.UtcNow.AddDays(1), "OccurredAt"),
                     (r => r.HumanDecision = "Admitted maria@aluno.fgv.br after review", "HumanDecision"),
                     (r => r.Reason = "The applicant 987.654.321-00 had a transcript misread.", "Reason")
                 })
        {
            var request = Override();
            breakIt(request);
            var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.RecordOverrideAsync(id, request, MemberA));
            Assert.Equal(field, ex.ParameterName);
            Assert.DoesNotContain("maria@", ex.Message);
            Assert.DoesNotContain("987.654", ex.Message);
        }

        var voided = await Svc.VoidOverrideAsync(id, recorded.Id, Because("Recorded against the wrong model."), MemberB);
        Assert.Equal(MemberB, voided.VoidedById);
        Assert.Equal(AiGovernanceService.OverrideVoidedRule, (await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Svc.VoidOverrideAsync(id, recorded.Id, Because("Recorded against the wrong model."), MemberB))).RuleName);
        Assert.Empty(await Svc.GetOverridesAsync(id, false));
        Assert.Single(await Svc.GetOverridesAsync(id, true));
        Assert.Equal(0, (await Svc.GetModelAsync(id)).OverrideCount);
    }

    /// <summary>
    /// O2 — the human override rate is computed from the overrides recorded with author and reason, never typed: the live
    /// overrides of the current version in [start, end) over the outputs declared reviewed. Voided ones, earlier versions and
    /// overrides outside the period do not count.
    /// </summary>
    [Fact]
    public async Task TestO2_TheOverrideRateIsComputedFromTheRecordedOverrides()
    {
        var id = await NewModel(Model(oversight: AiHumanOversight.EveryOutput));
        var start = DateTime.UtcNow.AddDays(-30);
        var end = DateTime.UtcNow.AddDays(-1);

        await Svc.RecordOverrideAsync(id, Override(start.AddDays(-1)), MemberA); // before the period
        foreach (var day in new[] { 2, 5, 9 }) await Svc.RecordOverrideAsync(id, Override(start.AddDays(day)), MemberA);
        var mistaken = await Svc.RecordOverrideAsync(id, Override(start.AddDays(10)), MemberA);
        await Svc.VoidOverrideAsync(id, mistaken.Id, Because("Duplicate of another override."), Cro);
        await Svc.RecordOverrideAsync(id, Override(end.AddHours(12)), MemberA); // after the period

        var reading = await Svc.RecordReadingAsync(id, new AiModelReadingRequest
        {
            Metric = AiModelMetric.HumanOverrideRate, PeriodStart = start, PeriodEnd = end, SampleSize = 12,
            Method = "Admissions officers' reviews"
        }, Cro);

        Assert.Equal((0.25m, 3, 12, end), (reading.Value, reading.OverrideCount, reading.SampleSize, reading.MeasuredAt));
        Assert.Equal(0.25m, MetricOf(await Svc.GetModelAsync(id), AiModelMetric.HumanOverrideRate).Value);

        // A new version is not measured over the old version's period (T215d): the period is refused, not counted as zero.
        await Svc.UpdateAsync(id, Model(oversight: AiHumanOversight.EveryOutput, version: "1.1"), Cro);
        Assert.Equal("PeriodStart", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.RecordReadingAsync(id, new AiModelReadingRequest
                { Metric = AiModelMetric.HumanOverrideRate, PeriodStart = start, PeriodEnd = end, SampleSize = 12 }, Cro))).ParameterName);
    }

    /// <summary>O3 — a typed rate, a date, a missing period or sample, and more overrides than outputs reviewed are refused.</summary>
    [Fact]
    public async Task TestO3_ATypedOverrideRateIsRefused()
    {
        var id = await NewModel(Model(oversight: AiHumanOversight.EveryOutput));
        var start = DateTime.UtcNow.AddDays(-30);
        var end = DateTime.UtcNow.AddDays(-1);
        foreach (var day in new[] { 1, 2, 3 }) await Svc.RecordOverrideAsync(id, Override(start.AddDays(day)), MemberA);

        var cases = new (AiModelReadingRequest Request, string Field)[]
        {
            (new AiModelReadingRequest { Metric = AiModelMetric.HumanOverrideRate, Value = 0.01m, PeriodStart = start, PeriodEnd = end, SampleSize = 100 }, "Value"),
            (new AiModelReadingRequest { Metric = AiModelMetric.HumanOverrideRate, MeasuredAt = end, PeriodStart = start, PeriodEnd = end, SampleSize = 100 }, "MeasuredAt"),
            (new AiModelReadingRequest { Metric = AiModelMetric.HumanOverrideRate, SampleSize = 100 }, "PeriodStart"),
            (new AiModelReadingRequest { Metric = AiModelMetric.HumanOverrideRate, PeriodStart = start, PeriodEnd = end }, "SampleSize"),
            (new AiModelReadingRequest { Metric = AiModelMetric.HumanOverrideRate, PeriodStart = start, PeriodEnd = end, SampleSize = 2 }, "SampleSize")
        };

        foreach (var (request, field) in cases)
            Assert.Equal(field, (await Assert.ThrowsAsync<InvalidParameterException>(() =>
                Svc.RecordReadingAsync(id, request, Cro))).ParameterName);

        Assert.Empty(Read(ctx => ctx.AiModelMetricReadings.ToList()));
    }

    // --- R1–R3: the register's risks (T213) ----------------------------------------------------------------------------

    /// <summary>
    /// R1 — a risk is linked to a model, idempotently (linking again restates the note), shown on the risk and on the model,
    /// in the risk's trail, and unlinked; the link to a missing risk or model is not found.
    /// </summary>
    [Fact]
    public async Task TestR1_ARiskIsLinkedToAModel()
    {
        var id = await NewModel();
        AddRisk(1, UnitA);

        var view = await Svc.LinkRiskAsync(id, 1, new AiModelRiskLinkRequest { Note = "Bias against transfer students." }, Author);
        Assert.Equal((id, "Bias against transfer students."), (Assert.Single(view.Models).ModelId, view.Models[0].Note));

        view = await Svc.LinkRiskAsync(id, 1, new AiModelRiskLinkRequest { Note = "Hallucinated grades." }, Author);
        Assert.Equal("Hallucinated grades.", Assert.Single(view.Models).Note);
        Assert.Single(Read(ctx => ctx.AiModelRisks.ToList()));

        var model = await Svc.GetModelAsync(id);
        Assert.Equal((1, "R1"), (Assert.Single(model.Risks).RiskId, model.Risks[0].ReferenceId));
        Assert.DoesNotContain(AiModelFindingCode.NoRiskRegistered, Codes(model));

        var trail = await GetService<IAuditTrailService>().GetForRiskAsync(1, 100);
        Assert.Contains(trail, a => a.EntityType == nameof(AiModelRisk) && a.UserId == Author);

        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.LinkRiskAsync(id, 999, new AiModelRiskLinkRequest(), Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.LinkRiskAsync(999, 1, new AiModelRiskLinkRequest(), Author));

        await Svc.UnlinkRiskAsync(id, 1, Author);
        Assert.Empty((await Svc.GetRiskModelsAsync(1)).Models);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.UnlinkRiskAsync(id, 1, Author));
        Assert.Contains(AiModelFindingCode.NoRiskRegistered, Codes(await Svc.GetModelAsync(id)));
    }

    /// <summary>
    /// R2 — the link follows both ends: a scoped reader neither sees nor reaches a risk outside their scope, and a link to a
    /// model they cannot see is counted, never named; the model's finding counts every risk, so it does not depend on who asks.
    /// </summary>
    [Fact]
    public async Task TestR2_TheLinkFollowsTheRiskAndTheModel()
    {
        var hidden = await NewModel(Model("Unit B scoring", entityId: UnitB));
        var shared = await NewModel(Model("Organization chatbot"));
        AddRisk(1, UnitA);
        AddRisk(2, UnitB);
        await Svc.LinkRiskAsync(hidden, 1, new AiModelRiskLinkRequest(), Cro);
        await Svc.LinkRiskAsync(shared, 1, new AiModelRiskLinkRequest(), Cro);
        await Svc.LinkRiskAsync(shared, 2, new AiModelRiskLinkRequest(), Cro);

        ScopeTo(UnitA);
        var view = await Svc.GetRiskModelsAsync(1);
        Assert.Equal((shared, 1), (Assert.Single(view.Models).ModelId, view.HiddenModelCount));

        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetRiskModelsAsync(2));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.LinkRiskAsync(shared, 2, new AiModelRiskLinkRequest(), Owner));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.LinkRiskAsync(hidden, 1, new AiModelRiskLinkRequest(), Owner));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.UnlinkRiskAsync(hidden, 1, Owner));

        var model = await Svc.GetModelAsync(shared);
        Assert.Equal((1, 1), (Assert.Single(model.Risks).RiskId, model.HiddenRiskCount));
        Assert.Equal(2, Assert.Single(await Svc.GetModelsAsync(null, false, false), m => m.Id == shared).LinkedRiskCount);
    }

    // --- F1–F2: findings -----------------------------------------------------------------------------------------------

    /// <summary>
    /// F1 — absent is a finding, never compliant: owner, tier, oversight, data and the register; no review of a high-risk
    /// model's outputs is a finding; a proposed model has no use findings yet; a complete record in use with an evaluation
    /// has none.
    /// </summary>
    [Fact]
    public async Task TestF1_AbsentIsAFindingNeverCompliant()
    {
        var bare = Model("Bare model", tier: null, oversight: null);
        bare.OwnerId = null;
        var model = await Svc.CreateAsync(bare, Cro);
        Assert.Equal(
            [AiModelFindingCode.OwnerMissing, AiModelFindingCode.RiskTierUndeclared, AiModelFindingCode.HumanOversightUndeclared,
             AiModelFindingCode.DataUndeclared, AiModelFindingCode.NoRiskRegistered, AiModelFindingCode.NotEvaluated],
            Codes(model));

        var unreviewed = await Svc.CreateAsync(Model("Unreviewed scoring", tier: AiModelRiskTier.High), Cro);
        Assert.Contains(AiModelFindingCode.HumanOversightAbsent, Codes(unreviewed));

        var proposed = await Svc.CreateAsync(Model("Proposed model", AiModelStatus.Proposed), Cro);
        Assert.DoesNotContain(proposed.Findings, f => f.Code is AiModelFindingCode.NoRiskRegistered
            or AiModelFindingCode.NotEvaluated or AiModelFindingCode.EvaluationIncomplete);
        Assert.Equal(AiModelEvaluationState.NotEvaluated, proposed.Evaluation.State);

        var complete = await NewModel(Model("Complete model"));
        await Svc.SetDataAsync(complete, new AiModelDataRequest { Data = [] }, Cro);
        AddRisk(1, UnitA);
        await Svc.LinkRiskAsync(complete, 1, new AiModelRiskLinkRequest(), Cro);
        await Svc.RecordReadingAsync(complete, Reading(AiModelMetric.Drift, 0.02m), Cro);
        Assert.Empty((await Svc.GetModelAsync(complete)).Findings);

        Assert.Equal(["Bare model", "Proposed model", "Unreviewed scoring"],
            (await Svc.GetModelsAsync(null, false, true)).Select(m => m.Name).ToArray());
    }

    /// <summary>F2 — the incomplete evaluation names what is missing.</summary>
    [Fact]
    public async Task TestF2_AnIncompleteEvaluationNamesWhatIsMissing()
    {
        var id = await NewModel(Model(tier: AiModelRiskTier.Limited));
        await Svc.RecordReadingAsync(id, Reading(AiModelMetric.Drift, 0.02m), Cro);

        var finding = Assert.Single((await Svc.GetModelAsync(id)).Findings, f => f.Code == AiModelFindingCode.EvaluationIncomplete);
        Assert.Contains("accuracy", finding.Message);
        Assert.DoesNotContain("drift", finding.Message);
    }

    // --- P1: the methodology panel's M10 -------------------------------------------------------------------------------

    /// <summary>
    /// P1 — M10 is the share of models in use evaluated on every metric their tier requires, over the caller's scope; a
    /// proposed or retired model is not in use; no model in use is not computable — neither 0 % nor 100 %.
    /// </summary>
    [Fact]
    public async Task TestP1_M10IsTheShareOfModelsInUseEvaluated()
    {
        MethodologyMetricDto M10(MethodologyMetricsDto panel) =>
            panel.Metrics.Single(m => m.Metric == MethodologyMetric.ArtificialIntelligence);
        var metrics = GetService<IMethodologyMetricsService>();

        var empty = M10(await metrics.GetAsync());
        Assert.Equal((MetricAvailability.Available, (double?)null, "9.12"), (empty.Availability, empty.Value, empty.Stage));
        Assert.Contains("neither 0 % nor 100 %", empty.Detail);

        var evaluated = await NewModel(Model("Evaluated"));
        await Svc.RecordReadingAsync(evaluated, Reading(AiModelMetric.Drift, 0.01m), Cro);
        await NewModel(Model("Not evaluated", entityId: UnitB));
        await NewModel(Model("Proposed", AiModelStatus.Proposed));
        await Svc.RetireAsync(await NewModel(Model("Retired")), Because(), Cro);

        var all = M10(await metrics.GetAsync());
        Assert.Equal(((double?)1, (double?)2, (double?)0.5), (all.Numerator, all.Denominator, all.Value));

        ScopeTo(UnitA);
        var scoped = M10(await metrics.GetAsync());
        Assert.Equal(((double?)1, (double?)1, (double?)1), (scoped.Numerator, scoped.Denominator, scoped.Value));
        Assert.Contains("in your scope only", scoped.Detail);
    }

    // --- HI1, Y0 ---------------------------------------------------------------------------------------------------------

    /// <summary>HI1 — the history covers the model, its data, readings, overrides and risk links, newest first, within the limit.</summary>
    [Fact]
    public async Task TestHI1_TheHistoryCoversWhatHangsOffTheModel()
    {
        var id = await NewModel();
        AddRisk(1, UnitA);
        await Svc.SetDataAsync(id, new AiModelDataRequest
            { Data = [new AiModelDataLinkRequest { EntityId = Data, Usage = AiModelDataUsage.Input }] }, Cro);
        await Svc.RecordReadingAsync(id, Reading(AiModelMetric.Drift), Cro);
        await Svc.RecordOverrideAsync(id, Override(), Cro);
        await Svc.LinkRiskAsync(id, 1, new AiModelRiskLinkRequest(), Cro);

        var history = await Svc.GetHistoryAsync(id, 1000);
        Assert.Equal(
            [nameof(AiModel), nameof(AiModelDataLink), nameof(AiModelMetricReading), nameof(AiModelOverride), nameof(AiModelRisk)],
            history.Select(a => a.EntityType).Distinct().OrderBy(t => t, StringComparer.Ordinal).ToArray());
        Assert.Single(await Svc.GetHistoryAsync(id, 1));

        await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.GetHistoryAsync(id, 0));
        await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.GetHistoryAsync(id, AiGovernanceLimits.MaxHistoryLimit + 1));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetHistoryAsync(9999, 10));
    }

    /// <summary>
    /// Y0 (T215, structural) — the service decides nothing: its constructor takes the logger and the data access only — no
    /// acceptance, review, committee, workflow, flag, archive, reviewer or finding service, no HTTP client —, and no method is
    /// named as a decision. Governance, never use (S53 D1).
    /// </summary>
    [Fact]
    public void TestY0_TheServiceDependsOnNoDecisionService()
    {
        var parameters = typeof(AiGovernanceService).GetConstructors().Single().GetParameters().Select(p => p.ParameterType);
        Assert.Equal([typeof(Serilog.ILogger), typeof(ServerServices.Services.IDalService)], parameters);

        string[] decisionWords = ["Approve", "Accept", "Review", "Vote", "Countersign", "Close", "Renew", "Decide"];
        Assert.DoesNotContain(typeof(IAiGovernanceService).GetMethods(),
            m => decisionWords.Any(w => m.Name.Contains(w, StringComparison.Ordinal)));

        // A model is not a principal: no credential-shaped column, and registering one creates no user.
        string[] credential = ["Password", "Secret", "Token", "ApiKey", "Credential", "Login"];
        Assert.DoesNotContain(typeof(AiModel).GetProperties(BindingFlags.Public | BindingFlags.Instance),
            p => credential.Any(c => p.Name.Contains(c, StringComparison.OrdinalIgnoreCase)));
    }

    /// <summary>Registering a model creates no user — a model never becomes someone who could be named an approver.</summary>
    [Fact]
    public async Task TestY1_RegisteringAModelCreatesNoUser()
    {
        var before = Read(ctx => ctx.Users.Count());
        await NewModel();
        Assert.Equal(before, Read(ctx => ctx.Users.Count()));
    }
}

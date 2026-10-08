using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using DAL.Exceptions;
using JetBrains.Annotations;
using Model.DecisionCycle;
using Model.Exceptions;
using Model.Monitoring;
using Model.ThirdParties;
using ServerServices.Governance;
using ServerServices.Interfaces;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.10 (S51 §8) — the third-party register end to end on the in-memory provider: the record and its guards (R1–R8),
/// the links to IT services, processes and data records (L1, L2 — T204), sub-processors and data locations (SP1, DL1), the
/// HECVAT (H1–H5, with H2 the methodology's edge case: a partial HECVAT is incomplete, never a pass — T205), the SBOM
/// (SB1–SB3), the concentration (C1–C4, with C1 the other edge case: a supplier counted once per dependent critical
/// process — T205), the metrics panel's M8 and the scope-checked history.
///
/// The organization is <see cref="DecisionCycleTestBase"/>'s, plus the BIA dependencies the concentration reads:
/// process 10 "Enrolment" (criticality 5) depends on service 20 "Student portal", process 11 "Research" (4) on service 21
/// "Lab services"; both processes are critical and active, so the denominator is 2.
/// </summary>
[TestSubject(typeof(ThirdPartiesService))]
public class ThirdPartiesServiceInMemoryTest : DecisionCycleTestBase
{
    private const int Disabled = 12;

    private const string Bom = """
        {
          "bomFormat": "CycloneDX", "specVersion": "1.5", "serialNumber": "urn:uuid:3e671687-395b-41f5-a30f-a58921a69b79",
          "metadata": { "component": { "name": "Moodle LMS", "version": "4.3.2" } },
          "components": [
            { "name": "log4j-core", "version": "2.17.1", "purl": "pkg:maven/org.apache.logging.log4j/log4j-core@2.17.1" },
            { "name": "openssl", "version": "3.0.13" },
            { "name": "openssl", "version": "3.0.13" },
            { "name": "jquery", "version": "3.7.1", "licenses": [ { "license": { "id": "MIT" } } ] }
          ]
        }
        """;

    private IThirdPartiesService Svc => GetService<IThirdPartiesService>();

    public ThirdPartiesServiceInMemoryTest()
    {
        SeedUnscoped(ctx =>
        {
            var disabled = NewUser(Disabled, "disabled");
            disabled.Enabled = false;
            ctx.Users.Add(disabled);

            ctx.BiaDependencies.Add(new BiaDependency
                { DependentEntityId = Process, ProviderEntityId = Service, CreatedAt = DateTime.UtcNow });
            ctx.BiaDependencies.Add(new BiaDependency
                { DependentEntityId = Process2, ProviderEntityId = Service2, CreatedAt = DateTime.UtcNow });
        });
    }

    private static ThirdPartyRequest Request(string name = "Acme Cloud", int? entityId = null,
        ThirdPartyStatus? status = ThirdPartyStatus.Active) => new() { Name = name, EntityId = entityId, Status = status };

    private async Task<int> NewParty(string name = "Acme Cloud", int? entityId = null,
        ThirdPartyStatus status = ThirdPartyStatus.Active, bool cloud = false, bool identity = false)
    {
        var request = Request(name, entityId, status);
        request.IsCloudProvider = cloud;
        request.IsIdentityProvider = identity;
        return (await Svc.CreateAsync(request, Cro)).Id;
    }

    private int PartyCount() => Read(ctx => ctx.ThirdParties.Count());

    private static ThirdPartyAssessmentRequest Hecvat(int expected, DateTime? respondedAt = null) => new()
    {
        Variant = HecvatVariant.Full, FrameworkVersion = "3.06", ExpectedQuestionCount = expected,
        RespondedAt = respondedAt ?? DateTime.UtcNow.AddDays(-1), EvidenceReference = "GED 2026/114"
    };

    private static ThirdPartyAssessmentAnswersRequest Answers(int count, int blank = 0) => new()
    {
        Answers = Enumerable.Range(1, count).Select(i => new HecvatAnswerRequest
        {
            QuestionId = $"HFIH-{i:00}",
            Answer = i <= blank ? HecvatAnswer.Unanswered : HecvatAnswer.Yes,
            PreferredAnswer = HecvatAnswer.Yes
        }).ToList()
    };

    // --- the record ------------------------------------------------------------------------------------------------

    /// <summary>R1 — a registered third party reads back whole, prospective by default, with its trail and author.</summary>
    [Fact]
    public async Task TestR1_ARegisteredThirdPartyIsReadBackWithItsTrail()
    {
        var created = await Svc.CreateAsync(new ThirdPartyRequest
        {
            Name = "  Acme Cloud ", LegalName = "Acme Cloud Ltda.", TaxId = "12.345.678/0001-90", Country = "br",
            Website = "https://trust.acme.example/security", OwnerId = Owner, IsCloudProvider = true,
            ProcessesPersonalData = true, ContractReference = "CT-2026-031", ContractStart = DateTime.UtcNow.AddYears(-1),
            ContractEnd = DateTime.UtcNow.AddYears(2), SlaAvailabilityPercent = 99.95m, ContractedRtoMinutes = 240,
            ContractedRpoMinutes = 15, VulnerabilityFixDays = 30, RightToAudit = true, AuditClauseReference = "Clause 14.2",
            ExitPlan = "Move to the second provider within 90 days.", DataPortability = "Full export in JSON within 30 days."
        }, Cro);

        Assert.Equal(("Acme Cloud", "BR", ThirdPartyStatus.Prospective, "owner"),
            (created.Name, created.Country, created.Status, created.OwnerName));
        Assert.Equal((99.95m, 240, 15, true), (created.SlaAvailabilityPercent, created.ContractedRtoMinutes,
            created.ContractedRpoMinutes, created.RightToAudit));
        Assert.Null(created.TerminatedAt);
        Assert.Equal(Cro, created.CreatedById);
        Assert.Equal(HecvatState.NotAssessed, created.Hecvat.State);
        Assert.Contains(created.Findings, f => f.Code == ThirdPartyFindingCode.HecvatMissing);

        var trail = Audit(nameof(ThirdParty));
        Assert.NotEmpty(trail);
        Assert.All(trail, row => Assert.Equal(created.Id, row.EntityId));
        Assert.Contains(trail, row => row.UserId == Cro);
    }

    /// <summary>R2 — every invalid field is refused naming it, and nothing is written.</summary>
    [Fact]
    public async Task TestR2_AnInvalidRecordIsRefusedNamingTheField()
    {
        var cases = new (Action<ThirdPartyRequest> Break, string Field)[]
        {
            (r => r.Name = " ", "Name"),
            (r => r.Name = new string('n', ThirdPartyLimits.MaxNameLength + 1), "Name"),
            (r => r.Country = "BRA", "Country"),
            (r => r.Country = "1A", "Country"),
            (r => r.Website = "javascript:alert(1)", "Website"),
            (r => r.Website = "file:///etc/passwd", "Website"),
            (r => r.Website = "https://a.example/x y", "Website"),
            (r => r.SlaAvailabilityPercent = 0m, "SlaAvailabilityPercent"),
            (r => r.SlaAvailabilityPercent = 100.001m, "SlaAvailabilityPercent"),
            (r => r.SlaAvailabilityPercent = 99.9995m, "SlaAvailabilityPercent"),
            (r => r.ContractedRtoMinutes = -1, "ContractedRtoMinutes"),
            (r => r.ContractedRpoMinutes = ThirdPartyLimits.MaxDurationMinutes + 1, "ContractedRpoMinutes"),
            (r => r.VulnerabilityFixDays = -1, "VulnerabilityFixDays"),
            (r => { r.ContractStart = DateTime.UtcNow; r.ContractEnd = DateTime.UtcNow.AddDays(-1); }, "ContractEnd"),
            (r => r.ExitPlanTestedAt = DateTime.UtcNow.AddDays(1), "ExitPlanTestedAt"),
            (r => r.ExitPlanReviewedAt = DateTime.UtcNow.AddDays(1), "ExitPlanReviewedAt"),
            (r => r.Status = (ThirdPartyStatus)9, "Status"),
            (r => r.EntityId = 0, "EntityId"),
            (r => r.OwnerId = -1, "OwnerId"),
            (r => r.ExitPlan = new string('e', ThirdPartyLimits.MaxExitPlanLength + 1), "ExitPlan")
        };

        foreach (var (breakIt, field) in cases)
        {
            var request = Request();
            breakIt(request);

            var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.CreateAsync(request, Cro));
            Assert.Equal(field, ex.ParameterName);
        }

        Assert.Equal(0, PartyCount());
    }

    /// <summary>R3 — one row per supplier, organization-wide and case-insensitive, even against one the caller cannot see.</summary>
    [Fact]
    public async Task TestR3_ANameIsRegisteredOnce()
    {
        var first = await NewParty("Acme Cloud", UnitB);

        await Assert.ThrowsAsync<DataAlreadyExistsException>(() => Svc.CreateAsync(Request("ACME cloud"), Cro));

        ScopeTo(UnitA);
        await Assert.ThrowsAsync<DataAlreadyExistsException>(() => Svc.CreateAsync(Request("acme cloud", UnitA), Cro));

        ScopeToEverything();
        var other = await NewParty("Other");
        await Assert.ThrowsAsync<DataAlreadyExistsException>(() => Svc.UpdateAsync(other, Request("Acme Cloud"), Cro));
        Assert.Equal("Acme Cloud", (await Svc.UpdateAsync(first, Request("Acme Cloud", UnitB), Cro)).Name);
    }

    /// <summary>R4 — the owner exists, is enabled, and is never the third line — an administrator auditor included.</summary>
    [Fact]
    public async Task TestR4_TheOwnerIsAnEnabledFirstLineUser()
    {
        var missing = Request();
        missing.OwnerId = 9999;
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.CreateAsync(missing, Cro));

        var disabled = Request();
        disabled.OwnerId = Disabled;
        Assert.Equal("OwnerId", (await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.CreateAsync(disabled, Cro))).ParameterName);

        var auditor = Request();
        auditor.OwnerId = Auditor;
        Assert.Equal(ThirdLineAssurance.CannotApproveRule,
            (await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.CreateAsync(auditor, Cro))).RuleName);

        Assert.Equal(0, PartyCount());
    }

    /// <summary>R5 — the entity it is filed under must exist.</summary>
    [Fact]
    public async Task TestR5_TheEntityMustExist() =>
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.CreateAsync(Request(entityId: 9999), Cro));

    /// <summary>
    /// R6 — scope: a scoped caller cannot file another entity's supplier nor, holding two entities, the organization's; one
    /// holding a single entity gets it filed there; reads see the organization's and their own, and another entity's is
    /// not found — to read, update or delete.
    /// </summary>
    [Fact]
    public async Task TestR6_ScopeGovernsReadsAndWrites()
    {
        var org = await NewParty("Org-wide");
        var mine = await NewParty("Unit A supplier", UnitA);
        var theirs = await NewParty("Unit B supplier", UnitB);

        ScopeTo(UnitA);
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => Svc.CreateAsync(Request("New B", UnitB), Author));
        Assert.Equal(UnitA, (await Svc.CreateAsync(Request("Filed in A"), Author)).EntityId);

        Assert.Equal(new[] { "Filed in A", "Org-wide", "Unit A supplier" },
            (await Svc.GetThirdPartiesAsync(null, false)).Select(p => p.Name));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetThirdPartyAsync(theirs));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.UpdateAsync(theirs, Request("Unit B supplier", UnitB), Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.DeleteAsync(theirs, Author));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => Svc.UpdateAsync(mine, Request("Unit A supplier", UnitB), Author));

        ScopeTo(UnitA, UnitB);
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => Svc.CreateAsync(Request("Org by a unit"), Author));
        await Assert.ThrowsAsync<EntityScopeViolationException>(() => Svc.UpdateAsync(org, Request("Org-wide"), Author));
    }

    /// <summary>
    /// R6b (regression, review of M48) — every write needs the third party's <em>own</em> entity in scope, not only where the
    /// record goes: a Unit A writer can neither re-file the organization's supplier under Unit A (taking it over) nor touch
    /// what the organization's supplier declares — links, sub-processors, locations, HECVAT, SBOMs — nor delete it; and
    /// nothing changes. The context's write guard alone saw only modified or added third-party rows, and only their new
    /// entity.
    /// </summary>
    [Fact]
    public async Task TestR6b_EveryWriteNeedsTheThirdPartysOwnEntityInScope()
    {
        var org = await NewParty("Org-wide");
        await Svc.LinkAsync(org, Service, new ThirdPartyLinkRequest(), Cro);
        var assessment = await Svc.RecordAssessmentAsync(org, Hecvat(2), Cro);
        var sbom = await Svc.ImportSbomAsync(org, new ThirdPartySbomRequest { Document = Bom }, Cro);
        var spare = await NewParty("Spare org-wide");

        ScopeTo(UnitA);
        var writes = new (string Name, Func<Task> Write)[]
        {
            ("re-file under Unit A", () => Svc.UpdateAsync(org, Request("Org-wide", UnitA), Author)),
            ("update", () => Svc.UpdateAsync(org, Request("Org-wide"), Author)),
            ("link", () => Svc.LinkAsync(org, Process, new ThirdPartyLinkRequest(), Author)),
            ("unlink", () => Svc.UnlinkAsync(org, Service, Author)),
            ("sub-processors", () => Svc.SetSubprocessorsAsync(org, new ThirdPartySubprocessorsRequest { Subprocessors = [] }, Author)),
            ("locations", () => Svc.SetDataLocationsAsync(org, new ThirdPartyDataLocationsRequest { Locations = [] }, Author)),
            ("record HECVAT", () => Svc.RecordAssessmentAsync(org, Hecvat(2), Author)),
            ("answers", () => Svc.ReplaceAnswersAsync(org, assessment.Id, Answers(2), Author)),
            ("void", () => Svc.VoidAssessmentAsync(org, assessment.Id, new ThirdPartyAssessmentVoidRequest { Reason = "Not ours to void." }, Author)),
            ("import SBOM", () => Svc.ImportSbomAsync(org, new ThirdPartySbomRequest { Document = Bom.Replace("jquery", "zepto") }, Author)),
            ("delete SBOM", () => Svc.DeleteSbomAsync(org, sbom.Id, Author)),
            ("delete", () => Svc.DeleteAsync(spare, Author))
        };

        foreach (var (name, write) in writes)
        {
            var ex = await Record.ExceptionAsync(write);
            Assert.True(ex is EntityScopeViolationException, $"{name}: {ex?.GetType().Name ?? "no exception"}");
        }

        ScopeToEverything();
        var after = await Svc.GetThirdPartyAsync(org);
        Assert.Null(after.EntityId);
        Assert.Equal(new[] { Service }, after.Links.Select(l => l.EntityId));
        Assert.Null(after.SubprocessorsDeclaredAt);
        Assert.Empty(after.DataLocations);
        Assert.Single(after.Assessments);
        Assert.Null(after.Assessments[0].VoidedAt);
        Assert.Null(after.Assessments[0].AnswersUpdatedAt);
        Assert.Single(after.Sboms);
        Assert.Equal(2, PartyCount());

        // The unit's own supplier stays fully writable by the unit.
        var mine = await NewParty("Unit A supplier", UnitA);
        ScopeTo(UnitA);
        await Svc.LinkAsync(mine, Service, new ThirdPartyLinkRequest(), Author);
        await Svc.UnlinkAsync(mine, Service, Author);
        await Svc.DeleteAsync(mine, Author);
    }

    /// <summary>R7b — an owner since disabled does not block editing the supplier — terminating it included; changing to a disabled owner still does.</summary>
    [Fact]
    public async Task TestR7b_ADisabledOwnerDoesNotBlockEdits()
    {
        var request = Request("Owned");
        request.OwnerId = Owner;
        var id = (await Svc.CreateAsync(request, Cro)).Id;
        SeedUnscoped(ctx => ctx.Users.Single(u => u.Value == Owner).Enabled = false);

        request.Status = ThirdPartyStatus.Terminated;
        Assert.Equal(ThirdPartyStatus.Terminated, (await Svc.UpdateAsync(id, request, Cro)).Status);

        request.OwnerId = Disabled;
        Assert.Equal("OwnerId", (await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.UpdateAsync(id, request, Cro))).ParameterName);
    }

    /// <summary>R7 — updating replaces the record; terminated is dated exactly while it lasts; a status left out is kept.</summary>
    [Fact]
    public async Task TestR7_UpdateAndTheTerminationDate()
    {
        var id = await NewParty();

        var terminated = await Svc.UpdateAsync(id, Request(status: ThirdPartyStatus.Terminated), Owner);
        Assert.Equal(ThirdPartyStatus.Terminated, terminated.Status);
        var when = Assert.NotNull(terminated.TerminatedAt);
        Assert.Equal(Owner, terminated.UpdatedById);
        Assert.Empty(terminated.Findings);

        var kept = await Svc.UpdateAsync(id, Request(status: null), Owner);
        Assert.Equal((ThirdPartyStatus.Terminated, when), (kept.Status, kept.TerminatedAt!.Value));

        var resumed = await Svc.UpdateAsync(id, Request(status: ThirdPartyStatus.Active), Owner);
        Assert.Equal(ThirdPartyStatus.Active, resumed.Status);
        Assert.Null(resumed.TerminatedAt);

        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.UpdateAsync(9999, Request(), Owner));
    }

    /// <summary>
    /// R8 — a third party nothing refers to is deleted with its own declarations; one in use is refused with
    /// <c>third_party_in_use</c> — by a link, an assessment, an SBOM, or a row of a supplier the caller cannot see that
    /// names it as its sub-processor — and nothing is deleted.
    /// </summary>
    [Fact]
    public async Task TestR8_AThirdPartyInUseIsNeverDeleted()
    {
        var unused = await NewParty("Unused");
        await Svc.SetDataLocationsAsync(unused, new ThirdPartyDataLocationsRequest
            { Locations = [new ThirdPartyDataLocationRequest { Country = "BR", Purpose = ThirdPartyDataLocationPurpose.Storage }] }, Cro);
        await Svc.SetSubprocessorsAsync(unused, new ThirdPartySubprocessorsRequest
            { Subprocessors = [new ThirdPartySubprocessorRequest { Name = "Mailer" }] }, Cro);
        await Svc.DeleteAsync(unused, Cro);
        Assert.Equal(0, Read(ctx => ctx.ThirdParties.Count() + ctx.ThirdPartyDataLocations.Count() + ctx.ThirdPartySubprocessors.Count()));

        var linked = await NewParty("Linked");
        await Svc.LinkAsync(linked, Service, new ThirdPartyLinkRequest(), Cro);

        var assessed = await NewParty("Assessed");
        await Svc.RecordAssessmentAsync(assessed, Hecvat(10), Cro);

        var sbom = await NewParty("With SBOM");
        await Svc.ImportSbomAsync(sbom, new ThirdPartySbomRequest { Document = Bom }, Cro);

        var cloud = await NewParty("Shared cloud", cloud: true);
        var hiddenUser = await NewParty("Unit B SaaS", UnitB);
        await Svc.SetSubprocessorsAsync(hiddenUser, new ThirdPartySubprocessorsRequest
            { Subprocessors = [new ThirdPartySubprocessorRequest { Name = "Shared cloud", SubprocessorThirdPartyId = cloud }] }, Cro);

        // As an administrator with global scope: the use by the Unit B SaaS is still counted (an unscoped count), and the
        // scope guard (R6b) is not what stops these deletes.
        foreach (var (id, mention) in new[] { (linked, "link"), (assessed, "HECVAT"), (sbom, "SBOM"), (cloud, "sub-processor") })
        {
            var ex = await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.DeleteAsync(id, Cro));
            Assert.Equal(ThirdPartiesService.InUseRule, ex.RuleName);
            Assert.Contains(mention, ex.Message);
        }

        // A Unit A writer deleting their own supplier named as a sub-processor by a Unit B supplier they cannot see.
        var unitCloud = await NewParty("Unit A cloud", UnitA, cloud: true);
        await Svc.SetSubprocessorsAsync(hiddenUser, new ThirdPartySubprocessorsRequest
        {
            Subprocessors =
            [
                new ThirdPartySubprocessorRequest { Name = "Shared cloud", SubprocessorThirdPartyId = cloud },
                new ThirdPartySubprocessorRequest { Name = "Unit A cloud", SubprocessorThirdPartyId = unitCloud }
            ]
        }, Cro);
        ScopeTo(UnitA);
        Assert.Equal(ThirdPartiesService.InUseRule,
            (await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.DeleteAsync(unitCloud, Author))).RuleName);

        ScopeToEverything();
        Assert.Equal(6, PartyCount());
    }

    // --- links (T204) ----------------------------------------------------------------------------------------------

    /// <summary>
    /// L1 (T204) — a supplier links to an IT service, a business process or a data record, the kind derived from the
    /// entity; linking again restates the description; the link is read from the entity's side; anything else is refused.
    /// </summary>
    [Fact]
    public async Task TestL1_LinksToServicesProcessesAndDataRecords()
    {
        var id = await NewParty();

        await Svc.LinkAsync(id, Service, new ThirdPartyLinkRequest { Description = "Hosts the portal" }, Cro);
        await Svc.LinkAsync(id, Process, new ThirdPartyLinkRequest(), Cro);
        var dto = await Svc.LinkAsync(id, Data, new ThirdPartyLinkRequest { Description = "Stores the records" }, Cro);

        Assert.Equal(new[]
            {
                (Service, ThirdPartyLinkKind.ItService, "Student portal"), (Process, ThirdPartyLinkKind.BusinessProcess, "Enrolment"),
                (Data, ThirdPartyLinkKind.Data, "Student records")
            },
            dto.Links.Select(l => (l.EntityId, l.Kind, l.EntityName!)).OrderBy(l => l.Kind));

        var again = await Svc.LinkAsync(id, Service, new ThirdPartyLinkRequest { Description = "Hosts and operates the portal" }, Cro);
        Assert.Equal(3, again.Links.Count);
        Assert.Equal("Hosts and operates the portal", again.Links.Single(l => l.EntityId == Service).Description);

        var fromService = Assert.Single(await Svc.GetByEntityAsync(Service));
        Assert.Equal((id, ThirdPartyLinkKind.ItService, ThirdPartyStatus.Active), (fromService.ThirdPartyId, fromService.Kind, fromService.Status));

        foreach (var notLinkable in new[] { PortalApp, UnitA, Person })
            Assert.Equal(ThirdPartiesService.LinkTargetRule, (await Assert.ThrowsAsync<RuleBrokenException>(
                () => Svc.LinkAsync(id, notLinkable, new ThirdPartyLinkRequest(), Cro))).RuleName);

        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.LinkAsync(id, 9999, new ThirdPartyLinkRequest(), Cro));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.LinkAsync(9999, Service, new ThirdPartyLinkRequest(), Cro));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetByEntityAsync(9999));
        Assert.Equal("Description", (await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.LinkAsync(id, Service,
            new ThirdPartyLinkRequest { Description = new string('d', ThirdPartyLimits.MaxLinkDescriptionLength + 1) }, Cro))).ParameterName);

        await Svc.UnlinkAsync(id, Service, Cro);
        Assert.Empty(await Svc.GetByEntityAsync(Service));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.UnlinkAsync(id, Service, Cro));

        await Svc.UpdateAsync(id, Request(status: ThirdPartyStatus.Terminated), Cro);
        Assert.Equal(ThirdPartiesService.TerminatedRule, (await Assert.ThrowsAsync<RuleBrokenException>(
            () => Svc.LinkAsync(id, Service, new ThirdPartyLinkRequest(), Cro))).RuleName);
    }

    /// <summary>
    /// L2 (T204, S51 D4) — the data link is the forward-compatible association Stage 9.11 reads: the data record lists who
    /// processes it, and processing a data record asks for a data location even when personal data is not declared.
    /// </summary>
    [Fact]
    public async Task TestL2_TheDataRecordListsWhoProcessesIt()
    {
        var request = Request("Records processor");
        request.ProcessesPersonalData = false;
        var id = (await Svc.CreateAsync(request, Cro)).Id;
        await Svc.LinkAsync(id, Data, new ThirdPartyLinkRequest { Description = "Processes enrolment records" }, Cro);

        var processor = Assert.Single(await Svc.GetByEntityAsync(Data));
        Assert.Equal((id, ThirdPartyLinkKind.Data, "Processes enrolment records"), (processor.ThirdPartyId, processor.Kind, processor.Description));

        Assert.Contains((await Svc.GetThirdPartyAsync(id)).Findings, f => f.Code == ThirdPartyFindingCode.DataLocationMissing);

        var located = await Svc.SetDataLocationsAsync(id, new ThirdPartyDataLocationsRequest
            { Locations = [new ThirdPartyDataLocationRequest { Country = "BR", Purpose = ThirdPartyDataLocationPurpose.Storage }] }, Cro);
        Assert.DoesNotContain(located.Findings, f => f.Code == ThirdPartyFindingCode.DataLocationMissing);

        // A data record is not a dependency: it never counts in the concentration.
        Assert.Equal(0, located.Concentration.DependentCriticalProcessCount);
    }

    // --- declarations ----------------------------------------------------------------------------------------------

    /// <summary>
    /// SP1 — the sub-processor list is replaced whole and dated as declared, an empty list included; a kept row keeps its
    /// id; duplicates, itself, an unknown or invisible registered sub-processor, a bad country and a terminated supplier
    /// are refused.
    /// </summary>
    [Fact]
    public async Task TestSP1_SubprocessorsAreDeclaredWhole()
    {
        var cloud = await NewParty("Hyperscaler", cloud: true);
        var id = await NewParty("SaaS");

        var declared = await Svc.SetSubprocessorsAsync(id, new ThirdPartySubprocessorsRequest
        {
            Subprocessors =
            [
                new ThirdPartySubprocessorRequest { Name = "Hyperscaler", SubprocessorThirdPartyId = cloud, Country = "us", ProcessesPersonalData = true },
                new ThirdPartySubprocessorRequest { Name = "Mailer", Service = "Transactional e-mail", Country = "IE" }
            ]
        }, Owner);

        Assert.NotNull(declared.SubprocessorsDeclaredAt);
        Assert.Equal(new[] { ("Hyperscaler", (int?)cloud, "US"), ("Mailer", (int?)null, "IE") },
            declared.Subprocessors.Select(s => (s.Name, s.SubprocessorThirdPartyId, s.Country!)));
        var keptId = declared.Subprocessors[0].Id;

        var replaced = await Svc.SetSubprocessorsAsync(id, new ThirdPartySubprocessorsRequest
            { Subprocessors = [new ThirdPartySubprocessorRequest { Name = "HYPERSCALER", SubprocessorThirdPartyId = cloud }] }, Owner);
        Assert.Equal(keptId, Assert.Single(replaced.Subprocessors).Id);

        var none = await Svc.SetSubprocessorsAsync(id, new ThirdPartySubprocessorsRequest { Subprocessors = [] }, Owner);
        Assert.Empty(none.Subprocessors);
        Assert.NotNull(none.SubprocessorsDeclaredAt);

        await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.SetSubprocessorsAsync(id, new ThirdPartySubprocessorsRequest(), Owner));
        await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.SetSubprocessorsAsync(id, new ThirdPartySubprocessorsRequest
            { Subprocessors = [new ThirdPartySubprocessorRequest { Name = "A" }, new ThirdPartySubprocessorRequest { Name = "a" }] }, Owner));
        await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.SetSubprocessorsAsync(id, new ThirdPartySubprocessorsRequest
            { Subprocessors = [new ThirdPartySubprocessorRequest { Name = "A", Country = "USA" }] }, Owner));
        Assert.Equal(ThirdPartiesService.SelfSubprocessorRule, (await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Svc.SetSubprocessorsAsync(id, new ThirdPartySubprocessorsRequest
                { Subprocessors = [new ThirdPartySubprocessorRequest { Name = "Me", SubprocessorThirdPartyId = id }] }, Owner))).RuleName);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.SetSubprocessorsAsync(id, new ThirdPartySubprocessorsRequest
            { Subprocessors = [new ThirdPartySubprocessorRequest { Name = "Ghost", SubprocessorThirdPartyId = 9999 }] }, Owner));

        var hidden = await NewParty("Unit B only", UnitB);
        var mine = await NewParty("Unit A SaaS", UnitA);
        ScopeTo(UnitA);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.SetSubprocessorsAsync(mine, new ThirdPartySubprocessorsRequest
            { Subprocessors = [new ThirdPartySubprocessorRequest { Name = "Hidden", SubprocessorThirdPartyId = hidden }] }, Owner));

        ScopeToEverything();
        await Svc.UpdateAsync(id, Request("SaaS", status: ThirdPartyStatus.Terminated), Owner);
        Assert.Equal(ThirdPartiesService.TerminatedRule, (await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Svc.SetSubprocessorsAsync(id, new ThirdPartySubprocessorsRequest { Subprocessors = [] }, Owner))).RuleName);
    }

    /// <summary>
    /// SP2 (regression, review of M48) — a sub-processor registered in another unit reads by name without its id, and a
    /// writer who cannot see it resends the list without erasing it: null for a kept row means unchanged, so the fourth-party
    /// edge — and the hidden supplier's concentration and in-use state — survive. Naming it on purpose is still refused.
    /// </summary>
    [Fact]
    public async Task TestSP2_AHiddenRegisteredSubprocessorSurvivesAResend()
    {
        var hiddenCloud = await NewParty("Unit B cloud", UnitB, cloud: true);
        var saas = await NewParty("Unit A SaaS", UnitA);
        await Svc.LinkAsync(saas, Service, new ThirdPartyLinkRequest(), Cro);
        await Svc.SetSubprocessorsAsync(saas, new ThirdPartySubprocessorsRequest
            { Subprocessors = [new ThirdPartySubprocessorRequest { Name = "Unit B cloud", SubprocessorThirdPartyId = hiddenCloud }] }, Cro);

        ScopeTo(UnitA);
        var seen = await Svc.GetThirdPartyAsync(saas);
        var row = Assert.Single(seen.Subprocessors);
        Assert.Equal(("Unit B cloud", (int?)null), (row.Name, row.SubprocessorThirdPartyId));

        // The round trip a client makes: what it read, sent back, with a new country.
        await Svc.SetSubprocessorsAsync(saas, new ThirdPartySubprocessorsRequest
        {
            Subprocessors = seen.Subprocessors.Select(x => new ThirdPartySubprocessorRequest
                { Name = x.Name, SubprocessorThirdPartyId = x.SubprocessorThirdPartyId, Country = "US" }).ToList()
        }, Author);

        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.SetSubprocessorsAsync(saas, new ThirdPartySubprocessorsRequest
            { Subprocessors = [new ThirdPartySubprocessorRequest { Name = "Another", SubprocessorThirdPartyId = hiddenCloud }] }, Author));

        ScopeToEverything();
        Assert.Equal((int?)hiddenCloud, Read(ctx => ctx.ThirdPartySubprocessors.Single(x => x.ThirdPartyId == saas).SubprocessorThirdPartyId));
        Assert.Equal("US", Read(ctx => ctx.ThirdPartySubprocessors.Single(x => x.ThirdPartyId == saas).Country));
        Assert.Equal(1, (await Svc.GetThirdPartyAsync(hiddenCloud)).Concentration.DependentCriticalProcessCount);
        Assert.Equal(ThirdPartiesService.InUseRule,
            (await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.DeleteAsync(hiddenCloud, Cro))).RuleName);
    }

    /// <summary>DL1 — data locations are replaced whole, one per country and purpose; a kept row keeps its id.</summary>
    [Fact]
    public async Task TestDL1_DataLocationsAreDeclaredWhole()
    {
        var id = await NewParty();

        var set = await Svc.SetDataLocationsAsync(id, new ThirdPartyDataLocationsRequest
        {
            Locations =
            [
                new ThirdPartyDataLocationRequest { Country = "br", Purpose = ThirdPartyDataLocationPurpose.Storage },
                new ThirdPartyDataLocationRequest { Country = "US", Region = "us-east-1", Purpose = ThirdPartyDataLocationPurpose.Backup }
            ]
        }, Cro);
        Assert.Equal(new[] { ("BR", ThirdPartyDataLocationPurpose.Storage), ("US", ThirdPartyDataLocationPurpose.Backup) },
            set.DataLocations.Select(l => (l.Country, l.Purpose)));
        var brazil = set.DataLocations[0].Id;

        var replaced = await Svc.SetDataLocationsAsync(id, new ThirdPartyDataLocationsRequest
            { Locations = [new ThirdPartyDataLocationRequest { Country = "BR", Region = "sa-east-1", Purpose = ThirdPartyDataLocationPurpose.Storage }] }, Cro);
        var only = Assert.Single(replaced.DataLocations);
        Assert.Equal((brazil, "sa-east-1"), (only.Id, only.Region));

        var bad = new[]
        {
            new ThirdPartyDataLocationsRequest(),
            new ThirdPartyDataLocationsRequest { Locations = [new ThirdPartyDataLocationRequest { Purpose = ThirdPartyDataLocationPurpose.Storage }] },
            new ThirdPartyDataLocationsRequest { Locations = [new ThirdPartyDataLocationRequest { Country = "BR" }] },
            new ThirdPartyDataLocationsRequest { Locations = [new ThirdPartyDataLocationRequest { Country = "BR", Purpose = (ThirdPartyDataLocationPurpose)7 }] },
            new ThirdPartyDataLocationsRequest
            {
                Locations =
                [
                    new ThirdPartyDataLocationRequest { Country = "BR", Purpose = ThirdPartyDataLocationPurpose.Storage },
                    new ThirdPartyDataLocationRequest { Country = "br", Purpose = ThirdPartyDataLocationPurpose.Storage }
                ]
            }
        };
        foreach (var request in bad)
            await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.SetDataLocationsAsync(id, request, Cro));

        Assert.Single((await Svc.GetThirdPartyAsync(id)).DataLocations);
    }

    // --- HECVAT ----------------------------------------------------------------------------------------------------

    /// <summary>H1 — recording a HECVAT: with no answers yet it is incomplete; every invalid field is refused.</summary>
    [Fact]
    public async Task TestH1_RecordingAHecvat()
    {
        var id = await NewParty();

        var assessment = await Svc.RecordAssessmentAsync(id, Hecvat(120), Owner);
        Assert.Equal((HecvatState.Incomplete, 120, 0), (assessment.Result.State, assessment.ExpectedQuestionCount, assessment.Result.AnsweredCount));
        Assert.Equal(Owner, assessment.CreatedById);

        var cases = new (Action<ThirdPartyAssessmentRequest> Break, string Field)[]
        {
            (r => r.Variant = null, "Variant"),
            (r => r.Variant = (HecvatVariant)9, "Variant"),
            (r => r.FrameworkVersion = " ", "FrameworkVersion"),
            (r => r.ExpectedQuestionCount = 0, "ExpectedQuestionCount"),
            (r => r.ExpectedQuestionCount = ThirdPartyLimits.MaxExpectedQuestions + 1, "ExpectedQuestionCount"),
            (r => r.RespondedAt = DateTime.UtcNow.AddDays(1), "RespondedAt"),
            (r => r.ValidUntil = r.RespondedAt, "ValidUntil")
        };
        foreach (var (breakIt, field) in cases)
        {
            var request = Hecvat(10);
            breakIt(request);
            Assert.Equal(field, (await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.RecordAssessmentAsync(id, request, Owner))).ParameterName);
        }

        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.RecordAssessmentAsync(9999, Hecvat(10), Owner));
        await Svc.UpdateAsync(id, Request(status: ThirdPartyStatus.Terminated), Owner);
        Assert.Equal(ThirdPartiesService.TerminatedRule,
            (await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.RecordAssessmentAsync(id, Hecvat(10), Owner))).RuleName);
    }

    /// <summary>
    /// H2 (T205) — end to end: three of four questions answered, all as preferred, is incomplete — no score, not conforming,
    /// and the supplier's finding says so; the fourth answer makes it conform; a question left blank makes it incomplete
    /// again.
    /// </summary>
    [Fact]
    public async Task TestH2_APartialHecvatScoresIncompleteNeverAPass()
    {
        var id = await NewParty();
        var assessment = await Svc.RecordAssessmentAsync(id, Hecvat(4), Owner);

        var partial = await Svc.ReplaceAnswersAsync(id, assessment.Id, Answers(3), Owner);
        Assert.Equal(HecvatState.Incomplete, partial.Result.State);
        Assert.Null(partial.Result.Score);
        Assert.Equal((3, 1), (partial.Result.AnsweredCount, partial.Result.UnansweredCount));

        var detail = await Svc.GetThirdPartyAsync(id);
        Assert.Equal(HecvatState.Incomplete, detail.Hecvat.State);
        Assert.Contains(detail.Findings, f => f.Code == ThirdPartyFindingCode.HecvatIncomplete);
        Assert.Equal(HecvatState.Incomplete, (await Svc.GetThirdPartiesAsync(null, false)).Single().HecvatState);

        var complete = await Svc.ReplaceAnswersAsync(id, assessment.Id, Answers(4), Owner);
        Assert.Equal((HecvatState.Conforming, 1m), (complete.Result.State, complete.Result.Score!.Value));
        Assert.DoesNotContain((await Svc.GetThirdPartyAsync(id)).Findings,
            f => f.Code is ThirdPartyFindingCode.HecvatIncomplete or ThirdPartyFindingCode.HecvatMissing);

        var blank = await Svc.ReplaceAnswersAsync(id, assessment.Id, Answers(4, blank: 1), Owner);
        Assert.Equal(HecvatState.Incomplete, blank.Result.State);
        Assert.Equal(new[] { "HFIH-01" }, blank.Result.BlankQuestionIds);
        Assert.Equal(4, (await Svc.GetAssessmentAsync(id, assessment.Id)).Answers.Count);
    }

    /// <summary>H3 — every invalid answer is refused naming the field, and the stored answers do not change.</summary>
    [Fact]
    public async Task TestH3_InvalidAnswersAreRefusedWhole()
    {
        var id = await NewParty();
        var assessment = await Svc.RecordAssessmentAsync(id, Hecvat(3), Owner);
        await Svc.ReplaceAnswersAsync(id, assessment.Id, Answers(2), Owner);

        HecvatAnswerRequest Ok(string q) => new() { QuestionId = q, Answer = HecvatAnswer.Yes, PreferredAnswer = HecvatAnswer.Yes };

        var cases = new (List<HecvatAnswerRequest>? Answers, string Field)[]
        {
            (null, "Answers"),
            ([new HecvatAnswerRequest { QuestionId = "not an id!", Answer = HecvatAnswer.Yes }], "QuestionId"),
            ([new HecvatAnswerRequest { QuestionId = new string('A', 21), Answer = HecvatAnswer.Yes }], "QuestionId"),
            ([Ok("hfih-01"), Ok("HFIH-01")], "QuestionId"),
            ([new HecvatAnswerRequest { QuestionId = "HFIH-01" }], "Answer"),
            ([new HecvatAnswerRequest { QuestionId = "HFIH-01", Answer = (HecvatAnswer)7 }], "Answer"),
            ([new HecvatAnswerRequest { QuestionId = "HFIH-01", Answer = HecvatAnswer.Yes, PreferredAnswer = HecvatAnswer.NotApplicable }], "PreferredAnswer"),
            ([new HecvatAnswerRequest { QuestionId = "HFIH-01", Answer = HecvatAnswer.Yes, Weight = 0 }], "Weight"),
            ([new HecvatAnswerRequest { QuestionId = "HFIH-01", Answer = HecvatAnswer.Yes, Weight = 101 }], "Weight"),
            ([Ok("A-1"), Ok("A-2"), Ok("A-3"), Ok("A-4")], "Answers")
        };

        foreach (var (answers, field) in cases)
            Assert.Equal(field, (await Assert.ThrowsAsync<InvalidParameterException>(() =>
                Svc.ReplaceAnswersAsync(id, assessment.Id, new ThirdPartyAssessmentAnswersRequest { Answers = answers }, Owner))).ParameterName);

        Assert.Equal(new[] { "HFIH-01", "HFIH-02" }, (await Svc.GetAssessmentAsync(id, assessment.Id)).Answers.Select(a => a.QuestionId));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.ReplaceAnswersAsync(id, 9999, Answers(1), Owner));
    }

    /// <summary>
    /// H4 — voiding needs a reason, happens once, and freezes the answers; the supplier then reads its previous live
    /// assessment — or none.
    /// </summary>
    [Fact]
    public async Task TestH4_VoidingFallsBackToThePreviousAssessment()
    {
        var id = await NewParty();
        var older = await Svc.RecordAssessmentAsync(id, Hecvat(2, DateTime.UtcNow.AddDays(-60)), Owner);
        await Svc.ReplaceAnswersAsync(id, older.Id, Answers(2), Owner);
        var newer = await Svc.RecordAssessmentAsync(id, Hecvat(5), Owner);

        Assert.Equal(HecvatState.Incomplete, (await Svc.GetThirdPartyAsync(id)).Hecvat.State);

        Assert.Equal("Reason", (await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.VoidAssessmentAsync(id, newer.Id,
            new ThirdPartyAssessmentVoidRequest { Reason = "short" }, Owner))).ParameterName);

        var voided = await Svc.VoidAssessmentAsync(id, newer.Id,
            new ThirdPartyAssessmentVoidRequest { Reason = "Uploaded against the wrong vendor." }, Owner);
        Assert.Equal(HecvatState.Voided, voided.Result.State);
        Assert.NotNull(voided.VoidedAt);

        Assert.Equal(ThirdPartiesService.AssessmentVoidedRule, (await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Svc.VoidAssessmentAsync(id, newer.Id, new ThirdPartyAssessmentVoidRequest { Reason = "Uploaded twice by mistake." }, Owner))).RuleName);
        Assert.Equal(ThirdPartiesService.AssessmentVoidedRule, (await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Svc.ReplaceAnswersAsync(id, newer.Id, Answers(5), Owner))).RuleName);

        var detail = await Svc.GetThirdPartyAsync(id);
        Assert.Equal(HecvatState.Conforming, detail.Hecvat.State);
        Assert.Equal(new[] { HecvatState.Voided, HecvatState.Conforming }, detail.Assessments.Select(a => a.Result.State));

        await Svc.VoidAssessmentAsync(id, older.Id, new ThirdPartyAssessmentVoidRequest { Reason = "Superseded by the audit." }, Owner);
        Assert.Contains((await Svc.GetThirdPartyAsync(id)).Findings, f => f.Code == ThirdPartyFindingCode.HecvatMissing);
    }

    /// <summary>
    /// H5 — replacing answers is in the trail through the assessment row (who, when); the answers themselves are not, so an
    /// upload of hundreds does not bury it.
    /// </summary>
    [Fact]
    public async Task TestH5_AnswerReplacementIsInTheTrailThroughTheAssessment()
    {
        var id = await NewParty();
        var assessment = await Svc.RecordAssessmentAsync(id, Hecvat(3), Owner);

        await Svc.ReplaceAnswersAsync(id, assessment.Id, Answers(3), MemberA);

        Assert.Contains(Audit(nameof(ThirdPartyAssessment)),
            row => row.EntityId == assessment.Id && row.UserId == MemberA && row.Field == nameof(ThirdPartyAssessment.AnswersUpdatedAt));
        Assert.Empty(Audit(nameof(ThirdPartyAssessmentAnswer)));
        Assert.Equal(MemberA, (await Svc.GetAssessmentAsync(id, assessment.Id)).AnswersUpdatedById);
    }

    // --- SBOM ------------------------------------------------------------------------------------------------------

    /// <summary>
    /// SB1 — an SBOM is parsed and its components stored, deduplicated; the file name is reduced to its last segment; the
    /// same document twice is refused; nothing reaches the network.
    /// </summary>
    [Fact]
    public async Task TestSB1_AnSbomIsImportedFromItsText()
    {
        var id = await NewParty();

        var sbom = await Svc.ImportSbomAsync(id, new ThirdPartySbomRequest
        {
            ComponentName = "Moodle hosting", FileName = "../../etc/vendor/sbom.cdx.json", Document = Bom
        }, Owner);

        Assert.Equal(("Moodle hosting", "4.3.2", SbomFormat.CycloneDxJson, "1.5"),
            (sbom.ComponentName, sbom.ComponentVersion, sbom.Format, sbom.SpecVersion));
        Assert.Equal("sbom.cdx.json", sbom.FileName);
        Assert.Matches("^[0-9a-f]{64}$", sbom.DocumentSha256);
        Assert.Equal(3, sbom.ComponentCount);
        Assert.Equal(new[] { "jquery", "log4j-core", "openssl" }, sbom.Components.Select(c => c.Name));
        Assert.Equal("MIT", sbom.Components[0].License);
        Assert.Equal(Owner, sbom.UploadedById);

        Assert.Single((await Svc.GetThirdPartyAsync(id)).Sboms);
        Assert.Equal(3, (await Svc.GetSbomAsync(id, sbom.Id)).Components.Count);

        await Assert.ThrowsAsync<DataAlreadyExistsException>(() =>
            Svc.ImportSbomAsync(id, new ThirdPartySbomRequest { Document = Bom }, Owner));

        // Without a component name, the product the document describes.
        var other = await NewParty("Other vendor");
        Assert.Equal("Moodle LMS", (await Svc.ImportSbomAsync(other, new ThirdPartySbomRequest { Document = Bom }, Owner)).ComponentName);

        Assert.Empty(FakeOutboundHttpClient.Requests);
    }

    /// <summary>SB2 — a malformed, oversized or nameless document is refused with nothing written; terminated and missing refuse.</summary>
    [Fact]
    public async Task TestSB2_ABadSbomWritesNothing()
    {
        var id = await NewParty();

        var refused = new[]
        {
            "<?xml version=\"1.0\"?><bom/>",
            "{ \"bomFormat\": \"CycloneDX\", \"components\": [ { \"version\": \"1\" } ] }",
            "{\"bomFormat\":\"CycloneDX\",\"x\":\"" + new string('a', ThirdPartyLimits.MaxSbomDocumentBytes) + "\"}",
            "{ \"bomFormat\": \"CycloneDX\", \"components\": [] }"   // no component name given, none described
        };
        foreach (var document in refused)
            await Assert.ThrowsAsync<InvalidParameterException>(() =>
                Svc.ImportSbomAsync(id, new ThirdPartySbomRequest { Document = document }, Owner));

        Assert.Equal(0, Read(ctx => ctx.ThirdPartySboms.Count() + ctx.ThirdPartySbomComponents.Count()));

        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.ImportSbomAsync(9999, new ThirdPartySbomRequest { Document = Bom }, Owner));

        var sbom = await Svc.ImportSbomAsync(id, new ThirdPartySbomRequest { Document = Bom }, Owner);
        await Svc.DeleteSbomAsync(id, sbom.Id, Owner);
        Assert.Equal(0, Read(ctx => ctx.ThirdPartySboms.Count() + ctx.ThirdPartySbomComponents.Count()));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.DeleteSbomAsync(id, sbom.Id, Owner));

        await Svc.UpdateAsync(id, Request(status: ThirdPartyStatus.Terminated), Owner);
        Assert.Equal(ThirdPartiesService.TerminatedRule, (await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Svc.ImportSbomAsync(id, new ThirdPartySbomRequest { Document = Bom }, Owner))).RuleName);
    }

    /// <summary>SB3 — the service cannot fetch: it takes no HTTP client of any kind (S51 D9).</summary>
    [Fact]
    public void TestSB3_TheServiceHasNoWayToTheNetwork()
    {
        var parameters = typeof(ThirdPartiesService).GetConstructors().SelectMany(c => c.GetParameters())
            .Select(p => p.ParameterType).ToList();

        Assert.DoesNotContain(parameters, t => t == typeof(IOutboundHttpClient) || typeof(System.Net.Http.HttpClient).IsAssignableFrom(t)
                                                || t == typeof(System.Net.Http.IHttpClientFactory));
        Assert.Equal(new[] { typeof(Serilog.ILogger), typeof(ServerServices.Services.IDalService), typeof(IContinuityService) }, parameters);
    }

    // --- concentration (T203, T205) --------------------------------------------------------------------------------

    /// <summary>
    /// C1 (T205) — a supplier linked to the portal, to Enrolment itself and to the lab services supports two critical
    /// processes: Enrolment, reached twice, counted once. Another supplier of the portal alone supports one.
    /// </summary>
    [Fact]
    public async Task TestC1_ConcentrationCountsASupplierOncePerDependentCriticalProcess()
    {
        var hosting = await NewParty("Hosting");
        await Svc.LinkAsync(hosting, Service, new ThirdPartyLinkRequest(), Cro);
        await Svc.LinkAsync(hosting, Process, new ThirdPartyLinkRequest(), Cro);
        await Svc.LinkAsync(hosting, Service2, new ThirdPartyLinkRequest(), Cro);
        await Svc.LinkAsync(hosting, Data, new ThirdPartyLinkRequest(), Cro);

        var portal = await NewParty("Portal vendor");
        await Svc.LinkAsync(portal, Service, new ThirdPartyLinkRequest(), Cro);

        var report = await Svc.GetConcentrationAsync();

        Assert.Equal(2, report.CriticalProcessCount);
        Assert.Equal(new[] { ("Hosting", 2), ("Portal vendor", 1) },
            report.Suppliers.Entries.Select(e => (e.Name, e.DependentCriticalProcessCount)));
        Assert.Equal(new[] { Process, Process2 }, report.Suppliers.Entries[0].CriticalProcesses.Select(p => p.EntityId));
        Assert.Equal((1m, (int?)hosting), (report.Suppliers.MaxShare!.Value, report.Suppliers.MostConcentratedThirdPartyId));
        Assert.Equal(0.5m, report.Suppliers.Entries[1].Share);

        var detail = await Svc.GetThirdPartyAsync(hosting);
        Assert.Equal(2, detail.Concentration.DependentCriticalProcessCount);
        Assert.Equal(2, (await Svc.GetThirdPartiesAsync(null, false)).Single(p => p.Id == hosting).DependentCriticalProcessCount);
        Assert.Contains(detail.Findings, f => f.Code == ThirdPartyFindingCode.ExitPlanMissing);
    }

    /// <summary>
    /// C2 — cloud and identity: a cloud named as a sub-processor by a live SaaS carries what the SaaS supplies (the
    /// fourth-party path); an identity provider counts in its dimension; prospective and terminated suppliers are not
    /// listed.
    /// </summary>
    [Fact]
    public async Task TestC2_CloudAndIdentityConcentration()
    {
        var cloud = await NewParty("Hyperscaler", status: ThirdPartyStatus.Prospective, cloud: true);
        var saas = await NewParty("Lab SaaS");
        await Svc.LinkAsync(saas, Service2, new ThirdPartyLinkRequest(), Cro);
        await Svc.SetSubprocessorsAsync(saas, new ThirdPartySubprocessorsRequest
            { Subprocessors = [new ThirdPartySubprocessorRequest { Name = "Hyperscaler", SubprocessorThirdPartyId = cloud }] }, Cro);

        var idp = await NewParty("Identity provider", identity: true);
        await Svc.LinkAsync(idp, Service, new ThirdPartyLinkRequest(), Cro);

        var idle = await NewParty("Still negotiating", status: ThirdPartyStatus.Prospective);
        await Svc.LinkAsync(idle, Service, new ThirdPartyLinkRequest(), Cro);
        var ended = await NewParty("Former vendor");
        await Svc.LinkAsync(ended, Service, new ThirdPartyLinkRequest(), Cro);
        await Svc.UpdateAsync(ended, Request("Former vendor", status: ThirdPartyStatus.Terminated), Cro);

        var report = await Svc.GetConcentrationAsync();

        var cloudEntry = Assert.Single(report.Cloud.Entries);
        Assert.Equal((cloud, 1, true), (cloudEntry.ThirdPartyId, cloudEntry.DependentCriticalProcessCount, cloudEntry.ReachedThroughSubprocessing));
        Assert.Equal(new[] { Process2 }, cloudEntry.CriticalProcesses.Select(p => p.EntityId));

        var identityEntry = Assert.Single(report.Identity.Entries);
        Assert.Equal((idp, 1), (identityEntry.ThirdPartyId, identityEntry.DependentCriticalProcessCount));

        Assert.Equal(new[] { "Hyperscaler", "Identity provider", "Lab SaaS" }, report.Suppliers.Entries.Select(e => e.Name).OrderBy(n => n));
        Assert.DoesNotContain(report.Suppliers.Entries, e => e.ThirdPartyId == idle || e.ThirdPartyId == ended);
    }

    /// <summary>
    /// C3 — the count never depends on who asks: a reader of Unit A sees the organization's cloud carrying the process a
    /// Unit B SaaS supplies through it — counted, the SaaS itself not listed.
    /// </summary>
    [Fact]
    public async Task TestC3_TheCountDoesNotDependOnWhoAsks()
    {
        var cloud = await NewParty("Hyperscaler", cloud: true);
        var hiddenSaas = await NewParty("Unit B SaaS", UnitB);
        await Svc.LinkAsync(hiddenSaas, Service2, new ThirdPartyLinkRequest(), Cro);
        await Svc.SetSubprocessorsAsync(hiddenSaas, new ThirdPartySubprocessorsRequest
            { Subprocessors = [new ThirdPartySubprocessorRequest { Name = "Hyperscaler", SubprocessorThirdPartyId = cloud }] }, Cro);

        var unscoped = await Svc.GetConcentrationAsync();
        ScopeTo(UnitA);
        var scoped = await Svc.GetConcentrationAsync();

        Assert.False(unscoped.IsScopeRestricted);
        Assert.True(scoped.IsScopeRestricted);
        var entry = Assert.Single(scoped.Suppliers.Entries);
        Assert.Equal((cloud, 1), (entry.ThirdPartyId, entry.DependentCriticalProcessCount));
        Assert.Equal(unscoped.Suppliers.Entries.Single(e => e.ThirdPartyId == cloud).DependentCriticalProcessCount,
            entry.DependentCriticalProcessCount);
        Assert.Equal(1, (await Svc.GetThirdPartyAsync(cloud)).Concentration.DependentCriticalProcessCount);
    }

    /// <summary>
    /// C4 — the contracted RTO is checked against what the supplied service requires: Enrolment's BIA RTO of 60 minutes
    /// binds the portal's supplier; 240 contracted is a finding, 60 is not, none is "not contracted".
    /// </summary>
    [Fact]
    public async Task TestC4_TheContractedRtoMeetsTheBia()
    {
        SeedUnscoped(ctx => ctx.BusinessImpactAnalyses.Add(new BusinessImpactAnalysis
        {
            EntityId = Process, RtoMinutes = 60, AssessedAt = DateTime.UtcNow, CreatedAt = DateTime.UtcNow
        }));

        var request = Request("Portal vendor");
        request.ContractedRtoMinutes = 240;
        var id = (await Svc.CreateAsync(request, Cro)).Id;
        var dto = await Svc.LinkAsync(id, Service, new ThirdPartyLinkRequest(), Cro);

        Assert.Equal((60, (int?)Process, "Enrolment"), (dto.ContinuityRequirement.RequiredRtoMinutes!.Value,
            dto.ContinuityRequirement.RtoBindingEntityId, dto.ContinuityRequirement.RtoBindingName));
        Assert.Contains(dto.Findings, f => f.Code == ThirdPartyFindingCode.RtoExceedsRequirement);

        request.ContractedRtoMinutes = 60;
        Assert.DoesNotContain((await Svc.UpdateAsync(id, request, Cro)).Findings,
            f => f.Code is ThirdPartyFindingCode.RtoExceedsRequirement or ThirdPartyFindingCode.RtoNotContracted);

        request.ContractedRtoMinutes = null;
        Assert.Contains((await Svc.UpdateAsync(id, request, Cro)).Findings, f => f.Code == ThirdPartyFindingCode.RtoNotContracted);
    }

    /// <summary>M8 — the methodology panel computes M8 from the concentration (S51 §4.8, amending S49 §3.3).</summary>
    [Fact]
    public async Task TestM8_ThePanelComputesThirdPartyConcentration()
    {
        var hosting = await NewParty("Hosting");
        await Svc.LinkAsync(hosting, Service, new ThirdPartyLinkRequest(), Cro);
        var idp = await NewParty("Identity provider", identity: true);
        await Svc.LinkAsync(idp, Service2, new ThirdPartyLinkRequest(), Cro);

        var m8 = (await GetService<IMethodologyMetricsService>().GetAsync()).Metrics
            .Single(m => m.Metric == MethodologyMetric.ThirdPartyConcentration);

        Assert.Equal((MetricAvailability.Available, "9.10", "M8"), (m8.Availability, m8.Stage, m8.Code));
        Assert.Equal((0.5, 1d, 2d), (m8.Value!.Value, m8.Numerator!.Value, m8.Denominator!.Value));
        Assert.Contains("Hosting supports 1 of 2", m8.Detail);
        Assert.Contains("Most concentrated identity provider: Identity provider", m8.Detail);
        Assert.Contains("No cloud provider is registered as active", m8.Detail);
    }

    // --- history ---------------------------------------------------------------------------------------------------

    /// <summary>
    /// HI1 — the history reads the record and what it declares, after the visibility check; another entity's third party is
    /// not found, and the limit is bounded.
    /// </summary>
    [Fact]
    public async Task TestHI1_TheHistoryIsScopeChecked()
    {
        var id = await NewParty("Org-wide");
        await Svc.LinkAsync(id, Service, new ThirdPartyLinkRequest(), Owner);
        var theirs = await NewParty("Unit B supplier", UnitB);

        var history = await Svc.GetHistoryAsync(id, 500);
        Assert.Contains(history, row => row.EntityType == nameof(ThirdParty) && row.EntityId == id);
        Assert.Contains(history, row => row.EntityType == nameof(ThirdPartyLink) && row.UserId == Owner);
        Assert.Equal(history.OrderByDescending(r => r.OccurredAt).ThenByDescending(r => r.Id).Select(r => r.Id), history.Select(r => r.Id));

        ScopeTo(UnitA);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetHistoryAsync(theirs, 500));
        await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.GetHistoryAsync(id, 0));
        await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.GetHistoryAsync(id, ThirdPartiesService.MaxHistoryLimit + 1));
    }

    /// <summary>Y0 — nothing in the service lifts the query filters; the unscoped reads are the explicit system context.</summary>
    [Fact]
    public void TestY0_NoQueryFilterIsLifted()
    {
        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "ServerServices", "Governance", "ThirdPartiesService.cs"));

        Assert.DoesNotContain("IgnoreQueryFilters", source);
        Assert.DoesNotContain("HttpClient", source);
        Assert.DoesNotContain("System.IO", source);
    }
}

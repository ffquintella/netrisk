using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.DataCatalogue;
using Model.DecisionCycle;
using Model.Exceptions;
using Model.ThirdParties;
using ServerServices.Governance;
using ServerServices.Interfaces;
using ServerServices.Security;
using Xunit;

namespace ServerServices.Tests.Track9;

/// <summary>
/// Stage 9.11 (S52 §8) — the LGPD data catalogue end to end on the in-memory provider: the record and its guards (C1–C5),
/// the two edge cases the methodology names for this stage (T210a — sensitive data with no declared legal basis is a
/// finding; T210b/T210c — an expired retention signals and nothing deletes), the transfer through processors (TR1, TR2), the
/// RIPD (DP1–DP6), the legal requirements (LR1–LR3), the requirements of a risk (RL1–RL3) and the histories.
///
/// The organization is <see cref="DecisionCycleTestBase"/>'s — data record 50 "Student records", process 10, service 20,
/// units 100 and 200, the administrator 1 and the third-line auditor 9 —, plus data record 51 "Health records" inside the
/// data group 52 "Student data".
/// </summary>
[TestSubject(typeof(DataCatalogueService))]
public class DataCatalogueServiceInMemoryTest : DecisionCycleTestBase
{
    private const int Health = 51;
    private const int Group = 52;

    private IDataCatalogueService Svc => GetService<IDataCatalogueService>();

    private IThirdPartiesService ThirdParties => GetService<IThirdPartiesService>();

    public DataCatalogueServiceInMemoryTest()
    {
        SeedUnscoped(ctx =>
        {
            AddEntity(ctx, Group, "organizationDataGroup", "Student data");
            AddEntity(ctx, Health, "organizationData", "Health records");
        });
        SeedUnscoped(ctx => ctx.Entities.Single(e => e.Id == Health).Parent = Group);
    }

    // --- builders ------------------------------------------------------------------------------------------------------

    private static DataCatalogueEntryRequest Sensitive(LgpdLegalBasis? basis = LgpdLegalBasis.Art11HealthProtection) => new()
    {
        PersonalData = PersonalDataCategory.SensitivePersonal, InvolvesMinors = false,
        DataSubjects = "Undergraduate students", DataCategories = "Health conditions, allergies, vaccination",
        RetentionPeriodMonths = 60, RetentionTrigger = "End of enrolment", RetentionBasis = "Institutional norm",
        RetentionReviewDueAt = DateTime.UtcNow.AddYears(2), InternationalTransfer = false,
        Purposes = [new DataCataloguePurposeRequest { Purpose = "Student health care", LegalBasis = basis }],
        Locations = [new DataCatalogueLocationRequest { Country = "br", Region = "São Paulo", Purpose = DataLocationPurpose.Storage }]
    };

    private static DataCatalogueEntryRequest Personal() => new()
    {
        PersonalData = PersonalDataCategory.Personal, DataSubjects = "Applicants", RetentionPeriodMonths = 24,
        InternationalTransfer = false,
        Purposes = [new DataCataloguePurposeRequest { Purpose = "Admission", LegalBasis = LgpdLegalBasis.Art7Contract }],
        Locations = [new DataCatalogueLocationRequest { Country = "BR", Purpose = DataLocationPurpose.Storage }]
    };

    private static LegalRequirementRequest Requirement(string code = "LGPD art. 46",
        LegalRequirementKind kind = LegalRequirementKind.Law, int? thirdPartyId = null) => new()
    {
        Code = code, Title = "Security measures", Kind = kind, ThirdPartyId = thirdPartyId
    };

    private static DpiaRequest Complete() => new()
    {
        Title = "RIPD — student health", Summary = "Processing of health data for student care; measures in place.",
        ResidualRisk = DpiaResidualRisk.Low, PerformedAt = DateTime.UtcNow.AddDays(-5),
        NextReviewDueAt = DateTime.UtcNow.AddYears(1)
    };

    private async Task<int> NewParty(string name, int? unit = null, params (string Country, ThirdPartyDataLocationPurpose Purpose)[] locations)
    {
        var id = (await ThirdParties.CreateAsync(new ThirdPartyRequest
            { Name = name, EntityId = unit, Status = ThirdPartyStatus.Active }, Cro)).Id;
        if (locations.Length > 0)
            await ThirdParties.SetDataLocationsAsync(id, new ThirdPartyDataLocationsRequest
            {
                Locations = locations.Select(l => new ThirdPartyDataLocationRequest { Country = l.Country, Purpose = l.Purpose })
                    .ToList()
            }, Cro);
        return id;
    }

    private static List<DataCatalogueFindingCode> Codes(DataRecordDto record) => record.Findings.Select(f => f.Code).ToList();

    private int EntryCount() => Read(ctx => ctx.DataCatalogueEntries.Count());

    // --- C1–C5: the record (T207) ----------------------------------------------------------------------------------------

    /// <summary>C1 — a catalogued record reads back whole, with the article of each basis; a re-save keeps the ids and the trail.</summary>
    [Fact]
    public async Task TestC1_ACataloguedRecordIsReadBackAndKeepsItsIds()
    {
        var saved = await Svc.SaveRecordAsync(Data, Sensitive(), Cro);

        Assert.True(saved.Catalogued);
        Assert.Equal(("Student records", PersonalDataCategory.SensitivePersonal, 60), (saved.Name, saved.PersonalData,
            saved.RetentionPeriodMonths));
        var purpose = Assert.Single(saved.Purposes);
        Assert.Equal(("LGPD art. 11, II, f", LgpdLegalBasis.Art11HealthProtection), (purpose.LegalBasisArticle, purpose.LegalBasis));
        var location = Assert.Single(saved.Locations);
        Assert.Equal(("BR", "São Paulo"), (location.Country, location.Region));
        Assert.Equal(Cro, saved.CreatedById);

        var again = Sensitive();
        again.Purposes!.Add(new DataCataloguePurposeRequest { Purpose = "Research", LegalBasis = LgpdLegalBasis.Art11Research });
        again.Purposes[0].Purpose = "STUDENT HEALTH CARE";
        again.Locations![0].Region = "Rio de Janeiro";
        var resaved = await Svc.SaveRecordAsync(Data, again, Owner);

        Assert.Equal(purpose.Id, resaved.Purposes.Single(p => p.Purpose == "STUDENT HEALTH CARE").Id);
        Assert.Equal(location.Id, resaved.Locations.Single().Id);
        Assert.Equal("Rio de Janeiro", resaved.Locations.Single().Region);
        Assert.Equal(2, resaved.Purposes.Count);
        Assert.Equal(Owner, resaved.UpdatedById);
        Assert.Equal(1, EntryCount());

        var trail = Audit(nameof(DataCatalogueEntry));
        Assert.Contains(trail, a => a.Action == AuditLogAction.Create && a.UserId == Cro);
        Assert.Contains(Audit(nameof(DataCataloguePurpose)), a => a.Action == AuditLogAction.Create && a.UserId == Owner);

        var history = await Svc.GetRecordHistoryAsync(Data, 100);
        Assert.Contains(history, a => a.EntityType == nameof(DataCatalogueEntry));
        Assert.Contains(history, a => a.EntityType == nameof(DataCataloguePurpose));
        Assert.Contains(history, a => a.EntityType == nameof(DataCatalogueLocation));
    }

    /// <summary>C2 — every invalid field is refused naming it, and nothing is written.</summary>
    [Fact]
    public async Task TestC2_AnInvalidRecordIsRefusedNamingTheField()
    {
        var cases = new (Action<DataCatalogueEntryRequest> Break, string Field)[]
        {
            (r => r.PersonalData = (PersonalDataCategory)9, "PersonalData"),
            (r => r.RetentionPeriodMonths = -1, "RetentionPeriodMonths"),
            (r => r.RetentionPeriodMonths = DataCatalogueLimits.MaxRetentionMonths + 1, "RetentionPeriodMonths"),
            (r => r.RetentionRequirementId = 0, "RetentionRequirementId"),
            (r => r.RetentionReviewedAt = DateTime.UtcNow.AddDays(1), "RetentionReviewedAt"),
            (r => r.TransferMechanism = (InternationalTransferMechanism)13, "TransferMechanism"),
            (r => r.TransferMechanism = InternationalTransferMechanism.StandardContractualClauses, "TransferMechanism"),
            (r => r.DataSubjects = new string('s', DataCatalogueLimits.MaxDataSubjectsLength + 1), "DataSubjects"),
            (r => r.Notes = new string('n', DataCatalogueLimits.MaxNotesLength + 1), "Notes"),
            (r => r.Purposes = null, "Purposes"),
            (r => r.Purposes!.Add(new DataCataloguePurposeRequest { Purpose = " " }), "Purpose"),
            (r => r.Purposes!.Add(new DataCataloguePurposeRequest { Purpose = "student health CARE" }), "Purpose"),
            (r => r.Purposes!.Add(new DataCataloguePurposeRequest { Purpose = "Stúdent health care" }), "Purpose"),
            (r => r.Purposes!.Add(new DataCataloguePurposeRequest { Purpose = "X", LegalBasis = (LgpdLegalBasis)19 }), "LegalBasis"),
            (r => r.Purposes!.Add(new DataCataloguePurposeRequest { Purpose = "X", LegalRequirementId = -2 }), "LegalRequirementId"),
            (r => r.Purposes = Enumerable.Range(0, DataCatalogueLimits.MaxPurposes + 1)
                .Select(i => new DataCataloguePurposeRequest { Purpose = $"P{i}" }).ToList(), "Purposes"),
            (r => r.Locations = null, "Locations"),
            (r => r.Locations!.Add(new DataCatalogueLocationRequest { Country = "BRA", Purpose = DataLocationPurpose.Backup }), "Country"),
            (r => r.Locations!.Add(new DataCatalogueLocationRequest { Purpose = DataLocationPurpose.Backup }), "Country"),
            (r => r.Locations!.Add(new DataCatalogueLocationRequest { Country = "BR", Purpose = DataLocationPurpose.Storage }), "Country"),
            (r => r.Locations!.Add(new DataCatalogueLocationRequest { Country = "US" }), "Purpose")
        };

        foreach (var (breakIt, field) in cases)
        {
            var request = Sensitive();
            breakIt(request);

            var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.SaveRecordAsync(Data, request, Cro));
            Assert.Equal(field, ex.ParameterName);
        }

        Assert.Equal(0, EntryCount());

        // A cited requirement must exist.
        var missing = Sensitive();
        missing.RetentionRequirementId = 999;
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.SaveRecordAsync(Data, missing, Cro));
        Assert.Equal(0, EntryCount());
    }

    /// <summary>C3 — the node must exist (404) and be a data record (422), to read and to write.</summary>
    [Fact]
    public async Task TestC3_TheTargetIsADataRecord()
    {
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.SaveRecordAsync(9999, Sensitive(), Cro));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetRecordAsync(9999));

        foreach (var notData in new[] { Process, Service, Group, UnitA })
        {
            Assert.Equal(DataCatalogueService.TargetRule,
                (await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.SaveRecordAsync(notData, Sensitive(), Cro))).RuleName);
            Assert.Equal(DataCatalogueService.TargetRule,
                (await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.GetRecordAsync(notData))).RuleName);
        }

        Assert.Equal(0, EntryCount());
    }

    /// <summary>C4 — every write to the catalogue, the requirements and the RIPDs needs global scope; nothing is written.</summary>
    [Fact]
    public async Task TestC4_EveryWriteNeedsGlobalScope()
    {
        var requirement = (await Svc.CreateRequirementAsync(Requirement(), Cro)).Id;
        var dpia = (await Svc.CreateDpiaAsync(Complete(), Cro)).Id;
        await Svc.LinkDpiaAsync(dpia, Data, Cro);
        var audit = Read(ctx => ctx.AuditLogs.Count());

        ScopeTo(UnitA, UnitB);

        var writes = new (string Name, Func<Task> Call)[]
        {
            ("SaveRecord", () => Svc.SaveRecordAsync(Data, Sensitive(), Author)),
            ("CreateRequirement", () => Svc.CreateRequirementAsync(Requirement("New"), Author)),
            ("UpdateRequirement", () => Svc.UpdateRequirementAsync(requirement, Requirement("Changed"), Author)),
            ("DeleteRequirement", () => Svc.DeleteRequirementAsync(requirement, Author)),
            ("CreateDpia", () => Svc.CreateDpiaAsync(Complete(), Author)),
            ("UpdateDpia", () => Svc.UpdateDpiaAsync(dpia, Complete(), Author)),
            ("LinkDpia", () => Svc.LinkDpiaAsync(dpia, Process, Author)),
            ("UnlinkDpia", () => Svc.UnlinkDpiaAsync(dpia, Data, Author)),
            ("ApproveDpia", () => Svc.ApproveDpiaAsync(dpia, Author)),
            ("RetireDpia", () => Svc.RetireDpiaAsync(dpia, new DpiaRetireRequest { Reason = "Superseded by a newer one." }, Author)),
            // A missing target gets the same refusal as an existing one: the scope is checked first.
            ("SaveRecord#missing", () => Svc.SaveRecordAsync(9999, Sensitive(), Author))
        };

        foreach (var (name, call) in writes)
        {
            var ex = await Assert.ThrowsAsync<PermissionInvalidException>(call);
            Assert.True(ex.Permission == ContinuityAccess.GlobalScope, name);
        }

        ScopeToEverything();
        Assert.Equal(0, EntryCount());
        Assert.Equal(audit, Read(ctx => ctx.AuditLogs.Count()));
        Assert.Equal(DpiaStatus.Draft, (await Svc.GetDpiaAsync(dpia)).Status);

        // Reads are the organization's: a scoped reader sees the catalogue.
        await Svc.SaveRecordAsync(Data, Sensitive(), Cro);
        ScopeTo(UnitA);
        Assert.True((await Svc.GetRecordAsync(Data)).Catalogued);
        Assert.Single(await Svc.GetRequirementsAsync());
    }

    /// <summary>C5 — kinds, not values: a text carrying an e-mail address or a CPF is refused without being echoed.</summary>
    [Fact]
    public async Task TestC5_APersonalValueIsRefusedWithoutEcho()
    {
        var cases = new (Action<DataCatalogueEntryRequest> Break, string Field)[]
        {
            (r => r.DataSubjects = "Students like ana.lima@fgv.br", "DataSubjects"),
            (r => r.DataCategories = "CPF 123.456.789-09", "DataCategories"),
            (r => r.Notes = "Ask maria@fgv.br", "Notes"),
            (r => r.RetentionBasis = "Per joao@x.com.br", "RetentionBasis"),
            (r => r.Purposes![0].Purpose = "Care of 987.654.321-00", "Purpose"),
            (r => r.Purposes![0].BasisReference = "consent signed by a@b.co", "BasisReference"),
            (r => r.Locations![0].Region = "Server of ops@fgv.br", "Region")
        };

        foreach (var (breakIt, field) in cases)
        {
            var request = Sensitive();
            breakIt(request);

            var ex = await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.SaveRecordAsync(Data, request, Cro));
            Assert.Equal(field, ex.ParameterName);
            Assert.DoesNotContain("@", ex.Message.Replace("\"institutional e-mail\"", ""));
            Assert.DoesNotContain("123.456", ex.Message);
        }

        Assert.Equal(0, EntryCount());
    }

    // --- T210: the methodology's edge cases ------------------------------------------------------------------------------

    /// <summary>
    /// T210a — sensitive personal data with no declared legal basis is a finding, on the record and in the compliance list
    /// filtered by findings; it is never read as compliant by omission. Declaring the basis clears it.
    /// </summary>
    [Fact]
    public async Task TestT210a_SensitiveDataWithNoDeclaredLegalBasisIsAFinding()
    {
        var approved = (await Svc.CreateDpiaAsync(Complete(), Cro)).Id;
        await Svc.LinkDpiaAsync(approved, Data, Cro);
        await Svc.ApproveDpiaAsync(approved, Cro);

        var record = await Svc.SaveRecordAsync(Data, Sensitive(basis: null), Cro);

        var finding = Assert.Single(record.Findings);
        Assert.Equal(DataCatalogueFindingCode.LegalBasisMissing, finding.Code);
        Assert.Contains("art. 11", finding.Message);

        var flagged = await Svc.GetRecordsAsync(withFindingsOnly: true);
        Assert.Contains(flagged, r => r.EntityId == Data && r.FindingCodes.Contains(DataCatalogueFindingCode.LegalBasisMissing));

        var none = Sensitive();
        none.Purposes = [];
        Assert.Equal([DataCatalogueFindingCode.PurposeMissing, DataCatalogueFindingCode.LegalBasisMissing],
            Codes(await Svc.SaveRecordAsync(Data, none, Cro)));

        // Declared: the finding goes, and the record leaves the filtered list.
        Assert.Empty((await Svc.SaveRecordAsync(Data, Sensitive(), Cro)).Findings);
        Assert.DoesNotContain(await Svc.GetRecordsAsync(withFindingsOnly: true), r => r.EntityId == Data);
        Assert.Contains(await Svc.GetRecordsAsync(withFindingsOnly: false), r => r.EntityId == Data && r.FindingCodes.Count == 0);
    }

    /// <summary>
    /// T210b — an expired retention signals and nothing is deleted: the finding is there with its date; reading the record,
    /// the list and the risk view writes nothing at all; the catalogue, its purposes and locations and the data node are
    /// still there; and the trail has no deletion.
    /// </summary>
    [Fact]
    public async Task TestT210b_AnExpiredRetentionSignalsAndNothingIsDeleted()
    {
        var expired = Personal();
        expired.RetentionReviewDueAt = DateTime.UtcNow.AddDays(-30);
        await Svc.SaveRecordAsync(Data, expired, Cro);
        AddRisk(1, UnitA);
        AddLink(1, Data);

        var entries = Read(ctx => ctx.DataCatalogueEntries.Count());
        var purposes = Read(ctx => ctx.DataCataloguePurposes.Count());
        var locations = Read(ctx => ctx.DataCatalogueLocations.Count());
        var saves = SaveChangesCount;

        var record = await Svc.GetRecordAsync(Data);
        var list = await Svc.GetRecordsAsync(withFindingsOnly: true);
        var risk = await Svc.GetRiskComplianceAsync(1);

        Assert.Equal(saves, SaveChangesCount);

        var finding = Assert.Single(record.Findings);
        Assert.Equal(DataCatalogueFindingCode.RetentionExpired, finding.Code);
        Assert.Contains("Nothing is deleted", finding.Message);
        Assert.Contains(list, r => r.EntityId == Data && r.FindingCodes.Contains(DataCatalogueFindingCode.RetentionExpired));
        Assert.Contains(DataCatalogueFindingCode.RetentionExpired, risk.DataRecords.Single().FindingCodes);

        Assert.Equal(entries, Read(ctx => ctx.DataCatalogueEntries.Count()));
        Assert.Equal(purposes, Read(ctx => ctx.DataCataloguePurposes.Count()));
        Assert.Equal(locations, Read(ctx => ctx.DataCatalogueLocations.Count()));
        Assert.True(Read(ctx => ctx.Entities.Any(e => e.Id == Data)));
        Assert.True((await Svc.GetRecordAsync(Data)).Catalogued);
        Assert.DoesNotContain(Read(ctx => ctx.AuditLogs.ToList()), a => a.Action == AuditLogAction.Delete &&
            DataCatalogueService.TrailTypes.Contains(a.EntityType));
    }

    /// <summary>
    /// T210c — no path of the service deletes a catalogue: its only deleting operations are a requirement nothing cites and
    /// the links (a RIPD's, a risk's), and its source never removes a catalogue entry, a purpose or a location by itself
    /// except to replace the lists a person sends.
    /// </summary>
    [Fact]
    public void TestT210c_NoPathDeletesACatalogue()
    {
        var deleting = typeof(IDataCatalogueService).GetMethods()
            .Select(m => m.Name)
            .Where(n => n.StartsWith("Delete", StringComparison.Ordinal) || n.StartsWith("Remove", StringComparison.Ordinal) ||
                        n.StartsWith("Purge", StringComparison.Ordinal) || n.StartsWith("Unlink", StringComparison.Ordinal) ||
                        n.Contains("Expire", StringComparison.Ordinal) || n.Contains("Retention", StringComparison.Ordinal))
            .OrderBy(n => n)
            .ToList();
        Assert.Equal(["DeleteRequirementAsync", "UnlinkDpiaAsync", "UnlinkRiskRequirementAsync"], deleting);

        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "ServerServices", "Governance", "DataCatalogueService.cs"));
        Assert.DoesNotContain("DataCatalogueEntries.Remove", source);
        Assert.DoesNotContain("ExecuteDelete", source);
        Assert.DoesNotContain("RemoveRange", source);
        Assert.DoesNotContain("Entities.Remove", source);
    }

    // --- TR1–TR2: processors and transfer (S52 §4.7, D11) -----------------------------------------------------------------

    /// <summary>
    /// TR1 — the transfer reaches through the processors: a live supplier keeping the data in Ireland and a sub-processor in
    /// the United States make the record a transfer, whoever reads; the supplier the reader cannot see is counted, never
    /// named; a terminated supplier processes nothing.
    /// </summary>
    [Fact]
    public async Task TestTR1_TheTransferReachesThroughProcessors()
    {
        var visible = await NewParty("Campus SaaS", null, ("IE", ThirdPartyDataLocationPurpose.Storage));
        await ThirdParties.SetSubprocessorsAsync(visible, new ThirdPartySubprocessorsRequest
        {
            Subprocessors = [new ThirdPartySubprocessorRequest { Name = "Hyperscaler", Country = "US", ProcessesPersonalData = true }]
        }, Cro);
        await ThirdParties.LinkAsync(visible, Data, new ThirdPartyLinkRequest(), Cro);

        var hidden = await NewParty("Unit B mailer", UnitB, ("CA", ThirdPartyDataLocationPurpose.Processing));
        await ThirdParties.LinkAsync(hidden, Data, new ThirdPartyLinkRequest(), Cro);

        var ended = await NewParty("Old vendor", null, ("JP", ThirdPartyDataLocationPurpose.Backup));
        await ThirdParties.LinkAsync(ended, Data, new ThirdPartyLinkRequest(), Cro);
        await ThirdParties.UpdateAsync(ended, new ThirdPartyRequest { Name = "Old vendor", Status = ThirdPartyStatus.Terminated }, Cro);

        await Svc.SaveRecordAsync(Data, Personal(), Cro);

        var all = await Svc.GetRecordAsync(Data);
        Assert.Equal(["CA", "IE", "US"], all.TransferCountries);
        Assert.Contains(DataCatalogueFindingCode.InternationalTransferUndeclared, Codes(all));
        Assert.Equal(3, all.Processors.Count);
        Assert.Equal(0, all.HiddenProcessorCount);

        ScopeTo(UnitA);
        var scoped = await Svc.GetRecordAsync(Data);
        Assert.Equal(all.TransferCountries, scoped.TransferCountries);
        Assert.Equal(Codes(all), Codes(scoped));
        Assert.Equal(["Campus SaaS", "Old vendor"], scoped.Processors.Select(p => p.Name));
        Assert.Equal(1, scoped.HiddenProcessorCount);
        Assert.DoesNotContain(scoped.Processors, p => p.ThirdPartyId == hidden);

        // Declared with its safeguard: the finding goes.
        ScopeToEverything();
        var declared = Personal();
        declared.InternationalTransfer = true;
        declared.TransferMechanism = InternationalTransferMechanism.StandardContractualClauses;
        Assert.Empty((await Svc.SaveRecordAsync(Data, declared, Cro)).Findings);
    }

    /// <summary>TR2 — a supplier linked to the record's group processes the record too.</summary>
    [Fact]
    public async Task TestTR2_AProcessorOfTheGroupProcessesTheRecord()
    {
        var party = await NewParty("Records archive", null, ("US", ThirdPartyDataLocationPurpose.Backup));
        await ThirdParties.LinkAsync(party, Group, new ThirdPartyLinkRequest(), Cro);

        await Svc.SaveRecordAsync(Health, Personal(), Cro);

        var record = await Svc.GetRecordAsync(Health);
        var processor = Assert.Single(record.Processors);
        Assert.True(processor.ThroughGroup);
        Assert.Equal(["US"], record.TransferCountries);
        Assert.Empty((await Svc.GetRecordAsync(Data)).Processors);
    }

    // --- DP1–DP6: the RIPD (T208) ------------------------------------------------------------------------------------------

    /// <summary>DP1 — a RIPD is drafted and linked to a data record and a process, the kind derived; other targets are refused.</summary>
    [Fact]
    public async Task TestDP1_ADraftIsLinkedToDataAndProcess()
    {
        var dpia = await Svc.CreateDpiaAsync(new DpiaRequest { Title = "  RIPD — enrolment " }, Cro);
        Assert.Equal((DpiaStatus.Draft, "RIPD — enrolment"), (dpia.Status, dpia.Title));

        await Svc.LinkDpiaAsync(dpia.Id, Data, Cro);
        var linked = await Svc.LinkDpiaAsync(dpia.Id, Process, Cro);
        await Svc.LinkDpiaAsync(dpia.Id, Process, Cro);

        Assert.Equal([(Process, DpiaLinkKind.BusinessProcess, "Enrolment"), (Data, DpiaLinkKind.DataRecord, "Student records")],
            linked.Links.OrderByDescending(l => l.Kind).Select(l => (l.EntityId, l.Kind, l.EntityName)).ToList());
        Assert.Equal(2, (await Svc.GetDpiaAsync(dpia.Id)).Links.Count);

        foreach (var other in new[] { Service, PortalApp, Group })
            Assert.Equal(DataCatalogueService.DpiaLinkTargetRule,
                (await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.LinkDpiaAsync(dpia.Id, other, Cro))).RuleName);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.LinkDpiaAsync(dpia.Id, 9999, Cro));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.LinkDpiaAsync(9999, Data, Cro));

        await Svc.UnlinkDpiaAsync(dpia.Id, Process, Cro);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.UnlinkDpiaAsync(dpia.Id, Process, Cro));
        Assert.Single((await Svc.GetDpiaAsync(dpia.Id)).Links);

        var invalid = new (DpiaRequest Request, string Field)[]
        {
            (new DpiaRequest { Title = " " }, "Title"),
            (new DpiaRequest { Title = "T", ResidualRisk = (DpiaResidualRisk)4 }, "ResidualRisk"),
            (new DpiaRequest { Title = "T", PerformedAt = DateTime.UtcNow.AddDays(1) }, "PerformedAt"),
            (new DpiaRequest { Title = "T", PerformedAt = DateTime.UtcNow.AddDays(-1), NextReviewDueAt = DateTime.UtcNow.AddDays(-2) },
                "NextReviewDueAt"),
            (new DpiaRequest { Title = "T", Summary = new string('s', DataCatalogueLimits.MaxDpiaSummaryLength + 1) }, "Summary")
        };
        foreach (var (request, field) in invalid)
            Assert.Equal(field, (await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.CreateDpiaAsync(request, Cro)))
                .ParameterName);
    }

    /// <summary>DP2 — an incomplete draft is not approved, naming what is missing; a complete one is approved by its approver.</summary>
    [Fact]
    public async Task TestDP2_ApprovalNeedsACompleteDraft()
    {
        var dpia = (await Svc.CreateDpiaAsync(new DpiaRequest { Title = "RIPD" }, Cro)).Id;
        await Svc.LinkDpiaAsync(dpia, Process, Cro);

        var refused = await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.ApproveDpiaAsync(dpia, Cro));
        Assert.Equal(DataCatalogueService.DpiaIncompleteRule, refused.RuleName);
        Assert.Contains("the date it was performed", refused.Message);
        Assert.Contains("the residual risk", refused.Message);
        Assert.Contains("a summary or a reference", refused.Message);
        Assert.Contains("a data record it covers", refused.Message);
        Assert.Equal(DpiaStatus.Draft, (await Svc.GetDpiaAsync(dpia)).Status);

        await Svc.LinkDpiaAsync(dpia, Data, Cro);

        // Complete but already due for review: it would be overdue the moment it is approved (review fix).
        var stale = Complete();
        stale.PerformedAt = DateTime.UtcNow.AddDays(-30);
        stale.NextReviewDueAt = DateTime.UtcNow.AddDays(-1);
        await Svc.UpdateDpiaAsync(dpia, stale, Cro);
        var overdue = await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.ApproveDpiaAsync(dpia, Cro));
        Assert.Equal(DataCatalogueService.DpiaIncompleteRule, overdue.RuleName);
        Assert.Contains("a next review date that has not passed already", overdue.Message);
        Assert.DoesNotContain("a data record", overdue.Message);

        await Svc.UpdateDpiaAsync(dpia, Complete(), Cro);

        var approved = await Svc.ApproveDpiaAsync(dpia, Owner);
        Assert.Equal((DpiaStatus.Approved, Owner, "owner"), (approved.Status, approved.ApprovedById, approved.ApprovedByName));
        Assert.NotNull(approved.ApprovedAt);
        Assert.Contains(Audit(nameof(Dpia)), a => a.Field == nameof(Dpia.Status) && a.UserId == Owner);
    }

    /// <summary>DP3 — the third line does not approve a RIPD, an administrator auditor included.</summary>
    [Fact]
    public async Task TestDP3_TheThirdLineDoesNotApprove()
    {
        var dpia = (await Svc.CreateDpiaAsync(Complete(), Cro)).Id;
        await Svc.LinkDpiaAsync(dpia, Data, Cro);

        Assert.Equal(ThirdLineAssurance.CannotApproveRule,
            (await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.ApproveDpiaAsync(dpia, Auditor))).RuleName);
        Assert.Equal(DpiaStatus.Draft, (await Svc.GetDpiaAsync(dpia)).Status);
    }

    /// <summary>DP4 — an approved RIPD is frozen: no update, no link, no unlink, no second approval.</summary>
    [Fact]
    public async Task TestDP4_AnApprovedRipdIsFrozen()
    {
        var dpia = (await Svc.CreateDpiaAsync(Complete(), Cro)).Id;
        await Svc.LinkDpiaAsync(dpia, Data, Cro);
        await Svc.ApproveDpiaAsync(dpia, Cro);

        var frozen = new Func<Task>[]
        {
            () => Svc.UpdateDpiaAsync(dpia, Complete(), Cro),
            () => Svc.LinkDpiaAsync(dpia, Process, Cro),
            () => Svc.UnlinkDpiaAsync(dpia, Data, Cro),
            () => Svc.ApproveDpiaAsync(dpia, Cro)
        };
        foreach (var call in frozen)
            Assert.Equal(DataCatalogueService.DpiaNotDraftRule, (await Assert.ThrowsAsync<RuleBrokenException>(call)).RuleName);

        Assert.Single((await Svc.GetDpiaAsync(dpia)).Links);
    }

    /// <summary>DP5 — retiring needs a reason; a retired RIPD is kept, counts for nothing and is not retired twice.</summary>
    [Fact]
    public async Task TestDP5_RetirementNeedsAReasonAndHappensOnce()
    {
        var dpia = (await Svc.CreateDpiaAsync(Complete(), Cro)).Id;

        Assert.Equal("Reason", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.RetireDpiaAsync(dpia, new DpiaRetireRequest { Reason = "short" }, Cro))).ParameterName);
        Assert.Equal("Reason", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.RetireDpiaAsync(dpia, new DpiaRetireRequest(), Cro))).ParameterName);

        var retired = await Svc.RetireDpiaAsync(dpia, new DpiaRetireRequest { Reason = "Superseded by the 2027 review." }, Owner);
        Assert.Equal((DpiaStatus.Retired, Owner, "Superseded by the 2027 review."),
            (retired.Status, retired.RetiredById, retired.RetireReason));

        Assert.Equal(DataCatalogueService.DpiaRetiredRule, (await Assert.ThrowsAsync<RuleBrokenException>(() =>
            Svc.RetireDpiaAsync(dpia, new DpiaRetireRequest { Reason = "Retired again by mistake." }, Cro))).RuleName);
        Assert.Equal(DataCatalogueService.DpiaRetiredRule,
            (await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.UpdateDpiaAsync(dpia, Complete(), Cro))).RuleName);

        Assert.Single(await Svc.GetDpiasAsync(DpiaStatus.Retired));
        Assert.Empty(await Svc.GetDpiasAsync(DpiaStatus.Draft));
        await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.GetDpiasAsync((DpiaStatus)7));
    }

    /// <summary>
    /// DP6 — high-risk data needs an approved RIPD: none, a draft or a retired one leave <c>DpiaMissing</c>; the approved one
    /// clears it and is listed on the record; past its review it is overdue.
    /// </summary>
    [Fact]
    public async Task TestDP6_AnApprovedRipdCoversHighRiskData()
    {
        await Svc.SaveRecordAsync(Data, Sensitive(), Cro);
        Assert.Equal([DataCatalogueFindingCode.DpiaMissing], Codes(await Svc.GetRecordAsync(Data)));

        var draft = (await Svc.CreateDpiaAsync(Complete(), Cro)).Id;
        await Svc.LinkDpiaAsync(draft, Data, Cro);
        Assert.Equal([DataCatalogueFindingCode.DpiaMissing], Codes(await Svc.GetRecordAsync(Data)));

        await Svc.ApproveDpiaAsync(draft, Cro);
        var covered = await Svc.GetRecordAsync(Data);
        Assert.Empty(covered.Findings);
        Assert.Equal((draft, DpiaStatus.Approved), (covered.Dpias.Single().Id, covered.Dpias.Single().Status));
        Assert.Equal(1, (await Svc.GetRecordsAsync(false)).Single(r => r.EntityId == Data).ApprovedDpiaCount);

        // The approved one past its review date.
        SeedUnscoped(ctx => ctx.Dpias.Single(d => d.Id == draft).NextReviewDueAt = DateTime.UtcNow.AddDays(-1));
        Assert.Equal([DataCatalogueFindingCode.DpiaReviewOverdue], Codes(await Svc.GetRecordAsync(Data)));
        Assert.True((await Svc.GetDpiaAsync(draft)).ReviewOverdue);

        await Svc.RetireDpiaAsync(draft, new DpiaRetireRequest { Reason = "Superseded by the next review." }, Cro);
        Assert.Equal([DataCatalogueFindingCode.DpiaMissing], Codes(await Svc.GetRecordAsync(Data)));
    }

    // --- LR1–LR3: the legal requirements (T209) ----------------------------------------------------------------------------

    /// <summary>LR1 — a requirement is catalogued once by code, case-insensitively; only a contract names a third party.</summary>
    [Fact]
    public async Task TestLR1_RequirementsAreCataloguedOnce()
    {
        var law = await Svc.CreateRequirementAsync(Requirement("  LGPD art. 46 "), Cro);
        Assert.Equal(("LGPD art. 46", LegalRequirementKind.Law), (law.Code, law.Kind));

        await Assert.ThrowsAsync<DataAlreadyExistsException>(() => Svc.CreateRequirementAsync(Requirement("lgpd ART. 46"), Cro));

        var party = await NewParty("Campus SaaS");
        Assert.Equal("ThirdPartyId", (await Assert.ThrowsAsync<InvalidParameterException>(() =>
            Svc.CreateRequirementAsync(Requirement("Law with party", LegalRequirementKind.Law, party), Cro))).ParameterName);
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Svc.CreateRequirementAsync(Requirement("Contract 9", LegalRequirementKind.Contract, 9999), Cro));

        var contract = await Svc.CreateRequirementAsync(Requirement("Contract 031/2026", LegalRequirementKind.Contract, party), Cro);
        Assert.Equal((party, "Campus SaaS", false), (contract.ThirdPartyId, contract.ThirdPartyName, contract.ThirdPartyHidden));

        var invalid = new (LegalRequirementRequest Request, string Field)[]
        {
            (new LegalRequirementRequest { Title = "T", Kind = LegalRequirementKind.Law }, "Code"),
            (new LegalRequirementRequest { Code = "C", Kind = LegalRequirementKind.Law }, "Title"),
            (new LegalRequirementRequest { Code = "C", Title = "T" }, "Kind"),
            (new LegalRequirementRequest { Code = "C", Title = "T", Kind = (LegalRequirementKind)5 }, "Kind"),
            (new LegalRequirementRequest { Code = new string('c', DataCatalogueLimits.MaxRequirementCodeLength + 1), Title = "T",
                Kind = LegalRequirementKind.Law }, "Code"),
            (new LegalRequirementRequest { Code = "C", Title = "T", Kind = LegalRequirementKind.Contract, ThirdPartyId = 0 },
                "ThirdPartyId")
        };
        foreach (var (request, field) in invalid)
            Assert.Equal(field, (await Assert.ThrowsAsync<InvalidParameterException>(() =>
                Svc.CreateRequirementAsync(request, Cro))).ParameterName);

        await Assert.ThrowsAsync<DataAlreadyExistsException>(() =>
            Svc.UpdateRequirementAsync(contract.Id, Requirement("LGPD ART. 46"), Cro));
        var renamed = await Svc.UpdateRequirementAsync(law.Id, Requirement("LGPD art. 46, caput"), Owner);
        Assert.Equal(("LGPD art. 46, caput", Owner), (renamed.Code, renamed.UpdatedById));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.UpdateRequirementAsync(9999, Requirement("X"), Cro));

        // The counterparty of another unit is not named to a scoped reader.
        var hidden = await NewParty("Unit B vendor", UnitB);
        await Svc.UpdateRequirementAsync(contract.Id, Requirement("Contract 031/2026", LegalRequirementKind.Contract, hidden), Cro);
        ScopeTo(UnitA);
        var seen = (await Svc.GetRequirementsAsync()).Single(r => r.Id == contract.Id);
        Assert.Equal((null, null, true), (seen.ThirdPartyId, seen.ThirdPartyName, seen.ThirdPartyHidden));

        // LR1b (regression, review of M49) — the trail does not hand the hidden counterparty's id back: the change of
        // ThirdPartyId and the creation summary show the visible supplier and mask the hidden one.
        var history = await Svc.GetRequirementHistoryAsync(contract.Id, 100);
        var hiddenText = hidden.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var change = Assert.Single(history, a => a.Field == nameof(LegalRequirement.ThirdPartyId));
        Assert.Equal((party.ToString(System.Globalization.CultureInfo.InvariantCulture), DataCatalogueService.HiddenValue),
            (change.OldValue, change.NewValue));
        Assert.Contains($"ThirdPartyId={party}", history.Single(a => a.Action == AuditLogAction.Create).NewValue);
        Assert.DoesNotContain(history, a => a.Field == nameof(LegalRequirement.ThirdPartyId) && a.NewValue == hiddenText);

        // The stored trail is untouched, and an unrestricted reader sees the id.
        ScopeToEverything();
        Assert.Equal(hiddenText, (await Svc.GetRequirementHistoryAsync(contract.Id, 100))
            .Single(a => a.Field == nameof(LegalRequirement.ThirdPartyId)).NewValue);
    }

    /// <summary>
    /// LR2 — a requirement in use is never deleted — by a purpose, a retention, or a risk the caller cannot see —, counted
    /// unscoped; one nothing cites is deleted.
    /// </summary>
    [Fact]
    public async Task TestLR2_ARequirementInUseIsNeverDeleted()
    {
        var byPurpose = (await Svc.CreateRequirementAsync(Requirement("CTN art. 195"), Cro)).Id;
        var byRetention = (await Svc.CreateRequirementAsync(Requirement("Res. CNE 1/2021"), Cro)).Id;
        var byRisk = (await Svc.CreateRequirementAsync(Requirement("LGPD art. 46"), Cro)).Id;
        var unused = (await Svc.CreateRequirementAsync(Requirement("Old norm", LegalRequirementKind.InternalNorm), Cro)).Id;

        var record = Personal();
        record.Purposes![0].LegalRequirementId = byPurpose;
        record.RetentionRequirementId = byRetention;
        await Svc.SaveRecordAsync(Data, record, Cro);

        AddRisk(7, UnitB);
        await Svc.LinkRiskRequirementAsync(7, byRisk, new RiskLegalRequirementRequest(), Cro);

        foreach (var (id, use) in new[] { (byPurpose, "purpose"), (byRetention, "retention"), (byRisk, "risk") })
        {
            var refused = await Assert.ThrowsAsync<RuleBrokenException>(() => Svc.DeleteRequirementAsync(id, Cro));
            Assert.Equal(DataCatalogueService.RequirementInUseRule, refused.RuleName);
            Assert.Contains(use, refused.Message);
        }

        // The risk's link is the organization's use too: invisible to a unit A reader, still counted.
        var visibleToA = (await RequirementsAs(UnitA)).Single(r => r.Id == byRisk);
        Assert.Equal(0, visibleToA.RiskLinkCount);
        ScopeToEverything();
        var counted = await LegalRequirementReferences.CountAsync(OpenContext(), byRisk);
        Assert.Equal(1, counted.RiskLinks);

        await Svc.DeleteRequirementAsync(unused, Cro);
        Assert.DoesNotContain(await Svc.GetRequirementsAsync(), r => r.Id == unused);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.DeleteRequirementAsync(unused, Cro));
        Assert.Equal(3, Read(ctx => ctx.LegalRequirements.Count()));
    }

    private async Task<List<LegalRequirementDto>> RequirementsAs(params int[] units)
    {
        ScopeTo(units);
        return await Svc.GetRequirementsAsync();
    }

    /// <summary>LR3 — a third party named as a contract's counterparty is in use and is not deleted (ThirdPartyReferences).</summary>
    [Fact]
    public async Task TestLR3_AThirdPartyNamedByAContractIsInUse()
    {
        var party = await NewParty("Campus SaaS");
        var contract = (await Svc.CreateRequirementAsync(Requirement("Contract 031/2026", LegalRequirementKind.Contract, party),
            Cro)).Id;

        var refused = await Assert.ThrowsAsync<RuleBrokenException>(() => ThirdParties.DeleteAsync(party, Cro));
        Assert.Equal(ThirdPartiesService.InUseRule, refused.RuleName);
        Assert.Contains("counterparty", refused.Message);

        await Svc.DeleteRequirementAsync(contract, Cro);
        await ThirdParties.DeleteAsync(party, Cro);
        Assert.Equal(0, Read(ctx => ctx.ThirdParties.Count()));
    }

    // --- RL1–RL3: the requirements of a risk (T209) ------------------------------------------------------------------------

    /// <summary>RL1 — linking is idempotent and restates the note; unlinking removes it; both are on the risk's trail.</summary>
    [Fact]
    public async Task TestRL1_LinkingARequirementToARisk()
    {
        AddRisk(1, UnitA);
        var requirement = (await Svc.CreateRequirementAsync(Requirement(), Cro)).Id;

        await Svc.LinkRiskRequirementAsync(1, requirement, new RiskLegalRequirementRequest { Note = "Encryption at rest" }, Owner);
        var view = await Svc.LinkRiskRequirementAsync(1, requirement, new RiskLegalRequirementRequest { Note = " Access control " }, Owner);

        var link = Assert.Single(view.Requirements);
        Assert.Equal((requirement, "LGPD art. 46", "Access control", Owner), (link.RequirementId, link.Code, link.Note, link.LinkedById));
        Assert.Equal(1, Read(ctx => ctx.RiskLegalRequirements.Count()));

        Assert.Equal("Note", (await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.LinkRiskRequirementAsync(1,
            requirement, new RiskLegalRequirementRequest { Note = new string('n', 501) }, Owner))).ParameterName);

        // The risk's trail shows the link and its note while it exists.
        var trail = await GetService<IAuditTrailService>().GetForRiskAsync(1);
        Assert.Contains(trail, a => a.EntityType == nameof(RiskLegalRequirement) && a.Action == AuditLogAction.Create);
        Assert.Contains(trail, a => a.EntityType == nameof(RiskLegalRequirement) && a.Field == nameof(RiskLegalRequirement.Note));

        await Svc.UnlinkRiskRequirementAsync(1, requirement, Owner);
        Assert.Empty((await Svc.GetRiskComplianceAsync(1)).Requirements);

        // The unlinking is recorded, with its author; the risk's trail resolves current links only, so it no longer reaches
        // these rows — the limit every per-risk child has (S51 R5, S52 R11).
        Assert.Contains(Audit(nameof(RiskLegalRequirement)), a => a.Action == AuditLogAction.Delete && a.UserId == Owner);
    }

    /// <summary>RL2 — a risk outside the caller's scope is not found to read or write; a missing requirement or link is 404.</summary>
    [Fact]
    public async Task TestRL2_TheRiskScopeGovernsItsRequirements()
    {
        AddRisk(1, UnitA);
        AddRisk(2, UnitB);
        var requirement = (await Svc.CreateRequirementAsync(Requirement(), Cro)).Id;
        await Svc.LinkRiskRequirementAsync(2, requirement, new RiskLegalRequirementRequest(), Cro);

        ScopeTo(UnitA);
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetRiskComplianceAsync(2));
        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Svc.LinkRiskRequirementAsync(2, requirement, new RiskLegalRequirementRequest(), Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.UnlinkRiskRequirementAsync(2, requirement, Author));

        await Assert.ThrowsAsync<DataNotFoundException>(() =>
            Svc.LinkRiskRequirementAsync(1, 9999, new RiskLegalRequirementRequest(), Author));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.UnlinkRiskRequirementAsync(1, requirement, Author));

        // A risk manager of unit A links a requirement to their own risk: the requirement is the organization's.
        Assert.Single((await Svc.LinkRiskRequirementAsync(1, requirement, new RiskLegalRequirementRequest(), Author)).Requirements);

        ScopeToEverything();
        Assert.Single((await Svc.GetRiskComplianceAsync(2)).Requirements);
    }

    /// <summary>
    /// RL3 — the risk's view: its own requirements, the data records it reaches through the chain with their findings, and
    /// the requirements those records cite that the risk does not link itself.
    /// </summary>
    [Fact]
    public async Task TestRL3_TheRiskViewReadsTheCatalogue()
    {
        var law = (await Svc.CreateRequirementAsync(Requirement(), Cro)).Id;
        var norm = (await Svc.CreateRequirementAsync(Requirement("Res. CNE 1/2021", LegalRequirementKind.Regulation), Cro)).Id;

        var record = Sensitive(basis: null);
        record.RetentionRequirementId = norm;
        await Svc.SaveRecordAsync(Data, record, Cro);

        AddRisk(1, UnitA);
        AddLink(1, Data);
        AddLink(1, Health);
        AddLink(1, Process);
        await Svc.LinkRiskRequirementAsync(1, law, new RiskLegalRequirementRequest { Note = "Encryption" }, Cro);

        var view = await Svc.GetRiskComplianceAsync(1);

        Assert.Equal([law], view.Requirements.Select(r => r.RequirementId));
        Assert.Equal([Health, Data], view.DataRecords.Select(d => d.EntityId));

        var student = view.DataRecords.Single(d => d.EntityId == Data);
        Assert.Equal((true, PersonalDataCategory.SensitivePersonal), (student.Catalogued, student.PersonalData));
        Assert.Contains(DataCatalogueFindingCode.LegalBasisMissing, student.FindingCodes);
        Assert.Equal([norm], student.CitedRequirementIds);

        var health = view.DataRecords.Single(d => d.EntityId == Health);
        Assert.Equal([DataCatalogueFindingCode.NotCatalogued], health.FindingCodes);

        Assert.Equal([norm], view.CatalogueRequirements.Select(r => r.Id));
    }

    // --- histories ---------------------------------------------------------------------------------------------------------

    /// <summary>HI1 — each history is limited, refuses a limit out of range, and answers 404 for what does not exist.</summary>
    [Fact]
    public async Task TestHI1_HistoriesAreBoundedAndFound()
    {
        var requirement = (await Svc.CreateRequirementAsync(Requirement(), Cro)).Id;
        await Svc.UpdateRequirementAsync(requirement, Requirement("LGPD art. 46, caput"), Cro);
        var dpia = (await Svc.CreateDpiaAsync(Complete(), Cro)).Id;
        await Svc.LinkDpiaAsync(dpia, Data, Cro);

        Assert.Empty(await Svc.GetRecordHistoryAsync(Data, 10));
        await Svc.SaveRecordAsync(Data, Sensitive(), Cro);
        Assert.Single(await Svc.GetRecordHistoryAsync(Data, 1));

        Assert.All(await Svc.GetRequirementHistoryAsync(requirement, 100),
            a => Assert.Equal((nameof(LegalRequirement), requirement), (a.EntityType, a.EntityId)));
        Assert.Contains(await Svc.GetDpiaHistoryAsync(dpia, 100), a => a.EntityType == nameof(DpiaLink));

        foreach (var limit in new[] { 0, DataCatalogueLimits.MaxHistoryLimit + 1 })
        {
            await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.GetRecordHistoryAsync(Data, limit));
            await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.GetRequirementHistoryAsync(requirement, limit));
            await Assert.ThrowsAsync<InvalidParameterException>(() => Svc.GetDpiaHistoryAsync(dpia, limit));
        }

        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetRecordHistoryAsync(9999, 10));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetRequirementHistoryAsync(9999, 10));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetDpiaHistoryAsync(9999, 10));
        await Assert.ThrowsAsync<DataNotFoundException>(() => Svc.GetDpiaAsync(9999));
    }

    /// <summary>The compliance list covers every data record, catalogued or not, in name order.</summary>
    [Fact]
    public async Task TestTheListCoversEveryDataRecord()
    {
        await Svc.SaveRecordAsync(Data, Personal(), Cro);

        var list = await Svc.GetRecordsAsync(withFindingsOnly: false);

        Assert.Equal([(Health, false), (Data, true)], list.Select(r => (r.EntityId, r.Catalogued)).ToList());
        Assert.Equal([DataCatalogueFindingCode.NotCatalogued], list[0].FindingCodes);
        Assert.Equal((PersonalDataCategory.Personal, 1), (list[1].PersonalData, list[1].PurposeCount));
    }

    /// <summary>Y0 — nothing in the service lifts the query filters, reaches the network or touches the disk.</summary>
    [Fact]
    public void TestY0_NoQueryFilterIsLiftedAndNothingLeavesTheProcess()
    {
        var source = System.IO.File.ReadAllText(System.IO.Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..",
            "ServerServices", "Governance", "DataCatalogueService.cs"));

        Assert.DoesNotContain("IgnoreQueryFilters", source);
        Assert.DoesNotContain("HttpClient", source);
        Assert.DoesNotContain("System.IO", source);
    }
}

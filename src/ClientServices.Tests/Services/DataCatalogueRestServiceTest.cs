using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Model.DataCatalogue;
using Model.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Stage 9.11 (S52 §7) — <see cref="DataCatalogueRestService"/> over <see cref="StubRestBackend"/>, so every URL it builds,
/// every body and query string it sends and every status branch runs for real. A refusal (a write without global scope, a
/// requirement in use, a frozen RIPD, a text carrying a personal value) keeps the server's sentence.
/// </summary>
[TestSubject(typeof(DataCatalogueRestService))]
public class DataCatalogueRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IDataCatalogueService _service;

    public DataCatalogueRestServiceTest()
    {
        _service = ResolveWith<IDataCatalogueService>(_backend);
    }

    private static DataRecordDto Record(int entityId = 50) => new()
    {
        EntityId = entityId, Name = "Student registry", Catalogued = true, PersonalData = PersonalDataCategory.SensitivePersonal,
        InvolvesMinors = true, RetentionPeriodMonths = 60, InternationalTransfer = true,
        TransferMechanism = InternationalTransferMechanism.StandardContractualClauses,
        Purposes =
        [
            new DataCataloguePurposeDto
            {
                Id = 1, Purpose = "Enrolment", LegalBasis = LgpdLegalBasis.Art11Consent,
                LegalRequirement = new LegalRequirementRefDto { Id = 3, Code = "LGPD-7", Kind = LegalRequirementKind.Law }
            }
        ],
        Locations = [new DataCatalogueLocationDto { Id = 1, Country = "US", Purpose = DataLocationPurpose.Backup }],
        Processors = [new DataRecordProcessorDto { ThirdPartyId = 7, Name = "Acme Cloud", Status = ThirdPartyStatus.Active, ThroughGroup = true }],
        HiddenProcessorCount = 1, TransferCountries = ["US"],
        Dpias = [new DataRecordDpiaDto { Id = 9, Title = "RIPD enrolment", Status = DpiaStatus.Approved }],
        Findings = [new DataCatalogueFindingDto { Code = DataCatalogueFindingCode.LegalBasisMissing, Message = "A purpose has no legal basis." }]
    };

    private static LegalRequirementDto Requirement(int id = 3) => new()
    {
        Id = id, Code = "LGPD-7", Title = "LGPD art. 7", Kind = LegalRequirementKind.Law, RiskLinkCount = 2, PurposeCount = 1
    };

    private static DpiaDto Dpia(int id = 9) => new()
    {
        Id = id, Title = "RIPD enrolment", Status = DpiaStatus.Draft, ResidualRisk = DpiaResidualRisk.Medium,
        Links = [new DpiaLinkDto { EntityId = 50, Kind = DpiaLinkKind.DataRecord, EntityName = "Student registry" }]
    };

    private static RiskComplianceDto Compliance(int riskId = 11) => new()
    {
        RiskId = riskId,
        Requirements = [new RiskRequirementDto { RequirementId = 3, Code = "LGPD-7", Title = "LGPD art. 7", Kind = LegalRequirementKind.Law, Note = "Cited by the DPO" }],
        DataRecords = [new RiskDataRecordDto { EntityId = 50, Name = "Student registry", Catalogued = true, CitedRequirementIds = [3] }],
        CatalogueRequirements = [new LegalRequirementRefDto { Id = 4, Code = "ANPD-15", Title = "ANPD res. 15", Kind = LegalRequirementKind.Regulation }]
    };

    private static AuditLog[] History(int entityId, string type) =>
        [new AuditLog { Id = 1, EntityType = type, EntityId = entityId, Field = "", Actor = "user:1" }];

    private bool BodyHas(string fragment) => _backend.LastRequest.Body.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    private bool QueryHas(string fragment) => _backend.LastRequest.Query.Contains(fragment, StringComparison.OrdinalIgnoreCase);

    // --- reads ------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheRecordListSendsItsFilterOnlyWhenAsked()
    {
        _backend.OnGet("/DataCatalogue/Records", new[]
        {
            new DataRecordSummaryDto
            {
                EntityId = 50, Name = "Student registry", Catalogued = true, PersonalData = PersonalDataCategory.Personal,
                PurposeCount = 2, ApprovedDpiaCount = 1, FindingCodes = [DataCatalogueFindingCode.LegalBasisMissing]
            }
        });

        var all = await _service.GetRecordsAsync();
        Assert.Equal((50, 2, DataCatalogueFindingCode.LegalBasisMissing),
            (Assert.Single(all).EntityId, all[0].PurposeCount, all[0].FindingCodes.Single()));
        Assert.True(string.IsNullOrEmpty(_backend.LastRequest.Query));

        await _service.GetRecordsAsync(true);
        Assert.True(QueryHas("withFindings=true"));
    }

    [Fact]
    public async Task TestTheRecordReadsHitTheirRoutes()
    {
        _backend.OnGet("/DataCatalogue/Records/50", Record());
        _backend.OnGet("/DataCatalogue/Records/50/History", History(50, "DataCatalogueEntry"));

        var record = await _service.GetRecordAsync(50);
        Assert.Equal((PersonalDataCategory.SensitivePersonal, InternationalTransferMechanism.StandardContractualClauses, 1),
            (record.PersonalData!.Value, record.TransferMechanism!.Value, record.HiddenProcessorCount));
        Assert.Equal(("Enrolment", LgpdLegalBasis.Art11Consent, "LGPD-7"),
            (record.Purposes.Single().Purpose, record.Purposes[0].LegalBasis!.Value, record.Purposes[0].LegalRequirement!.Code));
        Assert.Equal(("US", DataLocationPurpose.Backup), (record.Locations.Single().Country, record.Locations[0].Purpose));
        Assert.True(record.Processors.Single().ThroughGroup);
        Assert.Equal(DpiaStatus.Approved, record.Dpias.Single().Status);

        Assert.Equal(50, Assert.Single(await _service.GetRecordHistoryAsync(50, 25)).EntityId);
        Assert.True(QueryHas("limit=25"));
        await _service.GetRecordHistoryAsync(50);
        Assert.True(QueryHas("limit=500"));
    }

    [Fact]
    public async Task TestTheRequirementReadsHitTheirRoutes()
    {
        _backend.OnGet("/DataCatalogue/Requirements", new[] { Requirement() });
        _backend.OnGet("/DataCatalogue/Requirements/3/History", History(3, "LegalRequirement"));

        var requirements = await _service.GetRequirementsAsync();
        Assert.Equal(("LGPD-7", LegalRequirementKind.Law, 2), (Assert.Single(requirements).Code, requirements[0].Kind, requirements[0].RiskLinkCount));

        Assert.Equal(3, Assert.Single(await _service.GetRequirementHistoryAsync(3, 40)).EntityId);
        Assert.True(QueryHas("limit=40"));
    }

    [Fact]
    public async Task TestTheDpiaListSendsItsStatusOnlyWhenGiven()
    {
        _backend.OnGet("/DataCatalogue/Dpias", new[]
        {
            new DpiaSummaryDto { Id = 9, Title = "RIPD enrolment", Status = DpiaStatus.Approved, DataRecordCount = 2, ProcessCount = 1, ReviewOverdue = true }
        });

        var all = await _service.GetDpiasAsync();
        Assert.Equal((9, DpiaStatus.Approved, true), (Assert.Single(all).Id, all[0].Status, all[0].ReviewOverdue));
        Assert.True(string.IsNullOrEmpty(_backend.LastRequest.Query));

        await _service.GetDpiasAsync(DpiaStatus.Retired);
        Assert.True(QueryHas("status=3"));
    }

    [Fact]
    public async Task TestTheDpiaReadsHitTheirRoutes()
    {
        _backend.OnGet("/DataCatalogue/Dpias/9", Dpia());
        _backend.OnGet("/DataCatalogue/Dpias/9/History", History(9, "Dpia"));

        var dpia = await _service.GetDpiaAsync(9);
        Assert.Equal((DpiaStatus.Draft, DpiaResidualRisk.Medium, DpiaLinkKind.DataRecord),
            (dpia.Status, dpia.ResidualRisk!.Value, Assert.Single(dpia.Links).Kind));

        Assert.Equal(9, Assert.Single(await _service.GetDpiaHistoryAsync(9, 10)).EntityId);
        Assert.True(QueryHas("limit=10"));
    }

    [Fact]
    public async Task TestTheRiskComplianceReadHitsItsRoute()
    {
        _backend.OnGet("/DataCatalogue/Risks/11", Compliance());

        var compliance = await _service.GetRiskComplianceAsync(11);
        Assert.Equal((11, "Cited by the DPO", 3, "ANPD-15"),
            (compliance.RiskId, compliance.Requirements.Single().Note, compliance.DataRecords.Single().CitedRequirementIds.Single(),
                compliance.CatalogueRequirements.Single().Code));
    }

    // --- writes -----------------------------------------------------------------------------

    [Fact]
    public async Task TestTheRecordWriteSendsItsBody()
    {
        _backend.OnPut("/DataCatalogue/Records/50", Record());

        var saved = await _service.SaveRecordAsync(50, new DataCatalogueEntryRequest
        {
            PersonalData = PersonalDataCategory.Personal, RetentionPeriodMonths = 60, DataSubjects = "Enrolled students",
            Purposes = [new DataCataloguePurposeRequest { Purpose = "Enrolment administration", LegalBasis = LgpdLegalBasis.Art7Contract }],
            Locations = [new DataCatalogueLocationRequest { Country = "BR", Region = "sa-east-1", Purpose = DataLocationPurpose.Storage }]
        });

        Assert.Equal(50, saved.EntityId);
        Assert.True(_backend.Sent(Method.Put, "/DataCatalogue/Records/50"));
        Assert.True(BodyHas("Enrolment administration"));
        Assert.True(BodyHas("Enrolled students"));
        Assert.True(BodyHas("sa-east-1"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.SaveRecordAsync(50, null!));
    }

    [Fact]
    public async Task TestTheRequirementWritesSendTheirBodies()
    {
        _backend.OnPost("/DataCatalogue/Requirements", Requirement());
        _backend.OnPut("/DataCatalogue/Requirements/3", Requirement());
        _backend.OnStatus(Method.Delete, "/DataCatalogue/Requirements/3", HttpStatusCode.NoContent);

        await _service.CreateRequirementAsync(new LegalRequirementRequest
            { Code = "LGPD-7", Title = "LGPD art. 7", Kind = LegalRequirementKind.Contract, ThirdPartyId = 7 });
        Assert.True(_backend.Sent(Method.Post, "/DataCatalogue/Requirements"));
        Assert.True(BodyHas("LGPD-7"));

        await _service.UpdateRequirementAsync(3, new LegalRequirementRequest { Code = "LGPD-7B", Title = "LGPD art. 7 (rev.)" });
        Assert.True(_backend.Sent(Method.Put, "/DataCatalogue/Requirements/3"));
        Assert.True(BodyHas("LGPD-7B"));

        await _service.DeleteRequirementAsync(3);
        Assert.True(_backend.Sent(Method.Delete, "/DataCatalogue/Requirements/3"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateRequirementAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.UpdateRequirementAsync(3, null!));
    }

    [Fact]
    public async Task TestTheDpiaWritesSendTheirBodies()
    {
        _backend.OnPost("/DataCatalogue/Dpias", Dpia());
        _backend.OnPut("/DataCatalogue/Dpias/9", Dpia());

        await _service.CreateDpiaAsync(new DpiaRequest { Title = "RIPD enrolment", Summary = "Processing of minors enrolment data." });
        Assert.True(_backend.Sent(Method.Post, "/DataCatalogue/Dpias"));
        Assert.True(BodyHas("RIPD enrolment"));
        Assert.True(BodyHas("minors enrolment"));

        await _service.UpdateDpiaAsync(9, new DpiaRequest { Title = "RIPD enrolment v2", DocumentReference = "SEI-2026/77" });
        Assert.True(_backend.Sent(Method.Put, "/DataCatalogue/Dpias/9"));
        Assert.True(BodyHas("SEI-2026/77"));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateDpiaAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.UpdateDpiaAsync(9, null!));
    }

    [Fact]
    public async Task TestTheDpiaLinksApprovalAndRetirementHitTheirRoutes()
    {
        _backend.OnPut("/DataCatalogue/Dpias/9/Links/50", Dpia());
        _backend.OnStatus(Method.Delete, "/DataCatalogue/Dpias/9/Links/50", HttpStatusCode.NoContent);
        _backend.OnPost("/DataCatalogue/Dpias/9/Approve", new DpiaDto { Id = 9, Title = "RIPD enrolment", Status = DpiaStatus.Approved, ApprovedById = 4 });
        _backend.OnPost("/DataCatalogue/Dpias/9/Retire", new DpiaDto { Id = 9, Title = "RIPD enrolment", Status = DpiaStatus.Retired, RetireReason = "Superseded by v3." });

        var linked = await _service.LinkDpiaAsync(9, 50);
        Assert.Equal(50, Assert.Single(linked.Links).EntityId);
        Assert.True(_backend.Sent(Method.Put, "/DataCatalogue/Dpias/9/Links/50"));

        await _service.UnlinkDpiaAsync(9, 50);
        Assert.True(_backend.Sent(Method.Delete, "/DataCatalogue/Dpias/9/Links/50"));

        var approved = await _service.ApproveDpiaAsync(9);
        Assert.Equal((DpiaStatus.Approved, 4), (approved.Status, approved.ApprovedById!.Value));
        Assert.True(_backend.Sent(Method.Post, "/DataCatalogue/Dpias/9/Approve"));

        var retired = await _service.RetireDpiaAsync(9, new DpiaRetireRequest { Reason = "Superseded by the third review." });
        Assert.Equal(DpiaStatus.Retired, retired.Status);
        Assert.True(_backend.Sent(Method.Post, "/DataCatalogue/Dpias/9/Retire"));
        Assert.True(BodyHas("Superseded by the third review."));

        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.RetireDpiaAsync(9, null!));
    }

    [Fact]
    public async Task TestTheRiskRequirementLinksSendTheirNote()
    {
        _backend.OnPut("/DataCatalogue/Risks/11/Requirements/3", Compliance());
        _backend.OnStatus(Method.Delete, "/DataCatalogue/Risks/11/Requirements/3", HttpStatusCode.NoContent);

        var linked = await _service.LinkRiskRequirementAsync(11, 3, new RiskLegalRequirementRequest { Note = "Applies to the enrolment flow" });
        Assert.Equal(11, linked.RiskId);
        Assert.True(_backend.Sent(Method.Put, "/DataCatalogue/Risks/11/Requirements/3"));
        Assert.True(BodyHas("Applies to the enrolment flow"));

        // No request at all is a link without a note, not a null body.
        await _service.LinkRiskRequirementAsync(11, 3);
        Assert.True(_backend.Sent(Method.Put, "/DataCatalogue/Risks/11/Requirements/3"));

        await _service.UnlinkRiskRequirementAsync(11, 3);
        Assert.True(_backend.Sent(Method.Delete, "/DataCatalogue/Risks/11/Requirements/3"));
    }

    // --- errors -----------------------------------------------------------------------------

    private static (string Name, Method Method, string Path, Func<IDataCatalogueService, Task> Call)[] Calls() =>
    [
        ("GetRecords", Method.Get, "/DataCatalogue/Records", s => s.GetRecordsAsync()),
        ("GetRecord", Method.Get, "/DataCatalogue/Records/50", s => s.GetRecordAsync(50)),
        ("GetRecordHistory", Method.Get, "/DataCatalogue/Records/50/History", s => s.GetRecordHistoryAsync(50)),
        ("SaveRecord", Method.Put, "/DataCatalogue/Records/50", s => s.SaveRecordAsync(50, new DataCatalogueEntryRequest())),
        ("GetRequirements", Method.Get, "/DataCatalogue/Requirements", s => s.GetRequirementsAsync()),
        ("GetRequirementHistory", Method.Get, "/DataCatalogue/Requirements/3/History", s => s.GetRequirementHistoryAsync(3)),
        ("CreateRequirement", Method.Post, "/DataCatalogue/Requirements", s => s.CreateRequirementAsync(new LegalRequirementRequest())),
        ("UpdateRequirement", Method.Put, "/DataCatalogue/Requirements/3", s => s.UpdateRequirementAsync(3, new LegalRequirementRequest())),
        ("DeleteRequirement", Method.Delete, "/DataCatalogue/Requirements/3", s => s.DeleteRequirementAsync(3)),
        ("GetDpias", Method.Get, "/DataCatalogue/Dpias", s => s.GetDpiasAsync()),
        ("GetDpia", Method.Get, "/DataCatalogue/Dpias/9", s => s.GetDpiaAsync(9)),
        ("GetDpiaHistory", Method.Get, "/DataCatalogue/Dpias/9/History", s => s.GetDpiaHistoryAsync(9)),
        ("CreateDpia", Method.Post, "/DataCatalogue/Dpias", s => s.CreateDpiaAsync(new DpiaRequest())),
        ("UpdateDpia", Method.Put, "/DataCatalogue/Dpias/9", s => s.UpdateDpiaAsync(9, new DpiaRequest())),
        ("LinkDpia", Method.Put, "/DataCatalogue/Dpias/9/Links/50", s => s.LinkDpiaAsync(9, 50)),
        ("UnlinkDpia", Method.Delete, "/DataCatalogue/Dpias/9/Links/50", s => s.UnlinkDpiaAsync(9, 50)),
        ("ApproveDpia", Method.Post, "/DataCatalogue/Dpias/9/Approve", s => s.ApproveDpiaAsync(9)),
        ("RetireDpia", Method.Post, "/DataCatalogue/Dpias/9/Retire", s => s.RetireDpiaAsync(9, new DpiaRetireRequest())),
        ("GetRiskCompliance", Method.Get, "/DataCatalogue/Risks/11", s => s.GetRiskComplianceAsync(11)),
        ("LinkRiskRequirement", Method.Put, "/DataCatalogue/Risks/11/Requirements/3", s => s.LinkRiskRequirementAsync(11, 3)),
        ("UnlinkRiskRequirement", Method.Delete, "/DataCatalogue/Risks/11/Requirements/3", s => s.UnlinkRiskRequirementAsync(11, 3))
    ];

    [Fact]
    public void TestEveryClientMethodIsCoveredByTheRefusalChecks()
    {
        var methods = typeof(IDataCatalogueService).GetMethods().Select(m => m.Name).Distinct().Count();
        Assert.Equal(21, methods);
        Assert.Equal(methods, Calls().Length);
    }

    [Fact]
    public async Task TestA404IsNotFoundOnEveryMethod()
    {
        foreach (var (_, method, path, _) in Calls()) _backend.OnStatus(method, path, HttpStatusCode.NotFound);

        foreach (var (_, _, _, call) in Calls())
            await Assert.ThrowsAsync<DataNotFoundException>(() => call(_service));
    }

    /// <summary>A requirement in use, a duplicate code, a frozen RIPD and a refused scope keep the server's sentence.</summary>
    [Theory]
    [InlineData(HttpStatusCode.UnprocessableEntity, "The requirement is in use: unlink it first.")]
    [InlineData(HttpStatusCode.Conflict, "A requirement with this code is already catalogued.")]
    [InlineData(HttpStatusCode.BadRequest, "The text carries a personal value.")]
    [InlineData(HttpStatusCode.Forbidden, "Writing the catalogue needs global scope.")]
    public async Task TestARefusalKeepsTheServersSentenceOnEveryMethod(HttpStatusCode status, string sentence)
    {
        foreach (var (_, method, path, _) in Calls())
            _backend.On(method, path, new { error = "refused", message = sentence }, status);

        foreach (var (name, _, _, call) in Calls())
        {
            var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => call(_service));
            Assert.True(thrown.Message.Contains(sentence), name);
        }
    }

    [Fact]
    public async Task TestAServerErrorIsAGenericFailureOnEveryMethod()
    {
        foreach (var (_, method, path, _) in Calls()) _backend.OnStatus(method, path, HttpStatusCode.InternalServerError);

        foreach (var (name, _, path, call) in Calls())
        {
            var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => call(_service));
            Assert.True(thrown.Message.Contains($"Error calling {path}"), name);
        }
    }

    [Fact]
    public async Task TestAnEmptySuccessBodyIsAFailureNotANull()
    {
        _backend.OnStatus(Method.Get, "/DataCatalogue/Records/50", HttpStatusCode.OK);

        var thrown = await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetRecordAsync(50));
        Assert.Contains("empty body", thrown.Message);
    }

    [Fact]
    public async Task TestAnUnreachableServerIsACommunicationFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/DataCatalogue/Requirements");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetRequirementsAsync());
    }
}

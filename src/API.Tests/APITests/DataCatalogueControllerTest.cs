using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using DAL.Entities;
using DAL.Enums;
using DAL.Exceptions;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.DataCatalogue;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Stage 9.11 (S52 §6) — <see cref="DataCatalogueController"/>: each action's success shape, that the acting user and every
/// argument reach the service, and the mapping of every domain exception through <c>DecisionCycleErrors</c> onto the status
/// the other controllers use; a scope violation is re-thrown for <c>EntityScopeViolationMiddleware</c>.
/// </summary>
[TestSubject(typeof(DataCatalogueController))]
public class DataCatalogueControllerTest : BaseControllerTest
{
    private const int Known = DecisionCycleIds.Known;

    private readonly DataCatalogueController _controller;

    public DataCatalogueControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<DataCatalogueController>();
    }

    private static string? Property(object? body, string name) =>
        JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement.GetProperty(name).GetString();

    private static readonly DataCatalogueEntryRequest EntryBody = new()
    {
        PersonalData = PersonalDataCategory.Personal, InvolvesMinors = true, DataSubjects = "Students",
        InternationalTransfer = true, TransferMechanism = InternationalTransferMechanism.StandardContractualClauses,
        Purposes = [new DataCataloguePurposeRequest { Purpose = "Enrolment", LegalBasis = LgpdLegalBasis.Art7Contract }],
        Locations = [new DataCatalogueLocationRequest { Country = "US", Purpose = DataLocationPurpose.Storage }]
    };
    private static readonly LegalRequirementRequest RequirementBody = new()
        { Code = "LGPD-7", Title = "Legal bases", Kind = LegalRequirementKind.Law };
    private static readonly DpiaRequest DpiaBody = new() { Title = "Enrolment RIPD", ResidualRisk = DpiaResidualRisk.Medium };
    private static readonly DpiaRetireRequest RetireBody = new() { Reason = "Superseded by the 2027 RIPD." };
    private static readonly RiskLegalRequirementRequest RiskLinkBody = new() { Note = "Cited by the DPO." };

    /// <summary>Every action an id can drive to an error, called with that id in each position it can take.</summary>
    private IEnumerable<(string Name, Func<int, Task<ActionResult?>> Call)> IdDrivenActions()
    {
        yield return ("GetRecord", async id => (await _controller.GetRecord(id)).Result);
        yield return ("GetRecordHistory", async id => (await _controller.GetRecordHistory(id)).Result);
        yield return ("SaveRecord", async id => (await _controller.SaveRecord(id, EntryBody)).Result);
        yield return ("GetRequirementHistory", async id => (await _controller.GetRequirementHistory(id)).Result);
        yield return ("CreateRequirement",
            async id => (await _controller.CreateRequirement(new LegalRequirementRequest { Code = "X", ThirdPartyId = id })).Result);
        yield return ("UpdateRequirement", async id => (await _controller.UpdateRequirement(id, RequirementBody)).Result);
        yield return ("DeleteRequirement", async id => await _controller.DeleteRequirement(id));
        yield return ("GetDpia", async id => (await _controller.GetDpia(id)).Result);
        yield return ("GetDpiaHistory", async id => (await _controller.GetDpiaHistory(id)).Result);
        yield return ("CreateDpia", async id => (await _controller.CreateDpia(new DpiaRequest { Title = id.ToString() })).Result);
        yield return ("UpdateDpia", async id => (await _controller.UpdateDpia(id, DpiaBody)).Result);
        yield return ("LinkDpia", async id => (await _controller.LinkDpia(id, 20)).Result);
        yield return ("LinkDpia#", async id => (await _controller.LinkDpia(Known, id)).Result);
        yield return ("UnlinkDpia", async id => await _controller.UnlinkDpia(id, 20));
        yield return ("UnlinkDpia#", async id => await _controller.UnlinkDpia(Known, id));
        yield return ("ApproveDpia", async id => (await _controller.ApproveDpia(id)).Result);
        yield return ("RetireDpia", async id => (await _controller.RetireDpia(id, RetireBody)).Result);
        yield return ("GetRiskCompliance", async id => (await _controller.GetRiskCompliance(id)).Result);
        yield return ("LinkRiskRequirement", async id => (await _controller.LinkRiskRequirement(id, 8, RiskLinkBody)).Result);
        yield return ("LinkRiskRequirement#", async id => (await _controller.LinkRiskRequirement(Known, id, RiskLinkBody)).Result);
        yield return ("UnlinkRiskRequirement", async id => await _controller.UnlinkRiskRequirement(id, 8));
        yield return ("UnlinkRiskRequirement#", async id => await _controller.UnlinkRiskRequirement(Known, id));
    }

    // --- success ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheReadsAnswer200WithTheirDtos()
    {
        var records = Assert.IsType<OkObjectResult>((await _controller.GetRecords(true)).Result);
        var summary = Assert.Single(Assert.IsType<List<DataRecordSummaryDto>>(records.Value));
        Assert.Equal((Known, PersonalDataCategory.Personal), (summary.EntityId, summary.PersonalData));
        Assert.Equal(DataCatalogueFindingCode.DpiaMissing, Assert.Single(summary.FindingCodes));

        var record = Assert.IsType<DataRecordDto>(Assert.IsType<OkObjectResult>((await _controller.GetRecord(7)).Result).Value);
        Assert.Equal((7, true, "US"), (record.EntityId, record.Catalogued, Assert.Single(record.Locations).Country));
        Assert.Single(record.Purposes);
        Assert.Single(record.Findings);

        var recordHistory = Assert.IsType<OkObjectResult>((await _controller.GetRecordHistory(7)).Result);
        Assert.Equal(7, Assert.Single(Assert.IsType<List<AuditLog>>(recordHistory.Value)).EntityId);

        var requirements = Assert.IsType<OkObjectResult>((await _controller.GetRequirements()).Result);
        Assert.Equal(Known, Assert.Single(Assert.IsType<List<LegalRequirementDto>>(requirements.Value)).Id);

        var requirementHistory = Assert.IsType<OkObjectResult>((await _controller.GetRequirementHistory(7)).Result);
        Assert.Equal(nameof(LegalRequirement), Assert.Single(Assert.IsType<List<AuditLog>>(requirementHistory.Value)).EntityType);

        var dpias = Assert.IsType<OkObjectResult>((await _controller.GetDpias(DpiaStatus.Approved)).Result);
        Assert.Equal(DpiaStatus.Approved, Assert.Single(Assert.IsType<List<DpiaSummaryDto>>(dpias.Value)).Status);

        var dpia = Assert.IsType<DpiaDto>(Assert.IsType<OkObjectResult>((await _controller.GetDpia(7)).Result).Value);
        Assert.Equal((7, DpiaStatus.Draft), (dpia.Id, dpia.Status));
        Assert.Single(dpia.Links);

        var dpiaHistory = Assert.IsType<OkObjectResult>((await _controller.GetDpiaHistory(7)).Result);
        Assert.Equal(nameof(Dpia), Assert.Single(Assert.IsType<List<AuditLog>>(dpiaHistory.Value)).EntityType);

        var compliance = Assert.IsType<RiskComplianceDto>(Assert.IsType<OkObjectResult>((await _controller.GetRiskCompliance(7)).Result).Value);
        Assert.Equal(7, compliance.RiskId);
        Assert.Single(compliance.Requirements);
        Assert.Single(compliance.DataRecords);
        Assert.Single(compliance.CatalogueRequirements);
    }

    [Fact]
    public async Task TestTheWritesAnswerWithTheirDtos()
    {
        Assert.Equal(7, Assert.IsType<DataRecordDto>(
            Assert.IsType<OkObjectResult>((await _controller.SaveRecord(7, EntryBody)).Result).Value).EntityId);

        Assert.Equal(Known, Assert.IsType<LegalRequirementDto>(
            Assert.IsType<OkObjectResult>((await _controller.CreateRequirement(RequirementBody)).Result).Value).Id);
        Assert.Equal(7, Assert.IsType<LegalRequirementDto>(
            Assert.IsType<OkObjectResult>((await _controller.UpdateRequirement(7, RequirementBody)).Result).Value).Id);
        Assert.IsType<NoContentResult>(await _controller.DeleteRequirement(7));

        Assert.Equal(Known, Assert.IsType<DpiaDto>(
            Assert.IsType<OkObjectResult>((await _controller.CreateDpia(DpiaBody)).Result).Value).Id);
        Assert.Equal(7, Assert.IsType<DpiaDto>(
            Assert.IsType<OkObjectResult>((await _controller.UpdateDpia(7, DpiaBody)).Result).Value).Id);
        Assert.Single(Assert.IsType<DpiaDto>(
            Assert.IsType<OkObjectResult>((await _controller.LinkDpia(7, 20)).Result).Value).Links);
        Assert.IsType<NoContentResult>(await _controller.UnlinkDpia(7, 20));
        Assert.Equal(DpiaStatus.Approved, Assert.IsType<DpiaDto>(
            Assert.IsType<OkObjectResult>((await _controller.ApproveDpia(7)).Result).Value).Status);
        Assert.Equal(DpiaStatus.Retired, Assert.IsType<DpiaDto>(
            Assert.IsType<OkObjectResult>((await _controller.RetireDpia(7, RetireBody)).Result).Value).Status);

        Assert.Equal(7, Assert.IsType<RiskComplianceDto>(
            Assert.IsType<OkObjectResult>((await _controller.LinkRiskRequirement(7, 8, RiskLinkBody)).Result).Value).RiskId);
        Assert.IsType<NoContentResult>(await _controller.UnlinkRiskRequirement(7, 8));
    }

    /// <summary>
    /// HI2 (regression, S52 §4.10) — the data catalogue's types are audited, and the generic reader, which cannot apply the
    /// read policy of the catalogue or the caller's entity scope, refuses them and points at the catalogue's history routes,
    /// as it does for hosts and third parties. Without the refusal any reader of the generic trail would read the catalogue's
    /// history, whose own policy is stricter.
    /// </summary>
    [Theory]
    [InlineData("LegalRequirement")]
    [InlineData("DataCatalogueEntry")]
    [InlineData("DataCataloguePurpose")]
    [InlineData("DataCatalogueLocation")]
    [InlineData("Dpia")]
    [InlineData("DpiaLink")]
    [InlineData("RiskLegalRequirement")]
    public async Task TestHI2_TheGenericTrailRefusesDataCatalogueTypes(string entityType)
    {
        Assert.Contains(entityType, ServerServices.Governance.AuditTrailService.AuditedTypes);
        var trail = _serviceProvider.GetRequiredService<AuditTrailController>();

        var bad = Assert.IsType<BadRequestObjectResult>((await trail.GetForRecord(entityType, 1)).Result);

        Assert.Equal("use_data_catalogue_history", Property(bad.Value, "error"));
    }

    // --- error mapping ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestAnInvalidParameterIsA400NamingIt()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<BadRequestObjectResult>(await call(DecisionCycleIds.Invalid));
            Assert.True(("invalid_parameter", "Reason") == (Property(result.Value, "error"), Property(result.Value, "ParameterName")), name);
        }
    }

    [Fact]
    public async Task TestNotFoundIsA404OnEveryAction()
    {
        foreach (var (name, call) in IdDrivenActions())
            Assert.True(await call(DecisionCycleIds.Missing) is NotFoundResult, name);
    }

    [Fact]
    public async Task TestAnExistingRecordIsA409()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<ConflictObjectResult>(await call(DecisionCycleIds.Conflict));
            Assert.True("already_exists" == Property(result.Value, "error"), name);
        }
    }

    [Fact]
    public async Task TestAPermissionRefusalIsA403()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<ObjectResult>(await call(DecisionCycleIds.Forbidden));
            Assert.True(StatusCodes.Status403Forbidden == result.StatusCode, name);
        }
    }

    [Fact]
    public async Task TestABrokenRuleIsA422NamingTheRule()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<UnprocessableEntityObjectResult>(await call(DecisionCycleIds.Rule));
            Assert.True("risk_archive_not_live" == Property(result.Value, "error"), name);
        }
    }

    [Fact]
    public async Task TestAScopeViolationPropagatesToTheMiddleware()
    {
        foreach (var (_, call) in IdDrivenActions())
            await Assert.ThrowsAsync<EntityScopeViolationException>(() => call(DecisionCycleIds.Scope));
    }

    [Fact]
    public async Task TestAnUnexpectedFailureIsA500WithoutDetail()
    {
        foreach (var (name, call) in IdDrivenActions())
        {
            var result = Assert.IsType<StatusCodeResult>(await call(DecisionCycleIds.Broken));
            Assert.True(StatusCodes.Status500InternalServerError == result.StatusCode, name);
        }
    }

    /// <summary>The three lists no id drives map a failure like any other.</summary>
    [Fact]
    public async Task TestTheListsMapAFailureToo()
    {
        var broken = Substitute.For<IDataCatalogueService>();
        broken.GetRecordsAsync(Arg.Any<bool>())
            .Returns<List<DataRecordSummaryDto>>(_ => throw new Model.Exceptions.InvalidParameterException("withFindings", "Bad flag."));
        broken.GetRequirementsAsync()
            .Returns<List<LegalRequirementDto>>(_ => throw new InvalidOperationException("boom"));
        broken.GetDpiasAsync(Arg.Any<DpiaStatus?>())
            .Returns<List<DpiaSummaryDto>>(_ => throw new Model.Exceptions.InvalidParameterException("status", "Bad status."));

        var controller = ResolveController<DataCatalogueController>(s => s.AddSingleton(broken));

        Assert.IsType<BadRequestObjectResult>((await controller.GetRecords()).Result);
        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<StatusCodeResult>((await controller.GetRequirements()).Result).StatusCode);
        Assert.IsType<BadRequestObjectResult>((await controller.GetDpias((DpiaStatus)9)).Result);
    }

    // --- arguments ----------------------------------------------------------------------------------------------------

    /// <summary>The acting user's id and every argument reach the service; a controller passing a fixed value would pass above.</summary>
    [Fact]
    public async Task TestTheUserIdAndArgumentsReachTheService()
    {
        var recording = MockedDataCatalogueService.Create();
        var controller = ResolveController<DataCatalogueController>(s => s.AddSingleton(recording));

        await controller.GetRecords();
        await controller.GetRecords(true);
        await controller.GetRecord(7);
        await controller.GetRecordHistory(7, 25);
        await controller.SaveRecord(7, EntryBody);
        await controller.GetRequirements();
        await controller.GetRequirementHistory(8, 30);
        await controller.CreateRequirement(RequirementBody);
        await controller.UpdateRequirement(8, RequirementBody);
        await controller.DeleteRequirement(8);
        await controller.GetDpias();
        await controller.GetDpias(DpiaStatus.Approved);
        await controller.GetDpia(9);
        await controller.GetDpiaHistory(9, 35);
        await controller.CreateDpia(DpiaBody);
        await controller.UpdateDpia(9, DpiaBody);
        await controller.LinkDpia(9, 20);
        await controller.UnlinkDpia(9, 20);
        await controller.ApproveDpia(9);
        await controller.RetireDpia(9, RetireBody);
        await controller.GetRiskCompliance(11);
        await controller.LinkRiskRequirement(11, 8, RiskLinkBody);
        await controller.UnlinkRiskRequirement(11, 8);

        await recording.Received(1).GetRecordsAsync(false);
        await recording.Received(1).GetRecordsAsync(true);
        await recording.Received(1).GetRecordAsync(7);
        await recording.Received(1).GetRecordHistoryAsync(7, 25);
        await recording.Received(1).SaveRecordAsync(7, Arg.Is<DataCatalogueEntryRequest>(r =>
            r.PersonalData == PersonalDataCategory.Personal && r.DataSubjects == "Students" && r.InternationalTransfer == true &&
            r.Purposes!.Single().Purpose == "Enrolment" && r.Locations!.Single().Country == "US"), 1);
        await recording.Received(1).GetRequirementsAsync();
        await recording.Received(1).GetRequirementHistoryAsync(8, 30);
        await recording.Received(1).CreateRequirementAsync(Arg.Is<LegalRequirementRequest>(r => r.Code == "LGPD-7"), 1);
        await recording.Received(1).UpdateRequirementAsync(8, Arg.Is<LegalRequirementRequest>(r => r.Title == "Legal bases"), 1);
        await recording.Received(1).DeleteRequirementAsync(8, 1);
        await recording.Received(1).GetDpiasAsync(null);
        await recording.Received(1).GetDpiasAsync(DpiaStatus.Approved);
        await recording.Received(1).GetDpiaAsync(9);
        await recording.Received(1).GetDpiaHistoryAsync(9, 35);
        await recording.Received(1).CreateDpiaAsync(Arg.Is<DpiaRequest>(r => r.Title == "Enrolment RIPD"), 1);
        await recording.Received(1).UpdateDpiaAsync(9, Arg.Is<DpiaRequest>(r => r.ResidualRisk == DpiaResidualRisk.Medium), 1);
        await recording.Received(1).LinkDpiaAsync(9, 20, 1);
        await recording.Received(1).UnlinkDpiaAsync(9, 20, 1);
        await recording.Received(1).ApproveDpiaAsync(9, 1);
        await recording.Received(1).RetireDpiaAsync(9, Arg.Is<DpiaRetireRequest>(r => r.Reason == "Superseded by the 2027 RIPD."), 1);
        await recording.Received(1).GetRiskComplianceAsync(11);
        await recording.Received(1).LinkRiskRequirementAsync(11, 8, Arg.Is<RiskLegalRequirementRequest>(r => r.Note == "Cited by the DPO."), 1);
        await recording.Received(1).UnlinkRiskRequirementAsync(11, 8, 1);
    }

    /// <summary>A missing body reaches the service as an empty request, so the service names the missing field.</summary>
    [Fact]
    public async Task TestAMissingBodyReachesTheServiceAsAnEmptyRequest()
    {
        var recording = MockedDataCatalogueService.Create();
        var controller = ResolveController<DataCatalogueController>(s => s.AddSingleton(recording));

        await controller.SaveRecord(7, null);
        await controller.CreateRequirement(null);
        await controller.UpdateRequirement(8, null);
        await controller.CreateDpia(null);
        await controller.UpdateDpia(9, null);
        await controller.RetireDpia(9, null);
        await controller.LinkRiskRequirement(11, 8, null);

        await recording.Received(1).SaveRecordAsync(7, Arg.Is<DataCatalogueEntryRequest>(r => r != null && r.PersonalData == null && r.Purposes == null), 1);
        await recording.Received(1).CreateRequirementAsync(Arg.Is<LegalRequirementRequest>(r => r != null && r.Code == null), 1);
        await recording.Received(1).UpdateRequirementAsync(8, Arg.Is<LegalRequirementRequest>(r => r != null && r.Code == null), 1);
        await recording.Received(1).CreateDpiaAsync(Arg.Is<DpiaRequest>(r => r != null && r.Title == null), 1);
        await recording.Received(1).UpdateDpiaAsync(9, Arg.Is<DpiaRequest>(r => r != null && r.Title == null), 1);
        await recording.Received(1).RetireDpiaAsync(9, Arg.Is<DpiaRetireRequest>(r => r != null && r.Reason == null), 1);
        await recording.Received(1).LinkRiskRequirementAsync(11, 8, Arg.Is<RiskLegalRequirementRequest>(r => r != null && r.Note == null), 1);
    }
}

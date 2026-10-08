using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
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
using Model.ThirdParties;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Stage 9.10 (S51 §6, §8) — <see cref="ThirdPartiesController"/>: each action's success shape, that the acting user and
/// every argument reach the service, and the mapping of every domain exception through <c>DecisionCycleErrors</c> onto the
/// status the other controllers use; a scope violation is re-thrown for <c>EntityScopeViolationMiddleware</c>.
/// </summary>
[TestSubject(typeof(ThirdPartiesController))]
public class ThirdPartiesControllerTest : BaseControllerTest
{
    private const int Known = DecisionCycleIds.Known;

    private readonly ThirdPartiesController _controller;

    public ThirdPartiesControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<ThirdPartiesController>();
    }

    private static string? Property(object? body, string name) =>
        JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement.GetProperty(name).GetString();

    private static readonly ThirdPartyRequest Body = new() { Name = "Acme Cloud", EntityId = 3, IsCloudProvider = true };
    private static readonly ThirdPartyLinkRequest LinkBody = new() { Description = "Hosts the portal" };
    private static readonly ThirdPartySubprocessorsRequest SubprocessorsBody = new()
        { Subprocessors = [new ThirdPartySubprocessorRequest { Name = "Hyperscaler", Country = "US" }] };
    private static readonly ThirdPartyDataLocationsRequest LocationsBody = new()
        { Locations = [new ThirdPartyDataLocationRequest { Country = "BR", Purpose = ThirdPartyDataLocationPurpose.Storage }] };
    private static readonly ThirdPartyAssessmentRequest AssessmentBody = new()
        { Variant = HecvatVariant.Full, FrameworkVersion = "3.06", ExpectedQuestionCount = 4 };
    private static readonly ThirdPartyAssessmentAnswersRequest AnswersBody = new()
        { Answers = [new HecvatAnswerRequest { QuestionId = "HFIH-01", Answer = HecvatAnswer.Yes }] };
    private static readonly ThirdPartyAssessmentVoidRequest VoidBody = new() { Reason = "Uploaded against the wrong vendor." };
    private static readonly ThirdPartySbomRequest SbomBody = new() { ComponentName = "Moodle", FileName = "sbom.json", Document = "{}" };

    /// <summary>Every action an id can drive to an error, called with that id in each position it can take.</summary>
    private IEnumerable<(string Name, Func<int, Task<ActionResult?>> Call)> IdDrivenActions()
    {
        yield return ("GetThirdParty", async id => (await _controller.GetThirdParty(id)).Result);
        yield return ("GetByEntity", async id => (await _controller.GetByEntity(id)).Result);
        yield return ("GetAssessment", async id => (await _controller.GetAssessment(id, Known)).Result);
        yield return ("GetAssessment#", async id => (await _controller.GetAssessment(Known, id)).Result);
        yield return ("GetSbom", async id => (await _controller.GetSbom(id, Known)).Result);
        yield return ("GetSbom#", async id => (await _controller.GetSbom(Known, id)).Result);
        yield return ("GetHistory", async id => (await _controller.GetHistory(id)).Result);
        yield return ("Create", async id => (await _controller.Create(new ThirdPartyRequest { Name = "X", EntityId = id })).Result);
        yield return ("Update", async id => (await _controller.Update(id, Body)).Result);
        yield return ("Delete", async id => await _controller.Delete(id));
        yield return ("Link", async id => (await _controller.Link(id, 20, LinkBody)).Result);
        yield return ("Link#", async id => (await _controller.Link(Known, id, LinkBody)).Result);
        yield return ("Unlink", async id => await _controller.Unlink(id, 20));
        yield return ("Unlink#", async id => await _controller.Unlink(Known, id));
        yield return ("SetSubprocessors", async id => (await _controller.SetSubprocessors(id, SubprocessorsBody)).Result);
        yield return ("SetDataLocations", async id => (await _controller.SetDataLocations(id, LocationsBody)).Result);
        yield return ("RecordAssessment", async id => (await _controller.RecordAssessment(id, AssessmentBody)).Result);
        yield return ("ReplaceAnswers", async id => (await _controller.ReplaceAnswers(id, Known, AnswersBody)).Result);
        yield return ("ReplaceAnswers#", async id => (await _controller.ReplaceAnswers(Known, id, AnswersBody)).Result);
        yield return ("VoidAssessment", async id => (await _controller.VoidAssessment(id, Known, VoidBody)).Result);
        yield return ("VoidAssessment#", async id => (await _controller.VoidAssessment(Known, id, VoidBody)).Result);
        yield return ("ImportSbom", async id => (await _controller.ImportSbom(id, SbomBody)).Result);
        yield return ("DeleteSbom", async id => await _controller.DeleteSbom(id, Known));
        yield return ("DeleteSbom#", async id => await _controller.DeleteSbom(Known, id));
    }

    // --- success ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheReadsAnswer200WithTheirDtos()
    {
        var list = Assert.IsType<OkObjectResult>((await _controller.GetThirdParties(ThirdPartyStatus.Active, true)).Result);
        Assert.Equal(Known, Assert.Single(Assert.IsType<List<ThirdPartySummaryDto>>(list.Value)).Id);

        var one = Assert.IsType<OkObjectResult>((await _controller.GetThirdParty(7)).Result);
        var party = Assert.IsType<ThirdPartyDto>(one.Value);
        Assert.Equal((7, HecvatState.Incomplete, 2), (party.Id, party.Hecvat.State, party.Concentration.DependentCriticalProcessCount));
        Assert.Single(party.Findings);

        var concentration = Assert.IsType<OkObjectResult>((await _controller.GetConcentration()).Result);
        var report = Assert.IsType<ThirdPartyConcentrationReportDto>(concentration.Value);
        Assert.Equal((2, 1m), (report.CriticalProcessCount, report.Suppliers.MaxShare!.Value));

        var byEntity = Assert.IsType<OkObjectResult>((await _controller.GetByEntity(50)).Result);
        Assert.Equal(ThirdPartyLinkKind.Data, Assert.Single(Assert.IsType<List<EntityThirdPartyDto>>(byEntity.Value)).Kind);

        var assessment = Assert.IsType<OkObjectResult>((await _controller.GetAssessment(7, 8)).Result);
        Assert.Equal((8, 7), (Assert.IsType<ThirdPartyAssessmentDto>(assessment.Value).Id, ((ThirdPartyAssessmentDto)assessment.Value!).ThirdPartyId));

        var sbom = Assert.IsType<OkObjectResult>((await _controller.GetSbom(7, 9)).Result);
        Assert.Single(Assert.IsType<ThirdPartySbomDto>(sbom.Value).Components);

        var history = Assert.IsType<OkObjectResult>((await _controller.GetHistory(7)).Result);
        Assert.Equal(7, Assert.Single(Assert.IsType<List<AuditLog>>(history.Value)).EntityId);
    }

    [Fact]
    public async Task TestTheWritesAnswerWithTheirDtos()
    {
        Assert.Equal(Known, Assert.IsType<ThirdPartyDto>(Assert.IsType<OkObjectResult>((await _controller.Create(Body)).Result).Value).Id);
        Assert.Equal(7, Assert.IsType<ThirdPartyDto>(Assert.IsType<OkObjectResult>((await _controller.Update(7, Body)).Result).Value).Id);
        Assert.IsType<NoContentResult>(await _controller.Delete(7));
        Assert.Single(Assert.IsType<ThirdPartyDto>(Assert.IsType<OkObjectResult>((await _controller.Link(7, 20, LinkBody)).Result).Value).Links);
        Assert.IsType<NoContentResult>(await _controller.Unlink(7, 20));
        Assert.IsType<ThirdPartyDto>(Assert.IsType<OkObjectResult>((await _controller.SetSubprocessors(7, SubprocessorsBody)).Result).Value);
        Assert.IsType<ThirdPartyDto>(Assert.IsType<OkObjectResult>((await _controller.SetDataLocations(7, LocationsBody)).Result).Value);
        Assert.Equal(Known, Assert.IsType<ThirdPartyAssessmentDto>(
            Assert.IsType<OkObjectResult>((await _controller.RecordAssessment(7, AssessmentBody)).Result).Value).Id);
        Assert.Equal(HecvatState.Incomplete, Assert.IsType<ThirdPartyAssessmentDto>(
            Assert.IsType<OkObjectResult>((await _controller.ReplaceAnswers(7, 8, AnswersBody)).Result).Value).Result.State);
        Assert.Equal(HecvatState.Voided, Assert.IsType<ThirdPartyAssessmentDto>(
            Assert.IsType<OkObjectResult>((await _controller.VoidAssessment(7, 8, VoidBody)).Result).Value).Result.State);
        Assert.Equal(SbomFormat.CycloneDxJson, Assert.IsType<ThirdPartySbomDto>(
            Assert.IsType<OkObjectResult>((await _controller.ImportSbom(7, SbomBody)).Result).Value).Format);
        Assert.IsType<NoContentResult>(await _controller.DeleteSbom(7, 9));
    }

    /// <summary>The SBOM import caps the body before it is read: the 5 MiB document, escaped, fits; a flood does not.</summary>
    [Fact]
    public void TestTheSbomImportCapsTheRequestBody()
    {
        var limit = typeof(ThirdPartiesController).GetMethod(nameof(ThirdPartiesController.ImportSbom))!
            .GetCustomAttribute<RequestSizeLimitAttribute>();
        Assert.NotNull(limit);
        Assert.Equal(ThirdPartiesController.MaxSbomRequestBytes,
            ((Microsoft.AspNetCore.Http.Metadata.IRequestSizeLimitMetadata)limit!).MaxRequestBodySize);
        Assert.True(ThirdPartiesController.MaxSbomRequestBytes >= 2L * ThirdPartyLimits.MaxSbomDocumentBytes);
        Assert.True(ThirdPartiesController.MaxSbomRequestBytes <= 32L * 1024 * 1024);
    }

    /// <summary>
    /// HI2 (regression, S51 §4.10) — the third-party types are audited, and the generic reader, which cannot apply the
    /// caller's entity scope, refuses them and points at <c>/ThirdParties/{id}/History</c>, as it does for hosts. Without
    /// the refusal a reader of Unit A would read a Unit B supplier's contract terms out of the trail.
    /// </summary>
    [Theory]
    [InlineData("ThirdParty")]
    [InlineData("ThirdPartyLink")]
    [InlineData("ThirdPartySubprocessor")]
    [InlineData("ThirdPartyDataLocation")]
    [InlineData("ThirdPartyAssessment")]
    [InlineData("ThirdPartySbom")]
    public async Task TestHI2_TheGenericTrailRefusesThirdPartyTypes(string entityType)
    {
        Assert.Contains(entityType, ServerServices.Governance.AuditTrailService.AuditedTypes);
        var trail = _serviceProvider.GetRequiredService<AuditTrailController>();

        var bad = Assert.IsType<BadRequestObjectResult>((await trail.GetForRecord(entityType, 1)).Result);

        Assert.Equal("use_third_party_history", Property(bad.Value, "error"));
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

    /// <summary>The two reads no id drives map a failure like any other.</summary>
    [Fact]
    public async Task TestTheListAndConcentrationMapAFailureToo()
    {
        var broken = Substitute.For<IThirdPartiesService>();
        broken.GetThirdPartiesAsync(Arg.Any<ThirdPartyStatus?>(), Arg.Any<bool>())
            .Returns<List<ThirdPartySummaryDto>>(_ => throw new Model.Exceptions.InvalidParameterException("status", "Bad status."));
        broken.GetConcentrationAsync()
            .Returns<ThirdPartyConcentrationReportDto>(_ => throw new InvalidOperationException("boom"));

        var controller = ResolveController<ThirdPartiesController>(s => s.AddSingleton(broken));

        Assert.IsType<BadRequestObjectResult>((await controller.GetThirdParties((ThirdPartyStatus)9)).Result);
        Assert.Equal(StatusCodes.Status500InternalServerError,
            Assert.IsType<StatusCodeResult>((await controller.GetConcentration()).Result).StatusCode);
    }

    // --- arguments ----------------------------------------------------------------------------------------------------

    /// <summary>The acting user's id and every argument reach the service; a controller passing a fixed value would pass above.</summary>
    [Fact]
    public async Task TestTheUserIdAndArgumentsReachTheService()
    {
        var recording = MockedThirdPartiesService.Create();
        var controller = ResolveController<ThirdPartiesController>(s => s.AddSingleton(recording));

        await controller.GetThirdParties();
        await controller.GetThirdParties(ThirdPartyStatus.Terminated, true);
        await controller.GetHistory(7, 25);
        await controller.Create(Body);
        await controller.Update(7, Body);
        await controller.Delete(7);
        await controller.Link(7, 20, LinkBody);
        await controller.Unlink(7, 20);
        await controller.SetSubprocessors(7, SubprocessorsBody);
        await controller.SetDataLocations(7, LocationsBody);
        await controller.RecordAssessment(7, AssessmentBody);
        await controller.ReplaceAnswers(7, 8, AnswersBody);
        await controller.VoidAssessment(7, 8, VoidBody);
        await controller.ImportSbom(7, SbomBody);
        await controller.DeleteSbom(7, 9);

        await recording.Received(1).GetThirdPartiesAsync(null, false);
        await recording.Received(1).GetThirdPartiesAsync(ThirdPartyStatus.Terminated, true);
        await recording.Received(1).GetHistoryAsync(7, 25);
        await recording.Received(1).CreateAsync(Arg.Is<ThirdPartyRequest>(r => r.Name == "Acme Cloud" && r.EntityId == 3 && r.IsCloudProvider), 1);
        await recording.Received(1).UpdateAsync(7, Arg.Is<ThirdPartyRequest>(r => r.Name == "Acme Cloud"), 1);
        await recording.Received(1).DeleteAsync(7, 1);
        await recording.Received(1).LinkAsync(7, 20, Arg.Is<ThirdPartyLinkRequest>(r => r.Description == "Hosts the portal"), 1);
        await recording.Received(1).UnlinkAsync(7, 20, 1);
        await recording.Received(1).SetSubprocessorsAsync(7, Arg.Is<ThirdPartySubprocessorsRequest>(r =>
            r.Subprocessors!.Single().Name == "Hyperscaler"), 1);
        await recording.Received(1).SetDataLocationsAsync(7, Arg.Is<ThirdPartyDataLocationsRequest>(r =>
            r.Locations!.Single().Country == "BR"), 1);
        await recording.Received(1).RecordAssessmentAsync(7, Arg.Is<ThirdPartyAssessmentRequest>(r => r.ExpectedQuestionCount == 4), 1);
        await recording.Received(1).ReplaceAnswersAsync(7, 8, Arg.Is<ThirdPartyAssessmentAnswersRequest>(r =>
            r.Answers!.Single().QuestionId == "HFIH-01"), 1);
        await recording.Received(1).VoidAssessmentAsync(7, 8, Arg.Is<ThirdPartyAssessmentVoidRequest>(r =>
            r.Reason == "Uploaded against the wrong vendor."), 1);
        await recording.Received(1).ImportSbomAsync(7, Arg.Is<ThirdPartySbomRequest>(r => r.FileName == "sbom.json" && r.Document == "{}"), 1);
        await recording.Received(1).DeleteSbomAsync(7, 9, 1);
    }

    /// <summary>A missing body reaches the service as an empty request, so the service names the missing field.</summary>
    [Fact]
    public async Task TestAMissingBodyReachesTheServiceAsAnEmptyRequest()
    {
        var recording = MockedThirdPartiesService.Create();
        var controller = ResolveController<ThirdPartiesController>(s => s.AddSingleton(recording));

        await controller.Create(null);
        await controller.Update(7, null);
        await controller.Link(7, 20, null);
        await controller.SetSubprocessors(7, null);
        await controller.SetDataLocations(7, null);
        await controller.RecordAssessment(7, null);
        await controller.ReplaceAnswers(7, 8, null);
        await controller.VoidAssessment(7, 8, null);
        await controller.ImportSbom(7, null);

        await recording.Received(1).CreateAsync(Arg.Is<ThirdPartyRequest>(r => r != null && r.Name == null), 1);
        await recording.Received(1).UpdateAsync(7, Arg.Is<ThirdPartyRequest>(r => r != null && r.Name == null), 1);
        await recording.Received(1).LinkAsync(7, 20, Arg.Is<ThirdPartyLinkRequest>(r => r != null && r.Description == null), 1);
        await recording.Received(1).SetSubprocessorsAsync(7, Arg.Is<ThirdPartySubprocessorsRequest>(r => r != null && r.Subprocessors == null), 1);
        await recording.Received(1).SetDataLocationsAsync(7, Arg.Is<ThirdPartyDataLocationsRequest>(r => r != null && r.Locations == null), 1);
        await recording.Received(1).RecordAssessmentAsync(7, Arg.Is<ThirdPartyAssessmentRequest>(r => r != null && r.Variant == null), 1);
        await recording.Received(1).ReplaceAnswersAsync(7, 8, Arg.Is<ThirdPartyAssessmentAnswersRequest>(r => r != null && r.Answers == null), 1);
        await recording.Received(1).VoidAssessmentAsync(7, 8, Arg.Is<ThirdPartyAssessmentVoidRequest>(r => r != null && r.Reason == null), 1);
        await recording.Received(1).ImportSbomAsync(7, Arg.Is<ThirdPartySbomRequest>(r => r != null && r.Document == null), 1);
    }
}

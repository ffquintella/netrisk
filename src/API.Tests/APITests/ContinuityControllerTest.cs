using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.Mock;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Continuity;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Stage 9.3 (S43 §8) — <see cref="ContinuityController"/>: each action's success shape, and the mapping
/// of every domain exception onto the status code the other controllers use for it.
/// </summary>
[TestSubject(typeof(ContinuityController))]
public class ContinuityControllerTest : BaseControllerTest
{
    private readonly ContinuityController _controller;

    public ContinuityControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<ContinuityController>();
    }

    private static string? ErrorOf(object? body) =>
        JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement.GetProperty("error").GetString();

    private static string? PropertyOf(object? body, string name) =>
        JsonDocument.Parse(JsonSerializer.Serialize(body)).RootElement.GetProperty(name).GetString();

    // --- success shapes -------------------------------------------------------------------------

    [Fact]
    public async Task TestReadsAnswer200WithTheirDtos()
    {
        var subjects = Assert.IsType<OkObjectResult>((await _controller.GetSubjects()).Result);
        Assert.Single(Assert.IsType<List<ContinuitySubjectDto>>(subjects.Value));

        var profile = Assert.IsType<OkObjectResult>((await _controller.GetProfile(MockedContinuityService.NewBia)).Result);
        Assert.Equal(0.5m, Assert.IsType<ContinuityProfileDto>(profile.Value).Threat.ThreatWeight);

        var tests = Assert.IsType<OkObjectResult>((await _controller.GetRestorationTests(MockedContinuityService.NewBia)).Result);
        Assert.Single(Assert.IsType<List<RestorationTestDto>>(tests.Value));

        var metric = Assert.IsType<OkObjectResult>((await _controller.GetRestorationVerificationMetric()).Result);
        Assert.Equal(1.5m, Assert.IsType<RestorationVerificationMetricDto>(metric.Value).ThreatenedCriticalProcessesWeighted);

        var settings = Assert.IsType<OkObjectResult>((await _controller.GetSettings()).Result);
        Assert.Equal(365, Assert.IsType<ContinuitySettingsDto>(settings.Value).RestorationTestValidityDays);
    }

    [Fact]
    public async Task TestANewBiaIsCreatedAndPointsAtTheProfile()
    {
        var result = await _controller.SaveBia(MockedContinuityService.NewBia, new BusinessImpactAnalysisRequest { RtoMinutes = 240 });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(ContinuityController.GetProfile), created.ActionName);
        Assert.Equal(MockedContinuityService.NewBia, created.RouteValues!["entityId"]);
        Assert.Equal(240, Assert.IsType<BusinessImpactAnalysisDto>(created.Value).RtoMinutes);
    }

    [Fact]
    public async Task TestReplacingABiaAnswers200()
    {
        var result = await _controller.SaveBia(MockedContinuityService.ExistingBia, new BusinessImpactAnalysisRequest { RtoMinutes = 240 });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(MockedContinuityService.ExistingBia, Assert.IsType<BusinessImpactAnalysisDto>(ok.Value).EntityId);
    }

    [Fact]
    public async Task TestDeletesAnswer204()
    {
        Assert.IsType<NoContentResult>(await _controller.DeleteBia(MockedContinuityService.NewBia));
        Assert.IsType<NoContentResult>(await _controller.DeleteDependency(MockedContinuityService.NewBia, 3));
    }

    [Fact]
    public async Task TestADependencyIsCreated()
    {
        var result = await _controller.AddDependency(MockedContinuityService.NewBia,
            new BiaDependencyCreateRequest { ProviderEntityId = 20 });

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(ContinuityController.GetProfile), created.ActionName);
        Assert.Equal(20, Assert.IsType<BiaDependencyDto>(created.Value).ProviderEntityId);
    }

    [Fact]
    public async Task TestATestIsRecordedAndVoided()
    {
        var recorded = await _controller.RecordRestorationTest(MockedContinuityService.NewBia,
            new RestorationTestCreateRequest { TestedAt = DateTime.UtcNow.AddDays(-1), AchievedRtoMinutes = 200 });
        var created = Assert.IsType<CreatedAtActionResult>(recorded.Result);
        Assert.Equal(nameof(ContinuityController.GetRestorationTests), created.ActionName);

        var voided = await _controller.VoidRestorationTest(MockedContinuityService.NewBia, 5,
            new RestorationTestVoidRequest { Reason = "Recorded against the wrong process" });
        var ok = Assert.IsType<OkObjectResult>(voided.Result);
        Assert.Equal("Recorded against the wrong process", Assert.IsType<RestorationTestDto>(ok.Value).VoidReason);
    }

    [Fact]
    public async Task TestTheParametersAreSaved()
    {
        var result = await _controller.SaveSettings(new ContinuitySettingsRequest
            { RestorationTestValidityDays = 30, UnverifiedThreatWeight = 0.25m });

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((30, 0.25m), (Assert.IsType<ContinuitySettingsDto>(ok.Value).RestorationTestValidityDays,
            ((ContinuitySettingsDto)ok.Value!).UnverifiedThreatWeight));
    }

    // --- error mapping --------------------------------------------------------------------------

    [Fact]
    public async Task TestAnInvalidParameterIsA400NamingIt()
    {
        var bia = await _controller.SaveBia(MockedContinuityService.Invalid, new BusinessImpactAnalysisRequest());
        var bad = Assert.IsType<BadRequestObjectResult>(bia.Result);
        Assert.Equal("invalid_parameter", ErrorOf(bad.Value));
        Assert.Equal("RtoMinutes", PropertyOf(bad.Value, "ParameterName"));

        var settings = await _controller.SaveSettings(new ContinuitySettingsRequest { RestorationTestValidityDays = MockedContinuityService.Invalid });
        Assert.Equal("continuity_restoration_test_validity_days",
            PropertyOf(Assert.IsType<BadRequestObjectResult>(settings.Result).Value, "ParameterName"));
    }

    [Fact]
    public async Task TestAScopedCallerIsA403NamingGlobalScope()
    {
        var bia = await _controller.SaveBia(MockedContinuityService.Forbidden, new BusinessImpactAnalysisRequest { RtoMinutes = 1 });
        var forbidden = Assert.IsType<ObjectResult>(bia.Result);
        Assert.Equal(StatusCodes.Status403Forbidden, forbidden.StatusCode);
        Assert.Equal("insufficient_permission", ErrorOf(forbidden.Value));
        Assert.Equal("global_scope", PropertyOf(forbidden.Value, "Permission"));

        Assert.Equal(StatusCodes.Status403Forbidden,
            Assert.IsType<ObjectResult>(await _controller.DeleteBia(MockedContinuityService.Forbidden)).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>((await _controller.SaveSettings(
            new ContinuitySettingsRequest { RestorationTestValidityDays = MockedContinuityService.Forbidden })).Result).StatusCode);
    }

    [Fact]
    public async Task TestNotFoundIsA404OnEveryAction()
    {
        const int missing = MockedContinuityService.Missing;

        Assert.IsType<NotFoundResult>((await _controller.GetProfile(missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.GetRestorationTests(missing)).Result);
        Assert.IsType<NotFoundResult>((await _controller.SaveBia(missing, new BusinessImpactAnalysisRequest())).Result);
        Assert.IsType<NotFoundResult>(await _controller.DeleteBia(missing));
        Assert.IsType<NotFoundResult>((await _controller.AddDependency(1, new BiaDependencyCreateRequest { ProviderEntityId = missing })).Result);
        Assert.IsType<NotFoundResult>(await _controller.DeleteDependency(1, missing));
        Assert.IsType<NotFoundResult>((await _controller.RecordRestorationTest(missing, new RestorationTestCreateRequest())).Result);
        Assert.IsType<NotFoundResult>((await _controller.VoidRestorationTest(1, missing, new RestorationTestVoidRequest())).Result);
    }

    [Fact]
    public async Task TestADuplicateIsAConflict()
    {
        var result = await _controller.AddDependency(1, new BiaDependencyCreateRequest { ProviderEntityId = MockedContinuityService.Conflicting });

        Assert.Equal("already_exists", ErrorOf(Assert.IsType<ConflictObjectResult>(result.Result).Value));
    }

    [Fact]
    public async Task TestABrokenRuleIsA422NamingTheRule()
    {
        var profile = await _controller.GetProfile(MockedContinuityService.NotSubject);
        Assert.Equal("entity_not_bia_subject", ErrorOf(Assert.IsType<UnprocessableEntityObjectResult>(profile.Result).Value));

        var bia = await _controller.SaveBia(MockedContinuityService.NotSubject, new BusinessImpactAnalysisRequest());
        Assert.Equal("entity_not_bia_subject", ErrorOf(Assert.IsType<UnprocessableEntityObjectResult>(bia.Result).Value));
    }

    [Fact]
    public async Task TestAnUnexpectedFailureIsA500WithoutDetail()
    {
        var failing = Substitute.For<IContinuityService>();
        failing.GetRestorationVerificationMetricAsync().ThrowsAsync(new InvalidOperationException("connection string leaked here"));
        var controller = ResolveController<ContinuityController>(s => s.AddSingleton(failing));

        var result = await controller.GetRestorationVerificationMetric();

        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
    }

    // --- the principal and the user id reach the service ------------------------------------------

    /// <summary>The profile's caller flags are computed from the principal handed to the service, and the
    /// writes record the acting user's id; a controller passing null or a fixed value would be invisible
    /// to the mocks above.</summary>
    [Fact]
    public async Task TestTheRequestPrincipalAndUserIdReachTheService()
    {
        var recording = MockedContinuityService.Create();
        var controller = ResolveController<ContinuityController>(s => s.AddSingleton(recording));

        await controller.GetProfile(MockedContinuityService.NewBia);
        await controller.SaveBia(MockedContinuityService.NewBia, new BusinessImpactAnalysisRequest { RtoMinutes = 60 });
        await controller.RecordRestorationTest(MockedContinuityService.NewBia, new RestorationTestCreateRequest());

        await recording.Received(1).GetProfileAsync(MockedContinuityService.NewBia,
            Arg.Is<ClaimsPrincipal?>(p => p != null && p.Identity!.Name == "testUser"));
        await recording.Received(1).SaveBiaAsync(MockedContinuityService.NewBia,
            Arg.Is<BusinessImpactAnalysisRequest>(r => r.RtoMinutes == 60), 1);
        await recording.Received(1).RecordRestorationTestAsync(MockedContinuityService.NewBia,
            Arg.Any<RestorationTestCreateRequest>(), 1);
    }
}

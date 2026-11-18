using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using DAL.Enums;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Integrations;
using ServerServices.Integrations.Tenable;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// M56, T265 (S40 §8.7, §12.3): each /Tenable action's success shape — 201 with the location on create, 202 with the log id
/// on a queued sync, 204 on delete — and every refusal as <c>{ error, message }</c> with its status. No view carries a key.
/// </summary>
[TestSubject(typeof(TenableController))]
public class TenableControllerTest : BaseControllerTest
{
    private readonly TenableController _controller;

    public TenableControllerTest()
    {
        _controller = _serviceProvider.GetRequiredService<TenableController>();
        _controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
    }

    private static TenableConnectionUpsert Body(string name = "Tenable FGV") =>
        new(name, TenableProduct.VulnerabilityManagement, null, 8, true, 24, true, true, true, true, 1, 90, false, false,
            "access", "secret", null, null, false, false);

    private static (int? Status, string? Error) Refusal(IActionResult? result)
    {
        var refused = Assert.IsType<ObjectResult>(result);
        var error = JsonDocument.Parse(JsonSerializer.Serialize(refused.Value)).RootElement.GetProperty("error").GetString();
        return (refused.StatusCode, error);
    }

    [Fact]
    public async Task TestTheReadsAnswer200AndNoViewCarriesAKey()
    {
        Assert.Single(Assert.IsAssignableFrom<IReadOnlyList<TenableProductDto>>(
            Assert.IsType<OkObjectResult>(_controller.GetProducts().Result).Value));

        var all = Assert.IsType<List<TenableConnectionView>>(
            Assert.IsType<OkObjectResult>((await _controller.GetAll()).Result).Value);
        var one = Assert.IsType<TenableConnectionView>(Assert.IsType<OkObjectResult>((await _controller.Get(1)).Result).Value);
        Assert.Equal((1, true, true), (Assert.Single(all).Id, one.HasAccessKey, one.HasSecretKey));

        var json = JsonSerializer.Serialize(one);
        Assert.DoesNotContain("\"AccessKey\"", json);
        Assert.DoesNotContain("\"SecretKey\"", json);

        var log = Assert.IsType<List<IntegrationSyncLog>>(Assert.IsType<OkObjectResult>((await _controller.GetLog()).Result).Value);
        Assert.Equal(IntegrationSyncStatus.Queued, Assert.Single(log).Status);
    }

    [Fact]
    public async Task TestCreateUpdateTestAndDeleteAnswerTheirSuccess()
    {
        var created = Assert.IsType<CreatedResult>((await _controller.Create(Body())).Result);
        Assert.Equal(("Tenable/5", 5), (created.Location, Assert.IsType<TenableConnectionView>(created.Value).Id));

        Assert.Equal(1, Assert.IsType<TenableConnectionView>(
            Assert.IsType<OkObjectResult>((await _controller.Update(1, Body())).Result).Value).Id);
        Assert.True(Assert.IsType<TenableTestResult>(Assert.IsType<OkObjectResult>((await _controller.Test(1)).Result).Value).Success);
        Assert.IsType<NoContentResult>(await _controller.Delete(1));
    }

    [Fact]
    public async Task TestAManualSyncIsQueuedWith202AndNeverRunInTheApi()
    {
        var accepted = Assert.IsType<AcceptedResult>((await _controller.Sync(1)).Result);

        Assert.Equal(77, Assert.IsType<TenableSyncQueuedDto>(accepted.Value).LogId);
    }

    [Fact]
    public async Task TestEveryRefusalIsMappedWithItsStatusAndCode()
    {
        Assert.Equal((409, TenableErrors.SyncRunning), Refusal((await _controller.Sync(409)).Result));
        Assert.Equal((409, TenableErrors.ConnectionDisabled), Refusal((await _controller.Sync(410)).Result));
        Assert.Equal((404, TenableErrors.NotFound), Refusal((await _controller.Sync(404)).Result));
        Assert.Equal((404, TenableErrors.NotFound), Refusal((await _controller.Get(404)).Result));
        Assert.Equal((404, TenableErrors.NotFound), Refusal(await _controller.Delete(404)));
        Assert.Equal((404, TenableErrors.NotFound), Refusal((await _controller.Test(404)).Result));
        Assert.Equal((400, TenableErrors.BaseUrlInvalid), Refusal((await _controller.Create(Body("bad-url"))).Result));
        Assert.Equal((400, TenableErrors.CredentialsRequired), Refusal((await _controller.Update(1, Body("no-keys"))).Result));
        Assert.Equal((400, TenableErrors.InvalidParameter), Refusal((await _controller.Create(null)).Result));
    }

    [Fact]
    public async Task TestAnUnexpectedFailureIs500WithoutDetail()
    {
        var result = Assert.IsType<StatusCodeResult>((await _controller.Create(Body("boom"))).Result);

        Assert.Equal(StatusCodes.Status500InternalServerError, result.StatusCode);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using API.Controllers;
using API.Security;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.Entities;
using Model.Exceptions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// M55, T264 (S40 §8.5): the three risk-context actions of <see cref="EntitiesController"/> — what they pass to the
/// service from the caller's claims, and how each refusal is answered (status, <c>{ error, message }</c>, and
/// <c>details.entityId</c> when the refusal names a row). The rules themselves are <c>EntityRiskContextServiceTest</c>.
/// </summary>
[TestSubject(typeof(EntitiesController))]
public class EntitiesRiskContextControllerTest : BaseControllerTest
{
    private readonly IEntitiesService _service = Substitute.For<IEntitiesService>();

    private EntitiesController Controller(params Claim[] claims)
    {
        var all = new List<Claim> { new(ClaimTypes.Name, "testUser") };
        all.AddRange(claims);
        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(new DefaultHttpContext
            { User = new ClaimsPrincipal(new ClaimsIdentity(all, "Bearer")) });

        return ResolveController<EntitiesController>(s =>
        {
            s.AddSingleton(_service);
            s.AddSingleton(accessor);
        });
    }

    private static Claim Permission(string key) => new("Permission", key);

    private static EntityRiskContextBatchRequest Batch() =>
        new("Calibrated with the business owner", [new EntityRiskContextChange(12, 5, null, null)]);

    private static (int Status, string Error, object? EntityId) Refusal(IActionResult? result)
    {
        var refused = Assert.IsType<ObjectResult>(result);
        var body = refused.Value!;
        var error = (string)body.GetType().GetProperty("error")!.GetValue(body)!;
        Assert.False(string.IsNullOrWhiteSpace((string?)body.GetType().GetProperty("message")!.GetValue(body)));
        var details = body.GetType().GetProperty("details")?.GetValue(body);
        return (refused.StatusCode!.Value, error, details?.GetType().GetProperty("entityId")!.GetValue(details));
    }

    // --- GET /Entities/RiskContext ------------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheRowsAreReadForTheRequestedTypes()
    {
        var row = new EntityRiskContextRowDto(12, "P", "businessProcess", 10, true, 3, false, null, false, null, null, null);
        _service.GetRiskContextAsync(Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<CancellationToken>()).Returns([row]);

        var result = await Controller().GetRiskContext("application, businessProcess,,");

        Assert.Equal(row, Assert.Single(Assert.IsType<List<EntityRiskContextRowDto>>(Assert.IsType<OkObjectResult>(result.Result).Value)));
        await _service.Received(1).GetRiskContextAsync(
            Arg.Is<IReadOnlyCollection<string>?>(t => t!.SequenceEqual(new[] { "application", "businessProcess" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TestWithoutTypesTheServiceDefaultApplies()
    {
        _service.GetRiskContextAsync(Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<CancellationToken>()).Returns([]);

        Assert.IsType<OkObjectResult>((await Controller().GetRiskContext()).Result);
        await _service.Received(1).GetRiskContextAsync(null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TestAFailedReadIs500()
    {
        _service.GetRiskContextAsync(Arg.Any<IReadOnlyCollection<string>?>(), Arg.Any<CancellationToken>())
            .ThrowsAsync(new InvalidOperationException("boom"));

        var result = await Controller().GetRiskContext();

        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
    }

    // --- PUT /Entities/RiskContext ------------------------------------------------------------------------------------

    [Fact]
    public void TestTheBatchRequiresTheRiskContextPermission()
    {
        var action = typeof(EntitiesController).GetMethod(nameof(EntitiesController.SaveRiskContext))!;

        Assert.Equal(EntityPermissions.RiskContext, action.GetCustomAttribute<PermissionAuthorizeAttribute>()!.Permission);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(false, false)]
    public async Task TestTheBatchIsAppliedWithWhatTheCallerHolds(bool permission, bool admin)
    {
        var claims = new List<Claim>();
        if (permission) claims.Add(Permission(EntityPermissions.RiskContext));
        if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
        var batch = Batch();
        _service.SaveRiskContextAsync(batch, Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<CancellationToken>())
            .Returns(new EntityRiskContextBatchResultDto(1, "b1"));

        var result = await Controller(claims.ToArray()).SaveRiskContext(batch);

        Assert.Equal(new EntityRiskContextBatchResultDto(1, "b1"), Assert.IsType<OkObjectResult>(result.Result).Value);
        await _service.Received(1).SaveRiskContextAsync(batch, 1, permission || admin, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(EntityRiskContextErrors.FieldNotSupportedForType, 400, 40)]
    [InlineData(EntityRiskContextErrors.CriticalityOutOfRange, 400, 12)]
    [InlineData(EntityRiskContextErrors.JustificationTooShort, 400, null)]
    [InlineData(EntityRiskContextErrors.EntityNotFound, 404, 999)]
    [InlineData(EntityWriteErrors.RiskContextRequiresGlobalScope, 403, null)]
    public async Task TestABatchRefusalKeepsItsStatusCodeAndRow(string error, int status, int? entityId)
    {
        _service.SaveRiskContextAsync(Arg.Any<EntityRiskContextBatchRequest>(), Arg.Any<int>(), Arg.Any<bool>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new EntityWriteRefusedException(error, "Refused.", status, entityId));

        var result = await Controller(Permission(EntityPermissions.RiskContext)).SaveRiskContext(Batch());

        Assert.Equal((status, error, (object?)entityId), Refusal(result.Result));
    }

    [Fact]
    public async Task TestAFailedSaveIs500()
    {
        _service.SaveRiskContextAsync(Arg.Any<EntityRiskContextBatchRequest>(), Arg.Any<int>(), Arg.Any<bool>(),
            Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("database down"));

        var result = await Controller(Permission(EntityPermissions.RiskContext)).SaveRiskContext(Batch());

        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
    }

    // --- GET /Entities/{id}/History -----------------------------------------------------------------------------------

    [Fact]
    public async Task TestTheHistoryIsReadWithTheCallersPermissionsAndRole()
    {
        var entry = new EntityHistoryEntryDto(DateTime.UtcNow, "Update", "criticality", "3", "5", "ana", "b1", "why");
        _service.GetHistoryAsync(12, 50, Arg.Any<bool>(), Arg.Any<IReadOnlyCollection<string>>(), Arg.Any<CancellationToken>())
            .Returns([entry]);

        var result = await Controller(Permission(EntityPermissions.Manage), Permission("riskmanagement"),
            new Claim(ClaimTypes.Role, "Admin")).GetHistory(12, 50);

        Assert.Equal(entry, Assert.Single(Assert.IsType<List<EntityHistoryEntryDto>>(Assert.IsType<OkObjectResult>(result.Result).Value)));
        await _service.Received(1).GetHistoryAsync(12, 50, true,
            Arg.Is<IReadOnlyCollection<string>>(p => p.Order().SequenceEqual(new[] { EntityPermissions.Manage, "riskmanagement" })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task TestTheHistoryDefaultLimitIs200AndNoRoleIsNotAdmin()
    {
        _service.GetHistoryAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<CancellationToken>()).Returns([]);

        await Controller().GetHistory(12);

        await _service.Received(1).GetHistoryAsync(12, 200, false, Arg.Is<IReadOnlyCollection<string>>(p => p.Count == 0),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(EntityRiskContextErrors.InvalidLimit, 400, null)]
    [InlineData(EntityRiskContextErrors.HistoryRequiresPermission, 403, null)]
    [InlineData(EntityRiskContextErrors.EntityNotFound, 404, 12)]
    public async Task TestAHistoryRefusalKeepsItsStatusAndCode(string error, int status, int? entityId)
    {
        _service.GetHistoryAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<CancellationToken>()).ThrowsAsync(new EntityRequestRefusedException(error, "Refused.", status, entityId));

        var result = await Controller().GetHistory(12);

        Assert.Equal((status, error, (object?)entityId), Refusal(result.Result));
    }

    [Fact]
    public async Task TestAFailedHistoryReadIs500()
    {
        _service.GetHistoryAsync(Arg.Any<int>(), Arg.Any<int>(), Arg.Any<bool>(), Arg.Any<IReadOnlyCollection<string>>(),
            Arg.Any<CancellationToken>()).ThrowsAsync(new InvalidOperationException("boom"));

        var result = await Controller(Permission(EntityPermissions.Manage)).GetHistory(12);

        Assert.Equal(StatusCodes.Status500InternalServerError, Assert.IsType<StatusCodeResult>(result.Result).StatusCode);
    }
}

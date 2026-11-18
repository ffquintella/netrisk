using System;
using System.Collections.Generic;
using System.Threading;
using DAL.Entities;
using DAL.Enums;
using Model.Integrations;
using NSubstitute;
using ServerServices.Integrations.Tenable;

namespace API.Tests.Mock;

/// <summary>
/// The Tenable service's double (M56, T265; S40 §12.3 <c>MockedTenableService</c>): connection 1 exists, id 404 is missing,
/// id 409 is already queued, id 410 is disabled, and the upsert name chooses the refusal — <c>bad-url</c>/400, <c>boom</c>/500.
/// </summary>
public static class MockedTenableService
{
    public static TenableConnectionView View(int id = 1, string name = "Tenable FGV") =>
        new(id, name, TenableProduct.VulnerabilityManagement, "https://cloud.tenable.com", 8, true, 24, true, true, true, true, 1,
            90, false, false, true, true, null, null, null, null, null);

    private static void Gate(TenableConnectionUpsert request)
    {
        switch (request.Name)
        {
            case "bad-url": throw TenableException.BadRequest(TenableErrors.BaseUrlInvalid, "bad url");
            case "no-keys": throw TenableException.BadRequest(TenableErrors.CredentialsRequired, "no keys");
            case "boom": throw new InvalidOperationException("boom");
        }
    }

    private static void Known(int id)
    {
        if (id == 404) throw TenableException.NotFound("gone");
    }

    public static ITenableService Create()
    {
        var service = Substitute.For<ITenableService>();
        service.GetProducts().Returns(new List<TenableProductDto>
        {
            new(TenableProduct.VulnerabilityManagement, "TenableProduct_VulnerabilityManagement", "https://cloud.tenable.com", false)
        });
        service.GetConnectionsAsync(Arg.Any<CancellationToken>()).Returns(new List<TenableConnectionView> { View() });
        service.GetConnectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => { Known(call.ArgAt<int>(0)); return View(call.ArgAt<int>(0)); });
        service.CreateConnectionAsync(Arg.Any<TenableConnectionUpsert>(), Arg.Any<CancellationToken>())
            .Returns(call => { Gate(call.ArgAt<TenableConnectionUpsert>(0)); return View(5, call.ArgAt<TenableConnectionUpsert>(0).Name); });
        service.UpdateConnectionAsync(Arg.Any<int>(), Arg.Any<TenableConnectionUpsert>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Known(call.ArgAt<int>(0));
                Gate(call.ArgAt<TenableConnectionUpsert>(1));
                return View(call.ArgAt<int>(0), call.ArgAt<TenableConnectionUpsert>(1).Name);
            });
        service.DeleteConnectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => { Known(call.ArgAt<int>(0)); return System.Threading.Tasks.Task.CompletedTask; });
        service.TestConnectionAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                Known(call.ArgAt<int>(0));
                return new TenableTestResult(true, [new TenableTestStepDto("authentication", true, null, null)]);
            });
        service.QueueSyncAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<int>(0) switch
            {
                404 => throw TenableException.NotFound("gone"),
                409 => throw TenableException.Conflict(TenableErrors.SyncRunning, "running"),
                410 => throw TenableException.Conflict(TenableErrors.ConnectionDisabled, "disabled"),
                _ => new TenableSyncQueuedDto(77)
            });
        service.GetSyncLogAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
            .Returns(new List<IntegrationSyncLog>
            {
                new() { Id = 77, Integration = IntegrationKind.Tenable, ConnectionId = 1, Status = IntegrationSyncStatus.Queued }
            });
        return service;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using API.Controllers;
using API.Middleware;
using API.Tests.Mock;
using DAL.Context;
using DAL.Entities;
using DAL.Exceptions;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO;
using Model.Exceptions;
using NSubstitute;
using Serilog;
using ServerServices.Filtering;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;
using Host = DAL.Entities.Host;

namespace API.Tests.APITests;

/// <summary>
/// M58, T282 (S39 D-20, D-21): the HTTP contract of host-to-entity assignment — a nonexistent entity is a 400, one
/// outside the caller's scope reaches the 403 middleware rather than dying as a 500 — the backfill report, and the
/// target application and repository of a code-scanner import.
/// </summary>
[TestSubject(typeof(HostsController))]
public class HostEntityAssignmentControllerTest : BaseControllerTest
{
    private static HostsController Hosts(IHostsService service)
    {
        var controller = ResolveController<HostsController>(s => s.AddSingleton(service));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return controller;
    }

    private static Host SampleHost(int id = 1, int? entityId = 7) => new()
    {
        Id = id, Ip = "10.0.0.1", HostName = "host-" + id, Source = "manual", Status = 1, EntityId = entityId
    };

    [Fact]
    public void TestAssigningANonexistentEntityIsABadRequest()
    {
        var service = Substitute.For<IHostsService>();
        service.When(s => s.Update(Arg.Any<Host>()))
            .Do(_ => throw new InvalidParameterException(nameof(Host.EntityId), "Entity 999 does not exist."));
        service.Create(Arg.Any<Host>())
            .Returns(_ => throw new InvalidParameterException(nameof(Host.EntityId), "Entity 999 does not exist."));

        Assert.IsType<BadRequestObjectResult>(Hosts(service).Update(1, SampleHost(1, 999)).Result);
        Assert.IsType<BadRequestObjectResult>(Hosts(service).Create(SampleHost(0, 999)).Result);
    }

    /// <summary>
    /// The write guard's refusal used to be swallowed by the generic catch and answered 500; it now propagates to
    /// <c>EntityScopeViolationMiddleware</c>, which answers 403.
    /// </summary>
    [Fact]
    public void TestAnEntityOutsideTheCallersScopeReachesTheForbiddenMiddleware()
    {
        var service = Substitute.For<IHostsService>();
        service.When(s => s.Update(Arg.Any<Host>()))
            .Do(_ => throw new EntityScopeViolationException(nameof(Host), 8, "7"));
        service.Create(Arg.Any<Host>()).Returns(_ => throw new EntityScopeViolationException(nameof(Host), 8, "7"));

        Assert.Throws<EntityScopeViolationException>(() => Hosts(service).Update(1, SampleHost(1, 8)));
        Assert.Throws<EntityScopeViolationException>(() => Hosts(service).Create(SampleHost(0, 8)));
    }

    /// <summary>
    /// What a scoped caller sees for an entity it may not use, through the real service and the 403 middleware: the status
    /// and body, with the id the caller sent normalised away.
    /// </summary>
    private static async Task<(int Status, string Body)> Respond(Func<ActionResult?> action, string sentId)
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        ActionResult? result = null;

        await new EntityScopeViolationMiddleware(_ =>
        {
            result = action();
            return Task.CompletedTask;
        }, new LoggerConfiguration().CreateLogger()).InvokeAsync(context);

        if (result is IStatusCodeActionResult { StatusCode: { } code })
            return (code, ((result as ObjectResult)?.Value?.ToString() ?? "").Replace(sentId, "{id}"));

        context.Response.Body.Position = 0;
        var body = await new StreamReader(context.Response.Body).ReadToEndAsync();
        return (context.Response.StatusCode, body.Replace(sentId, "{id}"));
    }

    /// <summary>
    /// The defect: the hosts service checked that the entity existed before checking the caller's scope, so a scoped
    /// caller got 400 "Entity 999 does not exist" for an unknown id and 403 for a real one outside its scope — an oracle
    /// for which entity ids exist. Both are now the same 403, on create and on update.
    /// </summary>
    [Fact]
    public async Task TestAScopedCallerGetsTheSameAnswerForAnUnknownAndAnOutOfScopeEntity()
    {
        var dal = new InMemoryDalService(nameof(TestAScopedCallerGetsTheSameAnswerForAnUnknownAndAnOutOfScopeEntity));
        using (var ctx = dal.GetContext())
        {
            foreach (var id in new[] { Portal, Billing })
                ctx.Entities.Add(new Entity { Id = id, DefinitionName = "application", DefinitionVersion = "1", Status = "active" });
            ctx.Hosts.Add(SampleHost(1, Portal));
            ctx.SaveChanges();
        }

        dal.Scope = EntityScope.ForEntities([Portal]);
        var hosts = Hosts(new ServerServices.Services.HostsService(Substitute.For<Serilog.ILogger>(),
            Substitute.For<IEntityFilterMapperProvider>(), dal));

        var unknownCreate = await Respond(() => hosts.Create(SampleHost(0, 999)).Result, "999");
        var foreignCreate = await Respond(() => hosts.Create(SampleHost(0, Billing)).Result, Billing.ToString());
        var unknownUpdate = await Respond(() => hosts.Update(1, SampleHost(1, 999)).Result, "999");
        var foreignUpdate = await Respond(() => hosts.Update(1, SampleHost(1, Billing)).Result, Billing.ToString());

        Assert.Equal(StatusCodes.Status403Forbidden, unknownCreate.Status);
        Assert.Equal(foreignCreate, unknownCreate);
        Assert.Equal(foreignUpdate, unknownUpdate);
        Assert.Equal(foreignCreate, foreignUpdate);

        // Nothing was written: the host keeps its entity, and no second host appeared.
        dal.Scope = EntityScope.Unrestricted;
        using var check = dal.GetContext();
        Assert.Equal(Portal, check.Hosts.Single().EntityId);
    }

    [Fact]
    public void TestUpdatingAHostThatDoesNotExistIsNotFound()
    {
        var service = Substitute.For<IHostsService>();
        service.When(s => s.Update(Arg.Any<Host>()))
            .Do(_ => throw new DataNotFoundException("hosts", "1", new Exception("Host not found")));

        Assert.IsType<NotFoundResult>(Hosts(service).Update(1, SampleHost()).Result);
    }

    [Fact]
    public async Task TestTheBackfillReport()
    {
        var service = Substitute.For<IHostsService>();
        service.GetUnassignedAsync(25).Returns(new List<UnassignedHostDto>
        {
            new() { Id = 3, HostName = "db-01", SuggestedEntityId = 7, SuggestedBy = "visionone:5" }
        });

        var ok = Assert.IsType<OkObjectResult>((await Hosts(service).GetUnassigned(25)).Result);
        var row = Assert.Single(Assert.IsType<List<UnassignedHostDto>>(ok.Value));
        Assert.Equal((3, (int?)7), (row.Id, row.SuggestedEntityId));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(IHostsService.MaxUnassignedLimit + 1)]
    public async Task TestTheBackfillReportRefusesAnOutOfRangeLimit(int limit)
    {
        var service = Substitute.For<IHostsService>();

        Assert.IsType<BadRequestObjectResult>((await Hosts(service).GetUnassigned(limit)).Result);
        await service.DidNotReceive().GetUnassignedAsync(Arg.Any<int>());
    }

    [Fact]
    public async Task TestTheBackfillReportsUnexpectedFailureIsA500()
    {
        var service = Substitute.For<IHostsService>();
        service.GetUnassignedAsync(Arg.Any<int>()).Returns<Task<List<UnassignedHostDto>>>(_ => throw new Exception("boom"));

        var status = Assert.IsType<StatusCodeResult>((await Hosts(service).GetUnassigned()).Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    // --- the target application and repository of a code-scanner import (D-21) -------------------------------------

    private const int Portal = 40, Billing = 41;

    private static (VulnerabilitiesController Controller, InMemoryDalService Dal) Imports(string db)
    {
        var dal = new InMemoryDalService(db);
        using (var ctx = dal.GetContext())
        {
            foreach (var id in new[] { Portal, Billing })
                ctx.Entities.Add(new Entity { Id = id, DefinitionName = "application", DefinitionVersion = "1", Status = "active" });
            ctx.SaveChanges();
        }

        var controller = ResolveController<VulnerabilitiesController>(s => s.AddSingleton<IDalService>(dal));
        controller.ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        return (controller, dal);
    }

    [Fact]
    public async Task TestATargetApplicationOutsideTheCallersScopeIsForbidden()
    {
        var (controller, dal) = Imports(nameof(TestATargetApplicationOutsideTheCallersScopeIsForbidden));
        dal.Scope = EntityScope.ForEntities([Portal]);

        var byFile = await controller.ImportWithImporter("semgrep", "abc123", entityId: Billing);
        var direct = await controller.ImportDirect("semgrep", entityId: Billing);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(byFile.Result).StatusCode);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(direct.Result).StatusCode);
    }

    [Fact]
    public async Task TestAScopedCallerProbingAnUnknownEntityLearnsNothingAboutIt()
    {
        var (controller, dal) = Imports(nameof(TestAScopedCallerProbingAnUnknownEntityLearnsNothingAboutIt));
        dal.Scope = EntityScope.ForEntities([Portal]);

        var result = await controller.ImportWithImporter("semgrep", "abc123", entityId: 999);

        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(result.Result).StatusCode);
    }

    [Fact]
    public async Task TestATargetApplicationThatDoesNotExistIsABadRequest()
    {
        var (controller, _) = Imports(nameof(TestATargetApplicationThatDoesNotExistIsABadRequest));

        var result = await controller.ImportWithImporter("semgrep", "abc123", entityId: 999);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Theory]
    [InlineData(256)]
    [InlineData(-1)]
    public async Task TestAnUnusableRepositoryIsABadRequest(int length)
    {
        var (controller, _) = Imports(nameof(TestAnUnusableRepositoryIsABadRequest) + length);
        var repository = length < 0 ? "acme/web\nrm" : new string('r', length);

        var result = await controller.ImportWithImporter("semgrep", "abc123", repository: repository);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    /// <summary>A valid target passes validation: what stops this call is the missing upload, not the target.</summary>
    [Fact]
    public async Task TestATargetInsideTheCallersScopeIsAccepted()
    {
        var (controller, dal) = Imports(nameof(TestATargetInsideTheCallersScopeIsAccepted));
        dal.Scope = EntityScope.ForEntities([Portal]);

        var result = await controller.ImportWithImporter("semgrep", "missing-upload", entityId: Portal,
            repository: " acme/web ");

        Assert.IsType<NotFoundObjectResult>(result.Result);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using API.Controllers;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Model.DTO;
using Model.Exceptions;
using Model.File;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

[TestSubject(typeof(IncidentsController))]
public class IncidentsControllerTest : BaseControllerTest
{
    private readonly IIncidentsService _incidentsService = Substitute.For<IIncidentsService>();
    private readonly IFilesService _filesService = Substitute.For<IFilesService>();
    private readonly IAuditTrailService _auditTrail = Substitute.For<IAuditTrailService>();
    private readonly IncidentsController _controller;

    public IncidentsControllerTest()
    {
        _controller = ResolveController<IncidentsController>(s =>
        {
            s.AddSingleton(_incidentsService);
            s.AddSingleton(_filesService);
            s.AddSingleton(_auditTrail);
        });
    }

    private static Incident MakeIncident(int id, string name)
    {
        return new Incident
        {
            Id = id,
            Name = name,
            Description = "a description",
            Sequence = id,
            Year = 2026
        };
    }

    #region GetAllAsync

    [Fact]
    public async Task TestGetAllAsync()
    {
        _incidentsService.GetAllAsync().Returns(new List<Incident>
        {
            MakeIncident(1, "one"),
            MakeIncident(2, "two")
        });

        var result = await _controller.GetAllAsync();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<Incident>>(okResult.Value);

        Assert.Equal(2, list.Count);
        Assert.Equal("one", list[0].Name);
    }

    [Fact]
    public async Task TestGetAllAsyncReturns500OnError()
    {
        _incidentsService.GetAllAsync()
            .Returns<Task<List<Incident>>>(_ => throw new Exception("boom"));

        var result = await _controller.GetAllAsync();

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region GetNextSequenceAsync

    [Fact]
    public async Task TestGetNextSequenceAsync()
    {
        _incidentsService.GetNextSequenceAsync(2026).Returns(17);

        var result = await _controller.GetNextSequenceAsync(2026);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(17, (int)okResult.Value);
    }

    [Fact]
    public async Task TestGetNextSequenceAsyncDefaultYear()
    {
        _incidentsService.GetNextSequenceAsync(-1).Returns(1);

        var result = await _controller.GetNextSequenceAsync();

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(1, (int)okResult.Value);
    }

    [Fact]
    public async Task TestGetNextSequenceAsyncReturns500OnError()
    {
        _incidentsService.GetNextSequenceAsync(1999)
            .Returns<Task<int>>(_ => throw new Exception("boom"));

        var result = await _controller.GetNextSequenceAsync(1999);

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region GetByIdAsync

    [Fact]
    public async Task TestGetByIdAsync()
    {
        _incidentsService.GetByIdAsync(1).Returns(MakeIncident(1, "one"));

        var result = await _controller.GetByIdAsync(1);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var incident = Assert.IsType<Incident>(okResult.Value);

        Assert.Equal(1, incident.Id);
        Assert.Equal("one", incident.Name);
    }

    [Fact]
    public async Task TestGetByIdAsyncNotFound()
    {
        _incidentsService.GetByIdAsync(999)
            .Returns<Task<Incident>>(_ => throw new DataNotFoundException("incidents", "999"));

        var result = await _controller.GetByIdAsync(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestGetByIdAsyncReturns500OnError()
    {
        _incidentsService.GetByIdAsync(2)
            .Returns<Task<Incident>>(_ => throw new Exception("boom"));

        var result = await _controller.GetByIdAsync(2);

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region GetAttachmentsByIdAsync

    [Fact]
    public async Task TestGetAttachmentsByIdAsync()
    {
        _filesService.GetObjectFileListingsAsync(1, FileCollectionType.IncidentFile)
            .Returns(new List<FileListing>
            {
                new FileListing { Name = "evidence.txt", UniqueName = "unique-1", OwnerId = 1 }
            });

        var result = await _controller.GetAttachmentsByIdAsync(1);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var list = Assert.IsType<List<FileListing>>(okResult.Value);

        Assert.Single(list);
        Assert.Equal("evidence.txt", list[0].Name);
    }

    [Fact]
    public async Task TestGetAttachmentsByIdAsyncNotFound()
    {
        _filesService.GetObjectFileListingsAsync(999, FileCollectionType.IncidentFile)
            .Returns<Task<List<FileListing>>>(_ => throw new DataNotFoundException("incidents", "999"));

        var result = await _controller.GetAttachmentsByIdAsync(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestGetAttachmentsByIdAsyncReturns500OnError()
    {
        _filesService.GetObjectFileListingsAsync(2, FileCollectionType.IncidentFile)
            .Returns<Task<List<FileListing>>>(_ => throw new Exception("boom"));

        var result = await _controller.GetAttachmentsByIdAsync(2);

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region GetIncidentResponsePlansIdsByIdAsync

    [Fact]
    public async Task TestGetIncidentResponsePlansIdsByIdAsync()
    {
        _incidentsService.GetIncidentResponsPlanIdsByIdAsync(1).Returns(new List<int> { 4, 5, 6 });

        var result = await _controller.GetIncidentResponsePlansIdsByIdAsync(1);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var ids = Assert.IsType<List<int>>(okResult.Value);

        Assert.Equal(3, ids.Count);
        Assert.Contains(5, ids);
    }

    [Fact]
    public async Task TestGetIncidentResponsePlansIdsByIdAsyncNotFound()
    {
        _incidentsService.GetIncidentResponsPlanIdsByIdAsync(999)
            .Returns<Task<List<int>>>(_ => throw new DataNotFoundException("incidents", "999"));

        var result = await _controller.GetIncidentResponsePlansIdsByIdAsync(999);

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestGetIncidentResponsePlansIdsByIdAsyncReturns500OnError()
    {
        _incidentsService.GetIncidentResponsPlanIdsByIdAsync(2)
            .Returns<Task<List<int>>>(_ => throw new Exception("boom"));

        var result = await _controller.GetIncidentResponsePlansIdsByIdAsync(2);

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region AssociateIncidentResponsePlansIdsByIdAsync

    [Fact]
    public async Task TestAssociateIncidentResponsePlansIdsByIdAsync()
    {
        var irpIds = new List<int> { 1, 2 };

        var result = await _controller.AssociateIncidentResponsePlansIdsByIdAsync(1, irpIds);

        Assert.IsType<OkResult>(result);
        await _incidentsService.Received(1)
            .AssociateIncidentResponsPlanIdsByIdAsync(1, irpIds, Arg.Any<User>());
    }

    [Fact]
    public async Task TestAssociateIncidentResponsePlansIdsByIdAsyncNotFound()
    {
        _incidentsService
            .AssociateIncidentResponsPlanIdsByIdAsync(999, Arg.Any<List<int>>(), Arg.Any<User>())
            .Returns<Task>(_ => throw new DataNotFoundException("incidents", "999"));

        var result = await _controller.AssociateIncidentResponsePlansIdsByIdAsync(999, new List<int> { 1 });

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task TestAssociateIncidentResponsePlansIdsByIdAsyncReturns500OnError()
    {
        _incidentsService
            .AssociateIncidentResponsPlanIdsByIdAsync(2, Arg.Any<List<int>>(), Arg.Any<User>())
            .Returns<Task>(_ => throw new Exception("boom"));

        var result = await _controller.AssociateIncidentResponsePlansIdsByIdAsync(2, new List<int> { 1 });

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region CreateAsync

    [Fact]
    public async Task TestCreateAsync()
    {
        var incoming = MakeIncident(0, "new incident");
        _incidentsService.CreateAsync(incoming, Arg.Any<User>()).Returns(MakeIncident(7, "new incident"));

        var result = await _controller.CreateAsync(incoming);

        var createdResult = Assert.IsType<CreatedResult>(result.Result);
        var incident = Assert.IsType<Incident>(createdResult.Value);

        Assert.Equal(7, incident.Id);
        Assert.Equal("Incidents/7", createdResult.Location);
    }

    [Fact]
    public async Task TestCreateAsyncReturns500OnError()
    {
        var incoming = MakeIncident(0, "broken");
        _incidentsService.CreateAsync(incoming, Arg.Any<User>())
            .Returns<Task<Incident>>(_ => throw new Exception("boom"));

        var result = await _controller.CreateAsync(incoming);

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region UpdateAsync

    [Fact]
    public async Task TestUpdateAsync()
    {
        var incoming = MakeIncident(0, "renamed");
        _incidentsService.UpdateAsync(Arg.Is<Incident>(i => i.Id == 1), Arg.Any<User>())
            .Returns(MakeIncident(1, "renamed"));

        var result = await _controller.UpdateAsync(1, incoming);

        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var incident = Assert.IsType<Incident>(okResult.Value);

        Assert.Equal(1, incident.Id);
        Assert.Equal("renamed", incident.Name);
        // The controller stamps the route id onto the payload before calling the service.
        Assert.Equal(1, incoming.Id);
    }

    [Fact]
    public async Task TestUpdateAsyncNotFound()
    {
        _incidentsService.UpdateAsync(Arg.Is<Incident>(i => i.Id == 999), Arg.Any<User>())
            .Returns<Task<Incident>>(_ => throw new DataNotFoundException("incidents", "999"));

        var result = await _controller.UpdateAsync(999, MakeIncident(0, "gone"));

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestUpdateAsyncReturns500OnError()
    {
        _incidentsService.UpdateAsync(Arg.Is<Incident>(i => i.Id == 2), Arg.Any<User>())
            .Returns<Task<Incident>>(_ => throw new Exception("boom"));

        var result = await _controller.UpdateAsync(2, MakeIncident(0, "boom"));

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region DeleteAsync

    [Fact]
    public async Task TestDeleteAsync()
    {
        var result = await _controller.DeleteAsync(1);

        Assert.IsType<OkResult>(result);
        await _incidentsService.Received(1).DeleteByIdAsync(1);
    }

    [Fact]
    public async Task TestDeleteAsyncNotFound()
    {
        _incidentsService.DeleteByIdAsync(999)
            .Returns<Task>(_ => throw new DataNotFoundException("incidents", "999"));

        var result = await _controller.DeleteAsync(999);

        Assert.IsType<NotFoundResult>(result);
    }

    [Fact]
    public async Task TestDeleteAsyncReturns500OnError()
    {
        _incidentsService.DeleteByIdAsync(2)
            .Returns<Task>(_ => throw new Exception("boom"));

        var result = await _controller.DeleteAsync(2);

        var statusResult = Assert.IsType<StatusCodeResult>(result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    #endregion

    #region AddTimelineEntryAsync (S55 T354)

    [Fact]
    public async Task TestAddTimelineEntryAppendsAsTheAuthenticatedUserAndAnswersCreated()
    {
        _incidentsService.AddTimelineEntryAsync(1, "Contained", Arg.Any<User>())
            .Returns(new IncidentTimelineEntryDto { Id = 9, IncidentId = 1, UserId = 1, Text = "Contained" });

        var result = await _controller.AddTimelineEntryAsync(1, new NewIncidentTimelineEntryDto { Text = "Contained" });

        var created = Assert.IsType<CreatedResult>(result.Result);
        var entry = Assert.IsType<IncidentTimelineEntryDto>(created.Value);
        Assert.Equal(9, entry.Id);
        Assert.Equal("Incidents/1/Timeline/9", created.Location);
        // The author is the authenticated user the controller resolved, never anything the body could carry.
        await _incidentsService.Received(1).AddTimelineEntryAsync(1, "Contained", Arg.Is<User>(u => u.Value == 1));
    }

    [Fact]
    public async Task TestAddTimelineEntryRefusesInvalidTextAsBadRequest()
    {
        _incidentsService.AddTimelineEntryAsync(1, Arg.Any<string?>(), Arg.Any<User>())
            .Returns<Task<IncidentTimelineEntryDto>>(_ => throw new InvalidParameterException("text", "too long"));

        var result = await _controller.AddTimelineEntryAsync(1, new NewIncidentTimelineEntryDto { Text = "x" });

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestAddTimelineEntryWithNoBodyIsPassedOnAsNoTextAndRefused()
    {
        _incidentsService.AddTimelineEntryAsync(1, null, Arg.Any<User>())
            .Returns<Task<IncidentTimelineEntryDto>>(_ => throw new InvalidParameterException("text", "empty"));

        var result = await _controller.AddTimelineEntryAsync(1, null);

        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task TestAddTimelineEntryToAnUnknownOrOutOfScopeIncidentIsNotFound()
    {
        _incidentsService.AddTimelineEntryAsync(999, Arg.Any<string?>(), Arg.Any<User>())
            .Returns<Task<IncidentTimelineEntryDto>>(_ => throw new DataNotFoundException("incidents", "999"));

        var result = await _controller.AddTimelineEntryAsync(999, new NewIncidentTimelineEntryDto { Text = "x" });

        Assert.IsType<NotFoundResult>(result.Result);
    }

    [Fact]
    public async Task TestAddTimelineEntryReturns500OnError()
    {
        _incidentsService.AddTimelineEntryAsync(2, Arg.Any<string?>(), Arg.Any<User>())
            .Returns<Task<IncidentTimelineEntryDto>>(_ => throw new Exception("boom"));

        var result = await _controller.AddTimelineEntryAsync(2, new NewIncidentTimelineEntryDto { Text = "x" });

        var statusResult = Assert.IsType<StatusCodeResult>(result.Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, statusResult.StatusCode);
    }

    [Fact]
    public void TestAddTimelineEntryIsAuthorisedExactlyAsUpdatingTheIncident()
    {
        // Same policy as PUT /Incidents/{id}: the class's incident_management, and no action-level attribute that could
        // loosen or tighten it (FaceID applies to the list read only).
        static string[] Policies(System.Reflection.MemberInfo member) => member
            .GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: true)
            .Cast<Microsoft.AspNetCore.Authorization.AuthorizeAttribute>().Select(a => a.Policy ?? "").ToArray();

        var add = typeof(IncidentsController).GetMethod(nameof(IncidentsController.AddTimelineEntryAsync))!;
        var update = typeof(IncidentsController).GetMethod(nameof(IncidentsController.UpdateAsync))!;

        Assert.Equal(Policies(update), Policies(add));
        Assert.Empty(Policies(add));
        Assert.Contains(typeof(IncidentsController).GetCustomAttributes(typeof(API.Security.PermissionAuthorizeAttribute), true)
            .Cast<API.Security.PermissionAuthorizeAttribute>(), a => a.Permission == "incident_management");
    }

    #endregion

    #region History (M58, T279)

    /// <summary>The incident's field-by-field trail, found through the scoped incident first, without the user record.</summary>
    [Fact]
    public async Task TestGetHistoryReturnsTheTrailWithoutTheUserRecord()
    {
        _incidentsService.GetByIdAsync(1).Returns(MakeIncident(1, "one"));
        _auditTrail.GetForRecordAsync(nameof(Incident), 1, 500).Returns(new List<AuditLog>
        {
            new()
            {
                Id = 9, EntityType = nameof(Incident), EntityId = 1, Field = nameof(Incident.Severity), OldValue = null,
                NewValue = "High", UserId = 1, User = new User { Value = 1, Name = "Admin" }, Actor = "user"
            }
        });

        var ok = Assert.IsType<OkObjectResult>((await _controller.GetHistoryAsync(1)).Result);

        var row = Assert.Single(Assert.IsType<List<AuditLog>>(ok.Value));
        Assert.Equal((nameof(Incident.Severity), "High", (int?)1), (row.Field, row.NewValue, row.UserId));
        Assert.Null(row.User);
    }

    /// <summary>An incident outside the caller's scope (or gone) is a 404, and its trail is never read.</summary>
    [Fact]
    public async Task TestGetHistoryOfAnIncidentNotVisibleIsNotFound()
    {
        _incidentsService.GetByIdAsync(999).Returns<Task<Incident>>(_ => throw new DataNotFoundException("Incidents", "999"));

        Assert.IsType<NotFoundResult>((await _controller.GetHistoryAsync(999)).Result);
        await _auditTrail.DidNotReceive().GetForRecordAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(IncidentsController.MaxHistoryLimit + 1)]
    public async Task TestGetHistoryRefusesALimitOutOfRange(int limit)
    {
        Assert.IsType<BadRequestObjectResult>((await _controller.GetHistoryAsync(1, limit)).Result);
    }

    [Fact]
    public async Task TestGetHistoryReturns500OnError()
    {
        _incidentsService.GetByIdAsync(2).Returns(MakeIncident(2, "two"));
        _auditTrail.GetForRecordAsync(nameof(Incident), 2, 500).Returns<Task<List<AuditLog>>>(_ => throw new Exception("boom"));

        var status = Assert.IsType<StatusCodeResult>((await _controller.GetHistoryAsync(2)).Result);
        Assert.Equal(StatusCodes.Status500InternalServerError, status.StatusCode);
    }

    /// <summary>
    /// The generic trail's risk-management policy is not the incident permission: it refuses incidents and points at
    /// /Incidents/{id}/History, as it does for hosts, third parties, the catalogue and AI models.
    /// </summary>
    [Fact]
    public async Task TestTheGenericTrailRefusesIncidents()
    {
        Assert.Contains(nameof(Incident), ServerServices.Governance.AuditTrailService.AuditedTypes);
        var trail = ResolveController<AuditTrailController>(s => s.AddSingleton(_auditTrail));

        var bad = Assert.IsType<BadRequestObjectResult>((await trail.GetForRecord(nameof(Incident), 5)).Result);

        var body = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(bad.Value)).RootElement;
        Assert.Equal(("use_incident_history", "/Incidents/5/History"),
            (body.GetProperty("error").GetString(), body.GetProperty("route").GetString()));
        await _auditTrail.DidNotReceive().GetForRecordAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<int>());
    }

    #endregion
}

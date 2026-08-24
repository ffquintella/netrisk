using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Model.DTO;
using Model.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Covers every method of <see cref="IncidentsRestService"/> against a programmable HTTP backend,
/// so RestSharp's serialization and status handling run for real.
/// </summary>
[TestSubject(typeof(IncidentsRestService))]
public class IncidentsRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IIncidentsService _service;

    public IncidentsRestServiceTest()
    {
        _service = ResolveWith<IIncidentsService>(_backend);
    }

    private static List<Incident> TwoIncidents() =>
    [
        new()
        {
            Id = 1, Year = 2026, Sequence = 1, Name = "2026-1", Description = "First",
            Status = 0, Category = "phishing", CreatedById = 1
        },
        new()
        {
            Id = 2, Year = 2026, Sequence = 2, Name = "2026-2", Description = "Second",
            Status = 1, Category = "malware", CreatedById = 2
        }
    ];

    // ---------------------------------------------------------------- GetAllAsync

    [Fact]
    public async Task TestGetAllAsync()
    {
        _backend.OnGet("/Incidents", TwoIncidents());

        var incidents = await _service.GetAllAsync();

        Assert.Equal(2, incidents.Count);
        Assert.Equal("2026-1", incidents[0].Name);
        Assert.Equal("malware", incidents[1].Category);
        Assert.Equal("GET /Incidents", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAllAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Incidents", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetAllAsync());
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Incidents", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Incidents");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    // ------------------------------------------------------------ GetAttachmentsAsync

    [Fact]
    public async Task TestGetAttachmentsAsync()
    {
        _backend.OnGet("/Incidents/5/Attachments", new List<FileListing>
        {
            new()
            {
                Name = "evidence.png", UniqueName = "abc-123", Type = "image/png",
                OwnerId = 5, Timestamp = new DateTime(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc)
            }
        });

        var attachments = await _service.GetAttachmentsAsync(5);

        var attachment = Assert.Single(attachments);
        Assert.Equal("evidence.png", attachment.Name);
        Assert.Equal("abc-123", attachment.UniqueName);
        Assert.Equal(5, attachment.OwnerId);
        Assert.Equal("GET /Incidents/5/Attachments", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAttachmentsAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Incidents/5/Attachments", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetAttachmentsAsync(5));
    }

    [Fact]
    public async Task TestGetAttachmentsAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Incidents/5/Attachments", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAttachmentsAsync(5));
    }

    // ------------------------------------------- GetIncidentResponsPlanIdsByIdAsync

    [Fact]
    public async Task TestGetIncidentResponsPlanIdsByIdAsync()
    {
        _backend.OnGet("/Incidents/7/IncidentResponsePlans", new List<int> { 3, 4, 9 });

        var ids = await _service.GetIncidentResponsPlanIdsByIdAsync(7);

        Assert.Equal(new List<int> { 3, 4, 9 }, ids);
        Assert.Equal("GET /Incidents/7/IncidentResponsePlans", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetIncidentResponsPlanIdsByIdAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Get, "/Incidents/7/IncidentResponsePlans", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.GetIncidentResponsPlanIdsByIdAsync(7));
    }

    [Fact]
    public async Task TestGetIncidentResponsPlanIdsByIdAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Incidents/7/IncidentResponsePlans", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.GetIncidentResponsPlanIdsByIdAsync(7));
    }

    // -------------------------------------- AssociateIncidentResponsPlanIdsByIdAsync

    [Fact]
    public async Task TestAssociateIncidentResponsPlanIdsByIdAsync()
    {
        _backend.OnStatus(Method.Post, "/Incidents/7/IncidentResponsePlans", HttpStatusCode.OK);

        await _service.AssociateIncidentResponsPlanIdsByIdAsync(7, [3, 4]);

        Assert.Equal("POST /Incidents/7/IncidentResponsePlans", _backend.LastRequest.ToString());
        Assert.Equal("[3,4]", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestAssociateIncidentResponsPlanIdsByIdAsyncThrowsOnANonOkStatus()
    {
        _backend.OnStatus(Method.Post, "/Incidents/7/IncidentResponsePlans", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.AssociateIncidentResponsPlanIdsByIdAsync(7, [3]));
    }

    [Fact]
    public async Task TestAssociateIncidentResponsPlanIdsByIdAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Incidents/7/IncidentResponsePlans", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.AssociateIncidentResponsPlanIdsByIdAsync(7, [3]));
    }

    [Fact]
    public async Task TestAssociateIncidentResponsPlanIdsByIdAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, "/Incidents/7/IncidentResponsePlans");

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.AssociateIncidentResponsPlanIdsByIdAsync(7, [3]));
    }

    // ------------------------------------------------------------ GetNextSequenceAsync

    [Fact]
    public async Task TestGetNextSequenceAsyncSendsTheYearItWasGiven()
    {
        _backend.OnGet("/Incidents/NextSequence", 42);

        var sequence = await _service.GetNextSequenceAsync(2026);

        Assert.Equal(42, sequence);
        Assert.Equal("GET /Incidents/NextSequence?year=2026", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetNextSequenceAsyncDefaultsTheYearToMinusOne()
    {
        _backend.OnGet("/Incidents/NextSequence", 1);

        var sequence = await _service.GetNextSequenceAsync();

        Assert.Equal(1, sequence);
        Assert.Equal("?year=-1", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestGetNextSequenceAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Incidents/NextSequence", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetNextSequenceAsync(2026));
    }

    [Fact]
    public async Task TestGetNextSequenceAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Incidents/NextSequence");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetNextSequenceAsync(2026));
    }

    // ------------------------------------------------------------------- CreateAsync

    [Fact]
    public async Task TestCreateAsync()
    {
        _backend.OnPost("/Incidents", new Incident
        {
            Id = 11, Year = 2026, Sequence = 3, Name = "2026-3", Description = "Created",
            Category = "ransomware", CreatedById = 1
        });

        var created = await _service.CreateAsync(new Incident
        {
            Year = 2026, Sequence = 3, Name = "2026-3", Description = "Created", Category = "ransomware"
        });

        Assert.Equal(11, created.Id);
        Assert.Equal("2026-3", created.Name);
        Assert.Equal("POST /Incidents", _backend.LastRequest.ToString());
        Assert.Contains("ransomware", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestCreateAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Post, "/Incidents", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.CreateAsync(new Incident { Name = "x", Description = "x" }));
    }

    [Fact]
    public async Task TestCreateAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Incidents", HttpStatusCode.BadRequest);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.CreateAsync(new Incident { Name = "x", Description = "x" }));
    }

    [Fact]
    public async Task TestCreateAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, "/Incidents");

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.CreateAsync(new Incident { Name = "x", Description = "x" }));
    }

    // ------------------------------------------------------------------- UpdateAsync

    [Fact]
    public async Task TestUpdateAsync()
    {
        _backend.OnPut("/Incidents/11", new Incident
        {
            Id = 11, Year = 2026, Sequence = 3, Name = "2026-3", Description = "Updated",
            Status = 2, Category = "ransomware", CreatedById = 1
        });

        var updated = await _service.UpdateAsync(new Incident
        {
            Id = 11, Name = "2026-3", Description = "Updated", Status = 2
        });

        Assert.Equal(2, updated.Status);
        Assert.Equal("Updated", updated.Description);
        Assert.Equal("PUT /Incidents/11", _backend.LastRequest.ToString());
        Assert.Contains("Updated", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestUpdateAsyncThrowsWhenTheServerAnswersNothing()
    {
        _backend.OnStatus(Method.Put, "/Incidents/11", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(
            () => _service.UpdateAsync(new Incident { Id = 11, Name = "x", Description = "x" }));
    }

    [Fact]
    public async Task TestUpdateAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/Incidents/11", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.UpdateAsync(new Incident { Id = 11, Name = "x", Description = "x" }));
    }

    [Fact]
    public async Task TestUpdateAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Put, "/Incidents/11");

        await Assert.ThrowsAsync<RestComunicationException>(
            () => _service.UpdateAsync(new Incident { Id = 11, Name = "x", Description = "x" }));
    }

    // ------------------------------------------------------------------- DeleteAsync

    [Fact]
    public async Task TestDeleteAsync()
    {
        _backend.OnStatus(Method.Delete, "/Incidents/11", HttpStatusCode.OK);

        await _service.DeleteAsync(11);

        Assert.Equal("DELETE /Incidents/11", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestDeleteAsyncThrowsOnANonOkStatus()
    {
        _backend.OnStatus(Method.Delete, "/Incidents/11", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.DeleteAsync(11));
    }

    [Fact]
    public async Task TestDeleteAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/Incidents/11", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.DeleteAsync(11));
    }

    [Fact]
    public async Task TestDeleteAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Delete, "/Incidents/11");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.DeleteAsync(11));
    }
}

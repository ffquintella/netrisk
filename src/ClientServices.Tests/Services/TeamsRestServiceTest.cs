using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Drives <see cref="TeamsRestService"/> over <see cref="StubRestBackend"/>. Every failure branch in
/// this service funnels into <see cref="RestComunicationException"/>, so the tests separate them by
/// what the backend answered rather than by exception type.
/// </summary>
[TestSubject(typeof(TeamsRestService))]
public class TeamsRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly ITeamsService _service;

    public TeamsRestServiceTest()
    {
        _service = ResolveWith<ITeamsService>(_backend);
    }

    private static List<Team> TwoTeamsOutOfOrder() =>
    [
        new() { Value = 2, Name = "Zulu" },
        new() { Value = 1, Name = "Alpha" }
    ];

    // ---------------------------------------------------------------- GetAll

    [Fact]
    public async Task TestGetAllAsyncOrdersByName()
    {
        _backend.OnGet("/Teams", TwoTeamsOutOfOrder());

        var teams = await _service.GetAllAsync();

        Assert.Equal(2, teams.Count);
        Assert.Equal("Alpha", teams[0].Name);
        Assert.Equal(1, teams[0].Value);
        Assert.Equal("Zulu", teams[1].Name);
        Assert.Equal("GET /Teams", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAllAsyncThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Get, "/Teams", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Teams", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    [Fact]
    public async Task TestGetAllAsyncLogsAndWrapsAnUnauthorizedAnswer()
    {
        _backend.OnStatus(Method.Get, "/Teams", HttpStatusCode.Unauthorized);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Teams");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    [Fact]
    public void TestGetAllRunsTheAsyncPathSynchronously()
    {
        _backend.OnGet("/Teams", TwoTeamsOutOfOrder());

#pragma warning disable CS0618 // deliberately covering the obsolete synchronous wrapper
        var teams = _service.GetAll();
#pragma warning restore CS0618

        Assert.Equal(2, teams.Count);
        Assert.Equal("Alpha", teams[0].Name);
    }

    [Fact]
    public void TestGetByMitigationIdIsNotImplemented()
    {
        Assert.Throws<NotImplementedException>(() => _service.GetByMitigationId(1));
        Assert.Empty(_backend.Requests);
    }

    // ---------------------------------------------------------------- GetById

    [Fact]
    public async Task TestGetByIdAsync()
    {
        _backend.OnGet("/Teams/3", new Team { Value = 3, Name = "Blue" });

        var team = await _service.GetByIdAsync(3);

        Assert.Equal(3, team.Value);
        Assert.Equal("Blue", team.Name);
        Assert.Equal("GET /Teams/3", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetByIdAsyncIgnoresTheFullGetFlagInTheUrl()
    {
        _backend.OnGet("/Teams/3", new Team { Value = 3, Name = "Blue" });

        // fullGet is accepted by the contract but never reaches the request — recorded so a future
        // change to the URL is a visible failure.
        await _service.GetByIdAsync(3, fullGet: true);

        Assert.Equal("", _backend.LastRequest.Query);
    }

    [Fact]
    public async Task TestGetByIdAsyncThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Get, "/Teams/9", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetByIdAsync(9));
    }

    [Fact]
    public async Task TestGetByIdAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Teams/3", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetByIdAsync(3));
    }

    [Fact]
    public void TestGetByIdRunsTheAsyncPathSynchronously()
    {
        _backend.OnGet("/Teams/3", new Team { Value = 3, Name = "Blue" });

        Assert.Equal("Blue", _service.GetById(3).Name);
        Assert.True(_backend.Sent(Method.Get, "/Teams/3"));
    }

    // ---------------------------------------------------------------- GetUsersIds

    [Fact]
    public void TestGetUsersIds()
    {
        _backend.OnGet("/Teams/3/UserIds", new List<int> { 4, 5, 6 });

        var ids = _service.GetUsersIds(3);

        Assert.Equal(3, ids.Count);
        Assert.Equal(4, ids[0]);
        Assert.Equal("GET /Teams/3/UserIds", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetUsersIdsThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Get, "/Teams/9/UserIds", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.GetUsersIds(9));
    }

    [Fact]
    public void TestGetUsersIdsWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Teams/3/UserIds", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetUsersIds(3));
    }

    // ---------------------------------------------------------------- UpdateUsers

    [Fact]
    public void TestUpdateUsersPutsTheIdList()
    {
        _backend.OnPut("/Teams/3/UserIds", "");

        _service.UpdateUsers(3, [4, 5]);

        Assert.Equal("PUT /Teams/3/UserIds", _backend.LastRequest.ToString());
        Assert.Equal("[4,5]", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestUpdateUsersThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Put, "/Teams/9/UserIds", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.UpdateUsers(9, [4]));
    }

    [Fact]
    public void TestUpdateUsersWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/Teams/3/UserIds", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.UpdateUsers(3, [4]));
    }

    // ---------------------------------------------------------------- Delete

    [Fact]
    public void TestDelete()
    {
        _backend.OnDelete("/Teams/3", "");

        _service.Delete(3);

        Assert.Equal("DELETE /Teams/3", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestDeleteThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Delete, "/Teams/9", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.Delete(9));
    }

    [Fact]
    public void TestDeleteWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/Teams/3", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.Delete(3));
    }

    // ---------------------------------------------------------------- Create

    [Fact]
    public void TestCreateSendsAZeroedIdAndReturnsTheSavedTeam()
    {
        _backend.On(Method.Post, "/Teams", new Team { Value = 42, Name = "Ops" }, HttpStatusCode.Created);

        var created = _service.Create(new Team { Value = 77, Name = "Ops" });

        Assert.Equal(42, created.Value);
        Assert.Equal("Ops", created.Name);
        Assert.Equal("POST /Teams", _backend.LastRequest.ToString());
        // The service zeroes the key before posting so the server assigns it.
        Assert.Contains("\"value\":0", _backend.LastRequest.Body);
        Assert.Contains("\"name\":\"Ops\"", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestCreateRequiresTheCreatedStatus()
    {
        // A plain 200 is not good enough — the service insists on 201.
        _backend.OnPost("/Teams", new Team { Value = 42, Name = "Ops" });

        Assert.Throws<RestComunicationException>(() => _service.Create(new Team { Name = "Ops" }));
    }

    [Fact]
    public void TestCreateThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Post, "/Teams", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.Create(new Team { Name = "Ops" }));
    }

    [Fact]
    public void TestCreateWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Teams", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.Create(new Team { Name = "Ops" }));
    }
}

using System;
using System.Collections.Generic;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using ClientServices.Services;
using ClientServices.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Model.Exceptions;
using Model.Rest;
using RestSharp;
using Xunit;

namespace ClientServices.Tests.Services;

/// <summary>
/// Drives <see cref="UserAccessRestService"/> over <see cref="StubRestBackend"/>. This service uses
/// the reliable client, which the backend also serves, so the same routes apply.
/// </summary>
[TestSubject(typeof(UserAccessRestService))]
public class UserAccessRestServiceTest : BaseServiceTest
{
    private const string EntityRolesPath = "/UserAccess/users/7/entity-roles";
    private const string AssignmentPath = "/UserAccess/user-entity-roles/3";

    private readonly StubRestBackend _backend = new();
    private readonly IUserAccessService _service;

    public UserAccessRestServiceTest()
    {
        _service = ResolveWith<IUserAccessService>(_backend);
    }

    private static readonly DateTime Created = new(2026, 1, 15, 10, 0, 0, DateTimeKind.Utc);

    private static UserEntityRole Assignment() => new()
    {
        Id = 3,
        UserId = 7,
        EntityId = 5,
        RoleId = 2,
        CreatedAt = Created
    };

    // ---------------------------------------------------------------- GetUserEntityRolesAsync

    [Fact]
    public async Task TestGetUserEntityRolesAsync()
    {
        _backend.OnGet(EntityRolesPath, new List<UserEntityRole> { Assignment() });

        var roles = await _service.GetUserEntityRolesAsync(7);

        var role = Assert.Single(roles);
        Assert.Equal(3, role.Id);
        Assert.Equal(5, role.EntityId);
        Assert.Equal(2, role.RoleId);
        Assert.Equal(Created, role.CreatedAt);
        Assert.Null(role.RevokedAt);
        Assert.Equal($"GET {EntityRolesPath}", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetUserEntityRolesAsyncThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Get, EntityRolesPath, HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetUserEntityRolesAsync(7));
    }

    [Fact]
    public async Task TestGetUserEntityRolesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, EntityRolesPath, HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetUserEntityRolesAsync(7));
    }

    [Fact]
    public async Task TestGetUserEntityRolesAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, EntityRolesPath);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetUserEntityRolesAsync(7));
    }

    // ---------------------------------------------------------------- AssignEntityRoleAsync

    [Fact]
    public async Task TestAssignEntityRoleAsyncPostsTheEntityAndRole()
    {
        _backend.On(Method.Post, EntityRolesPath, Assignment(), HttpStatusCode.Created);

        var assignment = await _service.AssignEntityRoleAsync(7, 5, 2);

        Assert.Equal(3, assignment.Id);
        Assert.Equal(5, assignment.EntityId);
        Assert.Equal(2, assignment.RoleId);
        Assert.Equal($"POST {EntityRolesPath}", _backend.LastRequest.ToString());
        Assert.Contains("\"entityId\":5", _backend.LastRequest.Body);
        Assert.Contains("\"roleId\":2", _backend.LastRequest.Body);
    }

    [Fact]
    public async Task TestAssignEntityRoleAsyncRequiresTheCreatedStatus()
    {
        // A plain 200 is not good enough — the service insists on 201 and surfaces the server's
        // problem details.
        _backend.OnPost(EntityRolesPath, new OperationError { Title = "Already assigned", Status = 409 });

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.AssignEntityRoleAsync(7, 5, 2));
        Assert.Equal("Already assigned", ex.Result.Title);
        Assert.Equal(409, ex.Result.Status);
    }

    [Fact]
    public async Task TestAssignEntityRoleAsyncReportsTheServerErrorOnNotFound()
    {
        _backend.On(Method.Post, EntityRolesPath,
            new OperationError { Title = "Entity not found", Status = 404 }, HttpStatusCode.NotFound);

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.AssignEntityRoleAsync(7, 5, 2));
        Assert.Equal("Entity not found", ex.Result.Title);
    }

    [Fact]
    public async Task TestAssignEntityRoleAsyncLosesACamelCasedServerError()
    {
        // Known limitation: the failure path deserializes OperationError with the default
        // (case-sensitive) options instead of the class's own case-insensitive JsonOptions, so the
        // camelCase problem details an ASP.NET API actually returns arrive empty.
        // 200 rather than 409: RestSharp turns any non-2xx other than 404 into an
        // HttpRequestException before the service can read the body, so the wrong-status branch is
        // reachable only through 2xx-but-not-201 or 404.
        _backend.OnPost(EntityRolesPath, "{\"title\":\"Already assigned\",\"status\":409}");

        var ex = await Assert.ThrowsAsync<ErrorSavingException>(() => _service.AssignEntityRoleAsync(7, 5, 2));
        Assert.Equal("", ex.Result.Title);
        Assert.Equal(0, ex.Result.Status);
    }

    [Fact]
    public async Task TestAssignEntityRoleAsyncFailsHardOnAnEmptyErrorBody()
    {
        // Known limitation: `response.Content` is "" rather than null for an empty body, so the
        // `?? "{}"` guard never kicks in and System.Text.Json raises instead of the service's own
        // ErrorSavingException.
        _backend.OnStatus(Method.Post, EntityRolesPath, HttpStatusCode.NotFound);

        await Assert.ThrowsAnyAsync<JsonException>(() => _service.AssignEntityRoleAsync(7, 5, 2));
    }

    [Fact]
    public async Task TestAssignEntityRoleAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, EntityRolesPath, HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.AssignEntityRoleAsync(7, 5, 2));
    }

    [Fact]
    public async Task TestAssignEntityRoleAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Post, EntityRolesPath);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.AssignEntityRoleAsync(7, 5, 2));
    }

    // ---------------------------------------------------------------- RevokeEntityRoleAsync

    [Fact]
    public async Task TestRevokeEntityRoleAsyncAcceptsNoContent()
    {
        _backend.OnStatus(Method.Delete, AssignmentPath, HttpStatusCode.NoContent);

        await _service.RevokeEntityRoleAsync(3);

        Assert.Equal($"DELETE {AssignmentPath}", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestRevokeEntityRoleAsyncAcceptsOk()
    {
        _backend.OnDelete(AssignmentPath, "");

        await _service.RevokeEntityRoleAsync(3);

        Assert.True(_backend.Sent(Method.Delete, AssignmentPath));
    }

    [Fact]
    public async Task TestRevokeEntityRoleAsyncThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Delete, AssignmentPath, HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.RevokeEntityRoleAsync(3));
    }

    [Fact]
    public async Task TestRevokeEntityRoleAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, AssignmentPath, HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.RevokeEntityRoleAsync(3));
    }

    [Fact]
    public async Task TestRevokeEntityRoleAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Delete, AssignmentPath);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.RevokeEntityRoleAsync(3));
    }
}

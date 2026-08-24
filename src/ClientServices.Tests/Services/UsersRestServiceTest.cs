using System;
using System.Collections.Generic;
using System.Net;
using System.Threading.Tasks;
using ClientServices.Events;
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
/// Drives <see cref="UsersRestService"/> over <see cref="StubRestBackend"/>, so every request the
/// service builds — verb, path and JSON body — is asserted for real.
/// </summary>
[TestSubject(typeof(UsersRestService))]
public class UsersRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IUsersService _service;

    public UsersRestServiceTest()
    {
        _service = ResolveWith<IUsersService>(_backend);
    }

    private static List<UserListing> TwoListingsOutOfOrder() =>
    [
        new() { Id = 2, Name = "Zoe", Username = "zoe" },
        new() { Id = 1, Name = "Ana", Username = "ana" }
    ];

    private static List<Permission> TwoPermissions() =>
    [
        new() { Id = 1, Key = "risks", Name = "Risks", Description = "Manage risks", Order = 1 },
        new() { Id = 2, Key = "hosts", Name = "Hosts", Description = "Manage hosts", Order = 2 }
    ];

    private static UserDto SavedUser() => new()
    {
        Id = 7,
        Name = "Ana",
        UserName = "ana",
        Email = "ana@example.com",
        Enabled = true,
        RoleId = 3,
        Lang = "en"
    };

    // ---------------------------------------------------------------- GetUserName

    [Fact]
    public async Task TestGetUserNameAsync()
    {
        // A bare JSON string is the whole body, so it has to be quoted to be valid JSON.
        _backend.OnGet("/Users/Name/1", "\"Ana\"");

        var name = await _service.GetUserNameAsync(1);

        Assert.Equal("Ana", name);
        Assert.Equal("GET /Users/Name/1", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetUserNameAsyncFallsBackToAnEmptyNameWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Get, "/Users/Name/9", HttpStatusCode.NotFound);

        // The service logs and degrades to "" here instead of raising — asserted so the behaviour
        // stays deliberate.
        Assert.Equal("", await _service.GetUserNameAsync(9));
    }

    [Fact]
    public async Task TestGetUserNameAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Users/Name/1", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetUserNameAsync(1));
    }

    [Fact]
    public async Task TestGetUserNameAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Users/Name/1");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetUserNameAsync(1));
    }

    [Fact]
    public void TestGetUserNameRunsTheAsyncPathSynchronously()
    {
        _backend.OnGet("/Users/Name/4", "\"Bob\"");

        Assert.Equal("Bob", _service.GetUserName(4));
        Assert.True(_backend.Sent(Method.Get, "/Users/Name/4"));
    }

    // ---------------------------------------------------------------- ListUsers / GetAllAsync

    [Fact]
    public void TestListUsersOrdersByName()
    {
        _backend.OnGet("/Users/Listings", TwoListingsOutOfOrder());

        var listings = _service.ListUsers();

        Assert.Equal(2, listings.Count);
        Assert.Equal("Ana", listings[0].Name);
        Assert.Equal("Zoe", listings[1].Name);
        Assert.Equal("GET /Users/Listings", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestListUsersThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Get, "/Users/Listings", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(() => _service.ListUsers());
    }

    [Fact]
    public void TestListUsersWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Users/Listings", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.ListUsers());
    }

    [Fact]
    public async Task TestGetAllAsyncOrdersByName()
    {
        _backend.OnGet("/Users/Listings", TwoListingsOutOfOrder());

        var listings = await _service.GetAllAsync();

        Assert.Equal("Ana", listings[0].Name);
        Assert.Equal("Zoe", listings[1].Name);
        Assert.Equal(1, listings[0].Id);
    }

    [Fact]
    public async Task TestGetAllAsyncServesTheSecondCallFromTheInstanceCache()
    {
        _backend.OnGet("/Users/Listings", TwoListingsOutOfOrder());

        await _service.GetAllAsync();
        var second = await _service.GetAllAsync();

        Assert.Equal(2, second.Count);
        Assert.Single(_backend.Requests);
    }

    [Fact]
    public async Task TestGetAllAsyncRefetchesWhenTheCacheIsIgnored()
    {
        _backend.OnGet("/Users/Listings", TwoListingsOutOfOrder());

        await _service.GetAllAsync();
        await _service.GetAllAsync(ignoreCache: true);

        Assert.Equal(2, _backend.Requests.Count);
    }

    [Fact]
    public async Task TestGetAllAsyncThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Get, "/Users/Listings", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetAllAsync());
    }

    [Fact]
    public async Task TestGetAllAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Users/Listings", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllAsync());
    }

    [Fact]
    public void TestLoadCacheFetchesTheListingsOnlyOnce()
    {
        _backend.OnGet("/Users/Listings", TwoListingsOutOfOrder());

        _service.LoadCache();
        _service.LoadCache();

        Assert.Single(_backend.Requests);
    }

    // ---------------------------------------------------------------- CreateUser

    [Fact]
    public void TestCreateUserRejectsAUserThatAlreadyHasAnId()
    {
        Assert.Throws<ArgumentException>(() => _service.CreateUser(new UserDto { Id = 5, Name = "Ana" }));
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public void TestCreateUserPostsTheUserAndAnnouncesIt()
    {
        _backend.OnPost("/Users", SavedUser());

        UserAddedEventArgs? announced = null;
        _service.UserAdded += (_, args) => announced = args;

        var created = _service.CreateUser(new UserDto { Name = "Ana", UserName = "ana" });

        Assert.Equal(7, created.Id);
        Assert.Equal("Ana", created.Name);
        Assert.Equal("POST /Users", _backend.LastRequest.ToString());
        Assert.Contains("\"userName\":\"ana\"", _backend.LastRequest.Body);
        Assert.NotNull(announced);
        Assert.NotNull(announced!.User);
        Assert.Equal(7, announced.User!.Id);
        Assert.Equal("Ana", announced.User!.Name);
    }

    [Fact]
    public void TestCreateUserThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Post, "/Users", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(() => _service.CreateUser(new UserDto { Name = "Ana" }));
    }

    [Fact]
    public void TestCreateUserWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Users", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.CreateUser(new UserDto { Name = "Ana" }));
    }

    [Fact]
    public void TestCreateUserThrowsWhenTheBodyDeserializesToNothing()
    {
        _backend.OnPost("/Users", "null");

        var ex = Assert.Throws<Exception>(() => _service.CreateUser(new UserDto { Name = "Ana" }));
        Assert.Equal("Error deserializing user", ex.Message);
    }

    // ---------------------------------------------------------------- SaveUser

    [Fact]
    public void TestSaveUserRejectsAUserWithoutAnId()
    {
        Assert.Throws<ArgumentException>(() => _service.SaveUser(new UserDto { Id = 0, Name = "Ana" }));
        Assert.Empty(_backend.Requests);
    }

    [Fact]
    public void TestSaveUserPutsTheUserAndDefaultsTheLanguage()
    {
        _backend.OnPut("/Users/7", "");

        _service.SaveUser(new UserDto { Id = 7, Name = "Ana", Lang = null });

        Assert.Equal("PUT /Users/7", _backend.LastRequest.ToString());
        Assert.Contains("\"lang\":\"en\"", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestSaveUserKeepsAnExplicitLanguage()
    {
        _backend.OnPut("/Users/7", "");

        _service.SaveUser(new UserDto { Id = 7, Name = "Ana", Lang = "pt" });

        Assert.Contains("\"lang\":\"pt\"", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestSaveUserThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Put, "/Users/7", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(() => _service.SaveUser(new UserDto { Id = 7, Name = "Ana" }));
    }

    [Fact]
    public void TestSaveUserWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/Users/7", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.SaveUser(new UserDto { Id = 7, Name = "Ana" }));
    }

    // ---------------------------------------------------------------- GetUser

    [Fact]
    public async Task TestGetUserAsync()
    {
        _backend.OnGet("/Users/7", SavedUser());

        var user = await _service.GetUserAsync(7);

        Assert.Equal(7, user.Id);
        Assert.Equal("ana@example.com", user.Email);
        Assert.True(user.Enabled);
        Assert.Equal("GET /Users/7", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetUserAsyncThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Get, "/Users/9", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetUserAsync(9));
    }

    [Fact]
    public async Task TestGetUserAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Users/7", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetUserAsync(7));
    }

    [Fact]
    public void TestGetUserRunsTheAsyncPathSynchronously()
    {
        _backend.OnGet("/Users/7", SavedUser());

        Assert.Equal(3, _service.GetUser(7).RoleId);
        Assert.True(_backend.Sent(Method.Get, "/Users/7"));
    }

    // ---------------------------------------------------------------- DeleteUser

    [Fact]
    public void TestDeleteUser()
    {
        _backend.OnDelete("/Users/7", "");

        _service.DeleteUser(7);

        Assert.Equal("DELETE /Users/7", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestDeleteUserThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Delete, "/Users/9", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(() => _service.DeleteUser(9));
    }

    [Fact]
    public void TestDeleteUserWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/Users/7", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.DeleteUser(7));
    }

    // ---------------------------------------------------------------- permissions

    [Fact]
    public void TestGetAllPermissions()
    {
        _backend.OnGet("/Users/Permissions", TwoPermissions());

        var permissions = _service.GetAllPermissions();

        Assert.Equal(2, permissions.Count);
        Assert.Equal("risks", permissions[0].Key);
        Assert.Equal("GET /Users/Permissions", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetAllPermissionsThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Get, "/Users/Permissions", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(() => _service.GetAllPermissions());
    }

    [Fact]
    public void TestGetAllPermissionsWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Users/Permissions", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetAllPermissions());
    }

    [Fact]
    public async Task TestGetAllPermissionsAsync()
    {
        _backend.OnGet("/Users/Permissions", TwoPermissions());

        var permissions = await _service.GetAllPermissionsAsync();

        Assert.Equal(2, permissions.Count);
        Assert.Equal(2, permissions[1].Order);
    }

    [Fact]
    public async Task TestGetAllPermissionsAsyncThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Get, "/Users/Permissions", HttpStatusCode.NotFound);

        await Assert.ThrowsAsync<InvalidHttpRequestException>(() => _service.GetAllPermissionsAsync());
    }

    [Fact]
    public async Task TestGetAllPermissionsAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Users/Permissions", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllPermissionsAsync());
    }

    [Fact]
    public void TestGetUserPermissions()
    {
        _backend.OnGet("/Users/7/Permissions", TwoPermissions());

        var permissions = _service.GetUserPermissions(7);

        Assert.Equal(2, permissions.Count);
        Assert.Equal("Hosts", permissions[1].Name);
        Assert.Equal("GET /Users/7/Permissions", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetUserPermissionsThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Get, "/Users/9/Permissions", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(() => _service.GetUserPermissions(9));
    }

    [Fact]
    public void TestGetUserPermissionsWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Users/7/Permissions", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetUserPermissions(7));
    }

    [Fact]
    public void TestSaveUserPermissionsSendsOnlyTheIdsAndDropsNulls()
    {
        _backend.OnPut("/Users/7/Permissions", "");

        _service.SaveUserPermissions(7, [TwoPermissions()[0], null, TwoPermissions()[1]]);

        Assert.Equal("PUT /Users/7/Permissions", _backend.LastRequest.ToString());
        Assert.Equal("[1,2]", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestSaveUserPermissionsThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Put, "/Users/7/Permissions", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(() => _service.SaveUserPermissions(7, [TwoPermissions()[0]]));
    }

    [Fact]
    public void TestSaveUserPermissionsWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/Users/7/Permissions", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.SaveUserPermissions(7, [TwoPermissions()[0]]));
    }

    // ---------------------------------------------------------------- ChangePassword

    [Fact]
    public void TestChangePasswordPostsTheNewPassword()
    {
        _backend.OnPost("/Users/7/ChangePassword", "");

        _service.ChangePassword(7, "n3wSecret");

        Assert.Equal("POST /Users/7/ChangePassword", _backend.LastRequest.ToString());
        Assert.Contains("\"newPassword\":\"n3wSecret\"", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestChangePasswordThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Post, "/Users/9/ChangePassword", HttpStatusCode.NotFound);

        Assert.Throws<InvalidHttpRequestException>(() => _service.ChangePassword(9, "n3wSecret"));
    }

    [Fact]
    public void TestChangePasswordWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Users/7/ChangePassword", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.ChangePassword(7, "n3wSecret"));
    }

    // ---------------------------------------------------------------- caching

    /// <summary>
    /// A second service over the same backend, wired to a real <see cref="MemoryCacheService"/>
    /// instead of the container's substitute, so the branches that answer from cache actually run.
    /// </summary>
    private IUsersService CachingService() => ResolveWith<IUsersService>(_backend, new MemoryCacheService());

    [Fact]
    public async Task TestGetUserNameAsyncAnswersASecondCallFromCache()
    {
        _backend.OnGet("/Users/Name/1", "\"Ana\"");
        var service = CachingService();

        Assert.Equal("Ana", await service.GetUserNameAsync(1));
        Assert.Equal("Ana", await service.GetUserNameAsync(1));
        Assert.Single(_backend.Requests);
    }

    [Fact]
    public async Task TestGetUserNameAsyncCachesPerId()
    {
        _backend.OnGet("/Users/Name/1", "\"Ana\"");
        _backend.OnGet("/Users/Name/2", "\"Zoe\"");
        var service = CachingService();

        Assert.Equal("Ana", await service.GetUserNameAsync(1));
        Assert.Equal("Zoe", await service.GetUserNameAsync(2));
        Assert.Equal("Ana", await service.GetUserNameAsync(1));

        Assert.Equal(2, _backend.Requests.Count);
    }

    [Fact]
    public async Task TestGetUserAsyncAnswersASecondCallFromCache()
    {
        _backend.OnGet("/Users/7", SavedUser());
        var service = CachingService();

        await service.GetUserAsync(7);
        var second = await service.GetUserAsync(7);

        Assert.Equal("ana", second.UserName);
        Assert.Single(_backend.Requests);
    }
}

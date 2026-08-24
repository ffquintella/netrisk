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

/// <summary>Drives <see cref="RolesRestService"/> over <see cref="StubRestBackend"/>.</summary>
[TestSubject(typeof(RolesRestService))]
public class RolesRestServiceTest : BaseServiceTest
{
    private readonly StubRestBackend _backend = new();
    private readonly IRolesService _service;

    public RolesRestServiceTest()
    {
        _service = ResolveWith<IRolesService>(_backend);
    }

    private static List<Role> TwoRoles() =>
    [
        new() { Value = 1, Name = "Administrator", Admin = true, Default = false },
        new() { Value = 2, Name = "Analyst", Admin = false, Default = true }
    ];

    // ---------------------------------------------------------------- GetAllRoles

    [Fact]
    public void TestGetAllRoles()
    {
        _backend.OnGet("/Roles", TwoRoles());

        var roles = _service.GetAllRoles();

        Assert.Equal(2, roles.Count);
        Assert.Equal("Administrator", roles[0].Name);
        Assert.True(roles[0].Admin);
        Assert.True(roles[1].Default);
        Assert.Equal("GET /Roles", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetAllRolesFallsBackToAnEmptyListWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Get, "/Roles", HttpStatusCode.NotFound);

        // The service degrades to an empty list rather than raising — asserted so the choice stays
        // deliberate.
        Assert.Empty(_service.GetAllRoles());
    }

    [Fact]
    public void TestGetAllRolesWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Roles", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetAllRoles());
    }

    [Fact]
    public void TestGetAllRolesWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Roles");

        Assert.Throws<RestComunicationException>(() => _service.GetAllRoles());
    }

    [Fact]
    public async Task TestGetAllRolesAsync()
    {
        _backend.OnGet("/Roles", TwoRoles());

        var roles = await _service.GetAllRolesAsync();

        Assert.Equal(2, roles.Count);
        Assert.Equal(2, roles[1].Value);
        Assert.Equal("GET /Roles", _backend.LastRequest.ToString());
    }

    [Fact]
    public async Task TestGetAllRolesAsyncFallsBackToAnEmptyListWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Get, "/Roles", HttpStatusCode.NotFound);

        Assert.Empty(await _service.GetAllRolesAsync());
    }

    [Fact]
    public async Task TestGetAllRolesAsyncWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Roles", HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllRolesAsync());
    }

    [Fact]
    public async Task TestGetAllRolesAsyncWrapsATransportFailure()
    {
        _backend.OnTransportFailure(Method.Get, "/Roles");

        await Assert.ThrowsAsync<RestComunicationException>(() => _service.GetAllRolesAsync());
    }

    // ---------------------------------------------------------------- Delete

    [Fact]
    public void TestDelete()
    {
        _backend.OnDelete("/Roles/2", "");

        _service.Delete(2);

        Assert.Equal("DELETE /Roles/2", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestDeleteThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Delete, "/Roles/9", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.Delete(9));
    }

    [Fact]
    public void TestDeleteWrapsAServerError()
    {
        _backend.OnStatus(Method.Delete, "/Roles/2", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.Delete(2));
    }

    // ---------------------------------------------------------------- Create

    [Fact]
    public void TestCreatePostsTheRoleAndReturnsTheSavedOne()
    {
        _backend.OnPost("/Roles", new Role { Value = 5, Name = "Auditor", Admin = false, Default = false });

        var created = _service.Create(new Role { Name = "Auditor" });

        Assert.Equal(5, created.Value);
        Assert.Equal("Auditor", created.Name);
        Assert.Equal("POST /Roles", _backend.LastRequest.ToString());
        Assert.Contains("\"name\":\"Auditor\"", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestCreateAcceptsTheCreatedStatus()
    {
        _backend.On(Method.Post, "/Roles", new Role { Value = 5, Name = "Auditor" }, HttpStatusCode.Created);

        Assert.Equal(5, _service.Create(new Role { Name = "Auditor" }).Value);
    }

    [Fact]
    public void TestCreateThrowsWhenTheServerReturnsNothing()
    {
        _backend.OnStatus(Method.Post, "/Roles", HttpStatusCode.NotFound);

        Assert.Throws<RestComunicationException>(() => _service.Create(new Role { Name = "Auditor" }));
    }

    [Fact]
    public void TestCreateWrapsAServerError()
    {
        _backend.OnStatus(Method.Post, "/Roles", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.Create(new Role { Name = "Auditor" }));
    }

    // ---------------------------------------------------------------- role permissions

    [Fact]
    public void TestGetRolePermissions()
    {
        _backend.OnGet("/Roles/2/Permissions", new List<string> { "risks", "hosts" });

        var permissions = _service.GetRolePermissions(2);

        Assert.Equal(2, permissions.Count);
        Assert.Equal("risks", permissions[0]);
        Assert.Equal("GET /Roles/2/Permissions", _backend.LastRequest.ToString());
    }

    [Fact]
    public void TestGetRolePermissionsReportsAMissingRole()
    {
        _backend.OnStatus(Method.Get, "/Roles/9/Permissions", HttpStatusCode.NotFound);

        var ex = Assert.Throws<DataNotFoundException>(() => _service.GetRolePermissions(9));
        Assert.Equal("role", ex.Identification);
    }

    [Fact]
    public void TestGetRolePermissionsWrapsAServerError()
    {
        _backend.OnStatus(Method.Get, "/Roles/2/Permissions", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.GetRolePermissions(2));
    }

    [Fact]
    public void TestUpdateRolePermissionsPutsThePermissionKeys()
    {
        _backend.OnPut("/Roles/2/Permissions", "");

        _service.UpdateRolePermissions(2, ["risks", "hosts"]);

        Assert.Equal("PUT /Roles/2/Permissions", _backend.LastRequest.ToString());
        Assert.Equal("[\"risks\",\"hosts\"]", _backend.LastRequest.Body);
    }

    [Fact]
    public void TestUpdateRolePermissionsThrowsWhenTheServerAnswersNotFound()
    {
        _backend.OnStatus(Method.Put, "/Roles/9/Permissions", HttpStatusCode.NotFound);

        var ex = Assert.Throws<InvalidHttpRequestException>(
            () => _service.UpdateRolePermissions(9, ["risks"]));
        Assert.Equal("Error updating role permissions", ex.Message);
        Assert.Equal("/Roles/9/Permissions", ex.Url);
        Assert.Equal("PUT", ex.Method);
    }

    [Fact]
    public void TestUpdateRolePermissionsWrapsAServerError()
    {
        _backend.OnStatus(Method.Put, "/Roles/2/Permissions", HttpStatusCode.InternalServerError);

        Assert.Throws<RestComunicationException>(() => _service.UpdateRolePermissions(2, ["risks"]));
    }
}

using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using API.Security;
using API.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Xunit;

namespace API.Tests.Security;

[TestSubject(typeof(ValidUserRequirementHandler))]
public class ValidUserRequirementHandlerTest
{
    private readonly InMemoryDalService _dalService = new(Guid.NewGuid().ToString());
    private readonly ValidUserRequirementHandler _handler;

    public ValidUserRequirementHandlerTest()
    {
        using (var context = _dalService.GetContext())
        {
            context.Users.Add(NewUser(1, "localuser", "local"));
            context.Users.Add(NewUser(2, "legacyuser", "simplerisk"));
            context.Users.Add(NewUser(3, "samluser", "saml"));
            context.SaveChanges();
        }

        _handler = new ValidUserRequirementHandler(_dalService);
    }

    private static User NewUser(int value, string login, string type) => new()
    {
        Value = value,
        Enabled = true,
        Name = login,
        Login = login,
        Email = $"{login}@teste.com",
        Type = type,
        Password = "secret"u8.ToArray(),
        RoleId = 1
    };

    private static AuthorizationHandlerContext Context(string identityName, UserType userType)
    {
        var identity = identityName == null
            ? new ClaimsIdentity()
            : new ClaimsIdentity([new Claim(ClaimTypes.Name, identityName)], "Test");

        return new AuthorizationHandlerContext(
            new[] { new ValidUserRequirement(userType) },
            new ClaimsPrincipal(identity),
            resource: null);
    }

    [Theory]
    [InlineData("localuser", UserType.Local)]
    [InlineData("legacyuser", UserType.Local)]
    [InlineData("samluser", UserType.SAML)]
    [InlineData("localuser", UserType.Any)]
    [InlineData("samluser", UserType.Any)]
    public async Task TestHandleRequirementAsyncSucceedsForAKnownUser(string login, UserType userType)
    {
        var context = Context(login, userType);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task TestHandleRequirementAsyncMatchesLoginCaseInsensitively()
    {
        var context = Context("LocalUser", UserType.Local);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task TestHandleRequirementAsyncStripsTheDomainFromAnUpn()
    {
        var context = Context("localuser@example.com", UserType.Local);

        await _handler.HandleAsync(context);

        Assert.True(context.HasSucceeded);
    }

    [Fact]
    public async Task TestHandleRequirementAsyncFailsForAnUnknownUser()
    {
        var context = Context("nobody", UserType.Any);

        await _handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task TestHandleRequirementAsyncFailsWhenTheIdentityHasNoName()
    {
        var context = Context(null, UserType.Any);

        await _handler.HandleAsync(context);

        Assert.True(context.HasFailed);
    }

    [Fact]
    public async Task TestHandleRequirementAsyncRejectsASamlUserAskedForAsLocal()
    {
        var context = Context("samluser", UserType.Local);

        await _handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);
    }

    [Fact]
    public async Task TestHandleRequirementAsyncRejectsALocalUserAskedForAsSaml()
    {
        var context = Context("localuser", UserType.SAML);

        await _handler.HandleAsync(context);

        Assert.True(context.HasFailed);
        Assert.False(context.HasSucceeded);
    }
}

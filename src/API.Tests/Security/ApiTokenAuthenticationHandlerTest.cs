using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Threading.Tasks;
using API.Controllers;
using API.Security;
using API.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Model.Findings;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Services;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// The principal an API token signs in as must resolve, everywhere downstream, to the user the token belongs to.
///
/// Downstream code resolves "the current user" by <em>login</em> from the name claim — <see cref="ApiBaseController"/>
/// through <c>UserHelper.GetUserName</c> and <c>IUsersService.GetUserAsync</c>, and <see cref="ValidUserRequirementHandler"/>
/// on every policy — which is what the Basic and JWT handlers put there. The token handler used to put the owner's
/// <em>display name</em> there instead, so a token only worked when the two happened to be equal, and a token whose owner's
/// display name was another user's login acted as that other user (THREAT_MODEL "Open", S53 §11 defect 1).
///
/// These tests run the real handler and resolve its principal through the real downstream code over an in-memory store,
/// rather than asserting on the claim alone: the defect was a disagreement between the two ends.
/// </summary>
[TestSubject(typeof(ApiTokenAuthenticationHandler))]
public class ApiTokenAuthenticationHandlerTest
{
    private const string PresentedToken = ApiToken.SecretPrefix + "k1_secret";

    private const int OwnerId = 10;
    private const int OtherUserId = 20;

    private readonly InMemoryDalService _dal = new(Guid.NewGuid().ToString());
    private readonly IUsersService _usersService;
    private readonly IApiTokensService _tokensService = Substitute.For<IApiTokensService>();

    public ApiTokenAuthenticationHandlerTest()
    {
        var permissions = Substitute.For<IPermissionsService>();
        permissions.GetUserPermissionsAsync(Arg.Any<User>()).Returns(new List<string> { "riskmanagement" });

        _usersService = new UsersService(_dal, NullLoggerFactory.Instance, Substitute.For<IRolesService>(), permissions);

        _tokensService.AuthenticateAsync(PresentedToken).Returns(new ApiToken
        {
            Id = 5, KeyId = "k1", Name = "ci", SecretHash = "h", UserId = OwnerId,
            Scopes = ApiTokenScopes.VulnerabilitiesRead
        });
    }

    private void Seed(params User[] users)
    {
        using var db = _dal.GetContext();
        db.Users.AddRange(users);
        db.SaveChanges();
    }

    private static User NewUser(int id, string login, string displayName) => new()
    {
        Value = id, Enabled = true, Lockout = 0, Login = login, Name = displayName, Email = $"{login}@x.test",
        Type = "local", Password = "secret"u8.ToArray()
    };

    private async Task<AuthenticateResult> AuthenticateResultAsync()
    {
        var options = Substitute.For<IOptionsMonitor<AuthenticationSchemeOptions>>();
        options.Get(Arg.Any<string>()).Returns(new AuthenticationSchemeOptions());

        var handler = new ApiTokenAuthenticationHandler(options, NullLoggerFactory.Instance, UrlEncoder.Default,
            _tokensService, _usersService, Substitute.For<IRolesService>(), _dal, Serilog.Core.Logger.None);

        var context = new DefaultHttpContext();
        context.Request.Headers.Authorization = "Bearer " + PresentedToken;

        await handler.InitializeAsync(
            new AuthenticationScheme(ApiTokenAuthenticationHandler.SchemeName, null, typeof(ApiTokenAuthenticationHandler)),
            context);

        return await handler.AuthenticateAsync();
    }

    private async Task<ClaimsPrincipal> AuthenticateAsync()
    {
        var result = await AuthenticateResultAsync();

        Assert.True(result.Succeeded, result.Failure?.Message);
        return result.Principal!;
    }

    [Fact]
    public async Task TestTokenOwnedByDisabledUserIsRejected()
    {
        var owner = NewUser(OwnerId, "departed", "Departed User");
        owner.Enabled = false;
        Seed(owner);

        var result = await AuthenticateResultAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid API token", result.Failure?.Message);
        await _tokensService.DidNotReceiveWithAnyArgs().TouchAsync(default, default);
    }

    [Fact]
    public async Task TestTokenOwnedByLockedUserIsRejected()
    {
        var owner = NewUser(OwnerId, "locked", "Locked User");
        owner.Lockout = 1;
        Seed(owner);

        var result = await AuthenticateResultAsync();

        Assert.False(result.Succeeded);
        Assert.Equal("Invalid API token", result.Failure?.Message);
        await _tokensService.DidNotReceiveWithAnyArgs().TouchAsync(default, default);
    }

    /// <summary>The user <see cref="ApiBaseController.GetUserAsync"/> resolves for this principal — what controllers act as.</summary>
    private async Task<User> ResolveAsControllerAsync(ClaimsPrincipal principal)
    {
        var accessor = new HttpContextAccessor { HttpContext = new DefaultHttpContext { User = principal } };
        return await new ProbeController(accessor, _usersService).ResolveAsync();
    }

    private async Task<bool> PassesValidUserAsync(ClaimsPrincipal principal)
    {
        var requirement = new ValidUserRequirement();
        var context = new AuthorizationHandlerContext(new[] { requirement }, principal, null);
        await new ValidUserRequirementHandler(_dal).HandleAsync(context);
        return context.HasSucceeded;
    }

    private static int Sid(ClaimsPrincipal principal) => int.Parse(principal.FindFirst(ClaimTypes.Sid)!.Value);

    /// <summary>(a) The owner's display name is not their login: the token authenticates and acts as its owner.</summary>
    [Fact]
    public async Task TestTokenWhoseOwnerDisplayNameDiffersFromLoginResolvesToOwner()
    {
        Seed(NewUser(OwnerId, "msilva", "Maria Silva"));

        var principal = await AuthenticateAsync();

        Assert.True(await PassesValidUserAsync(principal));
        var resolved = await ResolveAsControllerAsync(principal);
        Assert.Equal(OwnerId, resolved.Value);

        Assert.Equal("msilva", principal.Identity!.Name);
        Assert.Equal(OwnerId, Sid(principal));
    }

    /// <summary>
    /// (b) The owner's display name is another user's login: the token still acts as its owner and never as that other
    /// user — the name claim and the Sid claim name the same person.
    /// </summary>
    [Fact]
    public async Task TestTokenWhoseOwnerDisplayNameIsAnotherUsersLoginNeverActsAsThatUser()
    {
        Seed(NewUser(OwnerId, "cibot", "victim"), NewUser(OtherUserId, "victim", "Victim Person"));

        var principal = await AuthenticateAsync();

        var resolved = await ResolveAsControllerAsync(principal);
        Assert.NotEqual(OtherUserId, resolved.Value);
        Assert.Equal(OwnerId, resolved.Value);
        Assert.Equal(Sid(principal), resolved.Value);

        Assert.Equal("cibot", principal.Identity!.Name);
    }

    /// <summary>
    /// The token identity keeps what makes it a token: its scheme (which <see cref="RequireApiScopeAttribute"/> keys on), its
    /// scopes and its id — and still never the Admin role, even for an administrator.
    /// </summary>
    [Fact]
    public async Task TestTokenIdentityKeepsSchemeScopesAndWithholdsAdmin()
    {
        var owner = NewUser(OwnerId, "root", "The Administrator");
        owner.Admin = true;
        Seed(owner);

        var principal = await AuthenticateAsync();

        Assert.Equal(ApiTokenAuthenticationHandler.SchemeName, principal.Identity!.AuthenticationType);
        Assert.Equal(new[] { ApiTokenScopes.VulnerabilitiesRead },
            principal.FindAll(ApiTokenAuthenticationHandler.ScopeClaimType).Select(c => c.Value));
        Assert.Equal("5", principal.FindFirst(ApiTokenAuthenticationHandler.TokenIdClaimType)!.Value);
        Assert.False(principal.IsInRole("Admin"));
        Assert.Contains(principal.FindAll("Permission"), c => c.Value == "riskmanagement");
    }

    /// <summary>Exposes the protected resolution every API controller uses, without picking any one controller.</summary>
    private sealed class ProbeController(IHttpContextAccessor accessor, IUsersService usersService)
        : ApiBaseController(Serilog.Core.Logger.None, accessor, usersService)
    {
        public Task<User> ResolveAsync() => GetUserAsync();
    }
}

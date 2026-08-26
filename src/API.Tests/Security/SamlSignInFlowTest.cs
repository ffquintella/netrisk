using System;
using System.Collections.Generic;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.APITests;
using DAL.Entities;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Model.Authentication;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// Track 7 finding NR-2026-001 — desktop SSO account takeover.
///
/// The original flow: <c>GET /Authentication/SAMLRequest?requestId=X</c> was anonymous and created a
/// pending sign-in under whatever <c>X</c> the caller chose. <c>GET /Authentication/SAMLSingIn</c>
/// then marked it accepted as soon as the browser presented a valid SAML identity, and
/// <c>GET /Authentication/AppSAMLToken?requestId=X</c> — also anonymous — returned a full session JWT
/// for that identity to anybody who asked.
///
/// So the attack needed no guessing at all: pick an id, send a colleague the link, let their existing
/// single-sign-on session complete the flow, and collect their session. One click, any account,
/// including an administrator's.
///
/// Four properties close it, and each has a test below:
///  1. the id is minted by the server, only for an administrator-approved client registration;
///  2. the browser endpoint refuses an id the server did not mint;
///  3. the person in the browser has to approve explicitly, with an anti-forgery token, because the
///     SAML session cookie is necessarily <c>SameSite=None</c>;
///  4. the token is handed only to the client registration that minted the request, once.
/// </summary>
public class SamlSignInFlowTest : BaseControllerTest
{
    private const string ApprovedClient = "approved-device-1";
    private const string SamlUserName = "alice@corp.example";

    private static readonly ClientRegistration Approved = new()
    {
        Id = 1,
        ExternalId = ApprovedClient,
        Hostname = "alice-laptop.corp.example",
        Status = "approved",
        Name = "ALICE"
    };

    /// <summary>
    /// Builds the controller over a real memory cache (the flow's state lives there, so substituting
    /// it would test nothing) and a principal of the caller's choosing.
    /// </summary>
    private static (AuthenticationController Controller, IMemoryCache Cache, HttpContext Http)
        Build(string? authenticatedUser = SamlUserName, string? clientIdHeader = ApprovedClient,
            bool clientApproved = true)
    {
        var cache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 1024 });

        var http = new DefaultHttpContext
        {
            Connection =
            {
                RemoteIpAddress = new IPAddress([203, 0, 113, 7])
            }
        };

        if (authenticatedUser != null)
            http.User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.Name, authenticatedUser)], "saml2"));

        if (clientIdHeader != null) http.Request.Headers["ClientId"] = clientIdHeader;

        var accessor = Substitute.For<IHttpContextAccessor>();
        accessor.HttpContext.Returns(http);

        var registrations = Substitute.For<IClientRegistrationService>();
        registrations.FindApprovedRegistrationAsync(Arg.Any<string>())
            .Returns(_ => Task.FromResult<ClientRegistration?>(clientApproved ? Approved : null));

        // A fixed 32-byte signing key. The controller mints a real HMAC-SHA256 JWT, so the key has to
        // be long enough for the algorithm — a short one throws rather than failing an assertion.
        var environment = Substitute.For<IEnvironmentService>();
        environment.ServerSecretToken.Returns(
            Convert.ToBase64String(System.Text.Encoding.ASCII.GetBytes("netrisk-test-signing-key-32-bytes")));

        var users = Substitute.For<IUsersService>();
        users.GetUser(Arg.Any<string>()).Returns(new User
        {
            Value = 7, Login = "alice", Name = "Alice", Email = SamlUserName, Type = "saml",
            Enabled = true
        });

        // SAMLSingIn resolves the SAML user from the database directly, so a real (in-memory) one is
        // needed — a substitute IDalService would hand back a null context and the flow would refuse
        // for the wrong reason.
        var dal = new API.Tests.Mock.InMemoryDalService(Guid.NewGuid().ToString());
        using (var seed = dal.GetContext())
        {
            seed.Users.Add(new User
            {
                Value = 7, Login = "alice", Name = "Alice", Email = SamlUserName, Type = "saml",
                Enabled = true, Lockout = 0, Password = [1], RoleId = 0
            });
            seed.SaveChanges();
        }

        var controller = ResolveController<AuthenticationController>(services =>
        {
            // The controller takes a generic ILogger<T>, which the shared mock registration does not
            // supply (it registers interfaces returned by Mocked*.Create factories).
            services.AddLogging();
            services.AddSingleton<IHttpContextAccessor>(accessor);
            services.AddSingleton<IMemoryCache>(cache);
            services.AddSingleton(registrations);
            services.AddSingleton(users);
            services.AddSingleton(environment);
            services.AddSingleton(Substitute.For<IRolesService>());
            services.AddSingleton<ServerServices.Services.IDalService>(dal);
            services.AddSingleton<IConfiguration>(
                new ConfigurationBuilder().AddInMemoryCollection(
                    new Dictionary<string, string?> { ["JWT:Timeout"] = "60" }).Build());
        });

        controller.ControllerContext = new ControllerContext { HttpContext = http };

        return (controller, cache, http);
    }

    private static SAMLRequest? Pending(IMemoryCache cache, string requestId) =>
        cache.TryGetValue("SAML_REQ_" + requestId, out SAMLRequest? request) ? request : null;

    // ---- 1. The server mints the id, and only for an approved client -------------------------

    [Fact]
    public async Task MintingRequiresAClientIdHeader()
    {
        var (controller, _, _) = Build(clientIdHeader: null);

        var result = await controller.CreateSamlRequestId();

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
    }

    /// <summary>
    /// The gate that keeps an anonymous outsider from creating a pending sign-in at all: minting
    /// needs a registration an administrator has approved.
    /// </summary>
    [Fact]
    public async Task MintingRequiresAnApprovedClientRegistration()
    {
        var (controller, cache, _) = Build(clientApproved: false);

        var result = await controller.CreateSamlRequestId();

        Assert.IsType<UnauthorizedObjectResult>(result.Result);
        Assert.Null(Pending(cache, "anything"));
    }

    [Fact]
    public async Task MintingProducesAnUnguessableIdBoundToTheClient()
    {
        var (controller, cache, _) = Build();

        var requestId = Assert.IsType<string>(
            Assert.IsType<OkObjectResult>((await controller.CreateSamlRequestId()).Result).Value);

        // 32 characters of a 65-symbol alphabet — about 190 bits.
        Assert.Equal(32, requestId.Length);

        var pending = Pending(cache, requestId);
        Assert.NotNull(pending);
        Assert.Equal("requested", pending!.Status);
        Assert.Equal(ApprovedClient, pending.ClientId);
        Assert.Equal(Approved.Hostname, pending.ClientHostname);
    }

    [Fact]
    public async Task TwoMintsNeverCollide()
    {
        var (controller, _, _) = Build();
        var seen = new HashSet<string>();

        for (var i = 0; i < 50; i++)
        {
            var id = (string)Assert.IsType<OkObjectResult>((await controller.CreateSamlRequestId()).Result).Value!;
            Assert.True(seen.Add(id));
        }
    }

    // ---- 2. The browser endpoint refuses an id the server did not mint -----------------------

    /// <summary>
    /// The regression assertion for the core defect. On the pre-fix code this call created the
    /// pending request, which is what let an attacker choose the id.
    /// </summary>
    [Fact]
    public void TheBrowserEndpointRefusesAnIdTheServerDidNotMint()
    {
        var (controller, cache, _) = Build();

        var result = controller.SAMLRequest("AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA");

        Assert.IsType<BadRequestObjectResult>(result);
        Assert.Null(Pending(cache, "AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("!!!!!!!!!!!!!!!!")]
    public void TheBrowserEndpointRefusesAMalformedId(string requestId) =>
        Assert.IsType<BadRequestObjectResult>(controllerFor().SAMLRequest(requestId));

    private static AuthenticationController controllerFor() => Build().Controller;

    [Fact]
    public async Task TheBrowserEndpointAcceptsAMintedIdAndSetsAHardenedCookie()
    {
        var (controller, _, http) = Build();
        var requestId = (string)Assert.IsType<OkObjectResult>((await controller.CreateSamlRequestId()).Result).Value!;

        var result = controller.SAMLRequest(requestId);

        Assert.IsType<RedirectResult>(result);

        var setCookie = http.Response.Headers.SetCookie.ToString();
        Assert.Contains("SAMLReqID=" + requestId, setCookie, StringComparison.Ordinal);
        Assert.Contains("secure", setCookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("httponly", setCookie, StringComparison.OrdinalIgnoreCase);
    }

    // ---- 3. Explicit approval, with an anti-forgery token ------------------------------------

    /// <summary>
    /// The regression assertion for the silent grant: reaching the sign-in page must not, by itself,
    /// make the request redeemable.
    /// </summary>
    [Fact]
    public async Task ReachingTheSignInPageDoesNotAcceptTheRequest()
    {
        var (controller, cache, http) = Build();
        var requestId = (string)Assert.IsType<OkObjectResult>((await controller.CreateSamlRequestId()).Result).Value!;
        http.Request.Headers.Cookie = "SAMLReqID=" + requestId;

        var result = Assert.IsType<ContentResult>(controller.SAMLSingIn());

        Assert.Equal("requested", Pending(cache, requestId)!.Status);
        // The page asks, and it names the machine that asked.
        Assert.Contains("Approve this sign-in?", result.Content!, StringComparison.Ordinal);
        Assert.Contains(Approved.Hostname!, result.Content!, StringComparison.Ordinal);
        Assert.NotEqual("", Pending(cache, requestId)!.ApprovalToken);
    }

    [Fact]
    public async Task ApprovingWithTheTokenFromThePageAcceptsTheRequest()
    {
        var (controller, cache, http) = Build();
        var requestId = (string)Assert.IsType<OkObjectResult>((await controller.CreateSamlRequestId()).Result).Value!;
        http.Request.Headers.Cookie = "SAMLReqID=" + requestId;
        controller.SAMLSingIn();

        var token = Pending(cache, requestId)!.ApprovalToken;

        Assert.IsType<ContentResult>(controller.ApproveSamlSignIn(token));

        var pending = Pending(cache, requestId)!;
        Assert.Equal("accepted", pending.Status);
        Assert.Equal(SamlUserName, pending.UserName);
        // Burned, so the page cannot be re-posted.
        Assert.Equal("", pending.ApprovalToken);
    }

    /// <summary>
    /// The regression assertion for cross-site approval. The SAML session cookie has to be
    /// <c>SameSite=None</c> to survive the identity provider's POST back, so a page under the
    /// attacker's control could auto-submit this form with the victim's cookie attached. Without the
    /// token check the consent screen would be decorative.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("guessed-token")]
    public async Task ApprovingWithoutTheRightTokenIsRefused(string presented)
    {
        var (controller, cache, http) = Build();
        var requestId = (string)Assert.IsType<OkObjectResult>((await controller.CreateSamlRequestId()).Result).Value!;
        http.Request.Headers.Cookie = "SAMLReqID=" + requestId;
        controller.SAMLSingIn();

        Assert.IsType<BadRequestObjectResult>(controller.ApproveSamlSignIn(presented));
        Assert.Equal("requested", Pending(cache, requestId)!.Status);
    }

    [Fact]
    public void ApprovingWithNoRequestCookieIsRefused() =>
        Assert.IsType<BadRequestObjectResult>(controllerFor().ApproveSamlSignIn("anything"));

    /// <summary>
    /// An approval token replayed from a different browser session must not approve somebody else's
    /// pending request.
    /// </summary>
    [Fact]
    public async Task AnApprovalFromADifferentIdentityIsRefused()
    {
        var (controller, cache, http) = Build();
        var requestId = (string)Assert.IsType<OkObjectResult>((await controller.CreateSamlRequestId()).Result).Value!;
        http.Request.Headers.Cookie = "SAMLReqID=" + requestId;
        controller.SAMLSingIn();
        var token = Pending(cache, requestId)!.ApprovalToken;

        // The browser is now somebody else.
        http.User = new ClaimsPrincipal(
            new ClaimsIdentity([new Claim(ClaimTypes.Name, "mallory@corp.example")], "saml2"));

        Assert.IsType<BadRequestObjectResult>(controller.ApproveSamlSignIn(token));
        Assert.Equal("requested", Pending(cache, requestId)!.Status);
    }

    // ---- 4. Redemption is bound to the minting client, and single-use ------------------------

    private static async Task<(AuthenticationController, IMemoryCache, HttpContext, string)> ApprovedFlow()
    {
        var (controller, cache, http) = Build();
        var requestId = (string)Assert.IsType<OkObjectResult>((await controller.CreateSamlRequestId()).Result).Value!;
        http.Request.Headers.Cookie = "SAMLReqID=" + requestId;
        controller.SAMLSingIn();
        controller.ApproveSamlSignIn(Pending(cache, requestId)!.ApprovalToken);

        return (controller, cache, http, requestId);
    }

    [Fact]
    public async Task AnApprovedRequestYieldsATokenToTheMintingClient()
    {
        var (controller, _, _, requestId) = await ApprovedFlow();

        var result = Assert.IsType<OkObjectResult>(controller.GetAppSAMLToken(requestId));

        Assert.False(string.IsNullOrWhiteSpace(result.Value as string));
    }

    /// <summary>
    /// The regression assertion for the collection half of the finding: knowing the id must not be
    /// enough.
    /// </summary>
    [Fact]
    public async Task AnotherClientCannotCollectTheToken()
    {
        var (controller, _, http, requestId) = await ApprovedFlow();

        http.Request.Headers["ClientId"] = "some-other-device";

        Assert.IsType<UnauthorizedObjectResult>(controller.GetAppSAMLToken(requestId));
    }

    [Fact]
    public async Task ACallerWithNoClientIdCannotCollectTheToken()
    {
        var (controller, _, http, requestId) = await ApprovedFlow();

        http.Request.Headers.Remove("ClientId");

        Assert.IsType<UnauthorizedObjectResult>(controller.GetAppSAMLToken(requestId));
    }

    /// <summary>
    /// Redemption is single-use: the entry is removed before the token is written, so a replay finds
    /// nothing even if it races.
    /// </summary>
    [Fact]
    public async Task TheTokenCanOnlyBeCollectedOnce()
    {
        var (controller, _, _, requestId) = await ApprovedFlow();

        Assert.IsType<OkObjectResult>(controller.GetAppSAMLToken(requestId));
        Assert.IsType<NotFoundObjectResult>(controller.GetAppSAMLToken(requestId));
    }

    [Fact]
    public async Task AnUnapprovedRequestYieldsNoToken()
    {
        var (controller, _, http) = Build();
        var requestId = (string)Assert.IsType<OkObjectResult>((await controller.CreateSamlRequestId()).Result).Value!;
        http.Request.Headers.Cookie = "SAMLReqID=" + requestId;
        controller.SAMLSingIn();

        Assert.IsType<UnauthorizedObjectResult>(controller.GetAppSAMLToken(requestId));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]
    [InlineData("never-minted-but-long-enough-id")]
    public void CollectingAnUnknownIdIsANotFound(string requestId) =>
        Assert.IsType<NotFoundObjectResult>(controllerFor().GetAppSAMLToken(requestId));
}

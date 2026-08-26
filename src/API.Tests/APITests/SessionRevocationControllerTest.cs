using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Security.Claims;
using System.Threading.Tasks;
using API.Controllers;
using API.Tests.DI;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.APITests;

/// <summary>
/// Per-session sign-out at the endpoint (security finding NR-2026-028).
///
/// The service-level behaviour is covered by
/// <c>ServerServices.Tests.Track8.DeferredSecurityFixesInMemoryTest</c>. What is only observable here
/// is the part the finding was actually about: the endpoint revokes <em>the token this request was
/// made with</em>, read out of the Authorization header, and takes no token from the caller. A
/// "log me out" endpoint that accepted a <c>jti</c> parameter would let any authenticated user sign
/// out anybody else's session.
/// </summary>
[TestSubject(typeof(SessionsController))]
public class SessionRevocationControllerTest : BaseControllerTest
{
    private const string Secret = "netrisk-session-revocation-test-signing-key-of-sufficient-length";

    private static string NewToken(string? jti, int minutesToLive = 60)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, "testUser") };

        if (jti is not null) claims.Add(new Claim(JwtRegisteredClaimNames.Jti, jti));

        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(Secret));

        var token = new JwtSecurityToken(
            claims: claims,
            expires: DateTime.UtcNow.AddMinutes(minutesToLive),
            signingCredentials: new SigningCredentials(key, SecurityAlgorithms.HmacSha256));

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// A controller whose request carries <paramref name="authorization"/>, over a revocation service
    /// that records what it was asked to revoke.
    /// </summary>
    private static (SessionsController Controller, ITokenRevocationService Revocation) Build(
        string? authorization)
    {
        var revocation = Substitute.For<ITokenRevocationService>();
        revocation.IsRevokedAsync(Arg.Any<string>()).Returns(Task.FromResult(false));

        var accessor = Substitute.For<IHttpContextAccessor>();

        var context = new DefaultHttpContext
        {
            Connection =
            {
                Id = Guid.NewGuid().ToString(),
                LocalIpAddress = IPAddress.Loopback,
                RemoteIpAddress = IPAddress.Loopback
            },
            User = new ClaimsPrincipal(
                new ClaimsIdentity([new Claim(ClaimTypes.Name, "testUser")], "Basic"))
        };

        if (authorization is not null) context.Request.Headers.Authorization = authorization;

        accessor.HttpContext.Returns(context);

        var controller = ResolveController<SessionsController>(services =>
        {
            services.AddSingleton(revocation);
            services.AddSingleton(accessor);
        });

        return (controller, revocation);
    }

    [Fact]
    public async Task TestSigningOutRevokesTheTokenThisRequestPresented()
    {
        var (controller, revocation) = Build("Bearer " + NewToken("session-abc"));

        Assert.IsType<OkResult>(await controller.Logout());

        await revocation.Received(1).RevokeAsync("session-abc", Arg.Any<int>(),
            Arg.Any<DateTime>(), Arg.Any<string>());
    }

    /// <summary>
    /// The expiry is carried across so the pruning job can drop the row once the token could no
    /// longer have been accepted anyway. Without it the revocation list grows forever.
    /// </summary>
    [Fact]
    public async Task TestTheRevocationRecordsWhenTheTokenWouldHaveExpired()
    {
        var (controller, revocation) = Build("Bearer " + NewToken("session-exp", minutesToLive: 30));

        await controller.Logout();

        await revocation.Received(1).RevokeAsync("session-exp", Arg.Any<int>(),
            Arg.Is<DateTime>(d => d > DateTime.UtcNow.AddMinutes(20) &&
                                  d < DateTime.UtcNow.AddMinutes(40)),
            Arg.Any<string>());
    }

    /// <summary>
    /// A token with no <c>jti</c> cannot be revoked individually, and the response says so and says
    /// what to do instead. Silently returning 200 would tell a client the session had ended when it
    /// had not — which is the worst possible answer for a sign-out.
    /// </summary>
    [Fact]
    public async Task TestATokenWithoutAJtiIsRefusedWithAnActionableMessage()
    {
        var (controller, revocation) = Build("Bearer " + NewToken(jti: null));

        var result = Assert.IsType<BadRequestObjectResult>(await controller.Logout());

        Assert.Contains("password", result.Value!.ToString(), StringComparison.OrdinalIgnoreCase);

        await revocation.DidNotReceive().RevokeAsync(Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<DateTime>(), Arg.Any<string>());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Basic dXNlcjpwYXNz")]
    [InlineData("Bearer not-a-jwt")]
    [InlineData("Bearer ")]
    public async Task TestARequestWithNoUsableBearerTokenIsRefusedRatherThanCrashing(string? header)
    {
        var (controller, revocation) = Build(header);

        Assert.IsType<BadRequestObjectResult>(await controller.Logout());

        await revocation.DidNotReceive().RevokeAsync(Arg.Any<string>(), Arg.Any<int>(),
            Arg.Any<DateTime>(), Arg.Any<string>());
    }

    /// <summary>The scheme is matched case-insensitively, as RFC 7235 requires.</summary>
    [Theory]
    [InlineData("bearer ")]
    [InlineData("BEARER ")]
    [InlineData("Bearer ")]
    public async Task TestTheBearerSchemeIsCaseInsensitive(string prefix)
    {
        var (controller, revocation) = Build(prefix + NewToken("session-case"));

        Assert.IsType<OkResult>(await controller.Logout());

        await revocation.Received(1).RevokeAsync("session-case", Arg.Any<int>(),
            Arg.Any<DateTime>(), Arg.Any<string>());
    }

    /// <summary>
    /// The verification endpoint exists so a client can confirm a sign-out took effect instead of
    /// assuming it did — which is what NR-2026-028 was: an endpoint that returned a cheerful string
    /// and revoked nothing.
    /// </summary>
    [Fact]
    public async Task TestTheCurrentSessionEndpointReportsWhetherThisTokenIsRevoked()
    {
        var (controller, revocation) = Build("Bearer " + NewToken("session-check"));

        revocation.IsRevokedAsync("session-check").Returns(Task.FromResult(true));

        var ok = Assert.IsType<OkObjectResult>(await controller.Current());

        var payload = ok.Value!.ToString()!;

        Assert.Contains("session-check", payload);
        Assert.Contains("True", payload);
    }

    [Fact]
    public async Task TestTheCurrentSessionEndpointReportsNoJtiWithoutFailing()
    {
        var (controller, _) = Build(null);

        var ok = Assert.IsType<OkObjectResult>(await controller.Current());

        Assert.Contains("revoked = False", ok.Value!.ToString());
    }
}

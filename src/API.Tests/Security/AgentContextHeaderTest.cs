using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Threading.Tasks;
using API.Controllers;
using API.Middleware;
using API.Security;
using API.Tests.Mock;
using DAL.Context;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Model.Agents;
using NSubstitute;
using ServerServices.Interfaces;
using ServerServices.Security;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// T358 (S55 "Audit and attribution"): <c>X-NetRisk-Agent</c> is informational. Every policy the API's actions use is
/// evaluated with and without it, for principals of every shape and for reads and writes, and must decide identically —
/// a forged, unknown or hostile value included. It annotates only the authenticated user's own audit records.
/// </summary>
[TestSubject(typeof(AgentAttribution))]
public class AgentContextHeaderTest
{
    /// <summary>A registration id as the desktop issues it: 16 random bytes, unpadded base64url, 22 characters.</summary>
    private const string Registration = "Qm9vdHN0cmFwSWQxMjM0NQ";

    /// <summary>Header values a client could send: none, a real id, and attempts to look like someone or something else.</summary>
    public static readonly string?[] HeaderValues =
    {
        null, Registration, "administrator_root_adm", "Admin", "ana", "bob_the_admin", "analyst\r\nX-Role: Admin",
        "third_line_assurance", new string('A', 500), "vault:v1:3:c2VjcmV0", "1 OR 1=1"
    };

    // ---- authorization never changes ----------------------------------------------------------------------------

    /// <summary>Every distinct authorization requirement set an API action carries: its class and method attributes.</summary>
    private static List<IReadOnlyList<IAuthorizeData>> ActionPolicies() =>
        typeof(ApiBaseController).Assembly.GetTypes()
            .Where(t => typeof(ControllerBase).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttributes<HttpMethodAttribute>().Any())
                .Select(m => (IReadOnlyList<IAuthorizeData>)t.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                    .Concat(m.GetCustomAttributes<AuthorizeAttribute>(inherit: true)).Cast<IAuthorizeData>().ToList()))
            .Where(d => d.Count > 0)
            .GroupBy(d => string.Join("|", d.Select(a => a.Policy ?? a.Roles ?? "")))
            .Select(g => g.First())
            .ToList();

    private static readonly InMemoryDalService Dal = SeedDal();

    private static InMemoryDalService SeedDal()
    {
        var dal = new InMemoryDalService("agent-context-header-" + Guid.NewGuid());
        using var db = dal.GetContext();
        db.Users.Add(new User
        {
            Value = 1, Enabled = true, Name = "ana", Login = "ana", Email = "ana@x.test", Type = "local",
            Password = "secret"u8.ToArray(), RoleId = 1
        });
        db.SaveChanges();
        return dal;
    }

    private static (IAuthorizationService Authorization, IAuthorizationPolicyProvider Provider) Authorization()
    {
        var providerType = typeof(ApiBaseController).Assembly.GetType("API.Security.PermissionPolicyProvider")!;
        var provider = (IAuthorizationPolicyProvider)Activator.CreateInstance(providerType,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            Substitute.For<IFaceIDService>(), Substitute.For<IPluginsService>())!;

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler>(new ValidUserRequirementHandler(Dal));
        services.AddSingleton<IAuthorizationHandler>(
            new PermissionAuthorizationHandler(NullLogger<PermissionAuthorizationHandler>.Instance));
        return (services.BuildServiceProvider().GetRequiredService<IAuthorizationService>(), provider);
    }

    /// <summary>Principals probing every rule shape: none, a role, each write permission, the third line, a stranger.</summary>
    private static IEnumerable<(string Shape, ClaimsPrincipal User)> Principals()
    {
        ClaimsPrincipal Of(string name, params Claim[] claims) =>
            new(new ClaimsIdentity(claims.Prepend(new Claim(ClaimTypes.Name, name)).Prepend(new Claim(ClaimTypes.Sid, "1")),
                "Bearer"));
        Claim P(string key) => new("Permission", key);

        yield return ("no permission", Of("ana"));
        yield return ("Admin role", Of("ana", new Claim(ClaimTypes.Role, "Admin")));
        yield return ("Administrator role", Of("ana", new Claim(ClaimTypes.Role, "Administrator")));
        yield return ("writer", Of("ana", P("submit_risks"), P("riskmanagement"), P("vulnerabilities"),
            P("vulnerabilities_update"), P("vulnerabilities_create"), P("incident_management"), P("assessments"),
            P("entities_manage"), P("hosts"), P("hosts_create")));
        yield return ("third line writer", Of("ana", P(Model.DecisionCycle.ThirdLineAssurance.PermissionKey),
            P("submit_risks"), P("riskmanagement"), P("vulnerabilities"), P("vulnerabilities_update")));
        yield return ("unknown user", Of("mallory", P("submit_risks"), new Claim(ClaimTypes.Role, "Admin")));
    }

    private static HttpContext Request(string method, string? header)
    {
        var http = new DefaultHttpContext();
        http.Request.Method = method;
        http.Request.Path = "/Risks/1";
        if (header is not null) http.Request.Headers[AgentContextHeader.Name] = header;
        return http;
    }

    [Fact]
    public async Task TestEveryActionPolicyDecidesIdenticallyWithAndWithoutTheHeader()
    {
        var (authorization, provider) = Authorization();
        var policies = ActionPolicies();
        Assert.True(policies.Count > 20, "the scan found too few action policies to mean anything");

        var differences = new List<string>();
        var allowedSomewhere = 0;
        foreach (var data in policies)
        {
            var policy = await AuthorizationPolicy.CombineAsync(provider, data);
            Assert.NotNull(policy);
            foreach (var (shape, user) in Principals())
            foreach (var method in new[] { "GET", "PUT" })
            {
                var baseline = (await authorization.AuthorizeAsync(user, Request(method, null), policy!)).Succeeded;
                if (baseline) allowedSomewhere++;
                foreach (var header in HeaderValues.Skip(1))
                {
                    var withHeader = (await authorization.AuthorizeAsync(user, Request(method, header), policy!)).Succeeded;
                    if (withHeader != baseline)
                        differences.Add($"{string.Join("+", data.Select(d => d.Policy))} {shape} {method} '{header}'");
                }
            }
        }

        Assert.True(differences.Count == 0, string.Join(Environment.NewLine, differences));
        Assert.True(allowedSomewhere > 0, "no principal was admitted anywhere: the comparison would be vacuous");
    }

    [Fact]
    public void TestNoAuthenticationOrAuthorizationCodeReadsTheHeader()
    {
        var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        var security = Directory.GetFiles(Path.Combine(root, "API", "Security"), "*.cs")
            .Concat(Directory.GetFiles(Path.Combine(root, "ServerServices", "Security"), "*.cs")
                .Where(f => Path.GetFileName(f) != "AgentAttribution.cs"));

        var readers = security.Where(f =>
        {
            var text = File.ReadAllText(f);
            return text.Contains("AgentContextHeader", StringComparison.Ordinal) ||
                   text.Contains("X-NetRisk-Agent", StringComparison.OrdinalIgnoreCase) ||
                   text.Contains("AgentAttribution", StringComparison.Ordinal);
        }).Select(Path.GetFileName).ToList();

        Assert.Empty(readers);
    }

    // ---- attribution: the user's own records, annotated -----------------------------------------------------------

    private static HttpContext Authenticated(string login, string? header, string method = "PUT")
    {
        var http = Request(method, header);
        http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.Name, login), new Claim(ClaimTypes.Sid, "1")
        }, "Bearer"));
        return http;
    }

    [Fact]
    public void TestAWriteThroughAnAgentIsRecordedAsTheUserViaTheAgent()
    {
        Assert.Equal("ana via agent " + Registration, AgentAttribution.AuditActor(Authenticated("ana", Registration)));
        Assert.Equal("ana", AgentAttribution.AuditActor(Authenticated("ana", null)));
    }

    [Theory]
    [InlineData("analyst\r\nX-Role: Admin")]
    [InlineData("1 OR 1=1")]
    [InlineData("bob")]
    [InlineData("vault:v1:3:c2VjcmV0")]
    public void TestAMalformedHeaderIsIgnoredAndTheActorStaysTheUser(string header)
    {
        Assert.Equal("ana", AgentAttribution.AuditActor(Authenticated("ana", header)));
        Assert.Null(AgentAttribution.AgentOf(Authenticated("ana", header)));
    }

    [Theory]
    [InlineData("administrator_root_adm")]
    [InlineData("bob_the_admin_00000000")]
    public void TestAHeaderNamingSomeoneElseCannotImpersonateThem(string header)
    {
        var actor = AgentAttribution.AuditActor(Authenticated("ana", header));

        // The login is always the authenticated one; the header can only append an agent annotation after it.
        Assert.StartsWith("ana via agent ", actor);
        Assert.Equal("ana via agent " + header, actor);
    }

    [Fact]
    public void TestAnAnonymousRequestIsNeverAttributedToAnAgent()
    {
        var anonymous = Request("PUT", Registration);

        Assert.Equal(AuditableContext.SystemActor, AgentAttribution.AuditActor(anonymous));
        Assert.Null(AgentAttribution.AgentOf(anonymous));
        Assert.Null(AgentAttributionMiddleware.Describe(anonymous));
    }

    /// <summary>
    /// Security review M63 #1: only an id of the issued length is honoured, and the login always survives whole in the
    /// 64-character actor column — a long header used to push it out entirely (53 characters) or past the column (54+).
    /// </summary>
    [Theory]
    [InlineData(64)]
    [InlineData(53)]
    [InlineData(54)]
    [InlineData(21)]
    [InlineData(23)]
    public void TestAHeaderOfAnyOtherLengthIsIgnoredAndTheLoginIsKept(int length)
    {
        var header = new string('A', length);

        Assert.Equal("ana", AgentAttribution.AuditActor(Authenticated("ana", header)));
        Assert.Null(AgentAttribution.AgentOf(Authenticated("ana", header)));
        Assert.Null(AgentContextHeader.Parse(header));
    }

    [Theory]
    [InlineData("ana")]
    [InlineData("a.very.long.corporate.login.example.org.br")]   // 42: the marker is shortened, the login kept
    public void TestAnIssuedIdIsRecordedAndTheLoginIsAlwaysWholeWithinTheColumn(string login)
    {
        Assert.Equal(AgentContextHeader.Length, Registration.Length);
        var actor = AgentAttribution.AuditActor(Authenticated(login, Registration));

        Assert.True(actor.Length <= 64, actor);
        Assert.StartsWith(login + " via agent ", actor);
        if (login.Length + 11 + Registration.Length <= 64) Assert.Equal(login + " via agent " + Registration, actor);
    }

    [Fact]
    public void TestALoginLongerThanTheColumnIsTheWholeActor()
    {
        var login = new string('l', 80);
        var actor = AgentAttribution.AuditActor(Authenticated(login, Registration));

        Assert.Equal(new string('l', 64), actor);
    }

    [Fact]
    public void TestTheRequestLineNamesUserAgentAndRouteButNeverTheQuery()
    {
        var http = Authenticated("ana", Registration);
        http.Request.QueryString = new QueryString("?filter=secret-ish");
        http.Response.StatusCode = 403;

        var line = AgentAttributionMiddleware.Describe(http);

        Assert.Equal($"user 1 (ana) via agent {Registration}: PUT /Risks/1 -> 403", line);
        Assert.Null(AgentAttributionMiddleware.Describe(Authenticated("ana", null)));
        Assert.Null(AgentAttributionMiddleware.Describe(Authenticated("ana", "x\r\ny")));
    }

    [Fact]
    public async Task TestTheMiddlewareChangesNothingAboutTheRequestOrTheResponse()
    {
        var logger = Substitute.For<Serilog.ILogger>();
        var http = Authenticated("ana", Registration);
        var reached = false;
        var middleware = new AgentAttributionMiddleware(context =>
        {
            reached = true;
            context.Response.StatusCode = 204;
            Assert.Same(http.User, context.User);
            return Task.CompletedTask;
        }, logger);

        await middleware.InvokeAsync(http);

        Assert.True(reached);
        Assert.Equal(204, http.Response.StatusCode);
        logger.Received(1).Information("Agent-assisted request: {Line}", Arg.Is<string>(s => s.Contains("via agent")));
    }
}

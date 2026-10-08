using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using API.Controllers;
using API.Security;
using API.Tests.Mock;
using DAL.Entities;
using JetBrains.Annotations;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Model.DecisionCycle;
using NSubstitute;
using ServerServices.Interfaces;
using Xunit;

namespace API.Tests.Security;

/// <summary>
/// Stage 9.9 (S50 §4.7, §8 TL-API1–TL-API8; T198, T199) — the third line reads and never writes, proven over the whole API.
///
/// The read-only rule is a requirement on every policy the API builds (<see cref="ThirdLineReadOnlyRequirement"/>), so
/// this test does not trust a list of "write endpoints" somebody maintains: it enumerates every action of every
/// controller and every HTTP method it answers, builds the action's real combined policy through the API's own
/// <c>PermissionPolicyProvider</c>, and evaluates it for an auditor who holds <em>every</em> permission any endpoint names,
/// the <c>Admin</c> and <c>Administrator</c> roles, and the third-line mark. Every write must be refused for the third-line
/// reason — and allowed to the same principal without the mark, so the refusal is the guard and not a missing permission.
/// A new endpoint is covered the day it is written; one that lets the auditor write fails here.
///
/// It then exercises the same claim through the real ASP.NET Core pipeline — endpoint routing, the authorization
/// middleware, the MVC endpoints of the API assembly — with one request per write route (TL-API7), because the guard
/// depends on the middleware handing it the request, which an attribute test cannot see (the Track 7 lesson).
/// </summary>
[TestSubject(typeof(ThirdLineReadOnlyRequirement))]
public class ThirdLineReadOnlyInventoryTest
{
    private const string AuditorName = "auditor";
    private const string AuditorId = "9";

    /// <summary>
    /// The writes the third line keeps, each because it touches only the caller's own session or credentials. Adding one is
    /// a reviewed act, like an anonymous endpoint: the attribute and this list must agree (TL-API3).
    /// </summary>
    private static readonly Dictionary<string, string> SelfService = new(StringComparer.Ordinal)
    {
        ["SessionsController.Logout"] = "Ends the caller's own session.",
        ["UsersController.ChangePassword"] = "The caller's own password; the route id must be the caller (TL-API4).",
        ["WebAuthnController.BeginRegistration"] = "Enrols a hardware factor on the caller's own account (MFA).",
        ["WebAuthnController.CompleteRegistration"] = "Completes that enrolment.",
        ["AuthenticationController.ApproveSamlSignIn"] = "Approves the caller's own SAML sign-in of the desktop client.",
        ["FaceIDController.CommitTransaction"] =
            "The caller's own biometric ceremony, which reading incidents needs when FaceID is on; route user = caller.",
        ["FaceIDController.ValidateTransactionToken"] = "Validates the caller's own biometric token."
    };

    /// <summary>The GETs that write, found by reading every GET action; each must carry <see cref="StateChangingGetAttribute"/>.</summary>
    private static readonly string[] KnownStateChangingGets =
    [
        "ClientsController.Approve",
        "ClientsController.Reject",
        "MitigationsController.AssociateTeamToMitigation",
        "FaceIDController.EnableUser",
        "FaceIDController.DisableUser",
        "PluginsController.EnablePlugin",
        "PluginsController.DisablePlugin"
    ];

    /// <summary>
    /// Controllers authorized by a role alone, which ASP.NET Core combines without the default policy — so without the
    /// read-only requirement either. Each must explain why no user, the auditor included, can hold that role (TL-API9).
    /// </summary>
    private static readonly Dictionary<string, string> RoleOnlyControllers = new(StringComparer.Ordinal)
    {
        ["ScimController"] =
            "Authorizes an identity provider by its SCIM bearer token, under the synthetic SCIM role that only "
            + "ScimAuthenticationHandler grants, with no user and no permission claim. A user's session never carries that "
            + "role, and issuing a SCIM token is an administrator's write the third line is refused."
    };

    private static bool IsRoleOnly(Type controller) => RoleOnlyControllers.ContainsKey(controller.Name);

    private static readonly string[] LegacyPermissionKeys =
    [
        "riskmanagement", "governance", "assessments", "submit_risks", "delete_risk", "close_risks", "review_insignificant",
        "review_low", "review_medium", "review_high", "review_veryhigh", "plan_mitigations", "accept_mitigation", "bia_manage",
        "restoration_test_record", "business_risk_review", "compliance", "reports", "modify_risks"
    ];

    // --- the API's actions ---------------------------------------------------------------------------------------

    private static IEnumerable<Type> Controllers() =>
        typeof(ApiBaseController).Assembly.GetTypes()
            .Where(t => t is { IsAbstract: false, IsClass: true } && typeof(ControllerBase).IsAssignableFrom(t));

    private static IEnumerable<(Type Controller, MethodInfo Action)> Actions() =>
        Controllers().SelectMany(c => c
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetCustomAttributes<HttpMethodAttribute>(inherit: true).Any())
            .Select(m => (c, m)));

    private static string Key(Type controller, MethodInfo action) => $"{controller.Name}.{action.Name}";

    private static IEnumerable<string> Verbs(MethodInfo action) =>
        action.GetCustomAttributes<HttpMethodAttribute>(inherit: true).SelectMany(h => h.HttpMethods)
            .Distinct(StringComparer.OrdinalIgnoreCase);

    private static bool IsAnonymous(Type controller, MethodInfo action) =>
        action.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any()
        || controller.GetCustomAttributes<AllowAnonymousAttribute>(inherit: true).Any();

    private static bool IsWrite(MethodInfo action, string verb) =>
        !ThirdLineReadOnly.SafeMethods.Contains(verb) || action.GetCustomAttribute<StateChangingGetAttribute>() is not null;

    // --- the principals ------------------------------------------------------------------------------------------

    private static IEnumerable<string> EveryPermissionKey() =>
        Actions().SelectMany(a => a.Action.GetCustomAttributes<PermissionAuthorizeAttribute>(inherit: true)
                .Concat(a.Controller.GetCustomAttributes<PermissionAuthorizeAttribute>(inherit: true)))
            .Select(p => p.Permission)
            .Concat(LegacyPermissionKeys)
            .Distinct(StringComparer.Ordinal);

    /// <summary>Everything any endpoint can ask for — and, when <paramref name="thirdLine"/>, the third-line mark.</summary>
    private static ClaimsPrincipal Omnipotent(bool thirdLine)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.Name, AuditorName), new(ClaimTypes.Sid, AuditorId),
            new(ClaimTypes.Role, "Admin"), new(ClaimTypes.Role, "Administrator"), new("scope", "global")
        };
        claims.AddRange(EveryPermissionKey().Select(k => new Claim("Permission", k)));
        if (thirdLine) claims.Add(new Claim("Permission", ThirdLineAssurance.PermissionKey));

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Bearer"));
    }

    private static InMemoryDalService DalWithAuditor()
    {
        var dal = new InMemoryDalService(Guid.NewGuid().ToString());
        using var db = dal.GetContext();
        db.Users.Add(new User
        {
            Value = int.Parse(AuditorId), Enabled = true, Name = AuditorName, Login = AuditorName, Email = "a@x.test",
            Type = "local", Password = "secret"u8.ToArray(), Admin = true
        });
        db.SaveChanges();
        return dal;
    }

    private static IAuthorizationPolicyProvider Provider()
    {
        var faceId = Substitute.For<IFaceIDService>();
        faceId.IsFaceIDPluginEnabled().Returns(false);

        var type = typeof(ApiBaseController).Assembly.GetType("API.Security.PermissionPolicyProvider")!;
        return (IAuthorizationPolicyProvider)Activator.CreateInstance(type,
            new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>()).Build(),
            faceId, Substitute.For<IPluginsService>())!;
    }

    private static IAuthorizationService AuthorizationService(InMemoryDalService dal)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthorization();
        services.AddSingleton<IAuthorizationHandler>(new ValidUserRequirementHandler(dal));
        services.AddSingleton<IAuthorizationHandler>(
            new PermissionAuthorizationHandler(NullLogger<PermissionAuthorizationHandler>.Instance));
        return services.BuildServiceProvider().GetRequiredService<IAuthorizationService>();
    }

    private static HttpContext Request(Type controller, MethodInfo action, string verb, string? routeId = "2")
    {
        var http = new DefaultHttpContext();
        http.Request.Method = verb;
        var metadata = controller.GetCustomAttributes(inherit: true).Concat(action.GetCustomAttributes(inherit: true));
        http.SetEndpoint(new Endpoint(null, new EndpointMetadataCollection(metadata), Key(controller, action)));
        if (routeId is not null)
        {
            http.Request.RouteValues["id"] = routeId;
            http.Request.RouteValues["userId"] = routeId;
        }

        return http;
    }

    private static async Task<AuthorizationResult> Evaluate(IAuthorizationService authorization,
        IAuthorizationPolicyProvider provider, ClaimsPrincipal user, Type controller, MethodInfo action, string verb,
        string? routeId = "2")
    {
        var data = controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
            .Concat(action.GetCustomAttributes<AuthorizeAttribute>(inherit: true))
            .Cast<IAuthorizeData>()
            .ToList();
        var policy = await AuthorizationPolicy.CombineAsync(provider, data);
        Assert.NotNull(policy);

        return await authorization.AuthorizeAsync(user, Request(controller, action, verb, routeId), policy!);
    }

    private static bool RefusedAsThirdLine(AuthorizationResult result) =>
        !result.Succeeded && result.Failure!.FailureReasons.Any(r => r.Message == ThirdLineAssurance.ReadOnlyRule);

    // --- TL-API1–TL-API6: every action, evaluated ---------------------------------------------------------------

    /// <summary>
    /// TL-API1 (T199) — every write the API exposes refuses the third line, for the third-line reason, although the
    /// auditor holds every permission and both administrator roles; only the reviewed self-service writes pass.
    /// </summary>
    [Fact]
    public async Task TestTLAPI1_EveryWriteRefusesTheThirdLine()
    {
        var provider = Provider();
        var authorization = AuthorizationService(DalWithAuditor());
        var auditor = Omnipotent(thirdLine: true);

        var permitted = new List<string>();
        var otherReason = new List<string>();
        var writes = 0;

        foreach (var (controller, action) in Actions().Where(a => !IsAnonymous(a.Controller, a.Action)))
        foreach (var verb in Verbs(action).Where(v => IsWrite(action, v)))
        {
            if (SelfService.ContainsKey(Key(controller, action))) continue;
            writes++;

            var result = await Evaluate(authorization, provider, auditor, controller, action, verb);
            if (result.Succeeded) permitted.Add($"{verb} {Key(controller, action)}");
            else if (!RefusedAsThirdLine(result) && !IsRoleOnly(controller)) otherReason.Add($"{verb} {Key(controller, action)}");
        }

        Assert.True(permitted.Count == 0,
            "The third line (internal audit) can write through these actions:\n  " + string.Join("\n  ", permitted));
        Assert.True(otherReason.Count == 0,
            "Refused, but not by the read-only rule — the policy lacks the requirement:\n  " + string.Join("\n  ", otherReason));

        // Not vacuous: the API has hundreds of writes, the Stage 9.9 ones among them.
        Assert.True(writes > 250, $"Only {writes} write(s) were evaluated — the enumeration is broken.");
    }

    /// <summary>
    /// TL-API2 — the control: the same principal without the mark is allowed on every one of those writes, so the refusal
    /// above is the read-only rule and not a permission the test forgot.
    /// </summary>
    [Fact]
    public async Task TestTLAPI2_WithoutTheMarkTheSamePrincipalWrites()
    {
        var provider = Provider();
        var authorization = AuthorizationService(DalWithAuditor());
        var everyone = Omnipotent(thirdLine: false);

        var refused = new List<string>();

        foreach (var (controller, action) in Actions().Where(a => !IsAnonymous(a.Controller, a.Action) && !IsRoleOnly(a.Controller)))
        foreach (var verb in Verbs(action))
        {
            var result = await Evaluate(authorization, provider, everyone, controller, action, verb);
            if (!result.Succeeded) refused.Add($"{verb} {Key(controller, action)}");
        }

        Assert.True(refused.Count == 0, "Refused even without the third-line mark:\n  " + string.Join("\n  ", refused));
    }

    /// <summary>TL-API3 — every read stays open to the third line: assurance is reading.</summary>
    [Fact]
    public async Task TestTLAPI3_EveryReadStaysOpenToTheThirdLine()
    {
        var provider = Provider();
        var authorization = AuthorizationService(DalWithAuditor());
        var auditor = Omnipotent(thirdLine: true);

        var refused = new List<string>();
        var reads = 0;

        foreach (var (controller, action) in Actions().Where(a => !IsAnonymous(a.Controller, a.Action) && !IsRoleOnly(a.Controller)))
        foreach (var verb in Verbs(action).Where(v => !IsWrite(action, v)))
        {
            reads++;
            var result = await Evaluate(authorization, provider, auditor, controller, action, verb);
            if (!result.Succeeded) refused.Add($"{verb} {Key(controller, action)}");
        }

        Assert.True(refused.Count == 0, "The third line cannot read:\n  " + string.Join("\n  ", refused));
        Assert.True(reads > 250, $"Only {reads} read(s) were evaluated.");
    }

    /// <summary>
    /// TL-API4 — the self-service writes are exactly the reviewed list, each with a reason, and they stay open to the
    /// third line; changing a password is allowed on the caller's own account and refused on anyone else's.
    /// </summary>
    [Fact]
    public async Task TestTLAPI4_SelfServiceIsTheReviewedListAndOnlyTheCallersOwn()
    {
        var marked = Actions()
            .Where(a => a.Action.GetCustomAttribute<ThirdLineSelfServiceAttribute>() is not null)
            .ToDictionary(a => Key(a.Controller, a.Action), a => a.Action.GetCustomAttribute<ThirdLineSelfServiceAttribute>()!);

        Assert.Equal(SelfService.Keys.OrderBy(k => k), marked.Keys.OrderBy(k => k));
        Assert.All(marked.Values, attribute => Assert.True(attribute.Reason.Length > 20, attribute.Reason));

        var provider = Provider();
        var authorization = AuthorizationService(DalWithAuditor());
        var auditor = Omnipotent(thirdLine: true);

        foreach (var (controller, action) in Actions().Where(a => marked.ContainsKey(Key(a.Controller, a.Action))))
        foreach (var verb in Verbs(action))
            Assert.True((await Evaluate(authorization, provider, auditor, controller, action, verb, AuditorId)).Succeeded,
                $"{verb} {Key(controller, action)} must stay open to the third line for its own account");

        var commit = typeof(FaceIDController).GetMethod(nameof(FaceIDController.CommitTransaction))!;
        Assert.Equal("userId", commit.GetCustomAttribute<ThirdLineSelfServiceAttribute>()!.RouteUserKey);
        Assert.True(RefusedAsThirdLine(await Evaluate(authorization, provider, auditor, typeof(FaceIDController), commit,
            "POST", routeId: "2")));

        var changePassword = typeof(UsersController).GetMethod(nameof(UsersController.ChangePassword))!;
        Assert.Equal("id", changePassword.GetCustomAttribute<ThirdLineSelfServiceAttribute>()!.RouteUserKey);
        Assert.True(RefusedAsThirdLine(await Evaluate(authorization, provider, auditor, typeof(UsersController),
            changePassword, "POST", routeId: "2")));
        Assert.True(RefusedAsThirdLine(await Evaluate(authorization, provider, auditor, typeof(UsersController),
            changePassword, "POST", routeId: null)));
    }

    /// <summary>TL-API5 — every GET known to write carries the attribute, and nothing else claims it.</summary>
    [Fact]
    public void TestTLAPI5_TheGetsThatWriteAreDeclared()
    {
        var declared = Actions()
            .Where(a => a.Action.GetCustomAttribute<StateChangingGetAttribute>() is not null)
            .Select(a => Key(a.Controller, a.Action))
            .OrderBy(k => k);

        Assert.Equal(KnownStateChangingGets.OrderBy(k => k), declared);
    }

    /// <summary>
    /// TL-API6 — the decision itself: a request with no HttpContext (an imperative check) fails closed for the third line
    /// and is open to everyone else; HEAD and OPTIONS read; PATCH writes.
    /// </summary>
    [Fact]
    public async Task TestTLAPI6_TheDecisionFailsClosed()
    {
        var authorization = AuthorizationService(DalWithAuditor());
        var policy = new AuthorizationPolicyBuilder().AddRequirements(ThirdLineReadOnlyRequirement.Instance).Build();

        Assert.False((await authorization.AuthorizeAsync(Omnipotent(true), null, policy)).Succeeded);
        Assert.True((await authorization.AuthorizeAsync(Omnipotent(false), null, policy)).Succeeded);

        Assert.False(ThirdLineReadOnly.IsWrite("HEAD", null));
        Assert.False(ThirdLineReadOnly.IsWrite("options", null));
        Assert.True(ThirdLineReadOnly.IsWrite("PATCH", null));
        Assert.False(ThirdLineReadOnly.IsThirdLine(null));
    }

    /// <summary>
    /// TL-API9 — the controllers authorized by a role alone (no policy, so no read-only requirement) are exactly the
    /// reviewed list, each with its reason: a new one cannot slip past the guard unexamined.
    /// </summary>
    [Fact]
    public void TestTLAPI9_RoleOnlyControllersAreReviewed()
    {
        static IEnumerable<AuthorizeAttribute> All(Type controller) =>
            controller.GetCustomAttributes<AuthorizeAttribute>(inherit: true)
                .Concat(controller.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
                    .SelectMany(m => m.GetCustomAttributes<AuthorizeAttribute>(inherit: true)));

        var roleOnly = Controllers()
            .Where(c => All(c).Any(a => !string.IsNullOrEmpty(a.Roles)) && All(c).All(a => string.IsNullOrEmpty(a.Policy)))
            .Select(c => c.Name)
            .OrderBy(n => n);

        Assert.Equal(RoleOnlyControllers.Keys.OrderBy(k => k), roleOnly);
        Assert.All(RoleOnlyControllers.Values, reason => Assert.True(reason.Length > 20));
    }

    // --- TL-API7–TL-API8: through the real pipeline -------------------------------------------------------------

    /// <summary>An authentication scheme that signs the request in as the principal the test chose.</summary>
    private sealed class TestScheme(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Scheme.Name != "Bearer") return Task.FromResult(AuthenticateResult.NoResult());

            var principal = Omnipotent(thirdLine: Request.Headers["X-Third-Line"] == "1");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }

    private static (RequestDelegate Pipeline, IServiceProvider Services) Pipeline()
    {
        var dal = DalWithAuditor();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(new DiagnosticListener("ThirdLineReadOnlyInventoryTest"));
        services.AddSingleton<DiagnosticSource>(sp => sp.GetRequiredService<DiagnosticListener>());
        services.AddRouting();
        services.AddControllers().AddApplicationPart(typeof(ApiBaseController).Assembly);
        services.AddAuthentication("Bearer")
            .AddScheme<AuthenticationSchemeOptions, TestScheme>("headerSelector", _ => { })
            .AddScheme<AuthenticationSchemeOptions, TestScheme>("BasicAuthentication", _ => { })
            .AddScheme<AuthenticationSchemeOptions, TestScheme>("Bearer", _ => { });
        services.AddAuthorization();
        services.AddSingleton(Provider());
        services.AddSingleton<IAuthorizationHandler>(new ValidUserRequirementHandler(dal));
        services.AddSingleton<IAuthorizationHandler>(
            new PermissionAuthorizationHandler(NullLogger<PermissionAuthorizationHandler>.Instance));

        var provider = services.BuildServiceProvider();
        var app = new ApplicationBuilder(provider);
        app.UseRouting();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseEndpoints(endpoints => endpoints.MapControllers());

        return (app.Build(), provider);
    }

    /// <summary>A concrete URL for a route template: every parameter filled with a value its constraint accepts.</summary>
    private static string UrlOf(RouteEndpoint endpoint)
    {
        var raw = "/" + endpoint.RoutePattern.RawText!.TrimStart('/');
        return Regex.Replace(raw, @"\{\*{0,2}([^}:=?]+)(:[^}=?]+)?(=[^}?]*)?\??\}", m =>
        {
            var constraint = m.Groups[2].Value;
            if (constraint.Contains("guid", StringComparison.OrdinalIgnoreCase)) return Guid.NewGuid().ToString();
            if (constraint.Contains("bool", StringComparison.OrdinalIgnoreCase)) return "true";
            return m.Groups[1].Value.Equals("id", StringComparison.OrdinalIgnoreCase) ? "2" : "1";
        });
    }

    /// <summary>
    /// Sends one request through routing and the authorization middleware. A request the middleware lets through reaches
    /// MVC, which cannot build the controller here (its services are not registered): that exception means "authorized",
    /// and is reported as status 0.
    /// </summary>
    private static async Task<int> Send(RequestDelegate pipeline, IServiceProvider services, string method, string url,
        bool thirdLine)
    {
        using var scope = services.CreateScope();
        var http = new DefaultHttpContext { RequestServices = scope.ServiceProvider };
        http.Request.Method = method;
        http.Request.Path = url;
        http.Request.Headers["X-Third-Line"] = thirdLine ? "1" : "0";
        http.Response.Body = new MemoryStream();

        try
        {
            await pipeline(http);
            return http.Response.StatusCode;
        }
        catch (InvalidOperationException)
        {
            return 0;
        }
    }

    /// <summary>
    /// TL-API7 (T199) — observed through the pipeline: one request per write route of the API, as the auditor, answers
    /// 403 before any controller runs; the reviewed self-service routes pass the middleware.
    /// </summary>
    [Fact]
    public async Task TestTLAPI7_ThroughThePipelineEveryWriteRouteAnswers403()
    {
        var (pipeline, services) = Pipeline();
        var endpoints = services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();

        var leaks = new List<string>();
        var sent = 0;

        foreach (var endpoint in endpoints)
        {
            if (endpoint.Metadata.GetMetadata<IAllowAnonymous>() is not null) continue;

            var methods = endpoint.Metadata.GetMetadata<IHttpMethodMetadata>()?.HttpMethods ?? ["POST"];
            var selfService = endpoint.Metadata.GetMetadata<ThirdLineSelfServiceAttribute>() is not null;

            foreach (var method in methods.Where(m => ThirdLineReadOnly.IsWrite(m, endpoint)))
            {
                var status = await Send(pipeline, services, method, UrlOf(endpoint), thirdLine: true);
                sent++;

                if (selfService) continue;
                if (status != StatusCodes.Status403Forbidden)
                    leaks.Add($"{method} {endpoint.RoutePattern.RawText} answered {status} ({endpoint.DisplayName})");
            }
        }

        Assert.True(leaks.Count == 0, "Through the pipeline the third line was not refused on:\n  " + string.Join("\n  ", leaks));
        Assert.True(sent > 250, $"Only {sent} write route(s) were sent — the endpoint enumeration is broken.");
    }

    /// <summary>
    /// TL-API8 — the control through the pipeline: the Stage 9.9 write routes pass the middleware for a principal without
    /// the mark, and the reads pass it for the auditor — the 403 above is the third-line rule, not a broken pipeline.
    /// </summary>
    [Theory]
    [InlineData("POST", "/RiskArchive/Risks/1", false)]
    [InlineData("POST", "/RiskArchive/Risks/1/Reopen", false)]
    [InlineData("POST", "/RiskArchive/Risks/1/Reviews", false)]
    [InlineData("PUT", "/Backtesting/Incidents/1", false)]
    [InlineData("POST", "/RiskCommittees", false)]
    [InlineData("POST", "/RiskCommittees/Decisions/1/Votes", false)]
    [InlineData("GET", "/RiskArchive/Risks", true)]
    [InlineData("GET", "/Backtesting", true)]
    [InlineData("GET", "/RiskCommittees/Decisions", true)]
    [InlineData("GET", "/AuditTrail/Evidence", true)]
    public async Task TestTLAPI8_ThePipelineLetsTheRightRequestsThrough(string method, string url, bool thirdLine)
    {
        var (pipeline, services) = Pipeline();

        var status = await Send(pipeline, services, method, url, thirdLine);

        Assert.True(status is not (StatusCodes.Status401Unauthorized or StatusCodes.Status403Forbidden
                or StatusCodes.Status404NotFound or StatusCodes.Status405MethodNotAllowed),
            $"{method} {url} answered {status} at the middleware");
    }
}

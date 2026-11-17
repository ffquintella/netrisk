using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using ClientServices.Interfaces;
using GUIClient.Agents;
using GUIClient.Agents.ReadTools;
using JetBrains.Annotations;
using Model.Authentication;
using NSubstitute;
using Xunit;

namespace GUIClient.Tests.Agents;

/// <summary>
/// T353 (S55 "the agent is the user, never more"): every registered tool, for a user lacking its permission, is left out
/// of tools/list and refused before any request reaches the API; when the API itself refuses, the tool answers a clean
/// error with no data. No tool is listed for a user the server's own policy would refuse (M62 review finding 5), and a
/// session or lease that changes while a call runs drops its result (finding 6).
/// </summary>
[TestSubject(typeof(McpDispatcher))]
[Collection(ClientServicesAccessorCollection.Name)]
public class AgentToolsNeverEscalateTest
{
    public static TheoryData<string> Tools() => AgentReadToolArguments.AllTools();

    /// <summary>A user holding every permission the catalog reads except <paramref name="tool"/>'s own.</summary>
    private static AuthenticatedUserInfo Lacking(IAgentTool tool) =>
        AgentRig.DefaultUser(AgentReadRig.AllCatalogKeys().Except(tool.Permission.Keys, StringComparer.Ordinal)
            .DefaultIfEmpty("nothing_at_all").ToArray());

    private static IAgentTool ToolNamed(string name) => new AgentReadFixtures().Catalog().Single(t => t.Name == name);

    [Theory]
    [MemberData(nameof(Tools))]
    public async Task TestAToolIsListedOnlyForAUserHoldingItsPermission(string name)
    {
        var tool = ToolNamed(name);
        var holding = AgentReadRig.OverFixtures(user: AgentRig.DefaultUser(
            tool.Permission.Keys.DefaultIfEmpty("nothing_at_all").ToArray()));
        Assert.Contains(name, await holding.ListAsync());

        if (tool.Permission.Keys.Count == 0) return; // whoami: any signed-in user
        var lacking = AgentReadRig.OverFixtures(user: Lacking(tool));
        Assert.DoesNotContain(name, await lacking.ListAsync());
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public async Task TestAUserLackingTheToolsPermissionIsRefusedBeforeAnyRequestReachesTheApi(string name)
    {
        var tool = ToolNamed(name);
        if (tool.Permission.Keys.Count == 0) return; // whoami: nothing to lack, and it makes no API call
        var api = new RefusingRestService();
        var rig = AgentReadRig.OverRealServices(api, Lacking(tool));

        var answer = await rig.CallAsync(name);

        Assert.True(answer.IsError);
        Assert.Equal("forbidden", answer.Status);
        Assert.Null(answer.Data);
        Assert.Empty(api.Requests);
        Assert.Contains(rig.Rig.Audit.Events, e => e.Kind == AgentAuditKind.ToolRefused && e.Detail.StartsWith(name + ":"));
    }

    [Theory]
    [MemberData(nameof(Tools))]
    public async Task TestACallTheApiRefusesComesBackAsACleanErrorWithNoData(string name)
    {
        if (name == "whoami") return; // describes the session the client already holds: no API call to refuse
        var api = new RefusingRestService(HttpStatusCode.Forbidden);
        var rig = AgentReadRig.OverRealServices(api); // the client-side mirror says yes to everything

        var answer = await rig.CallAsync(name);

        Assert.NotEmpty(api.Requests); // the refusal came from the API path, not from the MCP layer
        Assert.True(answer.IsError, name + " returned data although the server refused it");
        // Most REST services keep the status. The assessment reads swallow it — into null (AssessmentsRestService) or
        // an exception without a status code (AssessmentEvidenceRestService) — so those four say no_answer instead.
        var swallowsStatus = name is "list_assessments" or "list_assessment_runs" or "get_assessment_run_answers"
            or "list_assessment_evidence";
        Assert.Equal(swallowsStatus ? AgentToolErrors.NoAnswer : AgentToolErrors.Refused, answer.Status);
        Assert.Null(answer.Data);
        Assert.Null(answer.Structured["data_trust"]);
        Assert.DoesNotContain("vault:v1", answer.Body);       // the server's own error text is never relayed
        Assert.DoesNotContain("server text", answer.Body);
        Assert.Equal(2, answer.Structured.Count);             // status and message, nothing else
        Assert.Contains(rig.Rig.Audit.Events, e => e.Kind == AgentAuditKind.ToolFailed && e.Detail.StartsWith(name + ":"));
    }

    // ---- Finding 5: no tool is listed for a user the server's policy refuses -------------------------------------

    private static bool Holds(AuthenticatedUserInfo u, string p) => u.UserPermissions?.Contains(p) ?? false;

    private static bool AdminClaim(AuthenticatedUserInfo u) => u.IsAdmin || u.UserRole == "Admin";

    /// <summary>
    /// The API's own policy for the endpoints each tool calls, restated from PermissionPolicyProvider and the controllers'
    /// attributes — independent of the client's rule, so the two can be compared. Entities need only a valid user.
    /// </summary>
    private static readonly Dictionary<string, Func<AuthenticatedUserInfo, bool>> ServerAdmits = new()
    {
        ["whoami"] = _ => true,
        ["search_risks"] = u => u.UserRole == "Administrator" || Holds(u, "riskmanagement"),       // RequireRiskmanagement
        ["get_risk"] = u => u.UserRole == "Administrator" || Holds(u, "riskmanagement"),
        ["get_risk_mitigation"] = u => u.UserRole == "Administrator" || Holds(u, "riskmanagement"),
        ["list_risk_reviews"] = u => AgentReadToolCatalog.ReviewPermissions.Any(p => Holds(u, p)), // RequireMgmtReviewAccess
        ["search_findings"] = u => AdminClaim(u) || Holds(u, "vulnerabilities"),                   // [PermissionAuthorize]
        ["get_finding"] = u => AdminClaim(u) || Holds(u, "vulnerabilities"),
        ["get_finding_lifecycle"] = u => AdminClaim(u) || Holds(u, "vulnerabilities"),
        ["search_incidents"] = u => AdminClaim(u) || Holds(u, "incident_management"),
        ["get_incident"] = u => AdminClaim(u) || Holds(u, "incident_management"),
        ["search_response_plans"] = u => AdminClaim(u) || Holds(u, "incident-response-plans"),
        ["get_response_plan"] = u => AdminClaim(u) || Holds(u, "incident-response-plans"),
        ["list_assessments"] = u => Holds(u, "assessments"),                                       // RequireAssessmentAccess
        ["list_assessment_runs"] = u => Holds(u, "assessments"),
        ["get_assessment_run_answers"] = u => Holds(u, "assessments"),
        ["list_assessment_evidence"] = u => Holds(u, "assessments"),
        ["search_entities"] = _ => true,                                                           // RequireValidUser
        ["get_entity"] = _ => true,
        ["get_entity_relationships"] = _ => true,
        ["search_hosts"] = u => AdminClaim(u) || Holds(u, "hosts"),
        ["get_host"] = u => AdminClaim(u) || Holds(u, "hosts"),
        ["get_risk_overview"] = u => AdminClaim(u) || Holds(u, "risk_index_view"),
        ["list_risk_overview_children"] = u => AdminClaim(u) || Holds(u, "risk_index_view")
    };

    /// <summary>Users that probe every policy shape: no permission, each role variant, each single permission, all.</summary>
    public static TheoryData<string> Users()
    {
        var data = new TheoryData<string> { "none", "admin-flag", "role-Admin", "role-Administrator", "all", "all+admin" };
        foreach (var key in AgentReadRig.AllCatalogKeys()) data.Add("only:" + key);
        return data;
    }

    private static AuthenticatedUserInfo UserFor(string shape)
    {
        var user = AgentRig.DefaultUser("nothing_at_all");
        switch (shape)
        {
            case "admin-flag": user.IsAdmin = true; break;
            case "role-Admin": user.UserRole = "Admin"; break;
            case "role-Administrator": user.UserRole = "Administrator"; break;
            case "all": user.UserPermissions = AgentReadRig.AllCatalogKeys().ToList(); break;
            case "all+admin": user.UserPermissions = AgentReadRig.AllCatalogKeys().ToList(); user.IsAdmin = true; break;
            default:
                if (shape.StartsWith("only:", StringComparison.Ordinal)) user.UserPermissions = new List<string> { shape[5..] };
                break;
        }
        return user;
    }

    [Theory]
    [MemberData(nameof(Users))]
    public async Task TestNoToolIsListedForAUserTheServerPolicyRefuses(string shape)
    {
        var user = UserFor(shape);
        var listed = await AgentReadRig.OverFixtures(user: user).ListAsync();

        Assert.Equal(ServerAdmits.Keys.OrderBy(k => k), new AgentReadFixtures().Catalog().Select(t => t.Name).OrderBy(k => k));
        var overReach = listed.Where(ServerAdmits.ContainsKey).Where(tool => !ServerAdmits[tool](user)).ToList();
        Assert.True(overReach.Count == 0, $"Listed for '{shape}' though the API refuses it: {string.Join(", ", overReach)}");
    }

    [Theory]
    [MemberData(nameof(Users))]
    public async Task TestEveryToolButTheEntityToolsMirrorsTheServerPolicyExactly(string shape)
    {
        var user = UserFor(shape);
        var listed = (await AgentReadRig.OverFixtures(user: user).ListAsync()).ToHashSet();

        foreach (var (tool, admits) in ServerAdmits.Where(p => !p.Key.Contains("entit", StringComparison.Ordinal)))
            Assert.True(admits(user) == listed.Contains(tool), $"{tool} for '{shape}': server {admits(user)}, listed {listed.Contains(tool)}");
    }

    // ---- Finding 6: the session user, and check-then-use ---------------------------------------------------------

    [Fact]
    public void TestTheSessionUserIsNobodyOnceTheSessionIsNoLongerAuthenticated()
    {
        var user = AgentReadRig.Everything();
        var authentication = Substitute.For<IAuthenticationService>();
        authentication.AuthenticatedUserInfo.Returns(user);

        authentication.IsAuthenticated.Returns(false); // Logout and DiscardAuthenticationToken leave the user info set
        Assert.Null(AgentSessionUser.Of(authentication));

        authentication.IsAuthenticated.Returns(true);
        Assert.Same(user, AgentSessionUser.Of(authentication));
    }

    private static (AgentRig Rig, StubTool Tool, string Key, AgentRegistration Agent) Leased()
    {
        var tool = new StubTool("read_item", requiresEdit: false);
        var rig = new AgentRig(tools: new IAgentTool[] { tool });
        var (agent, key) = rig.RegisterApproved();
        rig.Grant(agent, 1, AgentAccessMode.Read);
        return (rig, tool, key, agent);
    }

    private static async Task<JsonObject> CallAsync(AgentRig rig, string key)
    {
        var response = await rig.SendAsync(AgentRig.Post(AgentRig.ToolCall(3, "read_item", $"{{\"agent_key\":\"{key}\"}}")));
        return JsonNode.Parse(response.Body)!["result"]!.AsObject();
    }

    [Fact]
    public async Task TestASignOutWhileACallRunsDropsItsResult()
    {
        var (rig, tool, key, _) = Leased();
        tool.OnInvoke = _ =>
        {
            rig.User = null; // the session ends while the tool is reading
            return new JsonObject { ["status"] = "secret-item-text", ["tool"] = "read_item" };
        };

        var result = await CallAsync(rig, key);

        Assert.True((bool)result["isError"]!);
        Assert.Equal("unavailable", (string?)result["structuredContent"]!["status"]);
        Assert.DoesNotContain("secret-item-text", result.ToJsonString());
        Assert.Contains(rig.Audit.Events, e => e.Kind == AgentAuditKind.ToolRefused && e.Detail.EndsWith("after the call"));
    }

    [Fact]
    public async Task TestAnotherUserSigningInWhileACallRunsDropsItsResult()
    {
        var (rig, tool, key, _) = Leased();
        tool.OnInvoke = _ =>
        {
            var other = AgentRig.DefaultUser(StubTool.StubPermission);
            other.UserId = AgentRig.UserId + 1;
            rig.User = other;
            return new JsonObject { ["status"] = "secret-item-text", ["tool"] = "read_item" };
        };

        var result = await CallAsync(rig, key);

        Assert.Equal("unavailable", (string?)result["structuredContent"]!["status"]);
        Assert.DoesNotContain("secret-item-text", result.ToJsonString());
    }

    [Fact]
    public async Task TestAPermissionLostWhileACallRunsDropsItsResult()
    {
        var (rig, tool, key, _) = Leased();
        tool.OnInvoke = _ =>
        {
            rig.User = AgentRig.DefaultUser("something_else");
            return new JsonObject { ["status"] = "secret-item-text", ["tool"] = "read_item" };
        };

        var result = await CallAsync(rig, key);

        Assert.Equal("forbidden", (string?)result["structuredContent"]!["status"]);
        Assert.DoesNotContain("secret-item-text", result.ToJsonString());
    }

    [Theory]
    [InlineData("ended", "access_expired")]
    [InlineData("paused", "paused")]
    [InlineData("replaced", "access_expired")]
    public async Task TestALeaseThatChangesWhileACallRunsDropsItsResult(string change, string status)
    {
        var (rig, tool, key, agent) = Leased();
        tool.OnInvoke = _ =>
        {
            switch (change)
            {
                case "ended": rig.Leases.End(agent.Id, LeaseEndReason.EndedByUser); break;
                case "paused": rig.Leases.Pause(agent.Id); break;
                default:
                    rig.Leases.End(agent.Id, LeaseEndReason.EndedByUser);
                    rig.Grant(agent, 1, AgentAccessMode.Read); // a new lease is not the one the call started under
                    break;
            }
            return new JsonObject { ["status"] = "secret-item-text", ["tool"] = "read_item" };
        };

        var result = await CallAsync(rig, key);

        Assert.Equal(status, (string?)result["structuredContent"]!["status"]);
        Assert.DoesNotContain("secret-item-text", result.ToJsonString());
    }

    [Fact]
    public async Task TestNoToolRunsForAnAgentWhoseUserIsNotTheOneSignedIn()
    {
        var api = new RefusingRestService(HttpStatusCode.OK);
        var rig = AgentReadRig.OverRealServices(api);

        rig.User = null;
        var signedOut = await rig.CallAsync("search_risks");
        var listedSignedOut = await rig.ListAsync();

        var other = AgentReadRig.Everything();
        other.UserId = AgentRig.UserId + 1;
        rig.User = other;
        var otherUser = await rig.CallAsync("whoami");

        Assert.Equal("unavailable", signedOut.Status);
        Assert.Equal("unavailable", otherUser.Status);
        Assert.Equal(McpDispatcher.BootstrapToolNames.OrderBy(n => n), listedSignedOut.OrderBy(n => n));
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task TestAPermissionLostDuringALeaseIsEnforcedOnTheNextCall()
    {
        var api = new RefusingRestService(HttpStatusCode.OK);
        var rig = AgentReadRig.OverRealServices(api);
        Assert.Contains("search_findings", await rig.ListAsync());

        // The user signs in again with a role that lost "vulnerabilities"; the lease is still running.
        rig.User = AgentRig.DefaultUser(AgentReadRig.AllCatalogKeys().Where(k => k != "vulnerabilities").ToArray());
        var answer = await rig.CallAsync("search_findings");

        Assert.Equal("forbidden", answer.Status);
        Assert.DoesNotContain("search_findings", await rig.ListAsync());
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task TestTheThirdLineMayReadButTheMirrorNeverAddsWhatItLacks()
    {
        // Reads are not writes: the third line's read-only rule refuses no read tool. Holding it grants nothing either.
        var auditor = AgentRig.DefaultUser(Model.DecisionCycle.ThirdLineAssurance.PermissionKey, "riskmanagement");
        var listed = await AgentReadRig.OverFixtures(user: auditor).ListAsync();

        Assert.Contains("search_risks", listed);
        Assert.DoesNotContain("search_findings", listed);
        Assert.DoesNotContain("search_hosts", listed);
    }
}
